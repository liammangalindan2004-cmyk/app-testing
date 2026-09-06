using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;

/// <summary>
/// Wires your existing Canvas buttons and text labels to the kanji writing system,
/// AND grades each character you draw, writing results to the same Realtime
/// Database used by the reading quiz (KanjiQuiz.cs).
/// Everything lives inside your Canvas — no 3D world-space objects needed.
///
/// ═══════════════════════════════════════════════════════════
///  WHAT'S NEW — DATABASE GRADING
/// ═══════════════════════════════════════════════════════════
///  • Practice characters can now be pulled from kanji/{jlptLevel} in the
///    Realtime Database (same source as the reading quiz), ordered by SRS
///    priority (overdue / unseen first) — or you can keep typing a fixed
///    `practiceList` locally by turning off `Use Firebase Kanji List`.
///  • Every completed character is graded from 0–100% using the accuracy
///    scores KanjiDrawingBoard already reports per stroke (including retries),
///    and:
///      1) appended to writingHistory/{userId}/{pushId}  — one record per
///         completed character, never overwritten
///      2) rolled into a running session average written LIVE to
///         grades/{jlptLevel}/{userId}/writing  — matches the existing
///         grade schema (reading/speaking/writing/listening)
///      3) used to update srsWriting/{userId}/{jlptLevel}/{character}
///         (SM-2 spaced repetition, same algorithm as the reading quiz's
///         SRS — kept in a SEPARATE node from the reading quiz's `srs/`
///         path so writing mastery and reading mastery of the same kanji
///         don't overwrite each other)
///  • This UI loops via Next/Prev with no explicit "quiz complete" screen,
///    so a "review" is defined as one full pass through every due character.
///    When that pass finishes, ONE summary record is appended to
///    writingSessionHistory/{userId}/{pushId} — correct / total kanji
///    reviewed plus the average accuracy for that pass — the same
///    "one instance per review" shape as the reading quiz's readingHistory.
///    The tally then resets so the next pass becomes its own new instance.
///
/// ═══════════════════════════════════════════════════════════
///  FULL SETUP GUIDE
/// ═══════════════════════════════════════════════════════════
///
///  STEP 1 — Create the drawing area inside your Panel:
///    • In your Panel, create an empty child GameObject
///    • Name it "KanjiDrawArea"
///    • Set its RectTransform to fill the area you want (e.g. stretch to fill)
///    • Add component: KanjiStrokeGraphic   (draws the kanji + ghost + trail)
///    • Add component: KanjiDrawingBoard    (handles mouse/touch input)
///    • Add component: Image → Color = white (so it has a clickable surface)
///      Set Image color to white/light grey so the EventSystem detects clicks on it
///
///  STEP 2 — Add this script to your KanjiManager (or any GameObject):
///    • Drag "KanjiDrawArea" into the "Drawing Board" field
///    • Drag your text labels into the label fields
///    • Wire your buttons via OnClick → KanjiPracticeUI → method name
///    • Set Jlpt Level / User Id under "Realtime Database Config" (same
///      values you use in the reading quiz, so both feed the same student)
///
///  STEP 3 — Wire buttons:
///    • Animate button  OnClick → DoAnimate()
///    • Reset button    OnClick → DoReset()
///    • Next button     OnClick → DoNext()
///    • Previous button OnClick → DoPrev()
///
///  FONT NOTE:
///    LiberationSans doesn't include Japanese characters.
///    The CharacterLabel will show □ boxes until you:
///    1. Download "Noto Sans JP" from fonts.google.com
///    2. Window → TextMeshPro → Font Asset Creator
///    3. Source Font = Noto Sans JP, Character Set = Unicode Range, Range = 4E00-9FFF
///    4. Generate & Save, then assign to your CharacterLabel's Font Asset field
///
///  REALTIME DATABASE PATHS (see KanjiQuiz.cs for the reading-quiz side)
///     kanji/{jlptLevel}/{character}            — read, if useFirebaseKanjiList
///     srsWriting/{userId}/{jlptLevel}/{char}   — write, SM-2 fields
///     writingHistory/{userId}/{pushId}         — write, one per completed char
///     writingSessionHistory/{userId}/{pushId}  — write, one per full review pass
///                                                 (fields: correct, total, percent)
///     grades/{jlptLevel}/{userId}/writing      — write, running session percent
/// ═══════════════════════════════════════════════════════════
/// </summary>
public class KanjiPracticeUI : MonoBehaviour
{
    [Header("── Drawing Area (has KanjiStrokeGraphic + KanjiDrawingBoard) ──")]
    public KanjiDrawingBoard drawingBoard;

    [Header("── Your Canvas Text Labels ──")]
    public TMP_Text characterLabel;
    public TMP_Text strokeProgressLabel;
    public TMP_Text scoreLabel;
    public TMP_Text feedbackLabel;

    [Header("── Practice List ──")]
    [Tooltip("Used only if 'Use Firebase Kanji List' is off, or as a fallback if the DB load fails/returns nothing.")]
    public string practiceList = "日月火水木金土";

    [Header("── Realtime Database Config ──")]
    [Tooltip("Your RTDB instance URL. Must be set explicitly because this project is NOT in the default us-central1 region.")]
    [SerializeField] private string databaseUrl = "https://unmei-nihongo-center-default-rtdb.asia-southeast1.firebasedatabase.app/";
    [Tooltip("Key under kanji/ and srsWriting/ — e.g. jlpt_n5, jlpt_n4, jlpt_n3, jlpt_n2.")]
    [SerializeField] private string jlptLevel = "jlpt_n5";
    [Tooltip("Used ONLY if no student is logged in via StudentSession (e.g. opening this scene directly in the Editor without going through login). Normally userId comes from whoever is actually logged in, so grades land on the right account.")]
    [SerializeField] private string debugUserIdOverride = "student_001";
    private string userId => string.IsNullOrEmpty(StudentSession.CurrentUid) ? debugUserIdOverride : StudentSession.CurrentUid;
    [Tooltip("If on, the practice character set is loaded from kanji/{jlptLevel} in the database, ordered by SRS priority — same source the reading quiz uses. If off, 'Practice List' above is used as typed.")]
    public bool useFirebaseKanjiList = true;

    [Header("── Stroke Direction Arrows ──")]
    [Tooltip("Show an arrowhead at the end of each guide stroke to indicate writing direction")]
    public bool showDirectionArrows = true;
    [Tooltip("Also show an arrowhead on the highlighted 'next stroke' ghost hint")]
    public bool showGhostArrow = true;
    [Tooltip("Show a small dot marking the start point of each stroke")]
    public bool showStartDots = true;
    [Tooltip("Length of the arrowhead, in UI pixels")]
    public float arrowLength = 40f;
    [Tooltip("Width of the arrowhead base, in UI pixels")]
    public float arrowWidth = 40f;

    // ── SRS constants (SM-2 algorithm — same as the reading quiz) ───────────
    private const float EaseDefault = 2.5f;
    private const float EaseMin = 1.3f;

    private class SrsRecord
    {
        public float easeFactor = EaseDefault;
        public int interval = 1;
        public long nextReviewUnix = 0;
        public int correctStreak = 0;
        public int totalCorrect = 0;
        public int totalAttempts = 0;
    }

    // ── Private ───────────────────────────────────────────────────────────────
    private string[] _chars;
    private int _idx = 0;
    private bool _ready = false;
    private bool _nothingDue = false;
    private KanjiStrokeGraphic _graphic;

    private DatabaseReference _dbRoot;
    private Dictionary<string, SrsRecord> _srsMap = new();

    // Per-character grading accumulators (reset every time a new kanji loads)
    private float _strokeScoreSum = 0f;
    private int _strokeScoreCount = 0;
    private int _wrongAttemptsForCurrent = 0;

    // Session-wide grading accumulators (persist across Next/Prev while this scene is open,
    // and reset every time a full pass through the due queue is completed — see
    // WriteWritingSessionHistory / ResetSessionAccumulators)
    private int _sessionCharsCompleted = 0;
    private float _sessionQualitySum = 0f;
    private int _sessionCorrectCount = 0;

    // Activity calendar bookkeeping
    private float _sessionStartRealtime;
    private int _lastLoggedWholeMinutes = 0;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Start()
    {
        _sessionStartRealtime = Time.realtimeSinceStartup;

        // Validate
        if (drawingBoard == null)
        {
            Debug.LogError("[KanjiPracticeUI] 'Drawing Board' is not assigned! " +
                           "Create a KanjiDrawArea GameObject with KanjiStrokeGraphic and " +
                           "KanjiDrawingBoard components, then drag it here.");
            return;
        }

        // Wire events from the drawing board
        drawingBoard.OnStrokeCompleted.AddListener(OnStrokeDone);
        drawingBoard.OnKanjiCompleted.AddListener(OnKanjiDone);
        drawingBoard.OnWrongStroke.AddListener(OnWrong);

        // Push our arrow settings into the drawing component
        _graphic = drawingBoard.GetComponent<KanjiStrokeGraphic>();
        ApplyArrowSettings();

        StartCoroutine(InitFirebaseThenLoad());
    }

    private IEnumerator InitFirebaseThenLoad()
    {
        bool depsDone = false;
        DependencyStatus depStatus = DependencyStatus.UnavailableOther;

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            depStatus = task.Result;
            depsDone = true;
        });
        yield return new WaitUntil(() => depsDone);

        if (depStatus != DependencyStatus.Available)
        {
            Debug.LogError("[Practice] Firebase unavailable: " + depStatus +
                            " — grading will not be saved, but drawing practice still works.");
        }
        else
        {
            FirebaseDatabase database = FirebaseDatabase.GetInstance(FirebaseApp.DefaultInstance, databaseUrl);
            _dbRoot = database.RootReference;
        }

        if (useFirebaseKanjiList && _dbRoot != null)
            yield return StartCoroutine(LoadCharsFromFirebase());
        else
            LoadCharsFromLocalString();

        _ready = true;

        if (_nothingDue)
        {
            ShowNothingDueMessage();
            yield break;
        }

        LoadKanji(0);
    }

    /// <summary>Shown when every character's SRS review date is still in the future.</summary>
    private void ShowNothingDueMessage()
    {
        if (characterLabel != null) { characterLabel.text = "🎉"; characterLabel.color = Color.white; }
        SetFeedback("Nothing due for review right now!");
        if (feedbackLabel != null) feedbackLabel.color = new Color(1f, 0.85f, 0.15f);
        SetScore("—");
        if (strokeProgressLabel != null) strokeProgressLabel.text = "";
    }

    // ── Character list loading ───────────────────────────────────────────────

    private void LoadCharsFromLocalString()
    {
        if (string.IsNullOrEmpty(practiceList)) practiceList = "日";
        _chars = new string[practiceList.Length];
        for (int i = 0; i < practiceList.Length; i++)
            _chars[i] = practiceList[i].ToString();
    }

    private IEnumerator LoadCharsFromFirebase()
    {
        bool kanjiDone = false;
        DataSnapshot kanjiSnap = null;

        _dbRoot.Child("kanji").Child(jlptLevel).GetValueAsync().ContinueWithOnMainThread(t =>
        {
            kanjiSnap = (t.IsFaulted || t.IsCanceled) ? null : t.Result;
            kanjiDone = true;
        });
        yield return new WaitUntil(() => kanjiDone);

        if (kanjiSnap == null || !kanjiSnap.Exists)
        {
            Debug.LogWarning("[Practice] kanji/" + jlptLevel + " not found — falling back to local Practice List.");
            LoadCharsFromLocalString();
            yield break;
        }

        var characters = new List<string>();
        foreach (var child in kanjiSnap.Children)
            characters.Add(child.Key);

        if (characters.Count == 0)
        {
            Debug.LogWarning("[Practice] kanji/" + jlptLevel + " was empty — falling back to local Practice List.");
            LoadCharsFromLocalString();
            yield break;
        }

        // Load SRS so we can order overdue/unseen characters first, same as the reading quiz.
        bool srsDone = false;
        DataSnapshot srsSnap = null;
        _dbRoot.Child("srsWriting").Child(userId).Child(jlptLevel).GetValueAsync().ContinueWithOnMainThread(t =>
        {
            srsSnap = (t.IsFaulted || t.IsCanceled) ? null : t.Result;
            srsDone = true;
        });
        yield return new WaitUntil(() => srsDone);

        _srsMap.Clear();
        if (srsSnap != null && srsSnap.Exists)
        {
            foreach (var child in srsSnap.Children)
            {
                var d = child.Value as Dictionary<string, object>;
                if (d == null) continue;
                _srsMap[child.Key] = new SrsRecord
                {
                    easeFactor = GetFloat(d, "easeFactor", EaseDefault),
                    interval = GetInt(d, "interval", 1),
                    nextReviewUnix = GetLong(d, "nextReviewUnix", 0),
                    correctStreak = GetInt(d, "correctStreak", 0),
                    totalCorrect = GetInt(d, "totalCorrect", 0),
                    totalAttempts = GetInt(d, "totalAttempts", 0)
                };
            }
        }

        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // SRS gate: same rule as the reading quiz — a character only shows up
        // if it's never been practiced, or its nextReviewUnix has already
        // passed. Anything scheduled for the future is left out entirely.
        var dueCharacters = characters
            .Where(c => !_srsMap.ContainsKey(c) || _srsMap[c].nextReviewUnix <= nowUnix)
            .OrderBy(c => _srsMap.ContainsKey(c) ? _srsMap[c].nextReviewUnix : long.MinValue)
            .ToList();

        Debug.Log($"[Practice] {dueCharacters.Count} of {characters.Count} characters due for review.");

        if (dueCharacters.Count == 0)
        {
            _chars = Array.Empty<string>();
            _nothingDue = true;
            yield break;
        }

        characters = dueCharacters;

        _chars = characters.ToArray();
        Debug.Log($"[Practice] Loaded {_chars.Length} characters from kanji/{jlptLevel}.");
    }

    /// <summary>Copies this script's arrow/dot settings onto the KanjiStrokeGraphic.</summary>
    private void ApplyArrowSettings()
    {
        if (_graphic == null) return;
        _graphic.showDirectionArrows = showDirectionArrows;
        _graphic.showGhostArrow = showGhostArrow;
        _graphic.startDotRadius = showStartDots ? Mathf.Max(_graphic.startDotRadius, 10f) : 0f;
        _graphic.arrowLength = arrowLength;
        _graphic.arrowWidth = arrowWidth;
    }

    private void OnValidate()
    {
        // Lets you tweak arrow settings in the Inspector while Play Mode is running
        if (Application.isPlaying) ApplyArrowSettings();
    }

    // ── Button methods — wire these in your Button OnClick ────────────────────

    /// <summary>Plays the stroke-order animation. Wire to your Animate button.</summary>
    public void DoAnimate()
    {
        if (!_ready) return;
        drawingBoard.PlayAnimation();
        SetFeedback("");
    }

    /// <summary>Clears drawn strokes and restarts from stroke 1. Wire to Reset button.</summary>
    public void DoReset()
    {
        if (!_ready) return;
        drawingBoard.ResetBoard();
        ResetGradingAccumulators();
        SetFeedback("");
        SetScore("—");
        RefreshProgress();
    }

    /// <summary>Loads the next kanji. Wire to Next button.</summary>
    public void DoNext()
    {
        if (!_ready || _chars == null || _chars.Length == 0) return;
        _idx = (_idx + 1) % _chars.Length;
        LoadKanji(_idx);
    }

    /// <summary>Loads the previous kanji. Wire to Previous button.</summary>
    public void DoPrev()
    {
        if (!_ready || _chars == null || _chars.Length == 0) return;
        _idx = (_idx - 1 + _chars.Length) % _chars.Length;
        LoadKanji(_idx);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnStrokeDone(float score)
    {
        bool pass = score >= drawingBoard.acceptanceThreshold;
        SetScore($"{Mathf.RoundToInt(score * 100f)}%");
        if (scoreLabel != null)
            scoreLabel.color = pass
                ? new Color(0.15f, 0.85f, 0.3f)
                : new Color(1f, 0.35f, 0.25f);
        RefreshProgress();

        // Grading: every stroke attempt (including retries) contributes to
        // this character's accuracy score.
        _strokeScoreSum += score;
        _strokeScoreCount++;
    }

    private void OnKanjiDone()
    {
        SetFeedback("Complete! ★");
        if (feedbackLabel != null) feedbackLabel.color = new Color(1f, 0.85f, 0.15f);

        GradeCurrentCharacter();

        Invoke(nameof(DoNext), 2f);
    }

    private void OnWrong()
    {
        SetFeedback("Wrong — try again");
        if (feedbackLabel != null) feedbackLabel.color = new Color(1f, 0.3f, 0.3f);
        Invoke(nameof(ClearFeedback), 1.8f);

        _wrongAttemptsForCurrent++;
    }

    // ── Grading  (writes to the same Realtime Database as the reading quiz) ──

    private void GradeCurrentCharacter()
    {
        if (_dbRoot == null || _chars == null || _idx >= _chars.Length) return;

        string character = _chars[_idx];
        float quality = _strokeScoreCount > 0 ? (_strokeScoreSum / _strokeScoreCount) : 0f;
        int percent = Mathf.RoundToInt(quality * 100f);
        bool correct = quality >= drawingBoard.acceptanceThreshold;
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 1) Append to history — one record per completed character, never overwritten.
        var historyEntry = new Dictionary<string, object>
        {
            ["studentUid"] = userId,
            ["jlptLevel"] = jlptLevel,
            ["character"] = character,
            ["percent"] = percent,
            ["wrongAttempts"] = _wrongAttemptsForCurrent,
            ["timestampUnix"] = nowUnix
        };
        DatabaseReference historyRef = _dbRoot.Child("writingHistory").Child(userId).Push();
        historyRef.SetValueAsync(historyEntry).ContinueWithOnMainThread(t =>
        {
            if (t.IsFaulted)
                Debug.LogWarning("[Grade] writingHistory write failed: " + t.Exception?.Message);
            else
                Debug.Log($"[Grade] {character} saved: {percent}% ({_wrongAttemptsForCurrent} wrong attempts)");
        });

        // 2) Roll into the running session average and push it live to
        //    grades/{jlptLevel}/{userId}/writing — no "exit" step needed here.
        _sessionCharsCompleted++;
        _sessionQualitySum += quality;
        if (correct) _sessionCorrectCount++;
        int sessionPercent = Mathf.RoundToInt(100f * _sessionQualitySum / _sessionCharsCompleted);

        _dbRoot.Child("grades").Child(jlptLevel).Child(userId).Child("writing")
               .SetValueAsync(sessionPercent)
               .ContinueWithOnMainThread(t =>
               {
                   if (t.IsFaulted)
                       Debug.LogWarning("[Grade] grades/writing write failed: " + t.Exception?.Message);
               });

        // 3) Update SRS (kept separate from the reading quiz's `srs/` node so
        //    the two skills don't overwrite each other's mastery of the same kanji).
        UpdateSrsWriting(character, correct);

        // 4) Log to the activity calendar. minutesStudied is an incremental
        //    whole-minute delta since the last log, not the full session
        //    elapsed time, so repeated per-character calls don't double-count.
        int totalWholeMinutes = Mathf.FloorToInt((Time.realtimeSinceStartup - _sessionStartRealtime) / 60f);
        int minutesDelta = Mathf.Max(0, totalWholeMinutes - _lastLoggedWholeMinutes);
        _lastLoggedWholeMinutes = totalWholeMinutes;

        ActivityCalendarWriter.LogActivity(_dbRoot, userId, "writing",
            kanjiDelta: 1, minutesDelta: minutesDelta, countSession: _sessionCharsCompleted == 1);

        // 5) Once every due character has been drawn once, this counts as one
        //    completed review — write a single summary record (like the reading
        //    quiz's readingHistory) and start a fresh tally for the next pass.
        if (_chars != null && _chars.Length > 0 && _sessionCharsCompleted >= _chars.Length)
        {
            WriteWritingSessionHistory();
            ResetSessionAccumulators();
        }
    }

    /// <summary>
    /// Writes ONE record per completed review (a full pass through every due
    /// character), so the portal can show "x / total kanji reviewed" plus the
    /// average accuracy for that review, the same way readingHistory does for
    /// the reading quiz. Never overwritten — a new push key is used each time.
    /// </summary>
    private void WriteWritingSessionHistory()
    {
        if (_dbRoot == null) return;

        int total = _sessionCharsCompleted;
        if (total == 0) return;

        int averagePercent = Mathf.RoundToInt(100f * _sessionQualitySum / total);
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var sessionEntry = new Dictionary<string, object>
        {
            ["studentUid"] = userId,
            ["jlptLevel"] = jlptLevel,
            ["correct"] = _sessionCorrectCount,
            ["total"] = total,
            ["percent"] = averagePercent,
            ["timestampUnix"] = nowUnix
        };

        DatabaseReference sessionRef = _dbRoot.Child("writingSessionHistory").Child(userId).Push();
        sessionRef.SetValueAsync(sessionEntry).ContinueWithOnMainThread(t =>
        {
            if (t.IsFaulted)
                Debug.LogWarning("[Grade] writingSessionHistory write failed: " + t.Exception?.Message);
            else
                Debug.Log($"[Grade] Writing review saved: {_sessionCorrectCount}/{total} correct, {averagePercent}% avg.");
        });
    }

    /// <summary>Starts a fresh tally for the next full pass through the due queue.</summary>
    private void ResetSessionAccumulators()
    {
        _sessionCharsCompleted = 0;
        _sessionQualitySum = 0f;
        _sessionCorrectCount = 0;
    }

    private void UpdateSrsWriting(string character, bool correct)
    {
        if (!_srsMap.ContainsKey(character)) _srsMap[character] = new SrsRecord();
        SrsRecord r = _srsMap[character];

        r.totalAttempts++;

        if (correct)
        {
            r.totalCorrect++;
            r.correctStreak++;
            r.interval = r.correctStreak switch
            {
                1 => 1,
                2 => 6,
                _ => Mathf.RoundToInt(r.interval * r.easeFactor)
            };
            r.easeFactor = Mathf.Min(r.easeFactor + 0.1f, 3.0f);
        }
        else
        {
            r.correctStreak = 0;
            r.interval = 1;
            r.easeFactor = Mathf.Max(r.easeFactor - 0.2f, EaseMin);
        }

        r.nextReviewUnix = DateTimeOffset.UtcNow.AddDays(r.interval).ToUnixTimeSeconds();

        var data = new Dictionary<string, object>
        {
            ["easeFactor"] = r.easeFactor,
            ["interval"] = r.interval,
            ["nextReviewUnix"] = r.nextReviewUnix,
            ["correctStreak"] = r.correctStreak,
            ["totalCorrect"] = r.totalCorrect,
            ["totalAttempts"] = r.totalAttempts
        };

        _dbRoot.Child("srsWriting").Child(userId).Child(jlptLevel).Child(character)
               .UpdateChildrenAsync(data)
               .ContinueWithOnMainThread(t =>
               {
                   if (t.IsFaulted)
                       Debug.LogWarning($"[SRS] writing SRS write failed for {character}: " + t.Exception?.Message);
               });
    }

    private void ResetGradingAccumulators()
    {
        _strokeScoreSum = 0f;
        _strokeScoreCount = 0;
        _wrongAttemptsForCurrent = 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void LoadKanji(int i)
    {
        if (_chars == null || _chars.Length == 0) return;
        char k = _chars[i][0];

        drawingBoard.SetTarget(k);
        ResetGradingAccumulators();

        if (characterLabel != null)
        {
            characterLabel.text = k.ToString();
            characterLabel.color = Color.white;
        }

        SetScore("—");
        if (scoreLabel != null) scoreLabel.color = new Color(0.9f, 0.85f, 0.3f);
        SetFeedback("");
        if (feedbackLabel != null) feedbackLabel.color = Color.white;
        RefreshProgress();
    }

    private void RefreshProgress()
    {
        if (strokeProgressLabel == null || drawingBoard == null) return;
        strokeProgressLabel.text =
            $"Stroke {drawingBoard.CurrentStrokeNumber} / {drawingBoard.TotalStrokes}";
    }

    private void SetFeedback(string msg) { if (feedbackLabel != null) feedbackLabel.text = msg; }
    private void ClearFeedback() => SetFeedback("");
    private void SetScore(string s) { if (scoreLabel != null) scoreLabel.text = $"Score: {s}"; }

    private static float GetFloat(Dictionary<string, object> d, string k, float def) =>
        d.ContainsKey(k) && float.TryParse(d[k]?.ToString(), out float v) ? v : def;

    private static int GetInt(Dictionary<string, object> d, string k, int def) =>
        d.ContainsKey(k) && int.TryParse(d[k]?.ToString(), out int v) ? v : def;

    private static long GetLong(Dictionary<string, object> d, string k, long def) =>
        d.ContainsKey(k) && long.TryParse(d[k]?.ToString(), out long v) ? v : def;
}
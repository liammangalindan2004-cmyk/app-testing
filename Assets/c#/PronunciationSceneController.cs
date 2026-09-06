using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using JapaneseLearning.Pronunciation;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;

/// <summary>
/// Wires a "record and check pronunciation" scene together:
/// - Press and hold the mic button to record; release it to end recording
///   and run recognition on whatever was captured
/// - Waveform bars react to live mic input while recording
/// - Result/score appears when Vosk finishes processing
/// - Every scored attempt is graded to the same Realtime Database used by
///   the reading quiz (KanjiQuiz.cs) and writing practice (KanjiPracticeUI.cs).
///
/// Attach this to an empty GameObject alongside VoskPronunciationChecker,
/// then drag your scene's UI elements into the fields below.
///
/// ═══════════════════════════════════════════════════════════
///  SESSION QUEUE  (same SRS gate as the reading quiz / writing practice)
/// ═══════════════════════════════════════════════════════════
///  • On start, all kanji under kanji/{jlptLevel} are loaded, along with the
///    student's existing srsSpeaking/{userId}/{jlptLevel} records.
///  • A pronunciation target (character + reading) is built from each
///    kanji's readings_kun/readings_on field.
///  • The session queue includes ONLY characters that are unseen or whose
///    nextReviewUnix has already passed — anything scheduled for the
///    future is left out entirely, same rule as the other two modules.
///  • After each graded attempt the scene auto-advances to the next due
///    item; when the queue is exhausted (or was empty to begin with) a
///    "nothing due" / "session complete" message is shown and the mic
///    button is disabled.
///
/// ═══════════════════════════════════════════════════════════
///  DATABASE GRADING
/// ═══════════════════════════════════════════════════════════
///  • Every completed pronunciation check (result.score, 0-100) is:
///      1) appended to speakingHistory/{userId}/{pushId} — one record per
///         attempt, never overwritten
///      2) rolled into a running session average written LIVE to
///         grades/{jlptLevel}/{userId}/speaking — matches the existing
///         grade schema (reading/speaking/writing/listening)
///      3) used to update srsSpeaking/{userId}/{jlptLevel}/{character}
///         (SM-2 spaced repetition, same algorithm/shape as the reading
///         quiz's srs/ and the writing practice's srsWriting/, kept in its
///         own node so the three skills don't overwrite each other's
///         mastery of the same character)
///  • When the whole due queue has been attempted once, ONE summary record
///    is appended to speakingSessionHistory/{userId}/{pushId} — correct /
///    total kanji reviewed for that review, the same "one instance per
///    review" shape as the reading quiz's readingHistory and the writing
///    practice's writingSessionHistory. "Correct" means the attempt scored
///    at or above Srs Pass Threshold.
///  • Set Jlpt Level / User Id under "Realtime Database Config" to the same
///    values used in the reading quiz and writing practice, so all three
///    modules feed the same student's grade record.
/// </summary>
public class PronunciationSceneController : MonoBehaviour
{
    [Header("Core")]
    public VoskPronunciationChecker checker;

    [Header("UI - Target")]
    [Tooltip("Shows the character/word the player should pronounce, e.g. 学生")]
    public TMP_Text targetCharacterText;
    [Tooltip("Optional: shows the reading in kana, e.g. がくせい")]
    public TMP_Text targetReadingText;

    [Header("UI - Mic Button")]
    public Button micButton;
    [Tooltip("Attach a MicHoldButton component to the same GameObject as micButton — this is what actually detects press/release for the hold-to-talk gesture.")]
    public MicHoldButton micHoldButton;
    public Image micIcon;
    public Color micIdleColor = new Color(0.42f, 0.24f, 0.58f); // purple, matches your circle
    public Color micRecordingColor = new Color(0.85f, 0.2f, 0.3f); // red while listening

    [Header("UI - Waveform")]
    [Tooltip("Bar RectTransforms under your waveform container, left to right")]
    public RectTransform[] waveformBars;
    public float waveformMinHeight = 6f;
    public float waveformMaxHeight = 60f;

    [Header("UI - Result")]
    public TMP_Text resultText;
    public TMP_Text scoreText;
    [Tooltip("Optional: shows 'N of M' like the reading quiz's progress label")]
    public TMP_Text progressText;

    [Header("Current Lesson Item")]
    [Tooltip("Populated automatically from the SRS-filtered session queue once it loads. You can still call SetLessonItem(...) to override manually — that bypasses the queue for that one attempt.")]
    public string currentCharacter = "";
    public string currentReadingKana = "";
    public List<string> currentLessonVocabKana = new List<string>();
    [Tooltip("Seconds to show the result before auto-advancing to the next due item.")]
    [SerializeField] private float autoAdvanceDelay = 2f;

    [Header("── Realtime Database Config ──")]
    [Tooltip("Your RTDB instance URL. Must be set explicitly because this project is NOT in the default us-central1 region.")]
    [SerializeField] private string databaseUrl = "https://unmei-nihongo-center-default-rtdb.asia-southeast1.firebasedatabase.app/";
    [Tooltip("Key under kanji/, srsSpeaking/, and grades/ — e.g. jlpt_n5, jlpt_n4, jlpt_n3, jlpt_n2.")]
    [SerializeField] private string jlptLevel = "jlpt_n5";
    [Tooltip("Used ONLY if no student is logged in via StudentSession (e.g. opening this scene directly in the Editor without going through login). Normally userId comes from whoever is actually logged in, so grades land on the right account.")]
    [SerializeField] private string debugUserIdOverride = "student_001";
    private string userId => string.IsNullOrEmpty(StudentSession.CurrentUid) ? debugUserIdOverride : StudentSession.CurrentUid;
    [Tooltip("A score at or above this (0-100) counts as a 'correct' attempt for SRS purposes.")]
    [SerializeField] private int srsPassThreshold = 70;

    // ── SRS constants (SM-2 algorithm — same as the reading quiz / writing practice) ──
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

    private class PronunciationEntry
    {
        public string character;
        public string reading;
    }

    private bool _isBusy;
    private DatabaseReference _dbRoot;
    private readonly Dictionary<string, SrsRecord> _srsMap = new Dictionary<string, SrsRecord>();

    private List<PronunciationEntry> _sessionQueue = new List<PronunciationEntry>();
    private int _sessionIndex = 0;

    // Session-wide grading accumulators (persist across attempts while this scene is open)
    private int _sessionAttemptsCompleted = 0;
    private float _sessionScoreSum = 0f;
    private int _sessionCorrectCount = 0;

    // Activity calendar bookkeeping
    private float _sessionStartRealtime;
    private int _lastLoggedWholeMinutes = 0;

    void Awake()
    {
        _sessionStartRealtime = Time.realtimeSinceStartup;

        if (micHoldButton != null)
        {
            micHoldButton.OnPressed.AddListener(OnMicButtonDown);
            micHoldButton.OnReleased.AddListener(OnMicButtonUp);
        }
        else
        {
            Debug.LogError("[Pronunciation] micHoldButton is not assigned — add a MicHoldButton " +
                            "component to the mic button's GameObject and drag it in, or the mic " +
                            "button won't respond to press/hold at all.");
        }
        checker.OnResult += HandleResult;
        checker.OnError += HandleError;
        ResetWaveform();

        resultText.text = "";
        scoreText.text = "";
        if (targetCharacterText != null) targetCharacterText.text = "…";
        if (targetReadingText != null) targetReadingText.text = "Loading…";
        micButton.interactable = false;

        StartCoroutine(InitFirebaseThenLoadQueue());
    }

    private IEnumerator InitFirebaseThenLoadQueue()
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
            Debug.LogError("[Pronunciation] Firebase unavailable: " + depStatus +
                            " — grading will not be saved, and the SRS queue can't load.");
            if (targetReadingText != null) targetReadingText.text = "Connection error";
            yield break;
        }

        // GetInstance(url) is required (not DefaultInstance) because this
        // database lives in asia-southeast1, not the default region.
        FirebaseDatabase database = FirebaseDatabase.GetInstance(FirebaseApp.DefaultInstance, databaseUrl);
        _dbRoot = database.RootReference;

        yield return StartCoroutine(LoadQueue());
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Session Queue  — same SRS gate as the reading quiz / writing practice
    // ═════════════════════════════════════════════════════════════════════

    private IEnumerator LoadQueue()
    {
        // 1) Load every kanji for this level and derive a spoken reading for each.
        bool kanjiDone = false;
        DataSnapshot kanjiSnap = null;
        _dbRoot.Child("kanji").Child(jlptLevel).GetValueAsync().ContinueWithOnMainThread(t =>
        {
            kanjiSnap = (t.IsFaulted || t.IsCanceled) ? null : t.Result;
            kanjiDone = true;
        });
        yield return new WaitUntil(() => kanjiDone);

        var allEntries = new List<PronunciationEntry>();
        if (kanjiSnap != null && kanjiSnap.Exists)
        {
            foreach (var child in kanjiSnap.Children)
            {
                var d = child.Value as Dictionary<string, object>;
                if (d == null) continue;

                string reading = ExtractPrimaryReading(d);
                if (string.IsNullOrEmpty(reading)) continue; // skip kanji with no usable reading data

                allEntries.Add(new PronunciationEntry { character = child.Key, reading = reading });
            }
        }

        if (allEntries.Count == 0)
        {
            Debug.LogWarning("[Pronunciation] kanji/" + jlptLevel + " had no usable readings.");
            ShowNothingDueMessage("No vocabulary found for " + jlptLevel + ".");
            yield break;
        }

        // 2) Load this student's existing srsSpeaking records so mastery
        //    persists across sessions instead of resetting every time this
        //    scene opens.
        bool srsDone = false;
        DataSnapshot srsSnap = null;
        _dbRoot.Child("srsSpeaking").Child(userId).Child(jlptLevel).GetValueAsync().ContinueWithOnMainThread(t =>
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

        // 3) SRS gate: only unseen characters, or ones whose nextReviewUnix
        //    has already passed, enter this session — same rule as the
        //    reading quiz and writing practice.
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _sessionQueue = allEntries
            .Where(e => !_srsMap.ContainsKey(e.character) || _srsMap[e.character].nextReviewUnix <= nowUnix)
            .OrderBy(e => _srsMap.ContainsKey(e.character) ? _srsMap[e.character].nextReviewUnix : long.MinValue)
            .ToList();

        Debug.Log($"[Pronunciation] Queue built: {_sessionQueue.Count} of {allEntries.Count} due for review.");

        if (_sessionQueue.Count == 0)
        {
            ShowNothingDueMessage("Nothing due for review right now! 🎉");
            yield break;
        }

        _sessionIndex = 0;
        micButton.interactable = true;
        LoadCurrentItem();
    }

    /// <summary>
    /// Pulls a plain spoken reading out of a kanji/ record's readings_kun /
    /// readings_on fields, e.g. "ひと.つ" or "ひと-" → "ひとつ" / "ひと".
    /// Prefers kun'yomi (more commonly how these are actually said in
    /// isolation) and falls back to on'yomi.
    /// </summary>
    private static string ExtractPrimaryReading(Dictionary<string, object> kanjiData)
    {
        string picked = FirstReading(kanjiData, "readings_kun") ?? FirstReading(kanjiData, "readings_on");
        if (string.IsNullOrEmpty(picked)) return null;

        // "." separates the kanji stem from okurigana in edict-style data
        // (e.g. "ひと.つ" is spoken "ひとつ"); "-" marks a bound/prefix form.
        return picked.Trim('-', '!').Replace(".", "");
    }

    private static string FirstReading(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out object raw) || raw == null) return null;
        if (raw is List<object> list)
        {
            foreach (var item in list)
            {
                string s = item?.ToString();
                if (!string.IsNullOrEmpty(s)) return s;
            }
        }
        return null;
    }

    private void LoadCurrentItem()
    {
        var entry = _sessionQueue[_sessionIndex];
        currentCharacter = entry.character;
        currentReadingKana = entry.reading;

        // Constrain Vosk's recognizer to this session's readings (plus the
        // current one) rather than open vocabulary — same "grammar" idea
        // VoskPronunciationChecker already supports.
        currentLessonVocabKana = _sessionQueue.Select(e => e.reading).Distinct().ToList();

        RefreshTargetDisplay();
        resultText.text = "";
        scoreText.text = "";
        if (progressText != null) progressText.text = $"{_sessionIndex + 1} of {_sessionQueue.Count}";
    }

    private void ShowNothingDueMessage(string message)
    {
        if (targetCharacterText != null) targetCharacterText.text = "🎉";
        if (targetReadingText != null) targetReadingText.text = message;
        if (progressText != null) progressText.text = "";
        resultText.text = "";
        scoreText.text = "";
        micButton.interactable = false;
    }

    private void ShowSessionCompleteMessage()
    {
        if (targetCharacterText != null) targetCharacterText.text = "✓";
        if (targetReadingText != null) targetReadingText.text = "Session complete — nice work!";
        if (progressText != null) progressText.text = $"{_sessionQueue.Count} of {_sessionQueue.Count}";
        micButton.interactable = false;
    }

    /// <summary>
    /// Writes ONE record per completed review (the whole due queue attempted
    /// once), so the portal can show "x / total kanji reviewed" for speaking —
    /// the same "one instance per review" shape as the reading quiz's
    /// readingHistory and the writing practice's writingSessionHistory.
    /// "Correct" here means the attempt scored at or above srsPassThreshold.
    /// </summary>
    private void WriteSpeakingSessionHistory()
    {
        if (_dbRoot == null) return;

        int total = _sessionQueue.Count;
        if (total == 0) return;

        int averagePercent = _sessionAttemptsCompleted > 0
            ? Mathf.RoundToInt(_sessionScoreSum / _sessionAttemptsCompleted)
            : 0;
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

        DatabaseReference sessionRef = _dbRoot.Child("speakingSessionHistory").Child(userId).Push();
        sessionRef.SetValueAsync(sessionEntry).ContinueWithOnMainThread(t =>
        {
            if (t.IsFaulted)
                Debug.LogWarning("[Grade] speakingSessionHistory write failed: " + t.Exception?.Message);
            else
                Debug.Log($"[Grade] Speaking review saved: {_sessionCorrectCount}/{total} correct ({averagePercent}% avg).");
        });
    }

    /// <summary>
    /// Manual override: call this from another script to force-show a
    /// specific character/reading for one attempt. This bypasses the SRS
    /// queue for that attempt only — the next auto-advance resumes the
    /// queue from wherever it left off.
    /// </summary>
    public void SetLessonItem(string character, string readingKana, List<string> lessonVocabKana)
    {
        currentCharacter = character;
        currentReadingKana = readingKana;
        currentLessonVocabKana = lessonVocabKana;
        RefreshTargetDisplay();
        resultText.text = "";
        scoreText.text = "";
    }

    private void RefreshTargetDisplay()
    {
        if (targetCharacterText != null) targetCharacterText.text = currentCharacter;
        if (targetReadingText != null) targetReadingText.text = currentReadingKana;
    }

    private void OnMicButtonDown()
    {
        if (_isBusy || !micButton.interactable) return;
        _isBusy = true;

        resultText.text = "Listening...";
        scoreText.text = "";
        if (micIcon != null) micIcon.color = micRecordingColor;

        checker.StartRecording(currentReadingKana, currentLessonVocabKana);
        StartCoroutine(AnimateWaveformWhileRecording());
    }

    private void OnMicButtonUp()
    {
        // Guards against a stray release with nothing actually in progress
        // (e.g. the button was disabled between press and release).
        if (!_isBusy) return;
        checker.StopRecordingAndProcess();
    }

    /// <summary>
    /// Reads live mic amplitude and scales the waveform bars accordingly.
    /// Runs for the duration of checker.maxRecordSeconds, matching StartCheck.
    /// </summary>
    private IEnumerator AnimateWaveformWhileRecording()
    {
        float elapsed = 0f;
        int sampleWindow = 128;
        float[] samples = new float[sampleWindow];

        // Unity needs a frame for Microphone.Start (called inside checker) to register
        // before checker.CurrentClip / CurrentMicDevice are populated.
        yield return null;

        AudioClip clip = checker.CurrentClip;
        string micDevice = checker.CurrentMicDevice;

        while (elapsed < checker.maxRecordSeconds && clip != null && micDevice != null && checker.IsRecording)
        {
            int micPos = Microphone.GetPosition(micDevice) - sampleWindow;
            if (micPos > 0)
            {
                clip.GetData(samples, micPos);
                float rms = 0f;
                for (int i = 0; i < samples.Length; i++)
                    rms += samples[i] * samples[i];
                rms = Mathf.Sqrt(rms / samples.Length);

                UpdateWaveformBars(rms);
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        ResetWaveform();
    }

    private void UpdateWaveformBars(float rms)
    {
        if (waveformBars == null || waveformBars.Length == 0) return;

        // Amplify since raw mic RMS is usually small; tune this multiplier by ear.
        float amplified = Mathf.Clamp01(rms * 8f);
        float targetHeight = Mathf.Lerp(waveformMinHeight, waveformMaxHeight, amplified);

        foreach (var bar in waveformBars)
        {
            if (bar == null) continue;
            // Slight per-bar jitter so it doesn't look like one flat block.
            float jitter = UnityEngine.Random.Range(0.8f, 1.05f);
            var size = bar.sizeDelta;
            size.y = Mathf.Lerp(size.y, targetHeight * jitter, 0.5f);
            bar.sizeDelta = size;
        }
    }

    private void ResetWaveform()
    {
        if (waveformBars == null) return;
        foreach (var bar in waveformBars)
        {
            if (bar == null) continue;
            var size = bar.sizeDelta;
            size.y = waveformMinHeight;
            bar.sizeDelta = size;
        }
    }

    private void HandleResult(PronunciationResult result)
    {
        _isBusy = false;
        if (micIcon != null) micIcon.color = micIdleColor;

        scoreText.text = $"{result.score}%";

        var sb = new StringBuilder();
        sb.AppendLine($"Heard: {result.recognizedText}");
        foreach (var mora in result.moraBreakdown)
            sb.Append(mora.mora).Append(mora.correct ? "✓ " : "✗ ");
        resultText.text = sb.ToString();

        GradeAttempt(result);

        if (_sessionQueue.Count > 0)
        {
            micButton.interactable = false;
            Invoke(nameof(AdvanceToNextItem), autoAdvanceDelay);
        }
    }

    /// <summary>Moves to the next due item in the queue, or shows the completion message.</summary>
    private void AdvanceToNextItem()
    {
        _sessionIndex++;
        if (_sessionIndex >= _sessionQueue.Count)
        {
            WriteSpeakingSessionHistory();
            ShowSessionCompleteMessage();
            return;
        }

        micButton.interactable = true;
        LoadCurrentItem();
    }

    // ── Grading  (writes to the same Realtime Database as the reading quiz
    //    and writing practice) ─────────────────────────────────────────────

    private void GradeAttempt(PronunciationResult result)
    {
        if (_dbRoot == null)
        {
            Debug.LogWarning("[Grade] Firebase not ready yet — this attempt's score wasn't saved.");
            return;
        }

        int percent = Mathf.Clamp(result.score, 0, 100);
        bool correct = percent >= srsPassThreshold;
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 1) Append to history — one record per attempt, nothing is overwritten.
        var historyEntry = new Dictionary<string, object>
        {
            ["studentUid"] = userId,
            ["jlptLevel"] = jlptLevel,
            ["character"] = currentCharacter,
            ["expectedReading"] = currentReadingKana,
            ["recognizedText"] = result.recognizedText,
            ["exactMatch"] = result.exactMatch,
            ["confidence"] = result.confidence,
            ["percent"] = percent,
            ["timestampUnix"] = nowUnix
        };

        DatabaseReference historyRef = _dbRoot.Child("speakingHistory").Child(userId).Push();
        historyRef.SetValueAsync(historyEntry).ContinueWithOnMainThread(t =>
        {
            if (t.IsFaulted)
                Debug.LogWarning("[Grade] speakingHistory write failed: " + t.Exception?.Message);
            else
                Debug.Log($"[Grade] {currentCharacter} saved: {percent}%");
        });

        // 2) Roll into the running session average and push it live to
        //    grades/{jlptLevel}/{userId}/speaking.
        _sessionAttemptsCompleted++;
        _sessionScoreSum += percent;
        if (correct) _sessionCorrectCount++;
        int sessionPercent = Mathf.RoundToInt(_sessionScoreSum / _sessionAttemptsCompleted);

        _dbRoot.Child("grades").Child(jlptLevel).Child(userId).Child("speaking")
               .SetValueAsync(sessionPercent)
               .ContinueWithOnMainThread(t =>
               {
                   if (t.IsFaulted)
                       Debug.LogWarning("[Grade] grades/speaking write failed: " + t.Exception?.Message);
               });

        // 3) Update SRS (kept separate from the reading/writing SRS nodes so
        //    the three skills don't overwrite each other's mastery of the
        //    same character).
        UpdateSrsSpeaking(currentCharacter, correct);

        // 4) Log to the activity calendar. minutesStudied is an incremental
        //    whole-minute delta since the last log, so repeated per-attempt
        //    calls don't double-count elapsed time.
        int totalWholeMinutes = Mathf.FloorToInt((Time.realtimeSinceStartup - _sessionStartRealtime) / 60f);
        int minutesDelta = Mathf.Max(0, totalWholeMinutes - _lastLoggedWholeMinutes);
        _lastLoggedWholeMinutes = totalWholeMinutes;

        ActivityCalendarWriter.LogActivity(_dbRoot, userId, "speaking",
            kanjiDelta: 1, minutesDelta: minutesDelta, countSession: _sessionAttemptsCompleted == 1);
    }

    private void UpdateSrsSpeaking(string character, bool correct)
    {
        if (string.IsNullOrEmpty(character)) return;

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

        _dbRoot.Child("srsSpeaking").Child(userId).Child(jlptLevel).Child(character)
               .UpdateChildrenAsync(data)
               .ContinueWithOnMainThread(t =>
               {
                   if (t.IsFaulted)
                       Debug.LogWarning($"[SRS] speaking SRS write failed for {character}: " + t.Exception?.Message);
               });
    }

    private void HandleError(string message)
    {
        _isBusy = false;
        if (micIcon != null) micIcon.color = micIdleColor;
        // Show the checker's actual message — e.g. the "hold the mic button
        // a little longer" hint for too-short presses reads very differently
        // from a genuine "couldn't understand you" recognition failure, and
        // collapsing both into one fixed string hid that distinction.
        resultText.text = string.IsNullOrEmpty(message) ? "Couldn't hear that — try again." : message;
        Debug.LogWarning(message);
    }

    void OnDestroy()
    {
        if (checker != null)
        {
            checker.OnResult -= HandleResult;
            checker.OnError -= HandleError;
        }
        if (micHoldButton != null)
        {
            micHoldButton.OnPressed.RemoveListener(OnMicButtonDown);
            micHoldButton.OnReleased.RemoveListener(OnMicButtonUp);
        }
    }

    private static float GetFloat(Dictionary<string, object> d, string k, float def) =>
        d.ContainsKey(k) && float.TryParse(d[k]?.ToString(), out float v) ? v : def;

    private static int GetInt(Dictionary<string, object> d, string k, int def) =>
        d.ContainsKey(k) && int.TryParse(d[k]?.ToString(), out int v) ? v : def;

    private static long GetLong(Dictionary<string, object> d, string k, long def) =>
        d.ContainsKey(k) && long.TryParse(d[k]?.ToString(), out long v) ? v : def;
}

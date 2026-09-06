using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════
///  KanjiQuiz  —  AI-assisted READING-focused Kanji Quiz with Spaced Repetition
///  (Firebase REALTIME DATABASE edition)
/// ═══════════════════════════════════════════════════════════════════════════
///
///  WHAT CHANGED FROM THE FIRESTORE VERSION
///  ─────────────────────────────────────────
///  • Uses Firebase.Database (Realtime Database), NOT Firebase.Firestore,
///    because the project's DB is a Realtime Database instance
///    (unmei-nihongo-center-default-rtdb, asia-southeast1 region).
///  • Only ONE quiz style remains: Reading Check — a short Japanese sentence
///    with the target kanji highlighted in 【】, asking the player to choose
///    its correct meaning. (The old Meaning / Sentence styles were removed
///    per project decision to keep this reading-focused.)
///  • Grading now writes to TWO places on quiz completion:
///      1) readingHistory/{studentUid}/{pushId}  — one record per attempt,
///         so history is never overwritten.
///      2) grades/{jlptLevel}/{studentUid}/reading — updated to the most
///         recent percentage, matching the existing grade schema so other
///         screens (progress dashboards, etc.) keep working.
///
///  HOW IT WORKS
///  ─────────────
///  1. On start, all kanji under kanji/{jlptLevel} are loaded from the RTDB.
///     Existing SRS records for the user (srs/{userId}/{jlptLevel}) are also
///     fetched.
///  2. A session queue is built ordered by SRS priority (overdue / unseen first).
///  3. For each question the AI generates a short Japanese sentence with the
///     target kanji bracketed, e.g. 今日は【日】が照っている。 While the AI is
///     thinking a spinner is shown so the player isn't blocked on a blank screen.
///  4. The 4 answer choices (English meanings) are assembled LOCALLY from the
///     kanji pool — no AI needed — so the quiz is still fully playable if the
///     AI is slow or down.
///  5. After the player taps, colour feedback is shown and the question
///     auto-advances after `autoAdvanceDelay` seconds.
///  6. SRS (SM-2) is updated per answer and written to the RTDB async.
///  7. On quiz completion, the session is graded and written to the database
///     (see WriteQuizResult). The Exit button preserves all SRS + grade data.
///
///  SCENE HIERARCHY  (drag into Inspector — unchanged from before)
///  ───────────────────────────────────────
///  ROOT CANVAS
///  └─ QuizPanel
///     ├─ JlptBadgeTMP       top-right  "N5"
///     ├─ QuizTypeTMP        top-centre "Reading Check"
///     ├─ ExitButton         top-left   "✕"  (preserves SRS — does NOT reset score)
///     ├─ QuestionTMP        question text (also shows spinner while AI thinks)
///     ├─ ChoiceButtonA      Button → child TextMeshProUGUI
///     ├─ ChoiceButtonB
///     ├─ ChoiceButtonC
///     ├─ ChoiceButtonD
///     └─ ProgressTMP        bottom "3 of 15"
///
///  REALTIME DATABASE PATHS
///     kanji/{jlptLevel}/{character}
///       Fields read: freq, grade, jlpt_new, jlpt_old, meanings (list)
///     srs/{userId}/{jlptLevel}/{character}
///       Fields: easeFactor, interval, nextReviewUnix,
///               correctStreak, totalCorrect, totalAttempts
///     readingHistory/{userId}/{pushId}   (new — one entry per completed quiz)
///       Fields: jlptLevel, correct, total, percent, timestampUnix
///     grades/{jlptLevel}/{userId}/reading   (existing schema — overwritten
///       with the latest percent so dashboards reading this field stay current)
///
///  SETUP REQUIRED IN UNITY
///  ─────────────────────────
///  1. Import the "Firebase Realtime Database" component of the Firebase
///     Unity SDK (this is a SEPARATE package from Firestore — if your project
///     only has FirebaseFirestore.dll / Firebase.Firestore.dll you need to
///     add FirebaseDatabase.dll too, via the Firebase SDK .unitypackage or
///     the Firebase Unity SDK's Package Manager entry).
///   2. Make sure google-services.json / GoogleService-Info.plist (or your
///     Firebase config) is present — GetInstance(url) below still needs a
///     valid FirebaseApp to attach to.
///   3. In the Firebase console, enable Realtime Database (already done,
///     since you have data in it) and set Database Rules that allow the
///     authenticated user to read kanji/* and read/write their own
///     srs/{uid}/*, readingHistory/{uid}/*, and grades/*/{uid}/reading.
/// ═══════════════════════════════════════════════════════════════════════════
/// </summary>
public class KanjiQuiz : MonoBehaviour
{
    // ── UI ────────────────────────────────────────────────────────────────────
    [Header("UI – Labels")]
    public TextMeshProUGUI jlptBadgeTMP;
    public TextMeshProUGUI quizTypeTMP;
    public TextMeshProUGUI questionTMP;
    public TextMeshProUGUI progressTMP;

    [Header("UI – Choice Buttons (A-D)")]
    public Button choiceButtonA;
    public Button choiceButtonB;
    public Button choiceButtonC;
    public Button choiceButtonD;

    [Header("UI – Navigation")]
    public Button exitButton;          // does NOT reset score

    // ── Colors ────────────────────────────────────────────────────────────────
    [Header("Button Colors")]
    public Color defaultColor = new Color(0.50f, 0.25f, 0.75f, 1f);
    public Color correctColor = new Color(0.18f, 0.72f, 0.42f, 1f);
    public Color wrongColor = new Color(0.85f, 0.22f, 0.22f, 1f);
    [Tooltip("Color the target kanji is shown in within the question sentence.")]
    public Color highlightColor = new Color(0.90f, 0.15f, 0.15f, 1f);

    // ── Realtime Database ─────────────────────────────────────────────────────
    [Header("Realtime Database Config")]
    [Tooltip("Your RTDB instance URL. Must be set explicitly because this project is NOT in the default us-central1 region.")]
    [SerializeField] private string databaseUrl = "https://unmei-nihongo-center-default-rtdb.asia-southeast1.firebasedatabase.app/";
    [Tooltip("Key under kanji/ and srs/ — e.g. jlpt_n5, jlpt_n4, jlpt_n3, jlpt_n2.")]
    [SerializeField] private string jlptLevel = "jlpt_n5";
    [Tooltip("Used ONLY if no student is logged in via StudentSession (e.g. opening this scene directly in the Editor without going through login). Normally userId comes from whoever is actually logged in, so grades land on the right account.")]
    [SerializeField] private string debugUserIdOverride = "student_001";
    private string userId => string.IsNullOrEmpty(StudentSession.CurrentUid) ? debugUserIdOverride : StudentSession.CurrentUid;

    // ── AI ────────────────────────────────────────────────────────────────────
    [Header("AI Config")]
    [SerializeField] private string ngrokUrl = "https://nondynastic-nonritualistically-carlie.ngrok-free.dev";
    [SerializeField] private string modelName = "qwen/qwen3-4b-2507";
    [Tooltip("Seconds before giving up on AI and using fallback question text.")]
    [SerializeField] private float aiTimeout = 8f;

    // ── Timing ────────────────────────────────────────────────────────────────
    [Header("Timing")]
    [Tooltip("Seconds to show the answer colour before auto-advancing.")]
    [SerializeField] private float autoAdvanceDelay = 1.6f;

    // ── Scene ─────────────────────────────────────────────────────────────────
    [Header("Scene Config")]
    [SerializeField] private string exitSceneName = "MainMenu";

    // ── SRS constants (SM-2 algorithm) ───────────────────────────────────────
    private const float EaseDefault = 2.5f;
    private const float EaseMin = 1.3f;

    // ─────────────────────────────────────────────────────────────────────────
    //  Internal data types
    // ─────────────────────────────────────────────────────────────────────────

    private class KanjiEntry
    {
        public string docId;       // the kanji character itself, e.g. "日"
        public string character;   // "日"
        public string meaning;     // "sun, day"
    }

    private class SrsRecord
    {
        public float easeFactor = EaseDefault;
        public int interval = 1;
        public long nextReviewUnix = 0;
        public int correctStreak = 0;
        public int totalCorrect = 0;
        public int totalAttempts = 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Runtime state
    // ─────────────────────────────────────────────────────────────────────────

    private List<KanjiEntry> allKanji = new();
    private Dictionary<string, SrsRecord> srsMap = new();

    private List<KanjiEntry> sessionQueue = new();
    private int sessionIndex = 0;
    private int sessionCorrect = 0;

    // Per-question
    private KanjiEntry currentEntry;
    private string correctAnswer = "";
    private bool hasAnswered = false;

    private Button[] choiceButtons;
    private TextMeshProUGUI[] choiceLabels;
    private DatabaseReference dbRoot;
    private string apiUrl;

    private Coroutine spinnerCoroutine;
    private float _quizStartRealtime;

    // ═════════════════════════════════════════════════════════════════════════
    //  Unity Lifecycle
    // ═════════════════════════════════════════════════════════════════════════

    void Start()
    {
        apiUrl = ngrokUrl.TrimEnd('/') + "/api/v1/chat";
        _quizStartRealtime = Time.realtimeSinceStartup;

        choiceButtons = new[] { choiceButtonA, choiceButtonB, choiceButtonC, choiceButtonD };
        choiceLabels = new TextMeshProUGUI[]
        {
            choiceButtonA.GetComponentInChildren<TextMeshProUGUI>(),
            choiceButtonB.GetComponentInChildren<TextMeshProUGUI>(),
            choiceButtonC.GetComponentInChildren<TextMeshProUGUI>(),
            choiceButtonD.GetComponentInChildren<TextMeshProUGUI>()
        };

        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            choiceButtons[i].onClick.AddListener(() => OnChoiceSelected(idx));
        }

        if (exitButton != null)
            exitButton.onClick.AddListener(OnExitPressed);

        if (quizTypeTMP != null) quizTypeTMP.text = "Reading Check";

        ShowSpinner("Connecting…");
        DisableChoiceButtons();

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result != DependencyStatus.Available)
            {
                Debug.LogError("[Quiz] Firebase unavailable: " + task.Result);
                StopSpinner("Firebase error.");
                return;
            }

            // GetInstance(url) is required (not DefaultInstance) because this
            // database lives in asia-southeast1, not the default region.
            FirebaseDatabase database = FirebaseDatabase.GetInstance(FirebaseApp.DefaultInstance, databaseUrl);
            dbRoot = database.RootReference;
            StartCoroutine(LoadAllKanji());
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Data Loading
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator LoadAllKanji()
    {
        ShowSpinner("Loading kanji…");

        bool done = false;
        Exception loadError = null;
        DataSnapshot snap = null;

        dbRoot.Child("kanji").Child(jlptLevel)
              .GetValueAsync()
              .ContinueWithOnMainThread(t =>
              {
                  if (t.IsFaulted || t.IsCanceled) loadError = t.Exception;
                  else snap = t.Result;
                  done = true;
              });

        yield return new WaitUntil(() => done);

        if (loadError != null || snap == null || !snap.Exists)
        {
            Debug.LogError("[Quiz] Kanji load failed: " + loadError);
            StopSpinner("DB error — check connection or jlptLevel key.");
            yield break;
        }

        allKanji.Clear();
        foreach (var child in snap.Children)
        {
            var d = child.Value as Dictionary<string, object>;
            if (d == null) continue;

            allKanji.Add(new KanjiEntry
            {
                docId = child.Key,
                character = child.Key,
                meaning = ReadFirstMeaning(d, "meanings", "meaning") ?? "unknown"
            });
        }

        Debug.Log($"[Quiz] Loaded {allKanji.Count} kanji from kanji/{jlptLevel}.");

        if (allKanji.Count < 4)
        {
            StopSpinner($"Need at least 4 kanji (found {allKanji.Count}).");
            yield break;
        }

        yield return StartCoroutine(LoadSrsRecords());
    }

    private IEnumerator LoadSrsRecords()
    {
        ShowSpinner("Loading your progress…");

        bool done = false;
        DataSnapshot snap = null;

        dbRoot.Child("srs").Child(userId).Child(jlptLevel)
              .GetValueAsync()
              .ContinueWithOnMainThread(t =>
              {
                  snap = (t.IsFaulted || t.IsCanceled) ? null : t.Result;
                  done = true;
              });

        yield return new WaitUntil(() => done);

        srsMap.Clear();
        if (snap != null && snap.Exists)
        {
            foreach (var child in snap.Children)
            {
                var d = child.Value as Dictionary<string, object>;
                if (d == null) continue;

                srsMap[child.Key] = new SrsRecord
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
        else
        {
            Debug.Log("[Quiz] No SRS records yet for this user — starting fresh.");
        }

        BuildSessionQueue();

        if (sessionQueue.Count == 0)
        {
            ShowNothingDueMessage();
            yield break;
        }

        yield return StartCoroutine(LoadAndShowQuestion());
    }

    /// <summary>Shown when every kanji's SRS review date is still in the future.</summary>
    private void ShowNothingDueMessage()
    {
        StopSpinner();
        if (questionTMP != null) questionTMP.text = "Nothing due for review right now! 🎉\nCheck back later.";
        if (progressTMP != null) progressTMP.text = "0 of 0";
        DisableChoiceButtons();
        for (int i = 0; i < 4; i++) if (choiceLabels[i] != null) choiceLabels[i].text = "";
        StartCoroutine(ExitAfterDelay(3f));
    }

    private IEnumerator ExitAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        SceneManager.LoadScene(exitSceneName);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Session Queue  — ordered by SRS due date (overdue / unseen first)
    // ═════════════════════════════════════════════════════════════════════════

    private void BuildSessionQueue()
    {
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // SRS gate: a kanji only enters this session if it's never been
        // reviewed, or its nextReviewUnix has already passed. Anything
        // scheduled for the future is left out entirely (not just
        // deprioritized), so the player only ever sees what's actually due.
        sessionQueue = allKanji
            .Where(k => !srsMap.ContainsKey(k.docId) || srsMap[k.docId].nextReviewUnix <= nowUnix)
            .OrderBy(k => srsMap.ContainsKey(k.docId)
                ? srsMap[k.docId].nextReviewUnix
                : long.MinValue)
            .ToList();

        sessionIndex = 0;
        sessionCorrect = 0;
        Debug.Log($"[Quiz] Queue built: {sessionQueue.Count} of {allKanji.Count} due for review.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Question Loading
    //  • Choices are built FIRST from local data (instant)
    //  • Then the AI is asked for a contextual reading sentence (with timeout)
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator LoadAndShowQuestion()
    {
        if (sessionIndex >= sessionQueue.Count)
        {
            yield return StartCoroutine(ShowSummaryThenExit());
            yield break;
        }

        currentEntry = sessionQueue[sessionIndex];
        hasAnswered = false;
        ResetButtonColors();
        DisableChoiceButtons();

        // Update static header UI immediately (no waiting on AI)
        if (jlptBadgeTMP != null) jlptBadgeTMP.text = jlptLevel.Replace("jlpt_", "").ToUpperInvariant();
        if (progressTMP != null) progressTMP.text = $"{sessionIndex + 1} of {sessionQueue.Count}";

        // Build the 4 choices locally — instant, no AI required
        BuildChoices(currentEntry);

        // Ask AI for a contextual reading sentence (with spinner + timeout)
        ShowSpinner("Generating question…");

        string aiQuestion = null;
        bool aiDone = false;
        float elapsed = 0f;

        StartCoroutine(AskAIForQuestion(currentEntry, result =>
        {
            aiQuestion = result;
            aiDone = true;
        }));

        while (!aiDone && elapsed < aiTimeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        StopSpinner();

        if (!string.IsNullOrWhiteSpace(aiQuestion))
            questionTMP.text = HighlightBrackets(aiQuestion.Trim());
        else
        {
            Debug.LogWarning("[Quiz] AI timed out or returned empty — using fallback.");
            questionTMP.text = FallbackQuestion(currentEntry);
        }

        EnableChoiceButtons();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  AI — Reading Question Generation
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calls the LLM and expects a SINGLE reading-comprehension sentence back.
    /// The prompt strictly forbids answer choices — those come from local data.
    /// </summary>
    private IEnumerator AskAIForQuestion(KanjiEntry entry, Action<string> onResult)
    {
        string prompt = BuildAIPrompt(entry);
        yield return StartCoroutine(PostToAI(prompt, onResult));
    }

    private string BuildAIPrompt(KanjiEntry entry)
    {
        return
            "You are a Japanese reading-comprehension quiz assistant for a mobile app. " +
            $"Kanji: {entry.character} | Meaning: {entry.meaning}. " +
            "Reply with ONE question string ONLY. " +
            "Do NOT include answer choices, labels (A/B/C/D), explanations, " +
            "markdown, or more than one line. Keep it under 30 words total. " +
            $"Write a short, natural Japanese sentence that uses 「{entry.character}」, " +
            $"and highlight it by wrapping it in 【】brackets, like 【{entry.character}】. " +
            "Then ask in English: What does the highlighted word mean? " +
            $"Example: 今日は【{entry.character}】が照っている。What does the highlighted word mean?";
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Choices — built locally from the kanji pool (no AI, instant)
    // ═════════════════════════════════════════════════════════════════════════

    private void BuildChoices(KanjiEntry entry)
    {
        // Player always picks the correct ENGLISH MEANING (reading comprehension)
        correctAnswer = entry.meaning;
        List<string> distractors = PickDistinct(
            e => e.meaning, e => e.meaning != entry.meaning, 3);

        var options = new List<string> { correctAnswer };
        options.AddRange(distractors);
        Shuffle(options);

        for (int i = 0; i < 4; i++)
            if (choiceLabels[i] != null)
                choiceLabels[i].text = $"  {options[i]}";
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Answer Handling
    // ═════════════════════════════════════════════════════════════════════════

    private void OnChoiceSelected(int idx)
    {
        if (hasAnswered) return;
        hasAnswered = true;
        DisableChoiceButtons();

        string raw = choiceLabels[idx].text;
        string selected = raw.Trim();
        bool correct = string.Equals(selected, correctAnswer,
                                         StringComparison.OrdinalIgnoreCase);

        ColorButton(idx, correct ? correctColor : wrongColor);

        if (!correct)
        {
            // Highlight the correct choice green
            for (int i = 0; i < 4; i++)
            {
                string btn = choiceLabels[i].text.Trim();
                if (string.Equals(btn, correctAnswer, StringComparison.OrdinalIgnoreCase))
                { ColorButton(i, correctColor); break; }
            }
        }

        if (correct) sessionCorrect++;

        // Persist SRS (fire-and-forget)
        UpdateSrs(currentEntry.docId, correct);

        Debug.Log($"[Quiz] {currentEntry.character} | " +
                  $"{(correct ? "✓" : "✗")} | Session: {sessionCorrect}/{sessionIndex + 1}");

        StartCoroutine(AutoAdvance());
    }

    private IEnumerator AutoAdvance()
    {
        yield return new WaitForSeconds(autoAdvanceDelay);
        sessionIndex++;
        yield return StartCoroutine(LoadAndShowQuestion());
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Fallback Question  (shown when AI times out or returns nothing)
    // ═════════════════════════════════════════════════════════════════════════

    private string FallbackQuestion(KanjiEntry e) =>
        $"What does 「{HighlightBrackets("【" + e.character + "】")}」 mean in Japanese?";

    /// <summary>
    /// The AI is instructed to wrap the target word in 【】 brackets; this
    /// swaps those brackets for an actual colored TMP rich-text span so the
    /// kanji is visually highlighted in the sentence instead of just being
    /// bracketed. Requires "Rich Text" enabled on questionTMP (TMP default).
    /// </summary>
    private string HighlightBrackets(string text)
    {
        string hex = ColorUtility.ToHtmlStringRGB(highlightColor);
        return text.Replace("【", $"<color=#{hex}>").Replace("】", "</color>");
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Spaced Repetition  (SM-2 variant)  →  srs/{userId}/{jlptLevel}/{docId}
    // ═════════════════════════════════════════════════════════════════════════

    private void UpdateSrs(string docId, bool correct)
    {
        if (!srsMap.ContainsKey(docId)) srsMap[docId] = new SrsRecord();
        SrsRecord r = srsMap[docId];

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

        dbRoot.Child("srs").Child(userId).Child(jlptLevel).Child(docId)
              .UpdateChildrenAsync(data)
              .ContinueWithOnMainThread(t =>
              {
                  if (t.IsFaulted)
                      Debug.LogWarning($"[SRS] Write failed for {docId}: " + t.Exception?.Message);
              });
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Grading  — writes the completed quiz to the database
    //     1) readingHistory/{userId}/{pushId}   — full attempt log, never overwritten
    //     2) grades/{jlptLevel}/{userId}/reading — latest percent (existing schema)
    // ═════════════════════════════════════════════════════════════════════════

    private void WriteQuizResult()
    {
        int total = sessionQueue.Count;
        if (total == 0) return;

        int percent = Mathf.RoundToInt(100f * sessionCorrect / total);
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 1) Append to history — one record per attempt, nothing is overwritten.
        var historyEntry = new Dictionary<string, object>
        {
            ["studentUid"] = userId,
            ["jlptLevel"] = jlptLevel,
            ["correct"] = sessionCorrect,
            ["total"] = total,
            ["percent"] = percent,
            ["timestampUnix"] = nowUnix
        };

        DatabaseReference historyRef = dbRoot.Child("readingHistory").Child(userId).Push();
        historyRef.SetValueAsync(historyEntry).ContinueWithOnMainThread(t =>
        {
            if (t.IsFaulted)
                Debug.LogWarning("[Grade] History write failed: " + t.Exception?.Message);
            else
                Debug.Log($"[Grade] History saved: {historyRef.Key} — {sessionCorrect}/{total} ({percent}%)");
        });

        // 2) Update the existing grades/{jlptLevel}/{userId}/reading field so
        //    dashboards / other screens reading that schema stay current.
        dbRoot.Child("grades").Child(jlptLevel).Child(userId).Child("reading")
              .SetValueAsync(percent)
              .ContinueWithOnMainThread(t =>
              {
                  if (t.IsFaulted)
                      Debug.LogWarning("[Grade] grades/reading write failed: " + t.Exception?.Message);
              });

        // 3) Log this session to the activity calendar so it shows up on the
        //    student's streak/calendar UI too, not just the reading grade.
        int minutesStudied = Mathf.Max(1, Mathf.RoundToInt((Time.realtimeSinceStartup - _quizStartRealtime) / 60f));
        ActivityCalendarWriter.LogActivity(dbRoot, userId, "reading",
            kanjiDelta: total, minutesDelta: minutesStudied, countSession: true);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Exit  — SRS already saved per question; just navigate away
    // ═════════════════════════════════════════════════════════════════════════

    private void OnExitPressed()
    {
        Debug.Log($"[Quiz] Exit — {sessionCorrect}/{sessionIndex} correct. SRS saved.");
        SceneManager.LoadScene("landing");
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Summary Screen
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator ShowSummaryThenExit()
    {
        StopSpinner();
        int total = sessionQueue.Count;

        WriteQuizResult();

        if (questionTMP != null) questionTMP.text = $"Quiz Complete!\n{sessionCorrect} / {total} correct 🎉";
        if (progressTMP != null) progressTMP.text = $"{total} of {total}";
        DisableChoiceButtons();
        for (int i = 0; i < 4; i++) if (choiceLabels[i] != null) choiceLabels[i].text = "";
        yield return new WaitForSeconds(3f);
        SceneManager.LoadScene(exitSceneName);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Spinner  (animated dots while waiting for AI)
    // ═════════════════════════════════════════════════════════════════════════

    private void ShowSpinner(string prefix = "")
    {
        StopSpinner();
        spinnerCoroutine = StartCoroutine(SpinnerLoop(prefix));
    }

    private void StopSpinner(string finalText = null)
    {
        if (spinnerCoroutine != null) { StopCoroutine(spinnerCoroutine); spinnerCoroutine = null; }
        if (finalText != null && questionTMP != null) questionTMP.text = finalText;
    }

    private IEnumerator SpinnerLoop(string prefix)
    {
        string[] frames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
        int f = 0;
        while (true)
        {
            if (questionTMP != null)
                questionTMP.text = string.IsNullOrEmpty(prefix) ? frames[f] : $"{prefix} {frames[f]}";
            f = (f + 1) % frames.Length;
            yield return new WaitForSeconds(0.1f);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  UI Helpers
    // ═════════════════════════════════════════════════════════════════════════

    private void EnableChoiceButtons() { foreach (var b in choiceButtons) if (b) b.interactable = true; }
    private void DisableChoiceButtons() { foreach (var b in choiceButtons) if (b) b.interactable = false; }
    private void ResetButtonColors() { foreach (var b in choiceButtons) if (b) SetButtonColor(b, defaultColor); }

    private void ColorButton(int idx, Color c) => SetButtonColor(choiceButtons[idx], c);

    private void SetButtonColor(Button btn, Color c)
    {
        var cols = btn.colors;
        cols.normalColor = cols.highlightedColor = cols.selectedColor = c;
        cols.pressedColor = c * 0.85f;
        btn.colors = cols;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  AI HTTP POST
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator PostToAI(string prompt, Action<string> onResult)
    {
        string safe = prompt
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "");

        string json = $"{{\"model\":\"{modelName}\",\"input\":\"{safe}\"}}";

        var req = new UnityWebRequest(apiUrl, "POST");
        req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = Mathf.CeilToInt(aiTimeout) + 2;

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            var resp = JsonUtility.FromJson<LMResponseQuiz>(req.downloadHandler.text);
            string content = (resp?.output?.Length > 0) ? resp.output[0].content ?? "" : "";
            content = content.Replace("```", "").Trim();
            onResult?.Invoke(content);
        }
        else
        {
            Debug.LogError("[AI] Request failed: " + req.error);
            onResult?.Invoke(null);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Utility
    // ═════════════════════════════════════════════════════════════════════════

    private List<string> PickDistinct(
        Func<KanjiEntry, string> selector,
        Func<KanjiEntry, bool> filter,
        int count)
    {
        var pool = allKanji.Where(filter).Select(selector).Distinct().ToList();
        Shuffle(pool);
        return pool.Take(Mathf.Min(count, pool.Count)).ToList();
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// Reads a field from an RTDB node dictionary. Handles plain strings,
    /// arrays (e.g. meanings: ["sun","day"]) and nested maps.
    /// </summary>
    private string ReadField(Dictionary<string, object> data, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!data.ContainsKey(key) || data[key] == null) continue;
            object raw = data[key];

            if (raw is List<object> list)
            {
                string joined = string.Join(", ", list.Where(x => x != null).Select(x => x.ToString()));
                if (!string.IsNullOrEmpty(joined)) return joined;
                continue;
            }

            if (raw is Dictionary<string, object> map)
            {
                var parts = new List<string>();
                for (int i = 0; i < map.Count; i++)
                    if (map.TryGetValue(i.ToString(), out object v) && v != null)
                        parts.Add(v.ToString());
                string joined = string.Join(", ", parts);
                if (!string.IsNullOrEmpty(joined)) return joined;
                continue;
            }

            string str = raw.ToString().Trim();
            if (!string.IsNullOrEmpty(str)) return str;
        }
        return null;
    }

    private static float GetFloat(Dictionary<string, object> d, string k, float def) =>
        d.ContainsKey(k) && float.TryParse(d[k]?.ToString(), out float v) ? v : def;

    private static int GetInt(Dictionary<string, object> d, string k, int def) =>
        d.ContainsKey(k) && int.TryParse(d[k]?.ToString(), out int v) ? v : def;

    private static long GetLong(Dictionary<string, object> d, string k, long def) =>
        d.ContainsKey(k) && long.TryParse(d[k]?.ToString(), out long v) ? v : def;

    /// <summary>
    /// Returns exactly ONE meaning for a kanji, instead of ReadField's
    /// behavior of joining every synonym together (e.g. "Middle, Center,
    /// In"). Each answer choice should read as a single definition, not a
    /// comma-separated pile of them.
    /// </summary>
    private static string ReadFirstMeaning(Dictionary<string, object> data, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!data.ContainsKey(key) || data[key] == null) continue;
            object raw = data[key];

            if (raw is List<object> list)
            {
                foreach (var item in list)
                {
                    string s = item?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                continue;
            }

            if (raw is Dictionary<string, object> map)
            {
                for (int i = 0; i < map.Count; i++)
                {
                    if (map.TryGetValue(i.ToString(), out object v) && v != null)
                    {
                        string s = v.ToString().Trim();
                        if (!string.IsNullOrEmpty(s)) return s;
                    }
                }
                continue;
            }

            // Plain string — may itself be comma-separated (e.g. "Middle, In");
            // take only the first segment.
            string str = raw.ToString().Trim();
            if (!string.IsNullOrEmpty(str))
            {
                int commaIdx = str.IndexOf(',');
                return commaIdx >= 0 ? str.Substring(0, commaIdx).Trim() : str;
            }
        }
        return null;
    }
}

// ── JSON serialization helpers ────────────────────────────────────────────────

[System.Serializable]
public class LMOutputQuiz
{
    public string type;
    public string content;
}

[System.Serializable]
public class LMResponseQuiz
{
    public LMOutputQuiz[] output;
}
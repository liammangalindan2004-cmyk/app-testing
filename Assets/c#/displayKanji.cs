using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;

/// <summary>
/// Displays kanji from Firebase Realtime Database (kanji/{jlptLevel}/{character}).
/// Clicking the SVG graphic plays the stroke-order animation.
/// After animation finishes the full static kanji is restored.
///
/// ═══════════════════════════════════════════════════════════
///  SCENE SETUP
/// ═══════════════════════════════════════════════════════════
///
///  1. INFO LABELS
///     • kanjiTMP        – large kanji character label
///     • meaningTMP      – meanings
///     • pronuncTMP      – readings (kun shown first, then on)
///     • aiOutputTMP     – AI example sentences
///     • wordsOutputTMP  – AI compound words
///     • nextButton      – loads next kanji
///
///  2. SVG GRAPHIC (always visible, click to animate)
///     • Create child GameObject "KanjiGraphic"
///     • Add KanjiStrokeGraphic component
///     • Drag it into the "kanjiGraphic" field below
///     • Clicking plays the stroke animation; after it finishes
///       the full static kanji is shown again automatically.
///
///  NOTE ON REALTIME DATABASE DATA FORMAT
///     Data is stored as kanji/{jlptLevel}/{character} with fields
///     like meanings (list/array), reading, etc.
///     ReadField() handles arrays, maps, and comma-separated strings.
/// ═══════════════════════════════════════════════════════════
/// </summary>
public class KanjiDisplayWithMeaning : MonoBehaviour
{
    // ── UI: Info panel ────────────────────────────────────────────────────────
    [Header("Info Panel")]
    public TextMeshProUGUI kanjiTMP;
    public TextMeshProUGUI meaningTMP;
    public TextMeshProUGUI pronuncTMP;
    public TextMeshProUGUI aiOutputTMP;
    public TextMeshProUGUI wordsOutputTMP;
    public Button nextButton;

    // ── UI: SVG Graphic ───────────────────────────────────────────────────────
    [Header("SVG Graphic (click to animate)")]
    [Tooltip("GameObject with KanjiStrokeGraphic — always visible, click animates strokes")]
    public KanjiStrokeGraphic kanjiGraphic;

    // ── Realtime Database ─────────────────────────────────────────────────────
    [Header("Realtime Database Config")]
    [Tooltip("Your RTDB instance URL")]
    [SerializeField] private string databaseUrl = "https://unmei-nihongo-center-default-rtdb.asia-southeast1.firebasedatabase.app/";
    [Tooltip("JLPT level under kanji/ (e.g. jlpt_n5, jlpt_n4, jlpt_n3, jlpt_n2)")]
    [SerializeField] private string jlptLevel = "jlpt_n5";

    // ── AI ────────────────────────────────────────────────────────────────────
    [Header("AI Config")]
    public string ngrokUrl = "https://nondynastic-nonritualistically-carlie.ngrok-free.dev";
    [SerializeField] private string modelName = "qwen/qwen3-4b-2507";

    // ── Settings ──────────────────────────────────────────────────────────────
    [Header("Settings")]
    [SerializeField] private bool loadOnStart = true;

    // ── Private state ─────────────────────────────────────────────────────────
    private int currentDocIndex = 1;
    private string apiUrl;
    private DatabaseReference dbRoot;
    private List<string> kanjiList = new List<string>();
    private char currentKanjiChar = '一';
    private bool isAnimating = false;

    // ═════════════════════════════════════════════════════════════════════════
    //  Unity Lifecycle
    // ═════════════════════════════════════════════════════════════════════════

    void Start()
    {
        apiUrl = ngrokUrl.TrimEnd('/') + "/api/v1/chat";

        // Disable navigation until Firebase is ready so the button
        // can never call LoadKanjiDocument while db is still null.
        nextButton.interactable = false;
        nextButton.onClick.AddListener(ShowNextKanji);

        SetupGraphicClickHandler();

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                FirebaseDatabase database = FirebaseDatabase.GetInstance(FirebaseApp.DefaultInstance, databaseUrl);
                dbRoot = database.RootReference;
                Debug.Log($"[Kanji] Realtime Database ready. JLPT Level: {jlptLevel}");
                nextButton.interactable = true;
                if (loadOnStart) StartCoroutine(LoadKanjiList());
            }
            else
            {
                Debug.LogError("[Kanji] Firebase init failed: " + task.Result);
                SetErrorState("Firebase unavailable");
            }
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Navigation
    // ═════════════════════════════════════════════════════════════════════════

    void ShowNextKanji()
    {
        currentDocIndex++;
        if (currentDocIndex >= kanjiList.Count) currentDocIndex = 0;
        LoadKanjiDocument(currentDocIndex);
    }

    IEnumerator LoadKanjiList()
    {
        SetLoadingState();

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
            Debug.LogError("[Kanji] Kanji list load failed: " + loadError);
            SetErrorState("DB error");
            yield break;
        }

        kanjiList.Clear();
        foreach (var child in snap.Children)
        {
            kanjiList.Add(child.Key);
        }

        Debug.Log($"[Kanji] Loaded {kanjiList.Count} kanji from kanji/{jlptLevel}.");

        if (kanjiList.Count > 0)
        {
            LoadKanjiDocument(currentDocIndex);
        }
        else
        {
            SetErrorState("No kanji found");
        }
    }

    void LoadKanjiDocument(int index)
    {
        if (dbRoot == null || kanjiList.Count == 0)
        {
            Debug.LogWarning("[Kanji] LoadKanjiDocument called before database was ready — ignoring.");
            return;
        }

        if (index < 0 || index >= kanjiList.Count)
        {
            index = 0;
        }

        string character = kanjiList[index];
        Debug.Log($"[Kanji] Loading: kanji/{jlptLevel}/{character}");
        SetLoadingState();

        dbRoot.Child("kanji").Child(jlptLevel).Child(character)
              .GetValueAsync()
              .ContinueWithOnMainThread(task =>
              {
                  if (task.IsFaulted || task.IsCanceled)
                  {
                      Debug.LogError("[Kanji] Database error: " + task.Exception);
                      SetErrorState("DB error");
                      return;
                  }

                  DataSnapshot snap = task.Result;
                  if (snap.Exists)
                      PopulateUI(snap, character);
                  else
                  {
                      Debug.LogWarning($"[Kanji] {character} not found.");
                      SetErrorState("Kanji not found");
                  }
              });
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  UI Population
    // ═════════════════════════════════════════════════════════════════════════

    void SetLoadingState()
    {
        if (kanjiTMP != null) kanjiTMP.text = "…";
        if (meaningTMP != null) meaningTMP.text = "Loading…";
        if (pronuncTMP != null) pronuncTMP.text = "Loading…";
        if (aiOutputTMP != null) aiOutputTMP.text = "Loading…";
        if (wordsOutputTMP != null) wordsOutputTMP.text = "Loading…";
    }

    void SetErrorState(string msg)
    {
        if (meaningTMP != null) meaningTMP.text = msg;
        if (pronuncTMP != null) pronuncTMP.text = msg;
        if (aiOutputTMP != null) aiOutputTMP.text = "";
        if (wordsOutputTMP != null) wordsOutputTMP.text = "";
    }

    void PopulateUI(DataSnapshot snap, string character)
    {
        var data = snap.Value as Dictionary<string, object>;
        if (data == null) data = new Dictionary<string, object>();

        // ── Kanji character ──────────────────────────────────────────────────
        string kanjiStr = character;
        kanjiTMP.text = kanjiStr;
        if (kanjiStr.Length > 0) currentKanjiChar = kanjiStr[0];

        // ── Meanings ─────────────────────────────────────────────────────────
        string meanings = ReadFlexibleField(data, "meanings", "meaning") ?? "";
        meaningTMP.text = string.IsNullOrEmpty(meanings) ? "No meaning" : meanings;

        // ── Readings ─────────────────────────────────────────────────────────
        // Combine kun'yomi + on'yomi into one list (instead of only showing
        // whichever field happens to exist first) so there are usually at
        // least a few readings shown, not just one or two.
        var readings = new List<string>();
        readings.AddRange(ReadReadingList(data, "readings_kun"));
        readings.AddRange(ReadReadingList(data, "readings_on"));

        // Fallback for older records that only have a plain "reading" field.
        if (readings.Count == 0)
        {
            string plain = ReadFlexibleField(data, "reading") ?? "";
            if (!string.IsNullOrEmpty(plain))
                readings.AddRange(plain.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
        }

        readings = readings.Distinct().ToList();
        pronuncTMP.text = readings.Count > 0 ? string.Join(", ", readings) : "No readings";

        Debug.Log($"[Kanji] {character} → {kanjiStr} | {meaningTMP.text} | {pronuncTMP.text}");

        // ── Load SVG graphic — show full static kanji on load ────────────────
        LoadSvgGraphic(currentKanjiChar);

        // ── AI ───────────────────────────────────────────────────────────────
        StartCoroutine(SendSentenceRequest(kanjiStr));
        StartCoroutine(SendWordRequest(kanjiStr));
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  SVG Graphic
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads the kanji and shows all strokes statically (no ghost, no animation).
    /// ghostIndex = -1  →  every stroke rendered in guide color; no blue ghost.
    /// </summary>
    void LoadSvgGraphic(char kanji)
    {
        if (kanjiGraphic == null) return;

        isAnimating = false;
        KanjiData kd = KanjiLoader.Load(kanji);
        if (kd != null)
        {
            // ghostIndex: -1 = show all strokes fully, no ghost hint
            kanjiGraphic.LoadKanji(kd, ghostIndex: -1);
            Debug.Log($"[Kanji] Graphic loaded for '{kanji}'");
        }
        else
        {
            Debug.LogWarning($"[Kanji] No SVG for '{kanji}' — check StreamingAssets/kanji/");
        }
    }

    /// <summary>
    /// Called when the user clicks the graphic.
    /// Plays the stroke-order animation; once done, restores the full static view.
    /// Clicking again while animating will restart from stroke 1.
    /// </summary>
    public void OnGraphicClicked()
    {
        if (kanjiGraphic == null) return;

        // Stop any running "restore" coroutine and restart animation
        StopAllCoroutines();
        isAnimating = true;

        kanjiGraphic.PlayAnimation();
        Debug.Log($"[Kanji] Animation triggered for '{currentKanjiChar}'");

        // Wait for animation to finish, then restore the static view
        StartCoroutine(WaitForAnimationThenRestore());
    }

    /// <summary>
    /// Polls until KanjiStrokeGraphic._animating is false, then reloads the static kanji.
    /// Uses the public PlayAnimation / a duration estimate because KanjiStrokeGraphic
    /// does not expose an "OnAnimationComplete" event.
    /// We calculate the duration from strokeCount × (strokeDuration + strokeDelay).
    /// </summary>
    private IEnumerator WaitForAnimationThenRestore()
    {
        KanjiData kd = KanjiLoader.Load(currentKanjiChar);
        if (kd == null) yield break;

        float totalDuration = kd.strokeCount *
            (kanjiGraphic.strokeDuration + kanjiGraphic.strokeDelay) + 0.3f; // small buffer

        yield return new WaitForSeconds(totalDuration);

        // Restore full static view (all strokes visible, no ghost)
        if (isAnimating) // only if we haven't already loaded a new kanji
        {
            kanjiGraphic.LoadKanji(kd, ghostIndex: -1);
            isAnimating = false;
            Debug.Log($"[Kanji] Animation done — static view restored for '{currentKanjiChar}'");
        }
    }

    /// <summary>
    /// Adds a PointerClick EventTrigger to both the graphic and the kanji
    /// character label, so clicking either one plays the stroke animation.
    /// Also ensures each target has a raycast-target so clicks register.
    /// </summary>
    void SetupGraphicClickHandler()
    {
        if (kanjiGraphic != null)
            WireClickToAnimate(kanjiGraphic.gameObject, ensureImage: true);

        // The graphic object is sometimes invisible/decorative-only, so also
        // let the player click the character label itself.
        if (kanjiTMP != null)
            WireClickToAnimate(kanjiTMP.gameObject, ensureImage: false);
    }

    void WireClickToAnimate(GameObject target, bool ensureImage)
    {
        if (ensureImage)
        {
            var img = target.GetComponent<Image>();
            if (img == null)
            {
                img = target.AddComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0.01f);
            }
            img.raycastTarget = true;
        }
        else
        {
            // TMP_Text already implements Graphic, so it has its own raycastTarget.
            var graphic = target.GetComponent<Graphic>();
            if (graphic != null) graphic.raycastTarget = true;
        }

        var trigger = target.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = target.AddComponent<EventTrigger>();

        // Guard against duplicate entries (e.g. if Start() is called more than once)
        trigger.triggers.RemoveAll(e => e.eventID == EventTriggerType.PointerClick);

        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        entry.callback.AddListener(_ => OnGraphicClicked());
        trigger.triggers.Add(entry);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Realtime Database field helpers
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads a Realtime Database field that may be stored as:
    ///   • a plain string             → returned as-is
    ///   • a comma-separated string   → returned as-is (display directly)
    ///   • a List&lt;object&gt; array → items joined with ", "
    ///   • a Dictionary map           → values joined with ", " (numeric keys)
    /// Returns null if the key is missing or the value is empty.
    /// </summary>
    /// <summary>
    /// Extracts a kanji's readings_kun / readings_on field as a cleaned list
    /// of plain kana, e.g. "ひと.つ" or "ひと-" → "ひとつ" / "ひと". Used to
    /// combine kun+on into one pronunciation list rather than only showing
    /// whichever single field ReadFlexibleField happened to pick.
    /// </summary>
    List<string> ReadReadingList(Dictionary<string, object> data, string key)
    {
        var result = new List<string>();
        if (!data.TryGetValue(key, out object raw) || raw == null) return result;

        IEnumerable<string> rawItems = raw switch
        {
            List<object> list => list.Select(x => x?.ToString()),
            Dictionary<string, object> map => Enumerable.Range(0, map.Count)
                .Select(i => map.TryGetValue(i.ToString(), out object v) ? v?.ToString() : null),
            _ => new[] { raw.ToString() }
        };

        foreach (var item in rawItems)
        {
            if (string.IsNullOrEmpty(item)) continue;
            // "." separates stem from okurigana (e.g. "ひと.つ" spoken "ひとつ");
            // "-" and "!" are edict/WaniKani boundary markers, not spoken sounds.
            string cleaned = item.Trim('-', '!').Replace(".", "");
            if (!string.IsNullOrEmpty(cleaned)) result.Add(cleaned);
        }
        return result;
    }

    string ReadFlexibleField(Dictionary<string, object> data, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!data.ContainsKey(key) || data[key] == null) continue;
            object raw = data[key];

            // Realtime Database array
            if (raw is List<object> list)
            {
                var parts = new List<string>();
                foreach (var item in list)
                    if (item != null) parts.Add(item.ToString());
                string joined = string.Join(", ", parts);
                if (!string.IsNullOrEmpty(joined)) return joined;
                continue;
            }

            // Realtime Database map with numeric keys (sometimes returned instead of array)
            if (raw is Dictionary<string, object> map)
            {
                var parts = new List<string>();
                for (int i = 0; i < map.Count; i++)
                {
                    string k = i.ToString();
                    if (map.ContainsKey(k) && map[k] != null)
                        parts.Add(map[k].ToString());
                }
                string joined = string.Join(", ", parts);
                if (!string.IsNullOrEmpty(joined)) return joined;
                continue;
            }

            // Plain string
            string str = raw.ToString().Trim();
            if (!string.IsNullOrEmpty(str)) return str;
        }
        return null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  AI: Example sentences
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator SendSentenceRequest(string kanji)
    {
        string prompt =
            "You are a Japanese language teacher. Generate exactly 5 example sentences using the kanji: "
            + kanji
            + ". Rules: "
            + "-Each entry must follow this exact format: [number]. [Japanese sentence] "
            + "-EN: [English translation] "
            + "-Number them 1 to 5 "
            + "-Japanese sentence first, English translation on the next line prefixed with EN: "
            + "-Use natural simple Japanese "
            + "-Do not add any extra commentary headers or blank lines between entries";

        yield return StartCoroutine(PostToAI(prompt, text => aiOutputTMP.text = text));
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  AI: Compound words
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator SendWordRequest(string kanji)
    {
        if (wordsOutputTMP == null) yield break;

        string prompt =
            "You are a Japanese language teacher. List exactly 5 common Japanese words or compounds that contain the kanji: "
            + kanji
            + ". Rules: "
            + "-Each entry must follow this exact format: [number]. [word in kanji/kana] ([reading in hiragana]) - [English meaning] "
            + "-Number them 1 to 5 "
            + "-Use common everyday vocabulary "
            + "-Do not add any extra commentary headers or blank lines between entries";

        yield return StartCoroutine(PostToAI(prompt, text => wordsOutputTMP.text = text));
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Shared AI POST
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator PostToAI(string prompt, Action<string> onResult)
    {
        string safe = prompt.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string json = "{\"model\":\"" + modelName + "\",\"input\":\"" + safe + "\"}";

        var req = new UnityWebRequest(apiUrl, "POST");
        req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            LMResponse resp = JsonUtility.FromJson<LMResponse>(req.downloadHandler.text);
            string output = (resp?.output?.Length > 0) ? resp.output[0].content : "No content received.";
            onResult?.Invoke(output);
        }
        else
        {
            Debug.LogError("[Kanji] AI error: " + req.error);
            onResult?.Invoke("Request failed: " + req.error);
        }
    }
}
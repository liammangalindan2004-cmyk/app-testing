using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// A 3x3 mix-and-match study game for kanji vocabulary, backed by the
/// unmei-nihongo-center Firebase Realtime Database.
///
/// Data source: {databaseBaseUrl}/kanji/{jlpt_level}.json — an object
/// keyed by kanji character, e.g.
///   "一": { "meanings": ["One", ...], "readings_on": ["いち","いつ"], "readings_kun": ["ひと-", ...] }
///
/// For the selected JLPT level, every kanji in that level is queued up
/// into rounds of 3. Each word contributes 3 tiles to the board: its
/// kanji, its meaning, and its reading (no label says which is which).
/// Tapping any two tiles that belong to the same word clears all three
/// of that word's tiles. Once a round is cleared, "Next" advances to
/// the next 3 kanji; once every kanji in the level has appeared, the
/// game reports the level complete and can restart with a fresh shuffle.
///
/// SETUP
/// 1. Package Manager > Add package by name > com.unity.nuget.newtonsoft-json
///    (needed to parse the arbitrary-keyed kanji JSON).
/// 2. Build a Canvas with a 3x3 grid of 9 Button GameObjects, each with
///    one TextMeshProUGUI child for the tile's text. Matched tiles are
///    disabled (Button.interactable = false), so give each Button's own
///    Disabled color/sprite in its Transition settings if you want that
///    to be visible.
/// 3. Add this script to a GameObject. In the Inspector:
///    - Drag the 9 buttons (with their label) into Tiles.
///    - Assign movesText, statusText, progressText, nextRoundButton,
///      and (optionally) a loadingIndicator GameObject.
///    - Pick a starting Level, or wire SelectN5/N4/N3/N2 to level-select
///      buttons in your UI.
/// 4. Make sure the database's read rules allow reading "kanji" without
///    auth — if not, you'll need to append "?auth=&lt;ID token&gt;" to the
///    request URL in FetchLevel.
/// </summary>
public class KanjiMatchGame : MonoBehaviour
{
    [Serializable]
    public class KanjiWord
    {
        public string kanji;
        public string reading;
        public string meaning;
    }

    [Serializable]
    public class TileView
    {
        public Button button;
        public TextMeshProUGUI label;
    }

    private class TileData
    {
        public int wordIndex;
        public string label;
    }

    public enum JlptLevel { N5, N4, N3, N2 }

    [Header("Firebase Realtime Database")]
    public string databaseBaseUrl = "https://unmei-nihongo-center-default-rtdb.asia-southeast1.firebasedatabase.app";
    public JlptLevel level = JlptLevel.N5;

    [Header("How much text to show per tile")]
    [Range(1, 3)] public int meaningsPerTile = 1;
    [Range(1, 3)] public int readingsPerTile = 2;

    [Header("Board — assign exactly 9 tiles")]
    public TileView[] tiles = new TileView[9];

    [Header("UI")]
    public TextMeshProUGUI movesText;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI progressText;
    public Button nextRoundButton;
    public GameObject loadingIndicator;

    [Header("Timing (seconds)")]
    public float matchDelay = 0.35f;
    public float wrongResetDelay = 0.5f;

    private List<KanjiWord> allWords = new List<KanjiWord>();
    private List<List<KanjiWord>> rounds = new List<List<KanjiWord>>();
    private int roundIndex = -1;

    private readonly List<KanjiWord> roundWords = new List<KanjiWord>();
    private TileData[] boardData = new TileData[9];
    private readonly HashSet<int> matchedWordIndices = new HashSet<int>();
    private readonly List<int> selectedTileIndices = new List<int>();
    private int moves;
    private bool locked;

    private void Start()
    {
        if (tiles.Length != 9)
        {
            Debug.LogError($"KanjiMatchGame requires exactly 9 tiles — found {tiles.Length}. Fix the size of the Tiles array in the Inspector.");
            return;
        }

        if (!ValidateTiles())
            return;

        for (int i = 0; i < tiles.Length; i++)
        {
            int idx = i; // capture for closure
            tiles[i].button.onClick.AddListener(() => OnTileClicked(idx));
        }

        if (nextRoundButton != null)
            nextRoundButton.onClick.AddListener(OnNextRoundPressed);

        LoadLevel(level);
    }

    /// <summary>
    /// Checks all 9 tile slots are fully wired before the game touches them,
    /// and logs exactly which slot/field is missing instead of throwing a
    /// bare NullReferenceException later on.
    /// </summary>
    private bool ValidateTiles()
    {
        bool ok = true;
        for (int i = 0; i < tiles.Length; i++)
        {
            if (tiles[i] == null)
            {
                Debug.LogError($"KanjiMatchGame: Tiles[{i}] is empty — expand the Tiles array in the Inspector and assign a TileView there.");
                ok = false;
                continue;
            }
            if (tiles[i].button == null)
                Debug.LogError($"KanjiMatchGame: Tiles[{i}].button is not assigned in the Inspector.");
            if (tiles[i].label == null)
                Debug.LogError($"KanjiMatchGame: Tiles[{i}].label is not assigned in the Inspector.");

            if (tiles[i].button == null || tiles[i].label == null)
                ok = false;
        }
        return ok;
    }

    // Wire these directly to level-select buttons in the Inspector.
    public void SelectN5() => LoadLevel(JlptLevel.N5);
    public void SelectN4() => LoadLevel(JlptLevel.N4);
    public void SelectN3() => LoadLevel(JlptLevel.N3);
    public void SelectN2() => LoadLevel(JlptLevel.N2);

    public void LoadLevel(JlptLevel newLevel)
    {
        level = newLevel;
        StopAllCoroutines();
        locked = true;
        SetTilesInteractable(false);
        if (nextRoundButton != null) nextRoundButton.gameObject.SetActive(false);
        if (loadingIndicator != null) loadingIndicator.SetActive(true);
        SetStatus($"Loading {LevelKey(level)}...");
        StartCoroutine(FetchLevel(LevelKey(level)));
    }

    private string LevelKey(JlptLevel lvl)
    {
        switch (lvl)
        {
            case JlptLevel.N5: return "jlpt_n5";
            case JlptLevel.N4: return "jlpt_n4";
            case JlptLevel.N3: return "jlpt_n3";
            case JlptLevel.N2: return "jlpt_n2";
            default: return "jlpt_n5";
        }
    }

    private IEnumerator FetchLevel(string levelKey)
    {
        string url = $"{databaseBaseUrl.TrimEnd('/')}/kanji/{levelKey}.json";

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (loadingIndicator != null) loadingIndicator.SetActive(false);

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to fetch kanji ({levelKey}): {req.error}");
                SetStatus("Couldn't load kanji — check your connection or database rules.");
                yield break;
            }

            List<KanjiWord> parsed;
            try
            {
                parsed = ParseKanjiJson(req.downloadHandler.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to parse kanji JSON for {levelKey}: {e}");
                SetStatus("Couldn't read kanji data.");
                yield break;
            }

            if (parsed.Count < 3)
            {
                SetStatus($"Not enough kanji found for {levelKey}.");
                yield break;
            }

            allWords = parsed;
            BuildRounds();
            roundIndex = -1;
            AdvanceRound();
        }
    }

    private List<KanjiWord> ParseKanjiJson(string json)
    {
        JObject root = JObject.Parse(json);
        List<KanjiWord> result = new List<KanjiWord>();

        foreach (var prop in root.Properties())
        {
            string character = prop.Name;
            if (!(prop.Value is JObject entry)) continue;

            List<string> meanings = entry["meanings"]?.Select(t => t.ToString()).ToList() ?? new List<string>();
            List<string> readingsKun = entry["readings_kun"]?.Select(t => t.ToString()).ToList() ?? new List<string>();
            List<string> readingsOn = entry["readings_on"]?.Select(t => t.ToString()).ToList() ?? new List<string>();

            List<string> combinedReadings = new List<string>();
            combinedReadings.AddRange(readingsKun);
            combinedReadings.AddRange(readingsOn);

            string meaning = string.Join(", ", meanings.Take(Mathf.Max(1, meaningsPerTile)));
            string reading = string.Join("・", combinedReadings.Take(Mathf.Max(1, readingsPerTile)));

            if (string.IsNullOrEmpty(meaning) || string.IsNullOrEmpty(reading)) continue;

            result.Add(new KanjiWord { kanji = character, meaning = meaning, reading = reading });
        }

        return result;
    }

    private void BuildRounds()
    {
        List<KanjiWord> shuffled = new List<KanjiWord>(allWords);
        Shuffle(shuffled);

        rounds = new List<List<KanjiWord>>();
        int i = 0;
        while (i < shuffled.Count)
        {
            int remaining = shuffled.Count - i;
            if (remaining >= 3)
            {
                rounds.Add(shuffled.GetRange(i, 3));
                i += 3;
            }
            else
            {
                // Pad a short final round with already-seen words so the
                // board always has exactly 3 words / 9 tiles. Every kanji
                // still appears at least once across the level.
                List<KanjiWord> last = new List<KanjiWord>(shuffled.GetRange(i, remaining));
                int padNeeded = 3 - remaining;
                for (int p = 0; p < padNeeded && p < shuffled.Count; p++)
                    last.Add(shuffled[p]);
                rounds.Add(last);
                i = shuffled.Count;
            }
        }
    }

    private void AdvanceRound()
    {
        roundIndex++;

        if (roundIndex >= rounds.Count)
        {
            SetStatus($"Level complete! You covered all {allWords.Count} kanji in {LevelKey(level)}.");
            if (progressText != null) progressText.text = $"{allWords.Count} / {allWords.Count} kanji";
            SetTilesInteractable(false);
            locked = true;

            if (nextRoundButton != null)
            {
                nextRoundButton.gameObject.SetActive(true);
                var label = nextRoundButton.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = "Restart Level";
            }
            return;
        }

        StartRound(rounds[roundIndex]);
    }

    private void OnNextRoundPressed()
    {
        if (roundIndex >= rounds.Count)
        {
            // Level was complete — restart from the top with a fresh shuffle.
            BuildRounds();
            roundIndex = -1;
        }
        AdvanceRound();
    }

    private void StartRound(List<KanjiWord> words)
    {
        moves = 0;
        selectedTileIndices.Clear();
        matchedWordIndices.Clear();
        locked = false;

        roundWords.Clear();
        roundWords.AddRange(words);
        boardData = BuildBoard(roundWords);

        for (int i = 0; i < tiles.Length; i++)
            RenderTile(i);

        if (nextRoundButton != null)
        {
            nextRoundButton.gameObject.SetActive(false);
            var label = nextRoundButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = "Next";
        }

        SetTilesInteractable(true);
        UpdateStatus();
        UpdateProgress();
    }

    private TileData[] BuildBoard(List<KanjiWord> words)
    {
        List<TileData> data = new List<TileData>(9);
        for (int w = 0; w < words.Count; w++)
        {
            data.Add(new TileData { wordIndex = w, label = words[w].kanji });
            data.Add(new TileData { wordIndex = w, label = words[w].meaning });
            data.Add(new TileData { wordIndex = w, label = words[w].reading });
        }
        Shuffle(data);
        return data.ToArray();
    }

    private void RenderTile(int i)
    {
        TileData d = boardData[i];
        tiles[i].label.text = d.label;
        tiles[i].button.interactable = true;
    }

    private void OnTileClicked(int idx)
    {
        if (locked) return;
        if (boardData[idx] == null) return;
        if (matchedWordIndices.Contains(boardData[idx].wordIndex)) return;
        if (selectedTileIndices.Contains(idx)) return;
        if (selectedTileIndices.Count >= 2) return;

        selectedTileIndices.Add(idx);

        if (selectedTileIndices.Count == 2)
        {
            locked = true;
            moves++;
            UpdateStatus();

            int a = selectedTileIndices[0];
            int b = selectedTileIndices[1];

            if (boardData[a].wordIndex == boardData[b].wordIndex)
                StartCoroutine(HandleMatch(boardData[a].wordIndex));
            else
                StartCoroutine(HandleMismatch(a, b));
        }
    }

    private IEnumerator HandleMatch(int wordIndex)
    {
        yield return new WaitForSeconds(matchDelay);

        matchedWordIndices.Add(wordIndex);

        for (int i = 0; i < boardData.Length; i++)
        {
            if (boardData[i].wordIndex == wordIndex)
                tiles[i].button.interactable = false;
        }

        selectedTileIndices.Clear();
        locked = false;
        UpdateStatus();
        CheckRoundWin();
    }

    private IEnumerator HandleMismatch(int a, int b)
    {
        yield return new WaitForSeconds(wrongResetDelay);

        selectedTileIndices.Clear();
        locked = false;
    }

    private void CheckRoundWin()
    {
        if (matchedWordIndices.Count != roundWords.Count) return;

        locked = true;

        if (nextRoundButton != null)
        {
            nextRoundButton.gameObject.SetActive(true);
            var label = nextRoundButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = (roundIndex + 1 >= rounds.Count) ? "Finish Level" : "Next";
        }
    }

    private void UpdateStatus()
    {
        if (movesText != null)
            movesText.text = $"{moves} moves";

        if (statusText == null) return;

        bool wonRound = matchedWordIndices.Count == roundWords.Count;
        statusText.text = wonRound
            ? $"Round cleared in {moves} moves!"
            : $"{matchedWordIndices.Count} / {roundWords.Count} words matched";
    }

    private void UpdateProgress()
    {
        if (progressText == null) return;
        progressText.text = $"Round {roundIndex + 1} / {rounds.Count} · {allWords.Count} kanji in {LevelKey(level)}";
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    private void SetTilesInteractable(bool value)
    {
        foreach (var t in tiles)
            if (t?.button != null) t.button.interactable = value;
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
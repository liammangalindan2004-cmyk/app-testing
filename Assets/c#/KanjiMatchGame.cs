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
/// For the selected JLPT level, every kanji is shuffled into one queue.
/// Each word contributes 2 tiles to the board, randomly chosen from its
/// kanji, its meaning, and its reading (no label says which is which).
/// With 9 tiles there are always 4 complete pairs plus one "lone" tile
/// whose partner isn't on the board yet. As soon as any pair is matched,
/// the lone tile's partner appears in one of the freed slots and the other
/// freed slot starts a new lone tile from the next kanji in the queue.
/// Once the queue runs out, freed slots are filled with distractors:
/// tiles taken from kanji that aren't on the board, which can never be
/// matched, so the board never has empty tiles. The level is complete when
/// every pair has been matched.
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
        public int wordIndex;   // >= 0 for real words, < 0 for distractors (never matches)
        public string label;
        public KanjiWord word;
    }

    public enum JlptLevel { N5, N4, N3, N2 }

    [Header("Firebase Realtime Database")]
    public string databaseBaseUrl = "https://unmei-nihongo-center-default-rtdb.asia-southeast1.firebasedatabase.app";
    public JlptLevel level = JlptLevel.N5;

    [Header("How much text to show per tile")]
    [Range(1, 3)] public int meaningsPerTile = 1;
    [Range(1, 3)] public int readingsPerTile = 2;

    [Header("Board — assign 9 tiles")]
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
    private List<KanjiWord> queue = new List<KanjiWord>();
    private int nextQueueIndex;
    private int pairsMatched;
    private TileData pendingPartner;   // partner of the lone tile on the board
    private int distractorCounter;

    private TileData[] boardData = new TileData[9];
    private readonly List<int> selectedTileIndices = new List<int>();
    private int moves;
    private bool locked;

    private void Start()
    {
        if (tiles.Length < 2)
        {
            Debug.LogError($"KanjiMatchGame requires at least 2 tiles — found {tiles.Length}. Fix the size of the Tiles array in the Inspector.");
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
            nextRoundButton.onClick.AddListener(OnRestartPressed);

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
            StartGame();
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

    private void OnRestartPressed()
    {
        StartGame();
    }

    private void StartGame()
    {
        queue = new List<KanjiWord>(allWords);
        Shuffle(queue);
        nextQueueIndex = 0;
        pairsMatched = 0;
        moves = 0;
        distractorCounter = 0;
        pendingPartner = null;
        selectedTileIndices.Clear();
        locked = false;

        if (nextRoundButton != null) nextRoundButton.gameObject.SetActive(false);

        for (int i = 0; i < tiles.Length; i++)
            tiles[i].button.gameObject.SetActive(true);

        boardData = new TileData[tiles.Length];
        FillSlots(Enumerable.Range(0, tiles.Length).ToList());

        UpdateStatus();
        UpdateProgress();
    }

    /// <summary>
    /// Fills the given empty tile slots. Order of priority:
    /// 1) the pending partner of the current lone tile,
    /// 2) new words from the queue (2 tiles each; if only one slot is left,
    ///    that word becomes the new lone tile and its partner is held back),
    /// 3) distractors once the queue is empty.
    /// </summary>
    private void FillSlots(List<int> slots)
    {
        Shuffle(slots);
        int i = 0;

        if (pendingPartner != null && slots.Count > 0)
        {
            PlaceTile(slots[i++], pendingPartner);
            pendingPartner = null;
        }

        while (i < slots.Count)
        {
            int remaining = slots.Count - i;

            if (nextQueueIndex < queue.Count)
            {
                int id = nextQueueIndex;
                KanjiWord word = queue[nextQueueIndex++];

                List<TileData> forms = new List<TileData>
                {
                    new TileData { wordIndex = id, label = word.kanji, word = word },
                    new TileData { wordIndex = id, label = word.meaning, word = word },
                    new TileData { wordIndex = id, label = word.reading, word = word }
                };
                Shuffle(forms); // first 2 are the chosen pair

                PlaceTile(slots[i++], forms[0]);
                if (remaining >= 2)
                    PlaceTile(slots[i++], forms[1]);
                else
                    pendingPartner = forms[1]; // lone tile: partner arrives after the next match
            }
            else
            {
                PlaceDistractor(slots[i++]);
            }
        }
    }

    /// <summary>
    /// Fills a slot with a tile from a kanji that is NOT on the board (and
    /// isn't waiting as a pending partner), using a label that doesn't
    /// already appear on the board. It gets a unique negative id, so it can
    /// never match anything.
    /// </summary>
    private void PlaceDistractor(int slot)
    {
        HashSet<KanjiWord> onBoard = new HashSet<KanjiWord>();
        HashSet<string> labelsOnBoard = new HashSet<string>();
        foreach (TileData t in boardData)
        {
            if (t == null) continue;
            if (t.word != null) onBoard.Add(t.word);
            labelsOnBoard.Add(t.label);
        }
        if (pendingPartner != null && pendingPartner.word != null)
            onBoard.Add(pendingPartner.word);

        List<TileData> candidates = new List<TileData>();
        foreach (KanjiWord w in allWords)
        {
            if (onBoard.Contains(w)) continue;
            foreach (string label in new[] { w.kanji, w.meaning, w.reading })
            {
                if (!labelsOnBoard.Contains(label))
                    candidates.Add(new TileData { label = label, word = w });
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning("KanjiMatchGame: not enough kanji in this level to make a distractor; leaving a tile blank.");
            boardData[slot] = null;
            tiles[slot].label.text = "";
            tiles[slot].button.interactable = false;
            return;
        }

        TileData chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        chosen.wordIndex = --distractorCounter; // unique negative id: never pairs
        PlaceTile(slot, chosen);
    }

    private void PlaceTile(int slot, TileData data)
    {
        boardData[slot] = data;
        tiles[slot].label.text = data.label;
        tiles[slot].button.interactable = true;
    }

    private void OnTileClicked(int idx)
    {
        if (locked) return;
        if (boardData[idx] == null) return;
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

        pairsMatched++;

        // Free the matched pair's tiles.
        List<int> freed = new List<int>();
        for (int i = 0; i < boardData.Length; i++)
        {
            if (boardData[i] != null && boardData[i].wordIndex == wordIndex)
            {
                freed.Add(i);
                boardData[i] = null;
                tiles[i].button.interactable = false;
            }
        }

        selectedTileIndices.Clear();

        if (PairsLeft == 0)
        {
            UpdateStatus();
            UpdateProgress();
            CompleteLevel();
            yield break;
        }

        // Lone tile's partner appears here; leftover slot gets a new lone tile or a distractor.
        FillSlots(freed);

        locked = false;
        UpdateStatus();
        UpdateProgress();
    }

    private IEnumerator HandleMismatch(int a, int b)
    {
        yield return new WaitForSeconds(wrongResetDelay);

        selectedTileIndices.Clear();
        locked = false;
    }

    private int PairsLeft => queue.Count - pairsMatched;

    private void CompleteLevel()
    {
        locked = true;
        SetTilesInteractable(false);
        SetStatus($"Level complete! You matched all {queue.Count} pairs in {moves} moves.");

        if (nextRoundButton != null)
        {
            nextRoundButton.gameObject.SetActive(true);
            var label = nextRoundButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = "Restart Level";
        }
    }

    private void UpdateStatus()
    {
        if (movesText != null)
            movesText.text = $"{moves} moves";

        if (statusText == null || PairsLeft == 0) return;

        statusText.text = PairsLeft == 1 ? "1 pair left" : $"{PairsLeft} pairs left";
    }

    private void UpdateProgress()
    {
        if (progressText == null) return;
        progressText.text = $"{pairsMatched} / {queue.Count} matched · {LevelKey(level)}";
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
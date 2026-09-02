using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using JapaneseLearning.Pronunciation;

/// <summary>
/// Cycles through a list of kanji lesson items and pushes each one into
/// PronunciationSceneController. Attach next to your scene controller and
/// hook a "Next" button to NextItem(), or call LoadItem(index) yourself
/// once you wire up a real lesson/vocab source.
///
/// ═══════════════════════════════════════════════════════════
///  ONE-SYLLABLE / TWO-SYLLABLE PRACTICE MODULES
/// ═══════════════════════════════════════════════════════════
///  Set "Module" in the Inspector to restrict this lesson set to only
///  1-mora or 2-mora readings — useful right now because pronunciation
///  grading is reliable on the first mora, rougher on the second, and
///  fails consistently from the third mora onward, so shorter words give
///  a much more trustworthy score.
///
///  TWO WAYS TO USE THIS:
///
///  A) Two separate scenes/panels — put one KanjiLessonManager on each,
///     set one to OneSyllable and the other to TwoSyllable, each pointing
///     at its own PronunciationSceneController (or the same one, if you
///     swap which lesson manager is active).
///
///  B) One scene, switch at runtime — wire two buttons to call
///     SetModule(PronunciationModule.OneSyllable) and
///     SetModule(PronunciationModule.TwoSyllable) respectively (e.g. via
///     two small wrapper methods, see SetModuleOneSyllable() /
///     SetModuleTwoSyllable() below, which you can hook directly to a
///     Button's OnClick with no code needed).
///
///  Filtering runs against whatever `lessonItems` resolves to (your
///  Inspector list if you filled one in, otherwise SampleKanjiData.N5Sample),
///  so it'll keep working if you swap in a real vocab source later.
/// ═══════════════════════════════════════════════════════════
/// </summary>
public class KanjiLessonManager : MonoBehaviour
{
    public enum PronunciationModule
    {
        All,
        OneSyllable,   // reading is exactly 1 mora, e.g. 木(き), 手(て), 目(め)
        TwoSyllable    // reading is exactly 2 mora, e.g. 山(やま), 人(ひと), 川(かわ)
    }

    [Header("References")]
    public PronunciationSceneController sceneController;

    [Header("Optional UI")]
    [Tooltip("Shows the English meaning, e.g. 'student'. Leave empty if you don't have this element.")]
    public TMP_Text meaningText;
    [Tooltip("Optional 'Next' button to manually advance to the next kanji")]
    public Button nextButton;

    [Header("Practice Module")]
    [Tooltip("Restrict this lesson set to 1-mora or 2-mora readings only, or use everything.")]
    public PronunciationModule module = PronunciationModule.All;

    [Header("Lesson Data")]
    [Tooltip("Populated from the built-in N5 sample set in Awake if left empty. Replace with your real vocab source later.")]
    public List<KanjiLessonItem> lessonItems = new List<KanjiLessonItem>();

    private List<KanjiLessonItem> _allItems;   // unfiltered source, so switching modules never compounds filters
    private int _currentIndex;

    void Awake()
    {
        _allItems = (lessonItems != null && lessonItems.Count > 0)
            ? lessonItems
            : SampleKanjiData.N5Sample;

        ApplyModuleFilter();

        if (nextButton != null)
            nextButton.onClick.AddListener(NextItem);
    }

    void Start()
    {
        LoadItem(0);
    }

    private void ApplyModuleFilter()
    {
        lessonItems = module switch
        {
            PronunciationModule.OneSyllable => KanjiLessonFilters.OneMora(_allItems),
            PronunciationModule.TwoSyllable => KanjiLessonFilters.TwoMora(_allItems),
            _ => _allItems
        };

        if (lessonItems.Count == 0)
        {
            Debug.LogWarning($"[KanjiLessonManager] No items matched module '{module}' " +
                              $"(checked {_allItems.Count} items) — falling back to the full list.");
            lessonItems = _allItems;
        }
        else
        {
            Debug.Log($"[KanjiLessonManager] Module '{module}': {lessonItems.Count}/{_allItems.Count} items loaded.");
        }

        _currentIndex = 0;
    }

    /// <summary>Switch modules at runtime — e.g. wire two buttons to the two convenience methods below.</summary>
    public void SetModule(PronunciationModule newModule)
    {
        module = newModule;
        ApplyModuleFilter();
        LoadItem(0);
    }

    /// <summary>Convenience wrapper so you can hook this directly to a Button's OnClick with no code.</summary>
    public void SetModuleOneSyllable() => SetModule(PronunciationModule.OneSyllable);

    /// <summary>Convenience wrapper so you can hook this directly to a Button's OnClick with no code.</summary>
    public void SetModuleTwoSyllable() => SetModule(PronunciationModule.TwoSyllable);

    /// <summary>Convenience wrapper so you can hook this directly to a Button's OnClick with no code.</summary>
    public void SetModuleAll() => SetModule(PronunciationModule.All);

    public void LoadItem(int index)
    {
        if (lessonItems == null || lessonItems.Count == 0)
        {
            Debug.LogWarning("No lesson items assigned to KanjiLessonManager.");
            return;
        }

        _currentIndex = Mathf.Clamp(index, 0, lessonItems.Count - 1);
        var item = lessonItems[_currentIndex];

        // Constrain Vosk's grammar to every reading in this lesson set,
        // not just the current word, so nearby answers are still recognized cleanly.
        var allReadings = SampleKanjiData.GetAllReadings(lessonItems);

        sceneController.SetLessonItem(item.kanji, item.readingKana, allReadings);

        if (meaningText != null)
            meaningText.text = item.meaningEnglish;
    }

    public void NextItem()
    {
        int next = (_currentIndex + 1) % lessonItems.Count;
        LoadItem(next);
    }

    public void PreviousItem()
    {
        int prev = (_currentIndex - 1 + lessonItems.Count) % lessonItems.Count;
        LoadItem(prev);
    }
}
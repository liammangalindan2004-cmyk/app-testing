using System.Collections.Generic;
using System.Linq;
using JapaneseLearning.Pronunciation;

/// <summary>
/// Filters any KanjiLessonItem list by how many mora (kana units) its
/// reading contains, using the same SplitMora logic PronunciationScorer
/// already uses for grading — so "one syllable" here means the same thing
/// the scorer means by "one mora" (e.g. き = 1, きょ = 1, きょう = 2).
///
/// This exists because pronunciation grading is currently reliable on the
/// first mora, shakier on the second, and consistently fails from the third
/// mora onward. Restricting practice to 1- or 2-mora readings keeps players
/// inside the range where scoring is trustworthy while that gets improved.
/// </summary>
public static class KanjiLessonFilters
{
    public static List<KanjiLessonItem> ByMoraCount(IEnumerable<KanjiLessonItem> source, int moraCount)
    {
        return source
            .Where(item => !string.IsNullOrEmpty(item.readingKana)
                           && PronunciationScorer.SplitMora(item.readingKana).Count == moraCount)
            .ToList();
    }

    /// <summary>e.g. 木 (き), 手 (て), 目(め), 日 (ひ)</summary>
    public static List<KanjiLessonItem> OneMora(IEnumerable<KanjiLessonItem> source) =>
        ByMoraCount(source, 1);

    /// <summary>e.g. 山 (やま), 人 (ひと), 川 (かわ), 空 (そら)</summary>
    public static List<KanjiLessonItem> TwoMora(IEnumerable<KanjiLessonItem> source) =>
        ByMoraCount(source, 2);
}
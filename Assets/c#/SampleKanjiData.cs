using System.Collections.Generic;
using UnityEngine;

namespace JapaneseLearning.Pronunciation
{
    [System.Serializable]
    public class KanjiLessonItem
    {
        public string kanji;
        public string readingKana;
        public string meaningEnglish;

        public KanjiLessonItem(string kanji, string readingKana, string meaningEnglish)
        {
            this.kanji = kanji;
            this.readingKana = readingKana;
            this.meaningEnglish = meaningEnglish;
        }
    }

    /// <summary>
    /// A small hardcoded pool of common JLPT N5 kanji for testing the
    /// pronunciation check flow before you wire up your real vocab/lesson
    /// system (Realtime Database, ScriptableObjects, etc.).
    /// </summary>
    public static class SampleKanjiData
    {
        public static List<KanjiLessonItem> N5Sample = new List<KanjiLessonItem>
        {
            new KanjiLessonItem("学生", "がくせい", "student"),
            new KanjiLessonItem("先生", "せんせい", "teacher"),
            new KanjiLessonItem("学校", "がっこう", "school"),
            new KanjiLessonItem("友達", "ともだち", "friend"),
            new KanjiLessonItem("水",   "みず",     "water"),
            new KanjiLessonItem("火",   "ひ",       "fire"),
            new KanjiLessonItem("山",   "やま",     "mountain"),
            new KanjiLessonItem("川",   "かわ",     "river"),
            new KanjiLessonItem("人",   "ひと",     "person"),
            new KanjiLessonItem("犬",   "いぬ",     "dog"),
        };

        /// <summary>All kana readings in a set, used as the Vosk grammar so
        /// recognition is constrained to just this lesson's vocabulary.</summary>
        public static List<string> GetAllReadings(List<KanjiLessonItem> items)
        {
            var readings = new List<string>();
            foreach (var item in items)
                readings.Add(item.readingKana);
            return readings;
        }
    }
}

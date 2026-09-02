using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace JapaneseLearning.Pronunciation
{
    /// <summary>
    /// Turns a raw Vosk JSON result into a PronunciationResult by splitting
    /// text into mora and aligning expected vs. recognized mora sequences.
    /// </summary>
    public static class PronunciationScorer
    {
        public static PronunciationResult Evaluate(string voskResultJson, string expectedKana)
        {
            var result = new PronunciationResult { expectedText = expectedKana };

            JObject parsed = JObject.Parse(voskResultJson);
            JArray words = parsed["result"] as JArray;
            string recognized = (parsed["text"]?.ToString() ?? "").Replace(" ", "");
            result.recognizedText = recognized;
            result.exactMatch = recognized == expectedKana;

            // Average per-word acoustic confidence from Vosk (0-1).
            float avgConfidence = 0f;
            if (words != null && words.Count > 0)
            {
                foreach (var w in words)
                    avgConfidence += w["conf"]?.Value<float>() ?? 0f;
                avgConfidence /= words.Count;
            }
            result.confidence = avgConfidence;

            var expectedMora = SplitMora(expectedKana);
            var actualMora = SplitMora(recognized);
            result.moraBreakdown = AlignMora(expectedMora, actualMora);

            int correctCount = result.moraBreakdown.FindAll(m => m.correct).Count;
            float moraAccuracy = expectedMora.Count > 0
                ? (float)correctCount / expectedMora.Count
                : 0f;

            // Blend: 40% acoustic confidence, 60% mora-match accuracy.
            // Tune this weighting based on how it feels in practice.
            result.score = Mathf.RoundToInt(((avgConfidence * 0.4f) + (moraAccuracy * 0.6f)) * 100f);

            return result;
        }

        /// <summary>
        /// Splits hiragana/katakana into mora units, keeping small kana
        /// (ゃゅょっぁぃぅぇぉ etc.) attached to the preceding character.
        /// e.g. "がくせい" -> [が, く, せ, い], "きょう" -> [きょ, う]
        /// </summary>
        public static List<string> SplitMora(string kana)
        {
            var moras = new List<string>();
            var smallKana = new HashSet<char>
            {
                'ゃ','ゅ','ょ','ぁ','ぃ','ぅ','ぇ','ぉ','っ',
                'ャ','ュ','ョ','ァ','ィ','ゥ','ェ','ォ','ッ'
            };

            for (int i = 0; i < kana.Length; i++)
            {
                if (i + 1 < kana.Length && smallKana.Contains(kana[i + 1])
                    && kana[i + 1] != 'っ' && kana[i + 1] != 'ッ')
                {
                    moras.Add(kana.Substring(i, 2));
                    i++;
                }
                else
                {
                    moras.Add(kana[i].ToString());
                }
            }
            return moras;
        }

        /// <summary>
        /// Levenshtein-based alignment. Walks the edit-distance table backward
        /// to mark each expected mora as matched (correct) or not.
        /// </summary>
        private static List<MoraFeedback> AlignMora(List<string> expected, List<string> actual)
        {
            int n = expected.Count, m = actual.Count;
            int[,] dp = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) dp[i, 0] = i;
            for (int j = 0; j <= m; j++) dp[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    dp[i, j] = expected[i - 1] == actual[j - 1]
                        ? dp[i - 1, j - 1]
                        : 1 + Mathf.Min(dp[i - 1, j], Mathf.Min(dp[i, j - 1], dp[i - 1, j - 1]));
                }
            }

            var reversed = new List<MoraFeedback>();
            int x = n, y = m;
            while (x > 0 && y > 0)
            {
                if (expected[x - 1] == actual[y - 1])
                {
                    reversed.Add(new MoraFeedback { mora = expected[x - 1], correct = true });
                    x--; y--;
                }
                else if (dp[x, y] == dp[x - 1, y - 1] + 1)
                {
                    reversed.Add(new MoraFeedback { mora = expected[x - 1], correct = false }); // substitution
                    x--; y--;
                }
                else if (dp[x, y] == dp[x - 1, y] + 1)
                {
                    reversed.Add(new MoraFeedback { mora = expected[x - 1], correct = false }); // deletion
                    x--;
                }
                else
                {
                    y--; // insertion in actual, doesn't map to an expected mora
                }
            }
            while (x > 0)
            {
                reversed.Add(new MoraFeedback { mora = expected[x - 1], correct = false });
                x--;
            }

            reversed.Reverse();
            return reversed;
        }
    }
}

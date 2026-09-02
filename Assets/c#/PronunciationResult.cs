using System.Collections.Generic;

namespace JapaneseLearning.Pronunciation
{
    /// <summary>
    /// Feedback for a single mora (Japanese "syllable" unit, e.g. が / く / せ / い).
    /// </summary>
    [System.Serializable]
    public class MoraFeedback
    {
        public string mora;
        public bool correct;
    }

    /// <summary>
    /// Full result of one pronunciation check.
    ///
    /// IMPORTANT (for thesis writeup): `confidence` is Vosk's acoustic decoding
    /// confidence for the recognized words, NOT a true phoneme-level pronunciation
    /// score. `score` blends that confidence with mora-level text alignment
    /// accuracy as a practical offline proxy for pronunciation feedback. True
    /// phoneme-level acoustic scoring (Goodness-of-Pronunciation / forced
    /// alignment) would require a cloud service like Azure Pronunciation
    /// Assessment or a custom-trained model — Vosk does not expose phoneme
    /// posteriors through its public API.
    /// </summary>
    [System.Serializable]
    public class PronunciationResult
    {
        public string expectedText;
        public string recognizedText;
        public bool exactMatch;
        public float confidence;      // 0-1, Vosk word-confidence average
        public int score;             // 0-100, blended proxy score
        public List<MoraFeedback> moraBreakdown;
    }
}

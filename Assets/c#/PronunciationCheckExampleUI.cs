using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using JapaneseLearning.Pronunciation;

/// <summary>
/// Minimal example wiring VoskPronunciationChecker to a UI. Attach this and
/// VoskPronunciationChecker to the same GameObject, assign the UI fields,
/// and call CheckPronunciation() from a "Record" button.
/// </summary>
public class PronunciationCheckExampleUI : MonoBehaviour
{
    public VoskPronunciationChecker checker;
    public Text targetWordText;   // e.g. shows "学生 (がくせい)"
    public Text resultText;       // shows recognized text + score
    public Text moraBreakdownText; // shows が✓ く✓ せ✗ い✓

    // Example lesson data — in your app this comes from your lesson/vocab system.
    private readonly List<string> _currentLessonVocab = new List<string>
    {
        "がくせい", "せんせい", "がっこう", "ともだち"
    };
    private const string ExpectedReading = "がくせい";

    void Awake()
    {
        checker.OnResult += HandleResult;
        checker.OnError += HandleError;
    }

    public void CheckPronunciation()
    {
        resultText.text = "Listening...";
        moraBreakdownText.text = "";
        checker.StartCheck(ExpectedReading, _currentLessonVocab);
    }

    private void HandleResult(PronunciationResult result)
    {
        resultText.text = $"Heard: {result.recognizedText}\nScore: {result.score}%";

        var sb = new StringBuilder();
        foreach (var mora in result.moraBreakdown)
            sb.Append(mora.mora).Append(mora.correct ? "✓ " : "✗ ");
        moraBreakdownText.text = sb.ToString();
    }

    private void HandleError(string message)
    {
        resultText.text = $"Error: {message}";
        Debug.LogWarning(message);
    }

    void OnDestroy()
    {
        if (checker != null)
        {
            checker.OnResult -= HandleResult;
            checker.OnError -= HandleError;
        }
    }
}

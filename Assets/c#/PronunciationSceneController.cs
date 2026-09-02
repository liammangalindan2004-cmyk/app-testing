using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using JapaneseLearning.Pronunciation;

/// <summary>
/// Wires a "record and check pronunciation" scene together:
/// - Tap mic button to start recording
/// - Waveform bars react to live mic input while recording
/// - Result/score appears when Vosk finishes processing
///
/// Attach this to an empty GameObject alongside VoskPronunciationChecker,
/// then drag your scene's UI elements into the fields below.
/// </summary>
public class PronunciationSceneController : MonoBehaviour
{
    [Header("Core")]
    public VoskPronunciationChecker checker;

    [Header("UI - Target")]
    [Tooltip("Shows the character/word the player should pronounce, e.g. 学生")]
    public TMP_Text targetCharacterText;
    [Tooltip("Optional: shows the reading in kana, e.g. がくせい")]
    public TMP_Text targetReadingText;

    [Header("UI - Mic Button")]
    public Button micButton;
    public Image micIcon;
    public Color micIdleColor = new Color(0.42f, 0.24f, 0.58f); // purple, matches your circle
    public Color micRecordingColor = new Color(0.85f, 0.2f, 0.3f); // red while listening

    [Header("UI - Waveform")]
    [Tooltip("Bar RectTransforms under your waveform container, left to right")]
    public RectTransform[] waveformBars;
    public float waveformMinHeight = 6f;
    public float waveformMaxHeight = 60f;

    [Header("UI - Result")]
    public TMP_Text resultText;
    public TMP_Text scoreText;

    [Header("Current Lesson Item")]
    [Tooltip("Set these from your lesson/vocab system before the player taps the mic")]
    public string currentCharacter = "学生";
    public string currentReadingKana = "がくせい";
    public List<string> currentLessonVocabKana = new List<string> { "がくせい", "せんせい", "がっこう" };

    private bool _isBusy;

    void Awake()
    {
        micButton.onClick.AddListener(OnMicButtonPressed);
        checker.OnResult += HandleResult;
        checker.OnError += HandleError;
        RefreshTargetDisplay();
        ResetWaveform();
    }

    /// <summary>
    /// Call this from your lesson controller when moving to a new character.
    /// </summary>
    public void SetLessonItem(string character, string readingKana, List<string> lessonVocabKana)
    {
        currentCharacter = character;
        currentReadingKana = readingKana;
        currentLessonVocabKana = lessonVocabKana;
        RefreshTargetDisplay();
        resultText.text = "";
        scoreText.text = "";
    }

    private void RefreshTargetDisplay()
    {
        if (targetCharacterText != null) targetCharacterText.text = currentCharacter;
        if (targetReadingText != null) targetReadingText.text = currentReadingKana;
    }

    private void OnMicButtonPressed()
    {
        if (_isBusy) return;
        _isBusy = true;

        resultText.text = "Listening...";
        scoreText.text = "";
        if (micIcon != null) micIcon.color = micRecordingColor;

        checker.StartCheck(currentReadingKana, currentLessonVocabKana);
        StartCoroutine(AnimateWaveformWhileRecording());
    }

    /// <summary>
    /// Reads live mic amplitude and scales the waveform bars accordingly.
    /// Runs for the duration of checker.maxRecordSeconds, matching StartCheck.
    /// </summary>
    private IEnumerator AnimateWaveformWhileRecording()
    {
        float elapsed = 0f;
        int sampleWindow = 128;
        float[] samples = new float[sampleWindow];

        // Unity needs a frame for Microphone.Start (called inside checker) to register
        // before checker.CurrentClip / CurrentMicDevice are populated.
        yield return null;

        AudioClip clip = checker.CurrentClip;
        string micDevice = checker.CurrentMicDevice;

        while (elapsed < checker.maxRecordSeconds && clip != null && micDevice != null)
        {
            int micPos = Microphone.GetPosition(micDevice) - sampleWindow;
            if (micPos > 0)
            {
                clip.GetData(samples, micPos);
                float rms = 0f;
                for (int i = 0; i < samples.Length; i++)
                    rms += samples[i] * samples[i];
                rms = Mathf.Sqrt(rms / samples.Length);

                UpdateWaveformBars(rms);
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        ResetWaveform();
    }

    private void UpdateWaveformBars(float rms)
    {
        if (waveformBars == null || waveformBars.Length == 0) return;

        // Amplify since raw mic RMS is usually small; tune this multiplier by ear.
        float amplified = Mathf.Clamp01(rms * 8f);
        float targetHeight = Mathf.Lerp(waveformMinHeight, waveformMaxHeight, amplified);

        foreach (var bar in waveformBars)
        {
            if (bar == null) continue;
            // Slight per-bar jitter so it doesn't look like one flat block.
            float jitter = Random.Range(0.8f, 1.05f);
            var size = bar.sizeDelta;
            size.y = Mathf.Lerp(size.y, targetHeight * jitter, 0.5f);
            bar.sizeDelta = size;
        }
    }

    private void ResetWaveform()
    {
        if (waveformBars == null) return;
        foreach (var bar in waveformBars)
        {
            if (bar == null) continue;
            var size = bar.sizeDelta;
            size.y = waveformMinHeight;
            bar.sizeDelta = size;
        }
    }

    private void HandleResult(PronunciationResult result)
    {
        _isBusy = false;
        if (micIcon != null) micIcon.color = micIdleColor;

        scoreText.text = $"{result.score}%";

        var sb = new StringBuilder();
        sb.AppendLine($"Heard: {result.recognizedText}");
        foreach (var mora in result.moraBreakdown)
            sb.Append(mora.mora).Append(mora.correct ? "✓ " : "✗ ");
        resultText.text = sb.ToString();
    }

    private void HandleError(string message)
    {
        _isBusy = false;
        if (micIcon != null) micIcon.color = micIdleColor;
        resultText.text = $"Couldn't hear that — try again.";
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

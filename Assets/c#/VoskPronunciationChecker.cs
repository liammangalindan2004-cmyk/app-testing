using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Vosk;

namespace JapaneseLearning.Pronunciation
{
    /// <summary>
    /// Records the mic, runs offline Vosk recognition constrained to the
    /// current lesson's vocabulary, and raises OnResult with a scored,
    /// mora-by-mora pronunciation breakdown.
    ///
    /// Setup required before use (see SETUP_STEPS.txt):
    /// - Vosk C# bindings (Vosk.cs) + native libvosk library in Plugins/
    /// - com.unity.nuget.newtonsoft-json package installed
    /// - vosk-model-small-ja-0.22 folder placed in StreamingAssets/
    /// - Microphone permission (Player Settings, and runtime request on
    ///   Android/iOS)
    /// </summary>
    public class VoskPronunciationChecker : MonoBehaviour
    {
        [Header("Model")]
        [Tooltip("Folder name of the Vosk model inside StreamingAssets/")]
        public string modelFolderName = "vosk-model-small-ja-0.22";

        [Header("Recording")]
        public int sampleRate = 16000;
        public int maxRecordSeconds = 4;
        [Tooltip("If the mic button is released before this many seconds have been recorded, treat it as too short to recognize instead of running Vosk on near-silence.")]
        public float minRecordSeconds = 0.3f;

        public event Action<PronunciationResult> OnResult;
        public event Action<string> OnError;

        /// <summary>Exposes the in-progress recording clip, e.g. for waveform visualization.</summary>
        public AudioClip CurrentClip => _clip;

        /// <summary>Exposes the mic device name currently in use.</summary>
        public string CurrentMicDevice => _micDevice;

        /// <summary>True while a press-and-hold recording is in progress.</summary>
        public bool IsRecording => _isRecording;

        private Model _model;
        private VoskRecognizer _recognizer;
        private AudioClip _clip;
        private string _micDevice;
        private bool _isRecording;
        private string _pendingExpectedReadingKana;
        private Coroutine _autoStopCoroutine;

        void Awake()
        {
            string modelPath = Path.Combine(Application.streamingAssetsPath, modelFolderName);

            if (!Directory.Exists(modelPath))
            {
                Debug.LogError($"Vosk model not found at {modelPath}. " +
                                "Download vosk-model-small-ja-0.22 and place it in StreamingAssets.");
                return;
            }

            _model = new Model(modelPath);
            _micDevice = Microphone.devices.Length > 0 ? Microphone.devices[0] : null;

            if (_micDevice == null)
                Debug.LogError("No microphone device found.");
        }

        /// <summary>
        /// Starts recording immediately, with no fixed duration — call
        /// StopRecordingAndProcess() (e.g. on mic-button release) to end it and
        /// run recognition on whatever was captured. This is what makes a
        /// press-and-hold mic button possible, instead of always recording a
        /// fixed window regardless of when the player actually spoke.
        /// Recording still auto-stops after maxRecordSeconds as a safety net,
        /// in case release never fires (e.g. the app loses focus mid-press).
        /// </summary>
        /// <param name="expectedReadingKana">
        /// The target reading in hiragana/katakana, e.g. "がくせい".
        /// </param>
        /// <param name="lessonVocabularyKana">
        /// Other kana readings from the current lesson. Constraining Vosk to
        /// this small vocabulary (a "grammar") avoids kanji-guessing errors
        /// and greatly improves accuracy vs. open-vocabulary recognition.
        /// </param>
        public void StartRecording(string expectedReadingKana, List<string> lessonVocabularyKana)
        {
            if (_isRecording)
            {
                OnError?.Invoke("Already recording.");
                return;
            }
            if (_model == null || _micDevice == null)
            {
                OnError?.Invoke("Vosk model or microphone not initialized.");
                return;
            }

            var vocab = new List<string>(lessonVocabularyKana ?? new List<string>());
            if (!vocab.Contains(expectedReadingKana))
                vocab.Add(expectedReadingKana);

            string grammarJson = "[\"" + string.Join("\",\"", vocab) + "\", \"[unk]\"]";

            _recognizer = new VoskRecognizer(_model, sampleRate, grammarJson);
            _recognizer.SetWords(true); // ask Vosk to include per-word confidence

            _pendingExpectedReadingKana = expectedReadingKana;
            _clip = Microphone.Start(_micDevice, false, maxRecordSeconds, sampleRate);
            _isRecording = true;

            StartCoroutine(LogClipInfoNextFrame());
            _autoStopCoroutine = StartCoroutine(AutoStopAfterMaxDuration());
        }

        private IEnumerator AutoStopAfterMaxDuration()
        {
            yield return new WaitForSeconds(maxRecordSeconds);
            _autoStopCoroutine = null;
            if (_isRecording) FinishRecordingAndProcess();
        }

        /// <summary>
        /// Ends the current recording early (e.g. on mic-button release) and
        /// runs recognition on whatever audio was captured so far. Safe to
        /// call when nothing is recording — does nothing in that case.
        /// </summary>
        public void StopRecordingAndProcess()
        {
            if (!_isRecording) return;

            if (_autoStopCoroutine != null)
            {
                StopCoroutine(_autoStopCoroutine);
                _autoStopCoroutine = null;
            }

            FinishRecordingAndProcess();
        }

        /// <summary>
        /// Starts a 1-shot pronunciation check that always records for the
        /// full maxRecordSeconds window before processing. Kept for the
        /// tap-to-record example UI (PronunciationCheckExampleUI) — prefer
        /// StartRecording()/StopRecordingAndProcess() for a press-and-hold
        /// mic button.
        /// </summary>
        /// <param name="expectedReadingKana">
        /// The target reading in hiragana/katakana, e.g. "がくせい".
        /// </param>
        /// <param name="lessonVocabularyKana">
        /// Other kana readings from the current lesson. Constraining Vosk to
        /// this small vocabulary (a "grammar") avoids kanji-guessing errors
        /// and greatly improves accuracy vs. open-vocabulary recognition.
        /// </param>
        public void StartCheck(string expectedReadingKana, List<string> lessonVocabularyKana)
        {
            if (_isRecording)
            {
                OnError?.Invoke("Already recording.");
                return;
            }
            if (_model == null || _micDevice == null)
            {
                OnError?.Invoke("Vosk model or microphone not initialized.");
                return;
            }

            var vocab = new List<string>(lessonVocabularyKana ?? new List<string>());
            if (!vocab.Contains(expectedReadingKana))
                vocab.Add(expectedReadingKana);

            string grammarJson = "[\"" + string.Join("\",\"", vocab) + "\", \"[unk]\"]";

            _recognizer = new VoskRecognizer(_model, sampleRate, grammarJson);
            _recognizer.SetWords(true); // ask Vosk to include per-word confidence

            _pendingExpectedReadingKana = expectedReadingKana;
            _clip = Microphone.Start(_micDevice, false, maxRecordSeconds, sampleRate);
            _isRecording = true;

            StartCoroutine(LogClipInfoNextFrame());
            StartCoroutine(WaitFullDurationThenFinish());
        }

        private IEnumerator WaitFullDurationThenFinish()
        {
            yield return new WaitForSeconds(maxRecordSeconds);
            if (_isRecording) FinishRecordingAndProcess();
        }

        /// <summary>
        /// Waits a frame for Microphone.Start to finish initializing, then
        /// logs whether the actual clip frequency/channel count matches what
        /// we asked for. Unity can silently give back a different rate.
        /// </summary>
        private IEnumerator LogClipInfoNextFrame()
        {
            yield return null;
            if (_clip != null)
            {
                Debug.Log($"[Vosk mic] requested {sampleRate}Hz mono, got clip: " +
                           $"{_clip.frequency}Hz, {_clip.channels} channel(s).");
            }
        }

        /// <summary>
        /// Downmixes an interleaved multi-channel float buffer to mono by
        /// averaging channels. Vosk expects single-channel audio.
        /// </summary>
        private static float[] DownmixToMono(float[] samples, int channels)
        {
            if (channels <= 1) return samples;

            int frameCount = samples.Length / channels;
            float[] mono = new float[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                    sum += samples[i * channels + c];
                mono[i] = sum / channels;
            }
            return mono;
        }

        /// <summary>
        /// Linear-interpolation resample. Not broadcast-quality, but plenty
        /// accurate for feeding a speech recognizer. Unity's Microphone.Start
        /// does NOT guarantee it honors the requested sampleRate — many
        /// devices (especially in-Editor default mics) silently substitute
        /// a different rate (commonly 44100/48000Hz). If we hand that audio
        /// straight to a VoskRecognizer configured for `sampleRate` (e.g.
        /// 16000Hz), the pitch/timing is effectively scrambled and Vosk will
        /// never recognize anything — producing a consistent 0% score
        /// regardless of what was actually said. Always resample to match
        /// what the recognizer expects.
        /// </summary>
        private static float[] Resample(float[] samples, int fromRate, int toRate)
        {
            if (fromRate == toRate || samples.Length == 0) return samples;

            double ratio = (double)toRate / fromRate;
            int outputLength = Mathf.Max(1, Mathf.RoundToInt(samples.Length * (float)ratio));
            float[] result = new float[outputLength];

            for (int i = 0; i < outputLength; i++)
            {
                double srcPos = i / ratio;
                int srcIndex = (int)srcPos;
                double frac = srcPos - srcIndex;

                if (srcIndex + 1 < samples.Length)
                    result[i] = Mathf.Lerp(samples[srcIndex], samples[srcIndex + 1], (float)frac);
                else
                    result[i] = samples[Mathf.Min(srcIndex, samples.Length - 1)];
            }

            return result;
        }

        /// <summary>
        /// Stops the mic and runs Vosk recognition on whatever was captured.
        /// Shared by both StopRecordingAndProcess() (press-and-hold) and the
        /// auto-stop timers in StartRecording()/StartCheck() (safety cap /
        /// tap-to-record).
        /// </summary>
        private void FinishRecordingAndProcess()
        {
            string expectedReadingKana = _pendingExpectedReadingKana;

            int micPos = Microphone.GetPosition(_micDevice);
            Microphone.End(_micDevice);
            _isRecording = false;

            if (micPos <= 0)
            {
                OnError?.Invoke("No audio captured.");
                return;
            }

            // If the button was released almost immediately, there isn't
            // enough audio for Vosk to work with — that's a "hold it a bit
            // longer" situation, not a "couldn't understand you" one, so
            // give a distinct, more useful message instead of running
            // recognition on near-silence and getting a generic failure.
            float recordedSeconds = micPos / (float)_clip.frequency;
            if (recordedSeconds < minRecordSeconds)
            {
                OnError?.Invoke("Hold the mic button a little longer while you speak.");
                return;
            }

            float[] samples = new float[micPos * _clip.channels];
            _clip.GetData(samples, 0);

            // Vosk expects mono. Microphone.Start may hand back a
            // multi-channel clip (e.g. stereo mic input) even though we
            // only asked for one logical stream.
            if (_clip.channels > 1)
                samples = DownmixToMono(samples, _clip.channels);

            // Unity's Microphone.Start does NOT guarantee it records at the
            // rate we requested — it silently substitutes a rate the device
            // actually supports (commonly 44100/48000Hz instead of 16000Hz).
            // The VoskRecognizer above was constructed assuming `sampleRate`.
            // If the clip came back at a different rate and we don't correct
            // for it here, every recording is effectively garbled from
            // Vosk's point of view, which produces a consistent 0% score no
            // matter what was actually said. Resample to match.
            if (_clip.frequency != sampleRate)
            {
                Debug.LogWarning($"[Vosk] Mic recorded at {_clip.frequency}Hz but recognizer " +
                                  $"expects {sampleRate}Hz — resampling before recognition.");
                samples = Resample(samples, _clip.frequency, sampleRate);
            }

            byte[] pcm = ConvertFloatTo16BitPCM(samples);
            _recognizer.AcceptWaveform(pcm, pcm.Length);
            string resultJson = _recognizer.FinalResult();
            Debug.Log($"[Vosk raw result] {resultJson}");
            _recognizer.Dispose();
            _recognizer = null;

            PronunciationResult evaluation;
            try
            {
                evaluation = PronunciationScorer.Evaluate(resultJson, expectedReadingKana);
            }
            catch (Exception e)
            {
                OnError?.Invoke($"Failed to parse Vosk result: {e.Message}");
                return;
            }

            OnResult?.Invoke(evaluation);
        }

        private static byte[] ConvertFloatTo16BitPCM(float[] samples)
        {
            byte[] bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short val = (short)Mathf.Clamp(samples[i] * 32767f, -32768f, 32767f);
                bytes[i * 2] = (byte)(val & 0xFF);
                bytes[i * 2 + 1] = (byte)((val >> 8) & 0xFF);
            }
            return bytes;
        }

        void OnDestroy()
        {
            _recognizer?.Dispose();
            _model?.Dispose();
        }
    }
}
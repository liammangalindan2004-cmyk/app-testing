using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Vosk;

namespace JapaneseLearning.Pronunciation
{
    /// <summary>
    /// DEBUG VERSION - Logs everything, catches all crashes, shows exact failure point
    /// </summary>
    public class VoskPronunciationChecker : MonoBehaviour
    {
        [Header("Model")]
        [Tooltip("Folder name of the Vosk model inside StreamingAssets/")]
        public string modelFolderName = "vosk-model-small-ja-0.22";

        [Header("Recording")]
        public int sampleRate = 16000;
        public int maxRecordSeconds = 4;
        public float minRecordSeconds = 0.3f;

        [Header("Debug")]
        public bool verboseLogging = true;

        public event Action<PronunciationResult> OnResult;
        public event Action<string> OnError;

        public AudioClip CurrentClip => _clip;
        public string CurrentMicDevice => _micDevice;
        public bool IsRecording => _isRecording;

        private enum InitState { NotStarted, Failed, Success }
        private InitState _initState = InitState.NotStarted;
        private string _initErrorMessage = "";

        private Model _model;
        private VoskRecognizer _recognizer;
        private AudioClip _clip;
        private string _micDevice;
        private bool _isRecording;
        private string _pendingExpectedReadingKana;
        private Coroutine _autoStopCoroutine;

        void Awake()
        {
            Log("════════════════════════════════════════");
            Log("VOSK INITIALIZATION STARTING");
            Log("════════════════════════════════════════");

            try
            {
                Log("Step 1: Checking platform");
#if UNITY_ANDROID
                Log("  → Platform: ANDROID");
                Log("  → Requesting microphone permission...");
                PermissionManager.RequestMicrophonePermission();
#else
                Log("  → Platform: NOT ANDROID (Editor/Standalone)");
#endif

                Log("\nStep 2: Getting model path");
                string modelPath = GetModelPath();
                Log($"  → Model path: {modelPath}");

                if (string.IsNullOrEmpty(modelPath))
                {
                    throw new Exception("Model path is null or empty!");
                }

                Log("\nStep 3: Checking if model directory exists");
                bool modelDirExists = Directory.Exists(modelPath);
                Log($"  → Directory.Exists({modelPath}): {modelDirExists}");

                if (!modelDirExists)
                {
                    throw new Exception($"❌ Model directory NOT FOUND at: {modelPath}\n" +
                        "→ Make sure VoskModelExtractor extracted files on app startup.\n" +
                        "→ Check Console for [VoskModelExtractor] logs.\n" +
                        "→ On emulator, microphone may not work anyway.");
                }

                Log($"  → ✓ Model directory found!");

                Log("\nStep 4: Creating Model object from Vosk");
                _model = new Model(modelPath);
                Log($"  → ✓ Model object created successfully");

                Log("\nStep 5: Checking for microphone devices");
                Log($"  → Microphone.devices.Length: {Microphone.devices.Length}");
                
                if (Microphone.devices.Length == 0)
                {
                    Log("  → ⚠️ WARNING: No microphone devices found!");
                    Log("  → This is NORMAL on emulators (they don't have mics)");
                    Log("  → TEST ON A REAL PHONE instead");
                    throw new Exception("No microphone devices found. " +
                        "Emulators don't have working microphones. Test on a real Android phone.");
                }

                Log($"  → Microphone devices available:");
                foreach (string device in Microphone.devices)
                {
                    Log($"     - {device}");
                }

                _micDevice = Microphone.devices[0];
                Log($"  → ✓ Selected microphone: {_micDevice}");

                _initState = InitState.Success;
                Log("\n════════════════════════════════════════");
                Log("✓✓✓ VOSK INITIALIZED SUCCESSFULLY ✓✓✓");
                Log("════════════════════════════════════════");
            }
            catch (Exception ex)
            {
                _initState = InitState.Failed;
                _initErrorMessage = ex.Message;
                
                Log("\n════════════════════════════════════════");
                Log("❌ VOSK INITIALIZATION FAILED");
                Log("════════════════════════════════════════");
                Log($"Error: {ex.Message}");
                Log($"Stack trace:\n{ex.StackTrace}");
                Log("════════════════════════════════════════\n");
            }
        }

        private string GetModelPath()
        {
            string modelFolderPath = modelFolderName;

#if UNITY_ANDROID
            string streamingPath = Path.Combine(Application.streamingAssetsPath, modelFolderPath);
            string persistentPath = Path.Combine(Application.persistentDataPath, modelFolderPath);

            Log($"  → Streaming Assets path: {streamingPath}");
            Log($"  → Persistent Data path: {persistentPath}");
            Log($"  → Streaming Assets exists: {Directory.Exists(streamingPath)}");
            Log($"  → Persistent Data exists: {Directory.Exists(persistentPath)}");

            return persistentPath;
#else
            return Path.Combine(Application.streamingAssetsPath, modelFolderPath);
#endif
        }

        public void StartRecording(string expectedReadingKana, List<string> lessonVocabularyKana)
        {
            Log("\n>>> StartRecording() called");
            Log($"    IsRecording: {_isRecording}");
            Log($"    InitState: {_initState}");

            try
            {
                // Check 1
                if (_isRecording)
                {
                    Log("    ❌ Already recording");
                    OnError?.Invoke("Already recording.");
                    return;
                }

                // Check 2
                if (_initState != InitState.Success)
                {
                    string msg = $"Vosk not initialized. Reason: {_initErrorMessage}";
                    Log($"    ❌ {msg}");
                    OnError?.Invoke(msg);
                    return;
                }

                // Check 3
                if (_model == null)
                {
                    Log("    ❌ Model is null");
                    OnError?.Invoke("Internal error: Model is null.");
                    return;
                }

                // Check 4
                if (_micDevice == null)
                {
                    Log("    ❌ Microphone device is null");
                    OnError?.Invoke("No microphone device available. Use a real Android phone (emulator mics don't work).");
                    return;
                }

                // Create recognizer
                Log("    Creating VoskRecognizer...");
                var vocab = new List<string>(lessonVocabularyKana ?? new List<string>());
                if (!vocab.Contains(expectedReadingKana))
                    vocab.Add(expectedReadingKana);

                string grammarJson = "[\"" + string.Join("\",\"", vocab) + "\", \"[unk]\"]";
                _recognizer = new VoskRecognizer(_model, sampleRate, grammarJson);
                _recognizer.SetWords(true);
                Log($"    ✓ VoskRecognizer created");

                // Start recording
                Log($"    Starting Microphone.Start(\"{_micDevice}\", false, {maxRecordSeconds}, {sampleRate})...");
                _clip = Microphone.Start(_micDevice, false, maxRecordSeconds, sampleRate);
                
                if (_clip == null)
                {
                    Log("    ❌ Microphone.Start() returned NULL");
                    throw new Exception("Microphone.Start returned null audio clip!");
                }

                _isRecording = true;
                _pendingExpectedReadingKana = expectedReadingKana;

                Log($"    ✓ Recording started");
                Log($"    ✓ Clip properties: {_clip.frequency}Hz, {_clip.channels}ch, {_clip.length:F2}s");

                StartCoroutine(LogClipInfoNextFrame());
                _autoStopCoroutine = StartCoroutine(AutoStopAfterMaxDuration());
            }
            catch (Exception ex)
            {
                Log($"    ❌ EXCEPTION: {ex.Message}");
                Log($"    Stack: {ex.StackTrace}");
                _isRecording = false;
                if (_recognizer != null)
                {
                    _recognizer.Dispose();
                    _recognizer = null;
                }
                OnError?.Invoke($"Failed to start recording: {ex.Message}");
            }
        }

        private IEnumerator AutoStopAfterMaxDuration()
        {
            yield return new WaitForSeconds(maxRecordSeconds);
            _autoStopCoroutine = null;
            if (_isRecording)
            {
                Log("Auto-stopping after max duration");
                FinishRecordingAndProcess();
            }
        }

        public void StopRecordingAndProcess()
        {
            Log("\n>>> StopRecordingAndProcess() called");
            if (!_isRecording)
            {
                Log("    Not recording, returning");
                return;
            }

            if (_autoStopCoroutine != null)
            {
                StopCoroutine(_autoStopCoroutine);
                _autoStopCoroutine = null;
            }

            FinishRecordingAndProcess();
        }

        public void StartCheck(string expectedReadingKana, List<string> lessonVocabularyKana)
        {
            Log("\n>>> StartCheck() called");
            if (_isRecording)
            {
                OnError?.Invoke("Already recording.");
                return;
            }
            if (_initState != InitState.Success)
            {
                OnError?.Invoke($"Vosk not initialized: {_initErrorMessage}");
                return;
            }
            if (_model == null || _micDevice == null)
            {
                OnError?.Invoke("Vosk model or microphone not initialized.");
                return;
            }

            try
            {
                var vocab = new List<string>(lessonVocabularyKana ?? new List<string>());
                if (!vocab.Contains(expectedReadingKana))
                    vocab.Add(expectedReadingKana);

                string grammarJson = "[\"" + string.Join("\",\"", vocab) + "\", \"[unk]\"]";

                _recognizer = new VoskRecognizer(_model, sampleRate, grammarJson);
                _recognizer.SetWords(true);

                _pendingExpectedReadingKana = expectedReadingKana;
                _clip = Microphone.Start(_micDevice, false, maxRecordSeconds, sampleRate);
                _isRecording = true;

                StartCoroutine(LogClipInfoNextFrame());
                StartCoroutine(WaitFullDurationThenFinish());
            }
            catch (Exception ex)
            {
                Log($"Exception in StartCheck: {ex}");
                _isRecording = false;
                OnError?.Invoke($"Failed to start check: {ex.Message}");
            }
        }

        private IEnumerator WaitFullDurationThenFinish()
        {
            yield return new WaitForSeconds(maxRecordSeconds);
            if (_isRecording) FinishRecordingAndProcess();
        }

        private IEnumerator LogClipInfoNextFrame()
        {
            yield return null;
            if (_clip != null)
            {
                Log($"    Clip info: {_clip.frequency}Hz, {_clip.channels}ch");
            }
        }

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

        private void FinishRecordingAndProcess()
        {
            try
            {
                Log("\n>>> FinishRecordingAndProcess() called");

                string expectedReadingKana = _pendingExpectedReadingKana;
                int micPos = Microphone.GetPosition(_micDevice);
                Microphone.End(_micDevice);
                _isRecording = false;

                Log($"    Mic position: {micPos}");

                if (micPos <= 0)
                {
                    Log("    No audio captured");
                    OnError?.Invoke("No audio captured.");
                    return;
                }

                float recordedSeconds = micPos / (float)_clip.frequency;
                Log($"    Recorded: {recordedSeconds:F2}s");

                if (recordedSeconds < minRecordSeconds)
                {
                    Log($"    Too short (< {minRecordSeconds}s)");
                    OnError?.Invoke("Hold the mic button longer.");
                    return;
                }

                if (_clip == null)
                {
                    throw new Exception("_clip is null!");
                }

                float[] samples = new float[micPos * _clip.channels];
                _clip.GetData(samples, 0);

                if (_clip.channels > 1)
                    samples = DownmixToMono(samples, _clip.channels);

                if (_clip.frequency != sampleRate)
                {
                    Log($"    Resampling {_clip.frequency}Hz → {sampleRate}Hz");
                    samples = Resample(samples, _clip.frequency, sampleRate);
                }

                if (_recognizer == null)
                    throw new Exception("_recognizer is null!");

                byte[] pcm = ConvertFloatTo16BitPCM(samples);
                _recognizer.AcceptWaveform(pcm, pcm.Length);
                string resultJson = _recognizer.FinalResult();
                Log($"    Vosk result: {resultJson}");
                _recognizer.Dispose();
                _recognizer = null;

                PronunciationResult evaluation;
                try
                {
                    evaluation = PronunciationScorer.Evaluate(resultJson, expectedReadingKana);
                }
                catch (Exception ex)
                {
                    Log($"    Failed to evaluate: {ex.Message}");
                    OnError?.Invoke($"Failed to parse result: {ex.Message}");
                    return;
                }

                Log($"    ✓ Evaluation complete");
                OnResult?.Invoke(evaluation);
            }
            catch (Exception ex)
            {
                Log($"    ❌ EXCEPTION: {ex.Message}");
                Log($"    Stack: {ex.StackTrace}");
                OnError?.Invoke($"Processing failed: {ex.Message}");
            }
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
            try
            {
                _recognizer?.Dispose();
                _model?.Dispose();
            }
            catch (Exception ex)
            {
                Log($"Exception in OnDestroy: {ex}");
            }
        }

        private void Log(string msg)
        {
            Debug.Log($"[VOSK_DEBUG] {msg}");
        }
    }
}

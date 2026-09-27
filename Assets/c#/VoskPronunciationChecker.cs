using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Vosk;
 
namespace JapaneseLearning.Pronunciation
{
    /// <summary>
    /// DEBUG VERSION - Logs everything, catches all C# exceptions, shows exact failure point.
    ///
    /// FIX: Model initialization no longer happens in Awake() based on a racy
    /// Directory.Exists() check. It now waits for VoskModelExtractor to report a
    /// VERIFIED, complete extraction before ever calling `new Model(...)`. Loading a
    /// partial/incomplete model is what caused the native SIGSEGV crash you hit — that
    /// crash happens inside native (Kaldi/Vosk) code and cannot be caught by C# try/catch,
    /// which is why it was instant and invisible in the Unity console.
    /// </summary>
    public class VoskPronunciationChecker : MonoBehaviour
    {
        [Header("Model")]
        [Tooltip("Folder name of the Vosk model inside StreamingAssets/ — must match VoskModelExtractor")]
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
 
        private enum InitState { NotStarted, WaitingForModel, Failed, Success }
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
 
#if UNITY_ANDROID
            Log("  → Platform: ANDROID — requesting microphone permission...");
            PermissionManager.RequestMicrophonePermission();
#else
            Log("  → Platform: NOT ANDROID (Editor/Standalone)");
#endif
 
            _initState = InitState.WaitingForModel;
 
            // Never touch new Model(...) until extraction is verified complete.
            // This is the fix for the crash: previously this ran immediately in Awake()
            // against a Directory.Exists() check that could be true before any files
            // were actually copied.
            if (VoskModelExtractor.IsExtractionFinished)
            {
                // Extractor already finished (e.g. this component was enabled late).
                HandleExtractionResult(VoskModelExtractor.IsModelReady);
            }
            else
            {
                Log("  → Waiting for VoskModelExtractor to finish verifying the model...");
                VoskModelExtractor.OnExtractionComplete += HandleExtractionResult;
            }
        }
 
        void OnDestroy()
        {
            VoskModelExtractor.OnExtractionComplete -= HandleExtractionResult;
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
 
        private void HandleExtractionResult(bool modelReady)
        {
            VoskModelExtractor.OnExtractionComplete -= HandleExtractionResult;
 
            if (!modelReady)
            {
                _initState = InitState.Failed;
                _initErrorMessage = "Model extraction failed or was incomplete. Check [VoskModelExtractor] logs — " +
                    "the model will NOT be loaded to avoid a native crash.";
                Log($"❌ {_initErrorMessage}");
                OnError?.Invoke(_initErrorMessage);
                return;
            }
 
            InitializeVoskModel();
        }
 
        private void InitializeVoskModel()
        {
            try
            {
                string modelPath = VoskModelExtractor.ModelPersistentPath;
                Log($"\nStep: Loading verified model at: {modelPath}");
 
                if (string.IsNullOrEmpty(modelPath) || !Directory.Exists(modelPath))
                {
                    throw new Exception($"Model path missing after extraction reported success: {modelPath}");
                }
 
                // Belt-and-suspenders: refuse to load if an essential config file is absent,
                // even though the extractor already verified every manifest file individually.
                string modelConfPath = Path.Combine(modelPath, "conf", "model.conf");
                if (!File.Exists(modelConfPath))
                {
                    Log("  → ⚠️ conf/model.conf not found — this model folder may still be incomplete " +
                        "(older/non-standard model layouts may not have this file; continuing cautiously).");
                }
 
                _model = new Model(modelPath);
                Log("  → ✓ Model object created successfully");
 
                Log($"\nStep: Checking for microphone devices");
                Log($"  → Microphone.devices.Length: {Microphone.devices.Length}");
 
                if (Microphone.devices.Length == 0)
                {
                    throw new Exception("No microphone devices found. " +
                        "Emulators without a configured virtual mic won't show any devices here — " +
                        "test on a real Android phone.");
                }
 
                foreach (string device in Microphone.devices)
                    Log($"     - {device}");
 
                _micDevice = Microphone.devices[0];
                Log($"  → ✓ Selected microphone: {_micDevice}");
 
                _initState = InitState.Success;
                Log("✓✓✓ VOSK INITIALIZED SUCCESSFULLY ✓✓✓");
            }
            catch (Exception ex)
            {
                _initState = InitState.Failed;
                _initErrorMessage = ex.Message;
                Log($"❌ VOSK INITIALIZATION FAILED: {ex.Message}\n{ex.StackTrace}");
                OnError?.Invoke(_initErrorMessage);
            }
        }
 
        public void StartRecording(string expectedReadingKana, List<string> lessonVocabularyKana)
        {
            Log("\n>>> StartRecording() called");
            Log($"    IsRecording: {_isRecording}");
            Log($"    InitState: {_initState}");
 
            try
            {
                if (_isRecording)
                {
                    OnError?.Invoke("Already recording.");
                    return;
                }
 
                if (_initState == InitState.WaitingForModel)
                {
                    OnError?.Invoke("Still preparing the pronunciation model, please wait a moment and try again.");
                    return;
                }
 
                if (_initState != InitState.Success)
                {
                    OnError?.Invoke($"Vosk not initialized. Reason: {_initErrorMessage}");
                    return;
                }
 
                if (_model == null)
                {
                    OnError?.Invoke("Internal error: Model is null.");
                    return;
                }
 
                if (_micDevice == null)
                {
                    OnError?.Invoke("No microphone device available. Use a real Android phone (emulator mics may not work).");
                    return;
                }
 
                var vocab = new List<string>(lessonVocabularyKana ?? new List<string>());
                if (!vocab.Contains(expectedReadingKana))
                    vocab.Add(expectedReadingKana);
 
                string grammarJson = "[\"" + string.Join("\",\"", vocab) + "\", \"[unk]\"]";
                _recognizer = new VoskRecognizer(_model, sampleRate, grammarJson);
                _recognizer.SetWords(true);
                Log("    ✓ VoskRecognizer created");
 
                _clip = Microphone.Start(_micDevice, false, maxRecordSeconds, sampleRate);
 
                if (_clip == null)
                    throw new Exception("Microphone.Start returned null audio clip!");
 
                _isRecording = true;
                _pendingExpectedReadingKana = expectedReadingKana;
 
                Log($"    ✓ Recording started ({_clip.frequency}Hz, {_clip.channels}ch, {_clip.length:F2}s)");
 
                StartCoroutine(LogClipInfoNextFrame());
                _autoStopCoroutine = StartCoroutine(AutoStopAfterMaxDuration());
            }
            catch (Exception ex)
            {
                Log($"    ❌ EXCEPTION: {ex.Message}\n{ex.StackTrace}");
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
            if (_initState == InitState.WaitingForModel)
            {
                OnError?.Invoke("Still preparing the pronunciation model, please wait a moment and try again.");
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
                Log($"    Clip info: {_clip.frequency}Hz, {_clip.channels}ch");
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
                    OnError?.Invoke("No audio captured.");
                    return;
                }
 
                float recordedSeconds = micPos / (float)_clip.frequency;
                Log($"    Recorded: {recordedSeconds:F2}s");
 
                if (recordedSeconds < minRecordSeconds)
                {
                    OnError?.Invoke("Hold the mic button longer.");
                    return;
                }
 
                if (_clip == null)
                    throw new Exception("_clip is null!");
 
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
                    OnError?.Invoke($"Failed to parse result: {ex.Message}");
                    return;
                }
 
                Log("    ✓ Evaluation complete");
                OnResult?.Invoke(evaluation);
            }
            catch (Exception ex)
            {
                Log($"    ❌ EXCEPTION: {ex.Message}\n{ex.StackTrace}");
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
 
        private void Log(string msg)
        {
            if (verboseLogging)
                Debug.Log($"[VOSK_DEBUG] {msg}");
        }
    }
}
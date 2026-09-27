using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Extracts the COMPLETE Vosk model from StreamingAssets to persistent storage on startup.
///
/// This replaces the old hardcoded 5-file extraction, which never copied a full/valid
/// model and caused a native SIGSEGV crash the moment Vosk tried to load it (native model
/// loaders don't throw catchable C# exceptions on malformed models — they crash the process).
///
/// It reads a manifest.txt (generated in-editor via Tools > Vosk > Generate Model Manifest)
/// that lists every file in the model folder, copies each one, and only reports success once
/// every file is verified present on disk. Nothing that depends on the model should proceed
/// until OnExtractionComplete(true) has fired.
///
/// SETUP:
/// 1. Put the FULL vosk model folder in Assets/StreamingAssets/<modelFolderName>/
/// 2. Run Tools > Vosk > Generate Model Manifest on that folder (writes manifest.txt inside it)
/// 3. Create an empty GameObject named "_VoskModelExtractor" in your first scene, attach this script
/// 4. Set Model Folder Name to match (e.g. "vosk-model-small-ja-0.22")
/// 5. VoskPronunciationChecker will automatically wait for this to finish (see that script) —
///    you do not need to wire anything else together.
/// </summary>
public class VoskModelExtractor : MonoBehaviour
{
    private const string ManifestFileName = "manifest.txt";
    private const string CompletionMarkerFileName = ".extraction_ok";

    [SerializeField]
    private string modelFolderName = "vosk-model-small-ja-0.22";

    /// <summary>
    /// Fires once, with true if the full model is verified present on disk and ready to load,
    /// false if extraction failed. Subscribe to this before anything calls into Vosk.
    /// Static so any script can subscribe without needing a scene reference.
    /// </summary>
    public static event Action<bool> OnExtractionComplete;

    /// <summary>
    /// True once extraction has finished (success or failure) this app session.
    /// </summary>
    public static bool IsExtractionFinished { get; private set; }

    /// <summary>
    /// True only if extraction finished successfully and the model is safe to load.
    /// </summary>
    public static bool IsModelReady { get; private set; }

    public static string ModelPersistentPath { get; private set; }

    private static bool _extractionStarted;

    void Start()
    {
        if (_extractionStarted)
        {
            Debug.Log("[VoskModelExtractor] Extraction already started this session.");
            return;
        }
        _extractionStarted = true;

        ModelPersistentPath = Path.Combine(Application.persistentDataPath, modelFolderName);

#if UNITY_ANDROID && !UNITY_EDITOR
        StartCoroutine(ExtractModelCoroutine());
#else
        // On Editor/Standalone, StreamingAssets is a normal folder on disk — no copy needed.
        string directPath = Path.Combine(Application.streamingAssetsPath, modelFolderName);
        bool exists = Directory.Exists(directPath);
        ModelPersistentPath = directPath;
        FinishWith(exists);
        if (!exists)
            Debug.LogError($"[VoskModelExtractor] Model folder not found at {directPath}");
#endif
    }

    private IEnumerator ExtractModelCoroutine()
    {
        // Skip re-extraction only if a previous successful run left the completion marker.
        string markerPath = Path.Combine(ModelPersistentPath, CompletionMarkerFileName);
        if (File.Exists(markerPath))
        {
            Debug.Log($"[VoskModelExtractor] Model already fully extracted at: {ModelPersistentPath}");
            FinishWith(true);
            yield break;
        }

        // Clean any partial extraction from a previous crashed/interrupted run.
        if (Directory.Exists(ModelPersistentPath))
        {
            Directory.Delete(ModelPersistentPath, true);
        }
        Directory.CreateDirectory(ModelPersistentPath);

        // 1. Fetch the manifest listing every file that must be copied.
        string manifestSrc = CombineUrl(Application.streamingAssetsPath, modelFolderName, ManifestFileName);
        Debug.Log($"[VoskModelExtractor] Fetching manifest from: {manifestSrc}");
        List<string> filesToCopy = new List<string>();

        using (UnityWebRequest manifestReq = UnityWebRequest.Get(manifestSrc))
        {
            yield return SendWithTimeout(manifestReq, 15f);

            if (manifestReq.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[VoskModelExtractor] Could not read manifest.txt. " +
                    $"Result={manifestReq.result}, Error='{manifestReq.error}', HTTP={manifestReq.responseCode}. " +
                    "Did you run Tools > Vosk > Generate Model Manifest on the model folder, and is manifest.txt " +
                    "actually inside Assets/StreamingAssets/<model>/? (check it wasn't left outside the folder, " +
                    "and that the .txt extension wasn't stripped/renamed).");
                FinishWith(false);
                yield break;
            }

            string manifestText = manifestReq.downloadHandler.text;
            Debug.Log($"[VoskModelExtractor] Manifest fetched ({manifestText.Length} chars).");
            foreach (string line in manifestText.Split('\n'))
            {
                string trimmed = line.Trim('\r', '\n', ' ');
                if (!string.IsNullOrEmpty(trimmed))
                    filesToCopy.Add(trimmed);
            }
        }

        if (filesToCopy.Count == 0)
        {
            Debug.LogError("[VoskModelExtractor] Manifest was empty. Model is incomplete — aborting.");
            FinishWith(false);
            yield break;
        }

        Debug.Log($"[VoskModelExtractor] Extracting {filesToCopy.Count} files to: {ModelPersistentPath}");

        bool allExtracted = true;
        int done = 0;

        foreach (string relativeFile in filesToCopy)
        {
            string srcUrl = CombineUrl(Application.streamingAssetsPath, modelFolderName, relativeFile);
            string destPath = Path.Combine(ModelPersistentPath, relativeFile.Replace('/', Path.DirectorySeparatorChar));
            string destDir = Path.GetDirectoryName(destPath);

            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            using (UnityWebRequest www = UnityWebRequest.Get(srcUrl))
            {
                Debug.Log($"[VoskModelExtractor] ({done + 1}/{filesToCopy.Count}) fetching '{relativeFile}'...");
                yield return SendWithTimeout(www, 20f);

                bool thisFileOk = false;
                try
                {
                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        // downloadHandler.data can come back null (not empty) for some local
                        // jar:file:// reads even when the request reports success — this is
                        // what killed the coroutine outright before, since File.WriteAllBytes
                        // throws on null and an uncaught exception here silently stops the
                        // whole extraction with no further logs and no callback ever firing.
                        byte[] data = www.downloadHandler.data ?? Array.Empty<byte>();
                        File.WriteAllBytes(destPath, data);
                        thisFileOk = true;
                    }
                    else
                    {
                        Debug.LogError($"[VoskModelExtractor] ✗ Failed '{relativeFile}': " +
                            $"Result={www.result}, Error='{www.error}', HTTP={www.responseCode}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[VoskModelExtractor] ✗ Exception writing '{relativeFile}': {ex.Message}");
                }

                if (thisFileOk)
                {
                    done++;
                    var writtenInfo = new FileInfo(destPath);
                    Debug.Log($"[VoskModelExtractor] ({done}/{filesToCopy.Count}) ✓ '{relativeFile}' " +
                        $"({writtenInfo.Length} bytes)");
                }
                else
                {
                    allExtracted = false;
                    // Keep going so the log shows every problem file, not just the first.
                }
            }
        }

        // Verify every file actually landed on disk. Note: some legitimate Vosk model files
        // (e.g. ivector/online_cmvn.conf in several official models) are intentionally 0 bytes,
        // so we only check existence here — a 0-byte file is not itself a sign of failure.
        // A genuinely failed download is already caught by allExtracted above.
        bool verified = allExtracted;
        if (verified)
        {
            foreach (string relativeFile in filesToCopy)
            {
                string destPath = Path.Combine(ModelPersistentPath, relativeFile.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(destPath))
                {
                    Debug.LogError($"[VoskModelExtractor] Verification failed for '{relativeFile}' (missing).");
                    verified = false;
                }
            }
        }

        if (verified)
        {
            File.WriteAllText(markerPath, DateTime.UtcNow.ToString("o"));
            Debug.Log($"[VoskModelExtractor] Extraction verified complete ({done}/{filesToCopy.Count} files).");
            FinishWith(true);
        }
        else
        {
            Debug.LogError("[VoskModelExtractor] Extraction incomplete — Vosk will NOT be initialized this session " +
                "to avoid a native crash. Fix the missing files / re-generate the manifest and rebuild.");
            FinishWith(false);
        }
    }

    /// <summary>
    /// Sends a UnityWebRequest but aborts it if it hasn't completed within timeoutSeconds.
    /// This exists because UnityWebRequest against jar:/streaming-assets URIs can hang
    /// indefinitely instead of failing on some Android/emulator configurations — without
    /// this, a single stuck file would look exactly like what you're seeing: nothing ever
    /// progresses and nothing ever logs an error.
    /// </summary>
    private static IEnumerator SendWithTimeout(UnityWebRequest request, float timeoutSeconds)
    {
        var op = request.SendWebRequest();
        float elapsed = 0f;
        while (!op.isDone)
        {
            elapsed += Time.unscaledDeltaTime;
            if (elapsed >= timeoutSeconds)
            {
                Debug.LogError($"[VoskModelExtractor] Request to {request.url} timed out after {timeoutSeconds}s — aborting.");
                request.Abort();
                yield break;
            }
            yield return null;
        }
    }

    private static string CombineUrl(params string[] parts)
    {
        return string.Join("/", parts).Replace("\\", "/");
    }

    private static void FinishWith(bool success)
    {
        IsModelReady = success;
        IsExtractionFinished = true;
        OnExtractionComplete?.Invoke(success);
    }
}

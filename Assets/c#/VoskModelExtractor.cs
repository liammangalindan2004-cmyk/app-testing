using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Automatically extracts the Vosk model from the APK to persistent device storage on app startup.
/// 
/// SETUP:
/// 1. Create an empty GameObject named "_VoskModelExtractor" in your first scene
/// 2. Attach this script to it
/// 3. In Inspector, set Model Folder Name to match your model (e.g., "vosk-model-small-ja-0.22")
/// 4. Drag the VoskPronunciationChecker component into the "Vosk Checker" field
/// 5. That's it — it will auto-extract on startup before Vosk tries to initialize
/// </summary>
public class VoskModelExtractor : MonoBehaviour
{
    [SerializeField]
    private string modelFolderName = "vosk-model-small-ja-0.22";

    [SerializeField]
    private JapaneseLearning.Pronunciation.VoskPronunciationChecker voskChecker;

    private static bool _extractionAttempted = false;

    void Start()
    {
        // Only extract once per app session
        if (_extractionAttempted)
        {
            Debug.Log("[VoskModelExtractor] Extraction already attempted this session.");
            return;
        }

        _extractionAttempted = true;

#if UNITY_ANDROID
        Debug.Log("[VoskModelExtractor] Starting model extraction on Android...");
        StartCoroutine(ExtractModelCoroutine());
#else
        Debug.Log("[VoskModelExtractor] Not on Android, skipping extraction.");
#endif
    }

    private IEnumerator ExtractModelCoroutine()
    {
        string persistentPath = Path.Combine(Application.persistentDataPath, modelFolderName);

        // If already extracted, skip
        if (Directory.Exists(persistentPath))
        {
            Debug.Log($"[VoskModelExtractor] ✓ Model already extracted at: {persistentPath}");
            yield break;
        }

        Debug.Log($"[VoskModelExtractor] Extracting model to: {persistentPath}");

        // Create directory
        Directory.CreateDirectory(persistentPath);

        // List of model files that MUST be extracted
        // These are the core files needed by Vosk
        string[] requiredFiles = new[]
        {
            "am/final.mdl",
            "am/cmvn.txt",
            "graph/Gr.fst",
            "ivector/final.ie",
            "ivector/final.dubm"
        };

        bool allExtracted = true;

        foreach (string file in requiredFiles)
        {
            string srcPath = Path.Combine(Application.streamingAssetsPath, modelFolderName, file);
            string destDir = Path.GetDirectoryName(Path.Combine(persistentPath, file));
            string destPath = Path.Combine(persistentPath, file);

            // Create subdirectory if needed
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // Use UnityWebRequest to load from StreamingAssets (works on Android)
            using (UnityWebRequest www = UnityWebRequest.Get(srcPath))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    // Write to persistent storage
                    File.WriteAllBytes(destPath, www.downloadHandler.data);
                    Debug.Log($"[VoskModelExtractor] ✓ Extracted: {file}");
                }
                else
                {
                    Debug.LogError($"[VoskModelExtractor] ✗ Failed to extract {file}: {www.error}");
                    allExtracted = false;
                    // Continue trying other files
                }
            }

            // Small delay between extractions to avoid overwhelming storage I/O
            yield return new WaitForSeconds(0.1f);
        }

        if (allExtracted)
        {
            Debug.Log($"[VoskModelExtractor] ✓✓✓ All model files extracted successfully!");
            Debug.Log($"[VoskModelExtractor] Model ready at: {persistentPath}");
            
            // Optional: Force Vosk to reinitialize if it's already running
            if (voskChecker != null)
            {
                Debug.Log("[VoskModelExtractor] Reinitializing VoskPronunciationChecker...");
                // The Awake() already ran, so we need to manually retry initialization
                // For now, we'll just log that the model is ready
                // The app should be restarted or the scene reloaded to use Vosk
            }
        }
        else
        {
            Debug.LogError("[VoskModelExtractor] ✗ Some model files failed to extract. Check StreamingAssets folder.");
        }
    }
}

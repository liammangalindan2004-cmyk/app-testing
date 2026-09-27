#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EDITOR ONLY. Generates a manifest.txt inside your Vosk model folder listing every
/// file (relative paths) in that folder. This is required because on Android,
/// StreamingAssets is packed inside the APK/AAB and cannot be listed at runtime with
/// normal Directory/File APIs — you can only fetch files you already know the names of
/// via UnityWebRequest. The manifest is how the runtime extractor knows what to copy.
///
/// USAGE:
/// 1. Put your full, complete Vosk model folder under:
///      Assets/StreamingAssets/<model-folder-name>/
///    (the WHOLE folder as downloaded/unzipped from the Vosk models page — do not
///    hand-pick files)
/// 2. Menu: Tools > Vosk > Generate Model Manifest
/// 3. Enter the model folder name when prompted (must match modelFolderName in
///    VoskModelExtractor and VoskPronunciationChecker)
/// 4. Re-run this any time you change/replace the model files, and re-build.
/// </summary>
public static class VoskModelManifestGenerator
{
    private const string ManifestFileName = "manifest.txt";

    [MenuItem("Tools/Vosk/Generate Model Manifest")]
    public static void GenerateManifest()
    {
        string defaultName = "vosk-model-small-ja-0.22";
        string modelFolderName = EditorUtility.SaveFolderPanel(
            "Select the Vosk model folder inside Assets/StreamingAssets",
            Path.Combine(Application.dataPath, "StreamingAssets"),
            defaultName);

        if (string.IsNullOrEmpty(modelFolderName))
        {
            Debug.Log("[VoskModelManifestGenerator] Cancelled.");
            return;
        }

        if (!modelFolderName.Replace('\\', '/').Contains("/StreamingAssets/"))
        {
            EditorUtility.DisplayDialog("Wrong folder",
                "Please select a folder inside Assets/StreamingAssets/", "OK");
            return;
        }

        string modelRoot = modelFolderName; // absolute path to the model folder
        var allFiles = Directory.GetFiles(modelRoot, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".meta"))
            .Where(f => !Path.GetFileName(f).Equals(ManifestFileName))
            .Select(f => f.Replace('\\', '/'))
            .Select(f => f.Substring(modelRoot.Replace('\\', '/').Length + 1)) // relative path
            .OrderBy(f => f)
            .ToList();

        if (allFiles.Count == 0)
        {
            EditorUtility.DisplayDialog("No files found",
                "That folder has no files. Make sure the model was actually unzipped there.", "OK");
            return;
        }

        string manifestPath = Path.Combine(modelRoot, ManifestFileName);
        File.WriteAllLines(manifestPath, allFiles);

        AssetDatabase.Refresh();

        Debug.Log($"[VoskModelManifestGenerator] Wrote {allFiles.Count} entries to {manifestPath}");
        EditorUtility.DisplayDialog("Manifest generated",
            $"Wrote {allFiles.Count} file paths to:\n{manifestPath}\n\n" +
            "Make sure this matches the modelFolderName used in VoskModelExtractor / VoskPronunciationChecker, then rebuild the app.",
            "OK");
    }
}
#endif

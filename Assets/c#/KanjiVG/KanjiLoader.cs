using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Loads and parses KanjiVG SVG data for a character, from StreamingAssets.
///
/// WHY ASYNC (LoadAsync), NOT File.Exists/File.Open (the old Load):
/// Application.streamingAssetsPath works fine with plain System.IO.File calls
/// in the Editor, because there it's just a folder on disk. On Android —
/// including the emulator, since it runs the same built APK — StreamingAssets
/// is packed as compressed data inside the APK, and plain File I/O cannot
/// read into it; File.Exists() silently returns false there. That was the
/// root cause of the "Stroke 1 / 0" bug (KanjiLoader.Load returned null, so
/// KanjiDrawingBoard's TotalStrokes fell back to 0).
///
/// The fix Android actually supports is UnityWebRequest, which understands
/// the special jar:file://...!/assets URI Android returns for StreamingAssets.
/// UnityWebRequest is inherently async (it needs a frame to complete), which
/// is why every call site uses LoadAsync from inside a coroutine instead of
/// a plain synchronous return value.
///
/// Usage (from a coroutine):
///   yield return KanjiLoader.LoadAsync(kanji, data => { ... use data ... });
/// </summary>
public static class KanjiLoader
{
    // SVG namespace used in KanjiVG files
    private static readonly XNamespace SvgNs = "http://www.w3.org/2000/svg";

    // Simple in-memory cache so we don't re-fetch the same file twice
    private static readonly Dictionary<string, KanjiData> Cache = new Dictionary<string, KanjiData>();

    // ─── Async API (use this — works in Editor, emulator, and on device) ───

    /// <summary>
    /// Load kanji data by character. Example:
    ///   yield return KanjiLoader.LoadAsync('日', data => { ... });
    /// Calls back with null if no matching file is found or it fails to parse.
    /// </summary>
    public static IEnumerator LoadAsync(char character, Action<KanjiData> callback)
    {
        string hex = GetHex(character);
        yield return LoadAsyncInternal(hex, character.ToString(), callback);
    }

    /// <summary>
    /// Load kanji data by 5-digit unicode hex string. Example:
    ///   yield return KanjiLoader.LoadAsync("065e5", data => { ... });
    /// Calls back with null if no matching file is found or it fails to parse.
    /// </summary>
    public static IEnumerator LoadAsync(string unicodeHex, Action<KanjiData> callback)
    {
        yield return LoadAsyncInternal(unicodeHex, null, callback);
    }

    private static IEnumerator LoadAsyncInternal(string unicodeHex, string character, Action<KanjiData> callback)
    {
        unicodeHex = unicodeHex.ToLower();

        // Return from cache if already loaded — no request needed.
        if (Cache.TryGetValue(unicodeHex, out KanjiData cached))
        {
            callback?.Invoke(cached);
            yield break;
        }

        string filePath = GetFilePath(unicodeHex);
        string requestUri = ToRequestUri(filePath);

        using (UnityWebRequest req = UnityWebRequest.Get(requestUri))
        {
            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            bool failed = req.result != UnityWebRequest.Result.Success;
#else
            bool failed = req.isNetworkError || req.isHttpError;
#endif
            if (failed)
            {
                Debug.LogWarning($"[KanjiLoader] Could not load {unicodeHex}.svg from {requestUri}: {req.error}");
                callback?.Invoke(null);
                yield break;
            }

            try
            {
                XDocument doc = XDocument.Parse(req.downloadHandler.text);
                KanjiData data = ParseSvg(doc, unicodeHex, character);
                Cache[unicodeHex] = data;
                callback?.Invoke(data);
            }
            catch (Exception e)
            {
                Debug.LogError($"[KanjiLoader] Failed to parse {unicodeHex}.svg: {e.Message}");
                callback?.Invoke(null);
            }
        }
    }

    /// <summary>
    /// Converts a StreamingAssets file path into a URI UnityWebRequest can open.
    /// On Android, Application.streamingAssetsPath is already a full URI
    /// (jar:file:///.../base.apk!/assets) — do not prefix it. On every other
    /// platform (Editor, Windows, iOS, etc.) it's a plain filesystem path,
    /// which needs a "file://" prefix to be treated as a URI.
    /// </summary>
    private static string ToRequestUri(string filePath)
    {
        if (Application.platform == RuntimePlatform.Android) return filePath;
        return "file://" + filePath;
    }

    // ─── Old synchronous API — DO NOT use for runtime loading on Android ───
    // Kept only in case something in the project still references it
    // directly; it uses plain File I/O and will silently fail (return null)
    // on Android/emulator, same as before. Prefer LoadAsync everywhere.

    [Obsolete("Uses File I/O on StreamingAssets, which silently fails on Android/emulator. Use LoadAsync instead.")]
    public static KanjiData Load(char character)
    {
        string hex = GetHex(character);
        return LoadSyncInternal(hex, character.ToString());
    }

    [Obsolete("Uses File I/O on StreamingAssets, which silently fails on Android/emulator. Use LoadAsync instead.")]
    public static KanjiData Load(string unicodeHex)
    {
        return LoadSyncInternal(unicodeHex, null);
    }

    private static KanjiData LoadSyncInternal(string unicodeHex, string character)
    {
        unicodeHex = unicodeHex.ToLower();
        if (Cache.TryGetValue(unicodeHex, out KanjiData cached)) return cached;

        string filePath = GetFilePath(unicodeHex);
        if (!System.IO.File.Exists(filePath))
        {
            Debug.LogWarning($"[KanjiLoader] (sync) SVG file not found: {filePath} — " +
                              "this is expected on Android/emulator; use LoadAsync instead.");
            return null;
        }

        try
        {
            XDocument doc = XDocument.Load(filePath);
            KanjiData data = ParseSvg(doc, unicodeHex, character);
            Cache[unicodeHex] = data;
            return data;
        }
        catch (Exception e)
        {
            Debug.LogError($"[KanjiLoader] Failed to parse {unicodeHex}.svg: {e.Message}");
            return null;
        }
    }

    // ─── Shared helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Converts a char to its 5-digit lowercase hex codepoint string.
    /// '日' → "065e5"
    /// </summary>
    public static string GetHex(char character)
    {
        return ((int)character).ToString("x5");
    }

    /// <summary>
    /// Returns the full file path to the SVG in StreamingAssets.
    /// </summary>
    public static string GetFilePath(string unicodeHex)
    {
        return System.IO.Path.Combine(Application.streamingAssetsPath, "kanji", unicodeHex + ".svg");
    }

    /// <summary>
    /// Clears the in-memory cache (call this if you need to free memory).
    /// </summary>
    public static void ClearCache()
    {
        Cache.Clear();
    }

    private static KanjiData ParseSvg(XDocument doc, string unicodeHex, string character)
    {
        KanjiData data = new KanjiData();
        data.unicodeHex = unicodeHex;

        if (string.IsNullOrEmpty(character))
        {
            try
            {
                int codepoint = Convert.ToInt32(unicodeHex, 16);
                data.character = char.ConvertFromUtf32(codepoint);
            }
            catch
            {
                data.character = "?";
            }
        }
        else
        {
            data.character = character;
        }

        foreach (XElement pathEl in doc.Descendants(SvgNs + "path"))
        {
            string d = pathEl.Attribute("d")?.Value;
            if (!string.IsNullOrEmpty(d))
            {
                data.strokePaths.Add(d);
            }
        }

        return data;
    }
}

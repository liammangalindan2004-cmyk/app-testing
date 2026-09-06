using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using UnityEngine;

/// <summary>
/// Loads and parses KanjiVG SVG files from StreamingAssets.
/// Usage: KanjiLoader.Load('日')  or  KanjiLoader.Load("065e5")
/// </summary>
public static class KanjiLoader
{
    // SVG namespace used in KanjiVG files
    private static readonly XNamespace SvgNs = "http://www.w3.org/2000/svg";

    // Simple in-memory cache so we don't re-parse the same file twice
    private static readonly Dictionary<string, KanjiData> Cache = new Dictionary<string, KanjiData>();

    /// <summary>
    /// Load kanji data by character. Example: KanjiLoader.Load('日')
    /// Returns null if the file is not found.
    /// </summary>
    public static KanjiData Load(char character)
    {
        string hex = GetHex(character);
        return Load(hex, character.ToString());
    }

    /// <summary>
    /// Load kanji data by 5-digit unicode hex string. Example: KanjiLoader.Load("065e5")
    /// Returns null if the file is not found.
    /// </summary>
    public static KanjiData Load(string unicodeHex)
    {
        return Load(unicodeHex, null);
    }

    private static KanjiData Load(string unicodeHex, string character)
    {
        unicodeHex = unicodeHex.ToLower();

        // Return from cache if already loaded
        if (Cache.TryGetValue(unicodeHex, out KanjiData cached))
            return cached;

        string filePath = GetFilePath(unicodeHex);

        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"[KanjiLoader] SVG file not found: {filePath}");
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
        return Path.Combine(Application.streamingAssetsPath, "kanji", unicodeHex + ".svg");
    }

    /// <summary>
    /// Returns true if an SVG file exists for the given character.
    /// </summary>
    public static bool Exists(char character)
    {
        return File.Exists(GetFilePath(GetHex(character)));
    }

    /// <summary>
    /// Clears the in-memory cache (call this if you need to free memory).
    /// </summary>
    public static void ClearCache()
    {
        Cache.Clear();
    }

    // ─── Private helpers ────────────────────────────────────────────────────

    private static KanjiData ParseSvg(XDocument doc, string unicodeHex, string character)
    {
        KanjiData data = new KanjiData();
        data.unicodeHex = unicodeHex;

        // Try to figure out the character from the hex if not provided
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

        // Find all <path> elements and extract the "d" attribute (the stroke shape)
        // KanjiVG paths are inside <g> groups; we grab them all in document order = stroke order
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
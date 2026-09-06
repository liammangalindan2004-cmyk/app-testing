using System.Collections.Generic;

/// <summary>
/// Holds all parsed data for a single kanji character.
/// </summary>
[System.Serializable]
public class KanjiData
{
    /// <summary>The kanji character itself, e.g. '日'</summary>
    public string character;

    /// <summary>Unicode codepoint as 5-digit hex, e.g. "065e5" — also the filename</summary>
    public string unicodeHex;

    /// <summary>Each stroke's SVG path "d" string, in order</summary>
    public List<string> strokePaths = new List<string>();

    /// <summary>Number of strokes</summary>
    public int strokeCount => strokePaths.Count;
}
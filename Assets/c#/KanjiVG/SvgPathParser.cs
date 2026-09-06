using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Parses SVG path "d" attribute strings into lists of Vector2 points.
/// Supports M, L, C, S, Q, Z commands (covers all KanjiVG paths).
/// The output points can be used with LineRenderer or GL drawing.
/// </summary>
public static class SvgPathParser
{
    // KanjiVG SVG viewBox is 0-109, we normalize to 0-1 for Unity flexibility
    private const float SvgSize = 109f;

    /// <summary>
    /// Parse a single SVG path "d" string into a list of world-space points.
    /// </summary>
    /// <param name="d">The SVG path data string</param>
    /// <param name="scale">How large to draw (default 1 = 1 Unity unit)</param>
    /// <param name="offset">World position offset</param>
    /// <param name="steps">How many points per curve segment (higher = smoother)</param>
    public static List<Vector2> Parse(string d, float scale = 1f, Vector2 offset = default, int steps = 20)
    {
        List<Vector2> points = new List<Vector2>();
        if (string.IsNullOrEmpty(d)) return points;

        // Tokenize the path string into commands + numbers
        List<string> tokens = Tokenize(d);

        Vector2 current = Vector2.zero;
        Vector2 start = Vector2.zero;
        Vector2 lastControl = Vector2.zero;
        char lastCmd = ' ';
        int i = 0;

        while (i < tokens.Count)
        {
            string token = tokens[i];

            // Check if it's a command letter
            if (token.Length == 1 && char.IsLetter(token[0]))
            {
                char cmd = token[0];
                i++;

                switch (cmd)
                {
                    // ── Move To ────────────────────────────────────────────
                    case 'M':
                        {
                            current = ReadPoint(tokens, ref i);
                            start = current;
                            points.Add(Transform(current, scale, offset));
                            lastCmd = 'M';
                            // Subsequent coords after M are implicit L
                            while (i < tokens.Count && IsNumber(tokens[i]))
                            {
                                current = ReadPoint(tokens, ref i);
                                points.Add(Transform(current, scale, offset));
                                lastCmd = 'L';
                            }
                            break;
                        }
                    case 'm':
                        {
                            Vector2 rel = ReadPoint(tokens, ref i);
                            current += rel;
                            start = current;
                            points.Add(Transform(current, scale, offset));
                            lastCmd = 'm';
                            while (i < tokens.Count && IsNumber(tokens[i]))
                            {
                                rel = ReadPoint(tokens, ref i);
                                current += rel;
                                points.Add(Transform(current, scale, offset));
                                lastCmd = 'l';
                            }
                            break;
                        }

                    // ── Line To ────────────────────────────────────────────
                    case 'L':
                        {
                            while (i < tokens.Count && IsNumber(tokens[i]))
                            {
                                current = ReadPoint(tokens, ref i);
                                points.Add(Transform(current, scale, offset));
                            }
                            lastCmd = 'L';
                            break;
                        }
                    case 'l':
                        {
                            while (i < tokens.Count && IsNumber(tokens[i]))
                            {
                                Vector2 rel = ReadPoint(tokens, ref i);
                                current += rel;
                                points.Add(Transform(current, scale, offset));
                            }
                            lastCmd = 'l';
                            break;
                        }

                    // ── Cubic Bezier ───────────────────────────────────────
                    case 'C':
                        {
                            while (i < tokens.Count && IsNumber(tokens[i]))
                            {
                                Vector2 c1 = ReadPoint(tokens, ref i);
                                Vector2 c2 = ReadPoint(tokens, ref i);
                                Vector2 end = ReadPoint(tokens, ref i);
                                AddCubicBezier(points, current, c1, c2, end, scale, offset, steps);
                                lastControl = c2;
                                current = end;
                            }
                            lastCmd = 'C';
                            break;
                        }
                    case 'c':
                        {
                            while (i < tokens.Count && IsNumber(tokens[i]))
                            {
                                Vector2 c1 = current + ReadPoint(tokens, ref i);
                                Vector2 c2 = current + ReadPoint(tokens, ref i);
                                Vector2 end = current + ReadPoint(tokens, ref i);
                                AddCubicBezier(points, current, c1, c2, end, scale, offset, steps);
                                lastControl = c2;
                                current = end;
                            }
                            lastCmd = 'c';
                            break;
                        }

                    // ── Smooth Cubic Bezier ────────────────────────────────
                    case 'S':
                        {
                            while (i < tokens.Count && IsNumber(tokens[i]))
                            {
                                // First control point is reflection of last control point
                                Vector2 c1 = (lastCmd == 'C' || lastCmd == 'S' || lastCmd == 'c' || lastCmd == 's')
                                    ? (2 * current - lastControl)
                                    : current;
                                Vector2 c2 = ReadPoint(tokens, ref i);
                                Vector2 end = ReadPoint(tokens, ref i);
                                AddCubicBezier(points, current, c1, c2, end, scale, offset, steps);
                                lastControl = c2;
                                current = end;
                            }
                            lastCmd = 'S';
                            break;
                        }
                    case 's':
                        {
                            while (i < tokens.Count && IsNumber(tokens[i]))
                            {
                                Vector2 c1 = (lastCmd == 'C' || lastCmd == 'S' || lastCmd == 'c' || lastCmd == 's')
                                    ? (2 * current - lastControl)
                                    : current;
                                Vector2 c2 = current + ReadPoint(tokens, ref i);
                                Vector2 end = current + ReadPoint(tokens, ref i);
                                AddCubicBezier(points, current, c1, c2, end, scale, offset, steps);
                                lastControl = c2;
                                current = end;
                            }
                            lastCmd = 's';
                            break;
                        }

                    // ── Quadratic Bezier ───────────────────────────────────
                    case 'Q':
                        {
                            while (i < tokens.Count && IsNumber(tokens[i]))
                            {
                                Vector2 ctrl = ReadPoint(tokens, ref i);
                                Vector2 end = ReadPoint(tokens, ref i);
                                AddQuadraticBezier(points, current, ctrl, end, scale, offset, steps);
                                lastControl = ctrl;
                                current = end;
                            }
                            lastCmd = 'Q';
                            break;
                        }
                    case 'q':
                        {
                            while (i < tokens.Count && IsNumber(tokens[i]))
                            {
                                Vector2 ctrl = current + ReadPoint(tokens, ref i);
                                Vector2 end = current + ReadPoint(tokens, ref i);
                                AddQuadraticBezier(points, current, ctrl, end, scale, offset, steps);
                                lastControl = ctrl;
                                current = end;
                            }
                            lastCmd = 'q';
                            break;
                        }

                    // ── Close Path ─────────────────────────────────────────
                    case 'Z':
                    case 'z':
                        points.Add(Transform(start, scale, offset));
                        current = start;
                        lastCmd = 'Z';
                        break;
                }
            }
            else
            {
                // Skip unexpected tokens
                i++;
            }
        }

        return points;
    }

    // ─── Math helpers ────────────────────────────────────────────────────────

    private static void AddCubicBezier(List<Vector2> points, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        float scale, Vector2 offset, int steps)
    {
        for (int s = 1; s <= steps; s++)
        {
            float t = s / (float)steps;
            float u = 1 - t;
            Vector2 pt = u * u * u * p0
                       + 3 * u * u * t * p1
                       + 3 * u * t * t * p2
                       + t * t * t * p3;
            points.Add(Transform(pt, scale, offset));
        }
    }

    private static void AddQuadraticBezier(List<Vector2> points, Vector2 p0, Vector2 p1, Vector2 p2,
        float scale, Vector2 offset, int steps)
    {
        for (int s = 1; s <= steps; s++)
        {
            float t = s / (float)steps;
            float u = 1 - t;
            Vector2 pt = u * u * p0 + 2 * u * t * p1 + t * t * p2;
            points.Add(Transform(pt, scale, offset));
        }
    }

    /// <summary>
    /// Converts SVG coordinates (0-109, Y flipped) to Unity world space.
    /// </summary>
    private static Vector2 Transform(Vector2 svgPoint, float scale, Vector2 offset)
    {
        float x = (svgPoint.x / SvgSize) * scale;
        float y = (1f - svgPoint.y / SvgSize) * scale; // Flip Y: SVG Y goes down, Unity Y goes up
        return new Vector2(x, y) + offset;
    }

    // ─── Tokenizer ───────────────────────────────────────────────────────────

    private static List<string> Tokenize(string d)
    {
        List<string> tokens = new List<string>();
        // Split on SVG command letters and numbers (including negatives and decimals)
        MatchCollection matches = Regex.Matches(d, @"[MmLlCcSsQqZz]|[-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?");
        foreach (Match m in matches)
            tokens.Add(m.Value);
        return tokens;
    }

    private static Vector2 ReadPoint(List<string> tokens, ref int i)
    {
        float x = float.Parse(tokens[i++], System.Globalization.CultureInfo.InvariantCulture);
        float y = float.Parse(tokens[i++], System.Globalization.CultureInfo.InvariantCulture);
        return new Vector2(x, y);
    }

    private static bool IsNumber(string token)
    {
        return token.Length > 0 && (char.IsDigit(token[0]) || token[0] == '-' || token[0] == '.' || token[0] == '+');
    }
}
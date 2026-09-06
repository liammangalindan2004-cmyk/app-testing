using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A UI Graphic component that draws kanji strokes directly on the Canvas.
/// Attach this to an empty GameObject INSIDE your Panel/Canvas.
/// Set its RectTransform to fill the area you want the kanji drawn in.
///
/// SETUP:
///   1. Inside your Panel, create an empty GameObject — name it "KanjiDrawArea"
///   2. Add this component to it
///   3. Set RectTransform to fill the drawing area (e.g. stretch to fill panel)
///   4. Assign this to KanjiPracticeUI's "Stroke Graphic" field
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class KanjiStrokeGraphic : MaskableGraphic
{
    [Header("Stroke Appearance")]
    [Tooltip("Thickness of the guide strokes in UI pixels")]
    public float strokeWidth = 8f;
    [Tooltip("Thickness of the ghost hint stroke")]
    public float ghostWidth = 18f;
    [Tooltip("Thickness of the trail you draw")]
    public float trailWidth = 12f;

    [Tooltip("Color of the completed/guide strokes")]
    public Color guideColor = new Color(0.1f, 0.1f, 0.1f, 1f);
    [Tooltip("Color of the ghost hint for next stroke")]
    public Color ghostColor = new Color(0.5f, 0.7f, 1f, 0.55f);
    [Tooltip("Color of the stroke you are currently drawing")]
    public Color trailColor = new Color(0.15f, 0.4f, 1f, 1f);
    [Tooltip("Color when a stroke is correct")]
    public Color correctColor = new Color(0.1f, 0.85f, 0.25f, 1f);
    [Tooltip("Color when a stroke is wrong")]
    public Color wrongColor = new Color(0.95f, 0.2f, 0.15f, 1f);

    [Header("Quality")]
    [Range(8, 40)] public int curveSteps = 20;

    [Header("Direction Arrows")]
    [Tooltip("Draw an arrowhead at the end of each guide stroke to show writing direction")]
    public bool showDirectionArrows = true;
    [Tooltip("Also draw an arrowhead on the ghost hint stroke")]
    public bool showGhostArrow = true;
    [Tooltip("Length of the arrowhead, in UI pixels")]
    public float arrowLength = 40f;
    [Tooltip("Width of the arrowhead base, in UI pixels")]
    public float arrowWidth = 40f;
    [Tooltip("Color of the direction arrowhead on guide strokes")]
    public Color arrowColor = new Color(1f, 1f, 1f, 1f);
    [Tooltip("Radius of the small start-point marker (0 = disabled)")]
    public float startDotRadius = 10f;
    [Tooltip("Color of the start-point marker")]
    public Color startDotColor = new Color(1f, 1f, 1f, 1f);

    // ── Data ──────────────────────────────────────────────────────────────────
    private KanjiData _data;
    private int _ghostStrokeIndex = -1;

    // Strokes already completed (drawn by the player, shown in correct/guide color)
    private List<List<Vector2>> _completedTrails = new List<List<Vector2>>();
    private List<Color> _completedColors = new List<Color>();

    // Current live trail (while mouse is held)
    private List<Vector2> _liveTrial = new List<Vector2>();
    private bool _hasLive = false;

    // Animation state
    private bool _animating = false;
    private int _animStrokeIdx = 0;
    private List<Vector2> _animPartial = new List<Vector2>();
    private List<List<Vector2>> _animDone = new List<List<Vector2>>();

    [Header("Animation")]
    public float strokeDuration = 0.6f;
    public float strokeDelay = 0.15f;
    public Color animActiveColor = new Color(0.9f, 0.2f, 0.2f, 1f);

    private Coroutine _animCoroutine;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Load a kanji and display all its strokes as a guide.</summary>
    public void LoadKanji(KanjiData data, int ghostIndex = 0)
    {
        _data = data;
        _ghostStrokeIndex = ghostIndex;
        _completedTrails.Clear();
        _completedColors.Clear();
        _liveTrial.Clear();
        _hasLive = false;
        StopAnim();
        SetVerticesDirty();
    }

    /// <summary>Show the ghost hint on a specific stroke (0-based). -1 = hide.</summary>
    public void SetGhost(int strokeIndex)
    {
        _ghostStrokeIndex = strokeIndex;
        SetVerticesDirty();
    }

    /// <summary>Update the live drawing trail while the player drags.</summary>
    public void SetLiveTrail(List<Vector2> uiPoints)
    {
        _liveTrial = uiPoints != null ? new List<Vector2>(uiPoints) : new List<Vector2>();
        _hasLive = _liveTrial.Count > 1;
        SetVerticesDirty();
    }

    /// <summary>Clear the live trail (call on mouse up).</summary>
    public void ClearLiveTrail()
    {
        _liveTrial.Clear();
        _hasLive = false;
        SetVerticesDirty();
    }

    /// <summary>Add a finished stroke result to the display.</summary>
    public void AddFinishedTrail(List<Vector2> uiPoints, bool wasCorrect)
    {
        _completedTrails.Add(new List<Vector2>(uiPoints));
        _completedColors.Add(wasCorrect ? correctColor : wrongColor);
        SetVerticesDirty();
    }

    /// <summary>Remove all player-drawn trails.</summary>
    public void ClearTrails()
    {
        _completedTrails.Clear();
        _completedColors.Clear();
        _liveTrial.Clear();
        _hasLive = false;
        SetVerticesDirty();
    }

    /// <summary>Animate stroke order.</summary>
    public void PlayAnimation()
    {
        if (_data == null) return;
        StopAnim();
        _animDone.Clear();
        _animPartial.Clear();
        _animStrokeIdx = 0;
        _animating = true;
        _ghostStrokeIndex = -1;
        SetVerticesDirty();
        _animCoroutine = StartCoroutine(AnimateCo());
    }

    public void StopAnim()
    {
        if (_animCoroutine != null) { StopCoroutine(_animCoroutine); _animCoroutine = null; }
        _animating = false;
        _animDone.Clear();
        _animPartial.Clear();
    }

    // ── Mesh generation ───────────────────────────────────────────────────────

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_data == null) return;

        Rect r = rectTransform.rect;

        // 1. Guide strokes (all strokes, dim)
        if (!_animating)
        {
            for (int i = 0; i < _data.strokePaths.Count; i++)
            {
                var pts = GetStrokePts(i, r);
                DrawStroke(vh, pts, guideColor, strokeWidth);

                if (showDirectionArrows)
                    DrawStrokeArrow(vh, pts, arrowColor, arrowLength, arrowWidth);
                if (startDotRadius > 0f)
                    DrawCircle(vh, pts[0], startDotRadius, startDotColor);
            }
        }

        // 2. Ghost hint
        if (!_animating && _ghostStrokeIndex >= 0 && _ghostStrokeIndex < _data.strokePaths.Count)
        {
            var pts = GetStrokePts(_ghostStrokeIndex, r);
            DrawStroke(vh, pts, ghostColor, ghostWidth);

            if (showGhostArrow)
                DrawStrokeArrow(vh, pts, ghostColor, arrowLength * 1.3f, arrowWidth * 1.3f);
            if (startDotRadius > 0f)
                DrawCircle(vh, pts[0], startDotRadius * 1.3f, ghostColor);
        }

        // 3. Animation strokes
        if (_animating)
        {
            foreach (var trail in _animDone)
            {
                DrawStroke(vh, trail, guideColor, strokeWidth);
                if (showDirectionArrows)
                    DrawStrokeArrow(vh, trail, arrowColor, arrowLength, arrowWidth);
            }
            if (_animPartial.Count > 1)
            {
                DrawStroke(vh, _animPartial, animActiveColor, strokeWidth + 4f);
                // Arrow follows the tip of the in-progress stroke as it draws
                if (showDirectionArrows)
                    DrawStrokeArrow(vh, _animPartial, animActiveColor, arrowLength, arrowWidth);
            }
        }

        // 4. Completed player trails
        for (int i = 0; i < _completedTrails.Count; i++)
            DrawStroke(vh, _completedTrails[i], _completedColors[i], trailWidth);

        // 5. Live trail (while drawing)
        if (_hasLive && _liveTrial.Count > 1)
            DrawStroke(vh, _liveTrial, trailColor, trailWidth);
    }

    // ── Geometry helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Draws a polyline as a series of quads (rectangular segments + round caps).
    /// Each segment is a screen-aligned rectangle between two consecutive points.
    /// </summary>
    private void DrawStroke(VertexHelper vh, List<Vector2> pts, Color c, float width)
    {
        if (pts == null || pts.Count < 2) return;

        float hw = width * 0.5f;

        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 a = pts[i];
            Vector2 b = pts[i + 1];
            Vector2 dir = (b - a).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x) * hw;

            // Quad: a-left, a-right, b-right, b-left
            int idx = vh.currentVertCount;
            AddVert(vh, a - perp, c);
            AddVert(vh, a + perp, c);
            AddVert(vh, b + perp, c);
            AddVert(vh, b - perp, c);
            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx, idx + 2, idx + 3);
        }

        // Round end cap on the last point
        DrawCircle(vh, pts[pts.Count - 1], hw, c);
        DrawCircle(vh, pts[0], hw, c);
    }

    private void DrawCircle(VertexHelper vh, Vector2 center, float radius, Color c)
    {
        int segs = 10;
        int centerIdx = vh.currentVertCount;
        AddVert(vh, center, c);
        for (int i = 0; i <= segs; i++)
        {
            float angle = i / (float)segs * Mathf.PI * 2f;
            AddVert(vh, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, c);
        }
        for (int i = 0; i < segs; i++)
            vh.AddTriangle(centerIdx, centerIdx + i + 1, centerIdx + i + 2);
    }

    /// <summary>
    /// Draws a small solid triangular arrowhead whose tip sits at the end of the
    /// stroke, pointing in the direction the stroke travels. This gives learners
    /// a clear visual cue for which way to draw each stroke.
    /// </summary>
    private void DrawStrokeArrow(VertexHelper vh, List<Vector2> pts, Color c, float length, float width)
    {
        if (pts == null || pts.Count < 2) return;

        Vector2 tip = pts[pts.Count - 1];
        Vector2 dir = ArrowDirection(pts);
        if (dir == Vector2.zero) return;

        Vector2 perp = new Vector2(-dir.y, dir.x) * (width * 0.5f);
        Vector2 back = tip - dir * length;

        int idx = vh.currentVertCount;
        AddVert(vh, tip, c);          // arrow point
        AddVert(vh, back + perp, c);  // base corner 1
        AddVert(vh, back - perp, c);  // base corner 2
        vh.AddTriangle(idx, idx + 1, idx + 2);
    }

    /// <summary>
    /// Finds a stable direction vector for the end of a stroke by walking
    /// backwards from the tip until the points are far enough apart to avoid
    /// jitter from tiny curve segments right at the end.
    /// </summary>
    private Vector2 ArrowDirection(List<Vector2> pts)
    {
        const float minDist = 6f;
        Vector2 tip = pts[pts.Count - 1];

        for (int i = pts.Count - 2; i >= 0; i--)
        {
            Vector2 delta = tip - pts[i];
            if (delta.magnitude >= minDist)
                return delta.normalized;
        }

        // Stroke is very short overall — fall back to first→last point
        Vector2 fallback = tip - pts[0];
        return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector2.zero;
    }

    private void AddVert(VertexHelper vh, Vector2 pos, Color c)
    {
        var uiv = new UIVertex();
        uiv.position = new Vector3(pos.x, pos.y, 0);
        uiv.color = c;
        uiv.uv0 = Vector2.zero;
        vh.AddVert(uiv);
    }

    // ── Coordinate conversion ─────────────────────────────────────────────────

    /// <summary>
    /// Convert a KanjiVG stroke path into UI-local pixel points inside this RectTransform.
    /// KanjiVG coords are 0–109 (Y down). RectTransform rect is e.g. (-200,-200) to (200,200).
    /// </summary>
    private List<Vector2> GetStrokePts(int strokeIdx, Rect r)
    {
        string path = _data.strokePaths[strokeIdx];
        // Parse to normalized 0-1 range first
        var raw = SvgPathParser.Parse(path, 1f, Vector2.zero, curveSteps);
        var result = new List<Vector2>(raw.Count);
        foreach (var p in raw)
        {
            // p.x and p.y are in 0–1 range (SvgPathParser normalizes by SvgSize=109)
            // Map to rect pixel space: x → rect.xMin..rect.xMax, y → already flipped in parser
            float px = Mathf.Lerp(r.xMin, r.xMax, p.x);
            float py = Mathf.Lerp(r.yMin, r.yMax, p.y);
            result.Add(new Vector2(px, py));
        }
        return result;
    }

    /// <summary>Convert a UI-local pixel point to 0-1 normalized for scoring.</summary>
    public Vector2 UIToNormalized(Vector2 uiPt)
    {
        Rect r = rectTransform.rect;
        return new Vector2(
            Mathf.InverseLerp(r.xMin, r.xMax, uiPt.x),
            Mathf.InverseLerp(r.yMin, r.yMax, uiPt.y)
        );
    }

    /// <summary>Get stroke points in 0-1 normalized space (for scoring).</summary>
    public List<Vector2> GetStrokePtsNormalized(int strokeIdx)
    {
        if (_data == null || strokeIdx >= _data.strokePaths.Count) return new List<Vector2>();
        return SvgPathParser.Parse(_data.strokePaths[strokeIdx], 1f, Vector2.zero, curveSteps);
    }

    // ── Animation coroutine ───────────────────────────────────────────────────

    private IEnumerator AnimateCo()
    {
        Rect r = rectTransform.rect;

        for (int i = 0; i < _data.strokePaths.Count; i++)
        {
            _animStrokeIdx = i;
            var allPts = GetStrokePts(i, r);
            _animPartial.Clear();

            float elapsed = 0f;
            while (elapsed < strokeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / strokeDuration);
                int show = Mathf.Max(2, Mathf.RoundToInt(t * allPts.Count));
                _animPartial = allPts.GetRange(0, show);
                SetVerticesDirty();
                yield return null;
            }

            _animPartial = allPts;
            _animDone.Add(new List<Vector2>(allPts));
            _animPartial.Clear();
            SetVerticesDirty();
            yield return new WaitForSeconds(strokeDelay);
        }

        _animating = false;
        _animCoroutine = null;
        _ghostStrokeIndex = 0; // restore ghost after animation
        SetVerticesDirty();
    }
}
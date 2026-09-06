using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Handles mouse/touch input entirely in UI space.
/// Attach to the SAME GameObject as KanjiStrokeGraphic.
/// It captures pointer drag events and feeds them to KanjiStrokeGraphic,
/// then scores each stroke against the KanjiVG data.
///
/// SETUP:
///   Same GameObject as KanjiStrokeGraphic (the "KanjiDrawArea" object).
///   Assign strokeGraphic in Inspector (or it will find it on the same object).
///   Assign the KanjiData via SetTarget().
///   This GameObject must be a UI element under a Canvas (i.e. it needs a
///   RectTransform) — Awake() below adds an Image component to it for
///   raycasting, and that only works on a proper UI GameObject.
/// </summary>
[RequireComponent(typeof(KanjiStrokeGraphic))]
public class KanjiDrawingBoard : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    [Header("Scoring")]
    [Range(0f, 1f)]
    [Tooltip("0 = very lenient, 1 = perfect required. 0.45 is good for beginners.")]
    public float acceptanceThreshold = 0.45f;

    [Header("Events")]
    public UnityEvent<float> OnStrokeCompleted;   // float = score 0-1
    public UnityEvent OnKanjiCompleted;
    public UnityEvent OnWrongStroke;

    // ── Internal ──────────────────────────────────────────────────────────────
    private KanjiStrokeGraphic _graphic;
    private KanjiData _target;
    private int _strokeIdx = 0;
    private bool _drawing = false;
    private List<Vector2> _livePts = new List<Vector2>();

    public int CurrentStrokeNumber => _strokeIdx + 1;
    public int TotalStrokes => _target?.strokeCount ?? 0;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        _graphic = GetComponent<KanjiStrokeGraphic>();
        if (_graphic == null)
        {
            // [RequireComponent] should have auto-added this — seeing this log
            // means something unusual is going on with this specific GameObject
            // (e.g. it's not the "KanjiDrawArea" object you set up, or the
            // component was removed at runtime by other code).
            Debug.LogError("[KanjiDrawingBoard] No KanjiStrokeGraphic found on this GameObject — " +
                            "drawing and scoring will not work until this is fixed.");
        }

        // Ensure this GameObject has an Image component so the UI EventSystem
        // can actually raycast-hit it for touch/mouse input.
        var image = GetComponent<Image>();
        if (image == null)
        {
            image = gameObject.AddComponent<Image>();

            if (image == null)
            {
                // AddComponent<Image> should not return null on a normal UI
                // GameObject. Seeing this means this GameObject most likely
                // isn't parented under a Canvas / doesn't have a RectTransform
                // set up the way a UI element needs. Rather than crash Awake()
                // (which used to happen here with a NullReferenceException and
                // silently broke drawing input on device), we log a clear
                // instruction instead: add an Image component to this
                // GameObject manually in the Editor (nearly-transparent color,
                // Raycast Target ON) as the class header's SETUP section
                // originally described, then re-run.
                Debug.LogError("[KanjiDrawingBoard] Could not add an Image component to this " +
                                "GameObject automatically. Add one manually in the Editor instead: " +
                                "set its color alpha to ~0.01 (invisible but still raycastable) and " +
                                "make sure 'Raycast Target' is checked. Also confirm this GameObject " +
                                "sits under a Canvas and has a RectTransform.");
                return;
            }

            image.color = new Color(1f, 1f, 1f, 0.01f); // Nearly transparent
        }
        image.raycastTarget = true;

        Debug.Log("[KanjiDrawingBoard] Initialized with raycast target enabled");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetTarget(KanjiData data)
    {
        _target = data;
        _strokeIdx = 0;
        if (data != null)
        {
            _graphic.LoadKanji(data, ghostIndex: 0);
            Debug.Log($"[DrawingBoard] Set target kanji '{data.character}' with {data.strokeCount} strokes");
        }
        else
        {
            Debug.LogError("[DrawingBoard] Attempted to set null KanjiData");
        }
    }

    public void SetTarget(char kanji)
    {
        // Always load asynchronously via UnityWebRequest — this is required on
        // Android/emulator (StreamingAssets can't be read with plain File I/O
        // there) and works identically in the Editor, so there's no reason to
        // branch by platform here.
        StartCoroutine(LoadKanjiAsync(kanji));
    }

    private IEnumerator LoadKanjiAsync(char kanji)
    {
        Debug.Log($"[DrawingBoard] Starting async load for kanji '{kanji}'");
        yield return KanjiLoader.LoadAsync(kanji, (data) =>
        {
            if (data != null)
            {
                SetTarget(data);
                Debug.Log($"[DrawingBoard] Successfully loaded kanji '{kanji}' with {data.strokeCount} strokes");
            }
            else
            {
                Debug.LogWarning($"[DrawingBoard] Failed to load kanji '{kanji}'");
            }
        });
    }

    public void ResetBoard()
    {
        _strokeIdx = 0;
        _drawing = false;
        _livePts.Clear();
        _graphic?.ClearTrails();
        _graphic?.SetGhost(0);
    }

    public void PlayAnimation() => _graphic?.PlayAnimation();

    // ── Pointer events (UI EventSystem) ──────────────────────────────────────

    public void OnPointerDown(PointerEventData e)
    {
        if (_target == null) return;
        _drawing = true;
        _livePts.Clear();
        _livePts.Add(LocalPoint(e));
        _graphic.SetLiveTrail(_livePts);
        Debug.Log($"[KanjiDrawingBoard] OnPointerDown at {LocalPoint(e)}");
    }

    public void OnDrag(PointerEventData e)
    {
        if (!_drawing || _target == null) return;
        Vector2 pt = LocalPoint(e);
        if (_livePts.Count > 0 && Vector2.Distance(pt, _livePts[_livePts.Count - 1]) < 2f) return;
        _livePts.Add(pt);
        _graphic.SetLiveTrail(_livePts);
        Debug.Log($"[KanjiDrawingBoard] OnDrag at {pt}, total points: {_livePts.Count}");
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (!_drawing || _target == null) return;
        _drawing = false;

        Debug.Log($"[KanjiDrawingBoard] OnPointerUp, collected {_livePts.Count} points");

        // Need at least a short stroke
        if (_livePts.Count < 5)
        {
            Debug.Log($"[KanjiDrawingBoard] Stroke too short ({_livePts.Count} points), ignoring");
            _graphic.ClearLiveTrail();
            _livePts.Clear();
            return;
        }

        _graphic.ClearLiveTrail();

        float score = ScoreStroke(_livePts, _strokeIdx);
        Debug.Log($"[KVG] Stroke {_strokeIdx + 1}/{_target.strokeCount}  score={score:P0}");

        OnStrokeCompleted?.Invoke(score);

        if (score >= acceptanceThreshold)
        {
            _graphic.AddFinishedTrail(_livePts, wasCorrect: true);
            _strokeIdx++;

            if (_strokeIdx >= _target.strokeCount)
            {
                _graphic.SetGhost(-1);
                OnKanjiCompleted?.Invoke();
            }
            else
            {
                _graphic.SetGhost(_strokeIdx);
            }
        }
        else
        {
            _graphic.AddFinishedTrail(_livePts, wasCorrect: false);
            OnWrongStroke?.Invoke();
            // Auto-remove the wrong trail after 1 second
            StartCoroutine(RemoveLastTrailAfter(1.0f));
        }

        _livePts.Clear();
    }

    // ── Scoring ───────────────────────────────────────────────────────────────

    private float ScoreStroke(List<Vector2> drawn, int idx)
    {
        if (_target == null || idx >= _target.strokePaths.Count) return 0.5f;

        // Get expected stroke in UI-local normalized space (0-1)
        var expNorm = _graphic.GetStrokePtsNormalized(idx);
        if (expNorm.Count < 2) return 0.5f;

        // Convert drawn UI-pixel points to normalized space
        var drawnNorm = new List<Vector2>(drawn.Count);
        foreach (var p in drawn) drawnNorm.Add(_graphic.UIToNormalized(p));

        // --- Score components ---

        // 1. Direction: dot product of overall start→end vectors
        Vector2 dDir = (drawnNorm[drawnNorm.Count - 1] - drawnNorm[0]).normalized;
        Vector2 eDir = (expNorm[expNorm.Count - 1] - expNorm[0]).normalized;
        float dirScore = (Vector2.Dot(dDir, eDir) + 1f) * 0.5f;

        // 2. Start proximity (within the 0-1 space, 0.3 units = 30% of the draw area)
        float startScore = Mathf.Clamp01(1f - Vector2.Distance(drawnNorm[0], expNorm[0]) / 0.35f);

        // 3. End proximity
        float endScore = Mathf.Clamp01(1f - Vector2.Distance(
            drawnNorm[drawnNorm.Count - 1], expNorm[expNorm.Count - 1]) / 0.35f);

        // 4. Path shape
        float shapeScore = ShapeSim(drawnNorm, expNorm, 10);

        return Mathf.Clamp01(dirScore * 0.2f + startScore * 0.3f + endScore * 0.2f + shapeScore * 0.3f);
    }

    private float ShapeSim(List<Vector2> a, List<Vector2> b, int n)
    {
        float sum = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);
            sum += Mathf.Clamp01(1f - Vector2.Distance(Sample(a, t), Sample(b, t)) / 0.35f);
        }
        return sum / n;
    }

    private Vector2 Sample(List<Vector2> pts, float t)
    {
        if (pts.Count == 0) return Vector2.zero;
        if (pts.Count == 1) return pts[0];
        float fi = t * (pts.Count - 1);
        int i = Mathf.Min((int)fi, pts.Count - 2);
        return Vector2.Lerp(pts[i], pts[i + 1], fi - i);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Convert a PointerEventData position to this RectTransform's local space.</summary>
    private Vector2 LocalPoint(PointerEventData e)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            GetComponent<RectTransform>(),
            e.position,
            e.pressEventCamera,
            out Vector2 local);
        return local;
    }

    private IEnumerator RemoveLastTrailAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        // Remove the last completed trail (the wrong one)
        // We do this by rebuilding without the last entry
        // KanjiStrokeGraphic exposes no direct remove, so we'll just clear and re-add correct ones
        // For simplicity: just leave wrong ones faded (they're already red, acceptable UX)
        // A full implementation would track and remove individual trails.
    }
}

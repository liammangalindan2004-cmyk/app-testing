using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace JapaneseLearning.Dashboard
{
    [System.Serializable]
  

    // Draws a multi-line chart (dots + connecting segments + grid + axis
    // labels + legend) entirely at runtime via UI Images and TMP text.
    // No external chart asset required.
    public class LineChartUI : MonoBehaviour
    {
        [Header("Layout containers (assign in Inspector)")]
        [SerializeField] private RectTransform plotArea;         // where dots/lines are drawn
        [SerializeField] private RectTransform xAxisLabelsRow;   // horizontal layout group, one child per week
        [SerializeField] private RectTransform yAxisLabelsColumn;// parent for the 0/25/50/75/100 labels
        [SerializeField] private RectTransform legendRow;        // horizontal layout group for the legend

        [Header("Style")]
        [SerializeField] private float yMin = 0f;
        [SerializeField] private float yMax = 100f;
        [SerializeField] private float[] yGridLines = { 0, 25, 50, 75, 100 };
        [SerializeField] private float dotSize = 10f;
        [SerializeField] private float lineThickness = 3f;
        [SerializeField] private TMP_FontAsset labelFont;
        [SerializeField] private Color gridColor = new Color(0, 0, 0, 0.08f);
        [SerializeField] private Color axisTextColor = new Color(0.55f, 0.5f, 0.65f);

        private Sprite dotSprite;

        public void Render(List<string> xLabels, List<ChartSeries> series)
        {
            Clear();
            dotSprite = CreateCircleSprite(64);

            DrawGrid();
            DrawAxisLabels(xLabels);
            DrawLegend(series);

            foreach (var s in series)
                DrawSeries(s);
        }

        private void Clear()
        {
            ClearChildren(plotArea);
            ClearChildren(xAxisLabelsRow);
            ClearChildren(yAxisLabelsColumn);
            ClearChildren(legendRow);
        }

        private void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }

        private void DrawGrid()
        {
            foreach (var gridValue in yGridLines)
            {
                float t = Mathf.InverseLerp(yMin, yMax, gridValue);

                var lineGO = new GameObject($"Grid_{gridValue}", typeof(Image));
                lineGO.transform.SetParent(plotArea, false);
                var rt = lineGO.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, t);
                rt.anchorMax = new Vector2(1, t);
                rt.sizeDelta = new Vector2(0, 1f);
                rt.anchoredPosition = Vector2.zero;
                lineGO.GetComponent<Image>().color = gridColor;

                var labelGO = new GameObject($"YLabel_{gridValue}", typeof(TextMeshProUGUI));
                labelGO.transform.SetParent(yAxisLabelsColumn, false);
                var label = labelGO.GetComponent<TextMeshProUGUI>();
                label.text = gridValue.ToString();
                label.font = labelFont;
                label.fontSize = 20;
                label.color = axisTextColor;
                label.alignment = TextAlignmentOptions.MidlineRight;
                var lrt = labelGO.GetComponent<RectTransform>();
                lrt.anchorMin = new Vector2(0, t);
                lrt.anchorMax = new Vector2(1, t);
                lrt.sizeDelta = new Vector2(0, 24);
                lrt.anchoredPosition = Vector2.zero;
            }
        }

        private void DrawAxisLabels(List<string> xLabels)
        {
            foreach (var xLabel in xLabels)
            {
                var labelGO = new GameObject($"XLabel_{xLabel}", typeof(TextMeshProUGUI));
                labelGO.transform.SetParent(xAxisLabelsRow, false);
                var label = labelGO.GetComponent<TextMeshProUGUI>();
                label.text = xLabel;
                label.font = labelFont;
                label.fontSize = 20;
                label.color = axisTextColor;
                label.alignment = TextAlignmentOptions.Center;

                var le = labelGO.AddComponent<LayoutElement>();
                le.flexibleWidth = 1;
            }
        }

        private void DrawLegend(List<ChartSeries> series)
        {
            foreach (var s in series)
            {
                var itemGO = new GameObject($"Legend_{s.label}", typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
                itemGO.transform.SetParent(legendRow, false);

                var hlg = itemGO.GetComponent<HorizontalLayoutGroup>();
                hlg.spacing = 6;
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = false;

                var fitter = itemGO.GetComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var dotGO = new GameObject("Dot", typeof(Image), typeof(LayoutElement));
                dotGO.transform.SetParent(itemGO.transform, false);
                var dotImg = dotGO.GetComponent<Image>();
                dotImg.sprite = dotSprite;
                dotImg.color = s.color;
                var dotLE = dotGO.GetComponent<LayoutElement>();
                dotLE.preferredWidth = dotSize;
                dotLE.preferredHeight = dotSize;

                var textGO = new GameObject("Label", typeof(TextMeshProUGUI), typeof(LayoutElement));
                textGO.transform.SetParent(itemGO.transform, false);
                var text = textGO.GetComponent<TextMeshProUGUI>();
                text.text = s.label;
                text.font = labelFont;
                text.fontSize = 22;
                text.color = Color.black;
                text.enableAutoSizing = false;
                text.overflowMode = TextOverflowModes.Overflow;
                var textLE = textGO.GetComponent<LayoutElement>();
                textLE.preferredWidth = 120;
                textLE.preferredHeight = 30;
            }
        }

        private void DrawSeries(ChartSeries series)
        {
            int count = series.values.Count;
            var points = new Vector2[count];

            for (int i = 0; i < count; i++)
            {
                float xT = count == 1 ? 0f : (float)i / (count - 1);
                float yT = Mathf.InverseLerp(yMin, yMax, series.values[i]);
                points[i] = new Vector2(xT, yT);
            }

            for (int i = 0; i < count - 1; i++)
                DrawLineSegment(points[i], points[i + 1], series.color);

            for (int i = 0; i < count; i++)
                DrawDot(points[i], series.color);
        }

        private void DrawLineSegment(Vector2 aNorm, Vector2 bNorm, Color color)
        {
            Vector2 a = NormToLocal(aNorm);
            Vector2 b = NormToLocal(bNorm);

            var lineGO = new GameObject("Line", typeof(Image));
            lineGO.transform.SetParent(plotArea, false);
            var rt = lineGO.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0.5f);

            Vector2 diff = b - a;
            float distance = diff.magnitude;
            float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

            rt.sizeDelta = new Vector2(distance, lineThickness);
            rt.anchoredPosition = a;
            rt.localRotation = Quaternion.Euler(0, 0, angle);

            lineGO.GetComponent<Image>().color = color;
        }

        private void DrawDot(Vector2 posNorm, Color color)
        {
            Vector2 pos = NormToLocal(posNorm);

            var dotGO = new GameObject("Dot", typeof(Image));
            dotGO.transform.SetParent(plotArea, false);
            var rt = dotGO.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(dotSize, dotSize);
            rt.anchoredPosition = pos;

            var img = dotGO.GetComponent<Image>();
            img.sprite = dotSprite;
            img.color = color;
        }

        private Vector2 NormToLocal(Vector2 norm)
        {
            float width = plotArea.rect.width;
            float height = plotArea.rect.height;
            return new Vector2(norm.x * width, norm.y * height);
        }

        // Generates a filled circle texture at runtime so no sprite asset is needed.
        private Sprite CreateCircleSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float radius = size / 2f - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    tex.SetPixel(x, y, dist <= radius ? Color.white : Color.clear);
                }
            }
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
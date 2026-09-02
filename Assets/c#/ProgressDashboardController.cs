using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace JapaneseLearning.Dashboard
{
    // Used by LineChartUI.Render(). This class lived in the original
    // ProgressDashboardController file, which is why it wasn't in any of the
    // other uploaded scripts - recreated here to match how LineChartUI reads it
    // (s.label, s.color, s.values).
    // [Serializable]
    // public class ChartSeries
    // {
    //     public string label;
    //     public Color color;
    //     public List<float> values = new List<float>();
    // }

    /// <summary>
    /// The "glue" script: owns one IProgressDataProvider (mock or Firebase) for
    /// grades, plus one FirebaseActivityCalendarProvider for the live
    /// avg-sessions-per-week figure, and pushes both into every UI piece
    /// (chart, stat cards, delta rows, level bars) whenever either updates.
    /// </summary>
    public class ProgressDashboardController : MonoBehaviour
    {
        [Header("Data source")]
        [SerializeField] private bool useMockData = true;
        [SerializeField] private string studentUid;
        [Tooltip("Leave blank to auto-resolve from students/{uid}/enrollment/courseId.")]
        [SerializeField] private string jlptLevel;

        [Header("Chart")]
        [SerializeField] private LineChartUI lineChart;

        [Header("Stat cards, in this exact order: Overall Improvement, Time Period, Avg Sessions/Week")]
        [SerializeField] private StatCardUI overallImprovementCard;
        [SerializeField] private StatCardUI timePeriodCard;
        [SerializeField] private StatCardUI avgSessionsCard;

        [Header("Skill delta rows, in this exact order: Reading, Writing, Listening, Speaking")]
        [SerializeField] private List<SkillDeltaRowUI> skillDeltaRows = new List<SkillDeltaRowUI>();

        [Header("Current level bars, in this exact order: Reading, Writing, Listening, Speaking")]
        [SerializeField] private List<SkillLevelBarUI> skillLevelBars = new List<SkillLevelBarUI>();

        private IProgressDataProvider gradeProvider;
        private FirebaseActivityCalendarProvider calendarProvider;

        private ProgressReport latestReport;
        private float? liveAvgSessionsPerWeek;

        void Start()
        {
            gradeProvider = useMockData
                ? new MockProgressDataProvider()
                : new FirebaseProgressDataProvider(studentUid, string.IsNullOrEmpty(jlptLevel) ? null : jlptLevel);

            gradeProvider.FetchProgressReport(OnReportLoaded, OnError);

            if (!useMockData && !string.IsNullOrEmpty(studentUid))
            {
                calendarProvider = new FirebaseActivityCalendarProvider(studentUid);
                calendarProvider.Listen(OnCalendarUpdated, OnError);
            }
        }

        void OnDestroy()
        {
            if (gradeProvider is FirebaseProgressDataProvider fp) fp.StopListening();
            calendarProvider?.StopListening();
        }

        private void OnReportLoaded(ProgressReport report)
        {
            latestReport = report;
            Render();
        }

        private void OnCalendarUpdated(Dictionary<string, DayActivity> calendar)
        {
            liveAvgSessionsPerWeek = FirebaseActivityCalendarProvider.ComputeAverageSessionsPerWeek(calendar);
            Render(); // re-render so the stat card reflects this even if grades haven't changed
        }

        private void OnError(string message)
        {
            Debug.LogError($"ProgressDashboardController: {message}");
        }

        private void Render()
        {
            if (latestReport == null) return;

            RenderChart(latestReport);
            RenderStatCards(latestReport);
            RenderSkillDeltaRows(latestReport);
            RenderSkillLevelBars(latestReport);
        }

        private void RenderChart(ProgressReport report)
        {
            if (lineChart == null) return;

            var xLabels = report.weeklyPoints.Select(p => p.weekLabel).ToList();
            var series = new List<ChartSeries>
            {
                new ChartSeries { label = "Reading",   color = new Color(0.51f, 0.20f, 0.78f), values = report.weeklyPoints.Select(p => p.reading).ToList() },
                new ChartSeries { label = "Writing",   color = new Color(0.16f, 0.60f, 0.42f), values = report.weeklyPoints.Select(p => p.writing).ToList() },
                new ChartSeries { label = "Listening", color = new Color(0.85f, 0.45f, 0.20f), values = report.weeklyPoints.Select(p => p.listening).ToList() },
                new ChartSeries { label = "Speaking",  color = new Color(0.20f, 0.45f, 0.85f), values = report.weeklyPoints.Select(p => p.speaking).ToList() },
            };

            lineChart.Render(xLabels, series);
        }

        private void RenderStatCards(ProgressReport report)
        {
            if (overallImprovementCard != null)
            {
                string sign = report.overallImprovementPercent >= 0 ? "+" : "";
                overallImprovementCard.SetValue($"{sign}{report.overallImprovementPercent:0}%", "Overall Improvement");
            }

            if (timePeriodCard != null)
                timePeriodCard.SetValue($"{report.weeksTracked} wks", "Time Period");

            if (avgSessionsCard != null)
            {
                float avgSessions = liveAvgSessionsPerWeek ?? report.avgSessionsPerWeek;
                avgSessionsCard.SetValue($"{avgSessions:0.0}", "Avg Sessions/Week");
            }
        }

        private void RenderSkillDeltaRows(ProgressReport report)
        {
            var summaries = report.BuildSkillSummaries(); // Reading, Writing, Listening, Speaking
            for (int i = 0; i < skillDeltaRows.Count && i < summaries.Count; i++)
                skillDeltaRows[i]?.SetData(summaries[i], report.weeksTracked);
        }

        private void RenderSkillLevelBars(ProgressReport report)
        {
            if (report.weeklyPoints.Count == 0) return;

            var latest = report.weeklyPoints[report.weeklyPoints.Count - 1];
            var summaries = report.BuildSkillSummaries(); // reused just for matching colors

            var latestValues = new (string name, float percent)[]
            {
                ("Reading", latest.reading),
                ("Writing", latest.writing),
                ("Listening", latest.listening),
                ("Speaking", latest.speaking),
            };

            for (int i = 0; i < skillLevelBars.Count && i < latestValues.Length; i++)
            {
                var (name, percent) = latestValues[i];
                var color = i < summaries.Count ? summaries[i].color : Color.white;
                skillLevelBars[i]?.SetData(name, percent, color);
            }
        }
    }
}
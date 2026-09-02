using System;
using System.Collections.Generic;
using UnityEngine;

namespace JapaneseLearning.Dashboard
{
    [Serializable]
    public class SkillPoint
    {
        public string weekLabel;
        public float reading;
        public float writing;
        public float listening;
        public float speaking;
    }

    [Serializable]
    public class SkillSummary
    {
        public string skillName;
        public float startPercent;
        public float endPercent;
        public Color color;

        public float DeltaPercent => endPercent - startPercent;
    }

    [Serializable]
    public class ProgressReport
    {
        public List<SkillPoint> weeklyPoints = new List<SkillPoint>();
        public float overallImprovementPercent;
        public int weeksTracked;
        public float avgSessionsPerWeek;

        // Derives the summary rows (Reading/Writing/Listening/Speaking with
        // start->end percentages) from the first and last week in the series.
        public List<SkillSummary> BuildSkillSummaries()
        {
            var summaries = new List<SkillSummary>();
            if (weeklyPoints.Count == 0) return summaries;

            var first = weeklyPoints[0];
            var last = weeklyPoints[weeklyPoints.Count - 1];

            summaries.Add(new SkillSummary { skillName = "Reading", startPercent = first.reading, endPercent = last.reading, color = new Color(0.51f, 0.20f, 0.78f) });
            summaries.Add(new SkillSummary { skillName = "Writing", startPercent = first.writing, endPercent = last.writing, color = new Color(0.16f, 0.60f, 0.42f) });
            summaries.Add(new SkillSummary { skillName = "Listening", startPercent = first.listening, endPercent = last.listening, color = new Color(0.85f, 0.45f, 0.20f) });
            summaries.Add(new SkillSummary { skillName = "Speaking", startPercent = first.speaking, endPercent = last.speaking, color = new Color(0.20f, 0.45f, 0.85f) });

            return summaries;
        }
    }
}
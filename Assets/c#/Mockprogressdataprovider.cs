using System;

namespace JapaneseLearning.Dashboard
{
    // Stand-in data source for development/demo. Reproduces the same shape
    // of numbers as the reference dashboard (8 weeks, four skills).
    public class MockProgressDataProvider : IProgressDataProvider
    {
        public void FetchProgressReport(Action<ProgressReport> onSuccess, Action<string> onError = null)
        {
            var report = new ProgressReport
            {
                overallImprovementPercent = 32f,
                weeksTracked = 8,
                avgSessionsPerWeek = 4.2f
            };

            float[] reading = { 25, 32, 40, 47, 55, 63, 73, 82 };
            float[] writing = { 15, 21, 27, 33, 40, 47, 56, 65 };
            float[] listening = { 10, 16, 22, 29, 36, 43, 51, 58 };
            float[] speaking = { 5, 10, 15, 20, 25, 30, 36, 41 };

            for (int i = 0; i < 8; i++)
            {
                report.weeklyPoints.Add(new SkillPoint
                {
                    weekLabel = $"W{i + 1}",
                    reading = reading[i],
                    writing = writing[i],
                    listening = listening[i],
                    speaking = speaking[i]
                });
            }

            onSuccess?.Invoke(report);
        }
    }
}
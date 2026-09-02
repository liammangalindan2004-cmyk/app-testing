using System;
using System.Collections.Generic;
using UnityEngine;

namespace JapaneseLearning.Dashboard
{
    /// <summary>
    /// Drives a grid of ProgressCircle instances (one per calendar day) from a
    /// single live Firebase listener on activityCalendar/{studentUid}. Each
    /// circle in dayCircles gets its own date and its own progress value -
    /// no more one script instance controlling every circle identically.
    ///
    /// Setup:
    ///   1. Drag your 28 ProgressCircle objects into dayCircles, in order from
    ///      oldest day (index 0) to most recent/today (last index).
    ///   2. Either leave each circle's dateKey blank (this controller will
    ///      auto-assign dates counting back from "today"), or hand-set
    ///      dateKey on individual circles if your grid isn't a simple rolling
    ///      window (e.g. a fixed calendar month).
    ///   3. Set studentUid (or wire it up from wherever you store the logged
    ///      in user's UID) and press Play - circles update live as activity
    ///      is logged.
    /// </summary>
    public class ActivityCalendarGridController : MonoBehaviour
    {
        [Header("Student")]
        [SerializeField] private string studentUid;

        [Header("Grid")]
        [Tooltip("Assign in order: oldest day first, most recent day (today) last.")]
        [SerializeField] private List<ProgressCircle> dayCircles = new List<ProgressCircle>();

        [Tooltip("Used only for circles whose dateKey is left blank - assigns dates " +
                 "counting back from today so the last circle in the list = today.")]
        [SerializeField] private bool autoAssignBlankDateKeys = true;

        [Header("Progress formula")]
        [Tooltip("minutesStudied on a day is divided by this to get 0-1 progress for that circle. " +
                 "Reaching or exceeding this fills the circle completely.")]
        [SerializeField] private float dailyGoalMinutes = 20f;

        private FirebaseActivityCalendarProvider provider;

        void Start()
        {
            if (autoAssignBlankDateKeys)
                AssignBlankDateKeys();

            if (string.IsNullOrEmpty(studentUid))
            {
                Debug.LogWarning("ActivityCalendarGridController: studentUid not set.");
                return;
            }

            provider = new FirebaseActivityCalendarProvider(studentUid);
            provider.Listen(OnCalendarUpdated, err => Debug.LogError($"ActivityCalendarGridController: {err}"));
        }

        void OnDestroy()
        {
            provider?.StopListening();
        }

        // Circles with no dateKey set in the Inspector get one assigned here so
        // the whole list represents a contiguous window ending today, e.g. with
        // 28 circles: index 0 = today-27, index 27 = today.
        private void AssignBlankDateKeys()
        {
            DateTime today = DateTime.Today;
            int count = dayCircles.Count;

            for (int i = 0; i < count; i++)
            {
                var circle = dayCircles[i];
                if (circle == null || !string.IsNullOrEmpty(circle.dateKey)) continue;

                int daysBack = (count - 1) - i; // last index = today (0 days back)
                circle.dateKey = today.AddDays(-daysBack).ToString("yyyy-MM-dd");
            }
        }

        private void OnCalendarUpdated(Dictionary<string, DayActivity> calendar)
        {
            foreach (var circle in dayCircles)
            {
                if (circle == null || string.IsNullOrEmpty(circle.dateKey)) continue;

                float progress = 0f;
                if (calendar.TryGetValue(circle.dateKey, out var day))
                    progress = Mathf.Clamp01(day.minutesStudied / dailyGoalMinutes);

                circle.SetProgress(progress);
            }
        }
    }
}

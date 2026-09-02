using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;

namespace JapaneseLearning.Dashboard
{
    /// <summary>
    /// Live-listens to activityCalendar/{studentUid} in the Realtime Database.
    /// Fires onUpdated immediately with the current snapshot and again every
    /// time any day under that node changes (e.g. right after a session ends
    /// and the app writes today's minutesStudied/sessionsCompleted).
    ///
    /// This one node backs two different UI pieces:
    ///   - ActivityCalendarGridController: per-day progress on the 28 circles
    ///   - Dashboard "avg sessions/week" stat card, via ComputeAverageSessionsPerWeek
    /// Both should share a single instance/subscription of this class rather
    /// than each attaching their own listener to the same path.
    /// </summary>
    public class FirebaseActivityCalendarProvider
    {
        private const string DatabaseUrl = "https://unmei-nihongo-center-default-rtdb.asia-southeast1.firebasedatabase.app/";

        private readonly string studentUid;
        private DatabaseReference calendarRef;
        private Action<Dictionary<string, DayActivity>> onUpdated;
        private Action<string> onError;

        public FirebaseActivityCalendarProvider(string studentUid)
        {
            this.studentUid = studentUid;
        }

        public void Listen(Action<Dictionary<string, DayActivity>> onUpdated, Action<string> onError = null)
        {
            this.onUpdated = onUpdated;
            this.onError = onError;

            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(depTask =>
            {
                if (depTask.Result != DependencyStatus.Available)
                {
                    onError?.Invoke($"Firebase dependencies not available: {depTask.Result}");
                    return;
                }

                var app = FirebaseApp.DefaultInstance;
                var db = FirebaseDatabase.GetInstance(app, DatabaseUrl);
                calendarRef = db.RootReference.Child("activityCalendar").Child(studentUid);
                calendarRef.ValueChanged += HandleValueChanged;
            });
        }

        public void StopListening()
        {
            if (calendarRef != null)
                calendarRef.ValueChanged -= HandleValueChanged;
        }

        private void HandleValueChanged(object sender, ValueChangedEventArgs args)
        {
            if (args.DatabaseError != null)
            {
                onError?.Invoke(args.DatabaseError.Message);
                return;
            }

            var snapshot = args.Snapshot;
            if (snapshot == null || !snapshot.Exists)
            {
                // No entries yet is a valid state (brand new student) - hand back
                // an empty map rather than erroring.
                onUpdated?.Invoke(new Dictionary<string, DayActivity>());
                return;
            }

            var result = new Dictionary<string, DayActivity>();
            foreach (var daySnap in snapshot.Children)
            {
                // Skip the template placeholder if it's ever present.
                if (daySnap.Key == "date_template") continue;

                result[daySnap.Key] = new DayActivity
                {
                    dateKey = daySnap.Key,
                    studied = ReadBool(daySnap.Child("studied")),
                    minutesStudied = ReadInt(daySnap.Child("minutesStudied")),
                    kanjiReviewed = ReadInt(daySnap.Child("kanjiReviewed")),
                    sessionsCompleted = ReadInt(daySnap.Child("sessionsCompleted")),
                    skillsPracticed = daySnap.Child("skillsPracticed").Children
                        .Select(c => c.Value?.ToString())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList(),
                    streakDay = ReadInt(daySnap.Child("streakDay")),
                };
            }

            onUpdated?.Invoke(result);
        }

        /// <summary>
        /// Averages sessionsCompleted across every day currently in the node and
        /// scales to a 7-day week. Feed this straight into ProgressReport.avgSessionsPerWeek
        /// once both the grades listener and this one have reported at least once.
        /// </summary>
        public static float ComputeAverageSessionsPerWeek(Dictionary<string, DayActivity> calendar)
        {
            if (calendar == null || calendar.Count == 0) return 0f;

            int totalSessions = calendar.Values.Sum(d => d.sessionsCompleted);
            float weeks = calendar.Count / 7f;
            return weeks > 0f ? totalSessions / weeks : 0f;
        }

        private static bool ReadBool(DataSnapshot snap) =>
            snap != null && snap.Exists && snap.Value != null && bool.TryParse(snap.Value.ToString(), out var b) && b;

        private static int ReadInt(DataSnapshot snap)
        {
            if (snap == null || !snap.Exists || snap.Value == null) return 0;
            return int.TryParse(snap.Value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var val)
                ? val
                : 0;
        }
    }
}

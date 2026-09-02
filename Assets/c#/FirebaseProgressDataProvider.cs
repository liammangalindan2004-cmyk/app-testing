using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine;

namespace JapaneseLearning.Dashboard
{
    /// <summary>
    /// Reads AND LIVE-LISTENS to a student's grade node in the Firebase Realtime
    /// Database. Unlike a one-shot fetch, this attaches a ValueChanged handler,
    /// which Firebase fires immediately with the current snapshot and then again
    /// every single time the node is written to (e.g. right after a pronunciation
    /// or kanji activity finishes and updates the student's grades). Each firing
    /// re-invokes the same onSuccess callback the dashboard already expects, so
    /// ProgressDashboardController.OnReportLoaded just gets called again and the
    /// chart/cards redraw with the new numbers - no polling required.
    ///
    /// Expected DB schema (matches unmei-realtime-database.json):
    ///   grades/{jlptLevel}/{studentUid}/reading      (number, current score)
    ///   grades/{jlptLevel}/{studentUid}/writing
    ///   grades/{jlptLevel}/{studentUid}/listening
    ///   grades/{jlptLevel}/{studentUid}/speaking
    ///   grades/{jlptLevel}/{studentUid}/average
    ///   grades/{jlptLevel}/{studentUid}/updatedAt
    ///   grades/{jlptLevel}/{studentUid}/history/week_01/{reading,writing,listening,speaking,recordedAt}
    ///   grades/{jlptLevel}/{studentUid}/history/week_02/...
    ///
    /// jlptLevel is one of: "jlpt_n5", "jlpt_n4", "jlpt_n3", "jlpt_n2".
    /// If jlptLevel is not supplied to the constructor, it is auto-resolved from
    /// students/{studentUid}/enrollment/courseId before the listener attaches -
    /// this matters because a student's active JLPT level can change over time,
    /// and hardcoding "jlpt_n5" would silently show the wrong node for anyone else.
    /// </summary>
    public class FirebaseProgressDataProvider : IProgressDataProvider
    {
        // From the DB you linked. Needed explicitly because the project's region
        // (asia-southeast1) isn't the default us-central1 Firebase assumes.
        private const string DatabaseUrl = "https://unmei-nihongo-center-default-rtdb.asia-southeast1.firebasedatabase.app/";

        private readonly string studentUid;
        private string jlptLevel; // may be null until auto-resolved

        private DatabaseReference gradeRef;
        private Action<ProgressReport> onDataChanged;
        private Action<string> onError;

        /// <param name="studentUid">Firebase Auth UID / student doc key.</param>
        /// <param name="jlptLevel">
        /// Optional. Pass e.g. "jlpt_n4" to skip the lookup. Leave null to have it
        /// auto-resolved from students/{studentUid}/enrollment/courseId.
        /// </param>
        public FirebaseProgressDataProvider(string studentUid, string jlptLevel = null)
        {
            this.studentUid = studentUid;
            this.jlptLevel = jlptLevel;
        }

        /// <summary>
        /// Matches IProgressDataProvider. Despite the name, this doesn't just fetch
        /// once - it subscribes, so onSuccess will be called again on every future
        /// change until StopListening() is called.
        /// </summary>
        public void FetchProgressReport(Action<ProgressReport> onSuccess, Action<string> onFailure = null)
        {
            onDataChanged = onSuccess;
            onError = onFailure;

            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(depTask =>
            {
                if (depTask.Result != DependencyStatus.Available)
                {
                    onError?.Invoke($"Firebase dependencies not available: {depTask.Result}");
                    return;
                }

                var app = FirebaseApp.DefaultInstance;
                var db = FirebaseDatabase.GetInstance(app, DatabaseUrl);

                if (!string.IsNullOrEmpty(jlptLevel))
                {
                    AttachGradeListener(db, jlptLevel);
                    return;
                }

                // jlptLevel wasn't supplied - look up the student's active course
                // once, then attach the live listener to the resolved node.
                db.RootReference.Child("students").Child(studentUid)
                    .Child("enrollment").Child("courseId")
                    .GetValueAsync()
                    .ContinueWithOnMainThread(courseTask =>
                    {
                        if (courseTask.IsFaulted || courseTask.IsCanceled || !courseTask.Result.Exists)
                        {
                            onError?.Invoke($"Could not resolve enrolled JLPT level for student '{studentUid}'.");
                            return;
                        }

                        jlptLevel = courseTask.Result.Value.ToString();
                        AttachGradeListener(db, jlptLevel);
                    });
            });
        }

        private void AttachGradeListener(FirebaseDatabase db, string resolvedLevel)
        {
            gradeRef = db.RootReference.Child("grades").Child(resolvedLevel).Child(studentUid);
            gradeRef.ValueChanged += HandleValueChanged;
        }

        /// <summary>
        /// Call from OnDestroy()/OnDisable() on whatever owns this provider
        /// (e.g. ProgressDashboardController) to unsubscribe and avoid leaks.
        /// </summary>
        public void StopListening()
        {
            if (gradeRef != null)
                gradeRef.ValueChanged -= HandleValueChanged;
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
                onError?.Invoke($"No grade data found for student '{studentUid}' under '{jlptLevel}'.");
                return;
            }

            try
            {
                var report = BuildReportFromSnapshot(snapshot);
                onDataChanged?.Invoke(report);
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Failed to parse grade data: {ex.Message}");
            }
        }

        private ProgressReport BuildReportFromSnapshot(DataSnapshot snapshot)
        {
            var weeklyPoints = new List<SkillPoint>();

            var historySnap = snapshot.Child("history");
            if (historySnap.Exists)
            {
                // "week_01", "week_02", ... sorts correctly as text since it's zero-padded.
                var orderedWeeks = historySnap.Children
                    .OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int weekNumber = 1;
                foreach (var weekSnap in orderedWeeks)
                {
                    weeklyPoints.Add(new SkillPoint
                    {
                        weekLabel = FormatWeekLabel(weekSnap.Key, weekNumber),
                        reading = ReadFloat(weekSnap.Child("reading")),
                        writing = ReadFloat(weekSnap.Child("writing")),
                        listening = ReadFloat(weekSnap.Child("listening")),
                        speaking = ReadFloat(weekSnap.Child("speaking")),
                    });
                    weekNumber++;
                }
            }

            // Append the live top-level totals as the most recent point so a just-
            // finished activity shows up immediately, even before a new "week_XX"
            // history entry has been written.
            weeklyPoints.Add(new SkillPoint
            {
                weekLabel = "Now",
                reading = ReadFloat(snapshot.Child("reading")),
                writing = ReadFloat(snapshot.Child("writing")),
                listening = ReadFloat(snapshot.Child("listening")),
                speaking = ReadFloat(snapshot.Child("speaking")),
            });

            float startAvg = AveragePoint(weeklyPoints.First());
            float endAvg = AveragePoint(weeklyPoints.Last());

            return new ProgressReport
            {
                weeklyPoints = weeklyPoints,
                weeksTracked = weeklyPoints.Count,
                overallImprovementPercent = startAvg > 0f ? (endAvg - startAvg) / startAvg * 100f : 0f,
                // Best-effort: the grades node doesn't log individual sessions, only
                // weekly snapshots, so this approximates 1 recorded session/week.
                avgSessionsPerWeek = weeklyPoints.Count > 1 ? 1f : 0f,
            };
        }

        private static float AveragePoint(SkillPoint p) =>
            (p.reading + p.writing + p.listening + p.speaking) / 4f;

        private static float ReadFloat(DataSnapshot snap)
        {
            if (snap == null || !snap.Exists || snap.Value == null) return 0f;
            return float.TryParse(snap.Value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var val)
                ? val
                : 0f;
        }

        private static string FormatWeekLabel(string key, int fallbackNumber)
        {
            var digits = new string(key.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var n) ? $"Wk {n}" : $"Wk {fallbackNumber}";
        }
    }
}
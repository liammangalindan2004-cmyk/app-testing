using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine;

/// <summary>
/// Shared helper for logging today's activity to
/// activityCalendar/{studentUid}/{yyyy-MM-dd} in the Realtime Database.
///
/// Used by the reading quiz (KanjiQuiz.cs), writing practice
/// (KanjiPracticeUI.cs), and pronunciation module
/// (PronunciationSceneController.cs) so a completed exercise always shows
/// up on the student's activity calendar / streak UI
/// (ActivityCalendarGridController, dashboard "avg sessions/week"), not
/// just in that skill's grade.
///
/// Schema (see DayActivity.cs / FirebaseActivityCalendarProvider.cs):
///   activityCalendar/{uid}/{yyyy-MM-dd} = {
///     studied, minutesStudied, kanjiReviewed, sessionsCompleted,
///     skillsPracticed: [...], streakDay
///   }
///
/// This reads-then-writes today's (and yesterday's, for the streak calc)
/// node rather than using a transaction — fine for this app's
/// single-active-session-per-student usage, matching how the rest of the
/// project already handles grading writes (KanjiPracticeUI's running
/// session average, etc.).
/// </summary>
public static class ActivityCalendarWriter
{
    /// <param name="dbRoot">Root reference of the Realtime Database.</param>
    /// <param name="userId">Student uid (from StudentSession).</param>
    /// <param name="skill">"reading", "writing", or "speaking" — added to skillsPracticed if not already present.</param>
    /// <param name="kanjiDelta">How many characters/items to add to today's kanjiReviewed.</param>
    /// <param name="minutesDelta">How many minutes to add to today's minutesStudied.</param>
    /// <param name="countSession">Pass true once per completed session (quiz finished, or the first graded item in a practice scene) to bump sessionsCompleted.</param>
    public static void LogActivity(
        DatabaseReference dbRoot,
        string userId,
        string skill,
        int kanjiDelta = 0,
        int minutesDelta = 0,
        bool countSession = false)
    {
        if (dbRoot == null || string.IsNullOrEmpty(userId)) return;

        DateTime today = DateTime.UtcNow.Date;
        string todayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string yesterdayKey = today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        DatabaseReference calRef = dbRoot.Child("activityCalendar").Child(userId);

        // Look at yesterday first so we can extend/break the streak correctly.
        calRef.Child(yesterdayKey).GetValueAsync().ContinueWithOnMainThread(yTask =>
        {
            bool yesterdayStudied = false;
            int yesterdayStreak = 0;

            if (!yTask.IsFaulted && !yTask.IsCanceled && yTask.Result != null && yTask.Result.Exists)
            {
                yesterdayStudied = ReadBool(yTask.Result, "studied");
                yesterdayStreak = ReadInt(yTask.Result, "streakDay");
            }

            DatabaseReference dayRef = calRef.Child(todayKey);
            dayRef.GetValueAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogWarning("[ActivityCalendar] read failed: " + task.Exception?.Message);
                    return;
                }

                DataSnapshot snap = task.Result;
                bool alreadyStudiedToday = ReadBool(snap, "studied");

                int kanjiReviewed = ReadInt(snap, "kanjiReviewed") + Mathf.Max(0, kanjiDelta);
                int minutesStudied = ReadInt(snap, "minutesStudied") + Mathf.Max(0, minutesDelta);
                int sessionsCompleted = ReadInt(snap, "sessionsCompleted") + (countSession ? 1 : 0);

                var skillsPracticed = snap.Child("skillsPracticed").Children
                    .Select(c => c.Value?.ToString())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
                if (!string.IsNullOrEmpty(skill) && !skillsPracticed.Contains(skill))
                    skillsPracticed.Add(skill);

                // Only (re)compute the streak the first time today is marked
                // studied — once it's set, later same-day writes shouldn't
                // recompute it from yesterday again.
                int streakDay = alreadyStudiedToday
                    ? ReadInt(snap, "streakDay")
                    : (yesterdayStudied ? yesterdayStreak + 1 : 0);

                var data = new Dictionary<string, object>
                {
                    ["studied"] = true,
                    ["kanjiReviewed"] = kanjiReviewed,
                    ["minutesStudied"] = minutesStudied,
                    ["sessionsCompleted"] = sessionsCompleted,
                    ["skillsPracticed"] = skillsPracticed,
                    ["streakDay"] = streakDay
                };

                dayRef.UpdateChildrenAsync(data).ContinueWithOnMainThread(t =>
                {
                    if (t.IsFaulted)
                        Debug.LogWarning("[ActivityCalendar] write failed: " + t.Exception?.Message);
                });
            });
        });
    }

    private static bool ReadBool(DataSnapshot snap, string key)
    {
        var child = snap.Child(key);
        return child.Exists && child.Value != null &&
               bool.TryParse(child.Value.ToString(), out bool b) && b;
    }

    private static int ReadInt(DataSnapshot snap, string key)
    {
        var child = snap.Child(key);
        if (!child.Exists || child.Value == null) return 0;
        return int.TryParse(child.Value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
            ? v
            : 0;
    }
}

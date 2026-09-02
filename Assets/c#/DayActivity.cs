using System;
using System.Collections.Generic;

namespace JapaneseLearning.Dashboard
{
    // Mirrors one child of activityCalendar/{studentUid}/{yyyy-MM-dd} in the
    // Realtime Database, e.g.:
    //   "2026-08-04": {
    //     "studied": true, "minutesStudied": 20, "kanjiReviewed": 8,
    //     "sessionsCompleted": 1, "skillsPracticed": ["listening"], "streakDay": 0
    //   }
    [Serializable]
    public class DayActivity
    {
        public string dateKey; // "yyyy-MM-dd", filled in from the RTDB child key
        public bool studied;
        public int minutesStudied;
        public int kanjiReviewed;
        public int sessionsCompleted;
        public List<string> skillsPracticed = new List<string>();
        public int streakDay;
    }
}

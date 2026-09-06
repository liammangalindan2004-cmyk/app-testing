using UnityEngine;

/// <summary>
/// Holds whichever student is currently logged in for this play session.
///
/// This app's login is database-driven, NOT Firebase Auth — the student
/// and admin accounts are test data seeded directly under users/{uid} in
/// the Realtime Database, not registered Firebase Auth accounts. See
/// AuthManager (loginValidation.cs) for the actual login check.
///
/// AuthManager calls SignIn(...) once, right after a successful login
/// lookup. Every other scene that needs "whose data is this" — the
/// multiple-choice quiz, writing practice, pronunciation grading,
/// progress dashboards, activity calendar — reads CurrentUid from here
/// instead of a hardcoded/serialized string, so grades always land on the
/// account that's actually logged in.
///
/// Backed by PlayerPrefs so the session survives scene loads (and app
/// restarts, which is harmless for test data) rather than only living as
/// long as this static class happens to stay in memory.
/// </summary>
public static class StudentSession
{
    private const string PrefKeyUid = "unmei_session_uid";
    private const string PrefKeyEmail = "unmei_session_email";
    private const string PrefKeyName = "unmei_session_name";
    private const string PrefKeyRole = "unmei_session_role";

    private static bool _loaded;
    private static string _uid;
    private static string _email;
    private static string _name;
    private static string _role;

    public static string CurrentUid { get { EnsureLoaded(); return _uid; } }
    public static string CurrentEmail { get { EnsureLoaded(); return _email; } }
    public static string CurrentName { get { EnsureLoaded(); return _name; } }
    public static string CurrentRole { get { EnsureLoaded(); return _role; } }
    public static bool IsLoggedIn { get { EnsureLoaded(); return !string.IsNullOrEmpty(_uid); } }

    /// <summary>Called once, right after AuthManager confirms a valid login.</summary>
    public static void SignIn(string uid, string email, string name, string role)
    {
        _uid = uid;
        _email = email;
        _name = name;
        _role = role;
        _loaded = true;

        PlayerPrefs.SetString(PrefKeyUid, uid ?? "");
        PlayerPrefs.SetString(PrefKeyEmail, email ?? "");
        PlayerPrefs.SetString(PrefKeyName, name ?? "");
        PlayerPrefs.SetString(PrefKeyRole, role ?? "");
        PlayerPrefs.Save();
    }

    /// <summary>Call from a Logout button so the next login doesn't inherit the old session.</summary>
    public static void SignOut()
    {
        _uid = _email = _name = _role = null;
        _loaded = true;

        PlayerPrefs.DeleteKey(PrefKeyUid);
        PlayerPrefs.DeleteKey(PrefKeyEmail);
        PlayerPrefs.DeleteKey(PrefKeyName);
        PlayerPrefs.DeleteKey(PrefKeyRole);
        PlayerPrefs.Save();
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _uid = PlayerPrefs.GetString(PrefKeyUid, "");
        _email = PlayerPrefs.GetString(PrefKeyEmail, "");
        _name = PlayerPrefs.GetString(PrefKeyName, "");
        _role = PlayerPrefs.GetString(PrefKeyRole, "");
        _loaded = true;
    }
}
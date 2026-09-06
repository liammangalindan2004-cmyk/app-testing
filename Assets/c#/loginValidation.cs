using System;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AuthManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField emailInput;
    public TMP_InputField passwordInput;
    public TMP_Text statusText;

    [Header("Database")]
    // Your Realtime Database instance is hosted in asia-southeast1, so it needs
    // to be addressed explicitly.
    [SerializeField]
    private string databaseUrl = "https://unmei-nihongo-center-default-rtdb.asia-southeast1.firebasedatabase.app/";

    [Header("Scenes")]
    public string studentLandingScene = "landing";
    public string adminDashboardScene = "admin_dashboard";

    DatabaseReference dbRoot;

    void Awake()
{
    FirebaseBootstrap.InitializationTask
        .ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                dbRoot = FirebaseDatabase.GetInstance(
                    FirebaseApp.DefaultInstance,
                    databaseUrl
                ).RootReference;

                Debug.Log("✅ AuthManager database ready.");
            }
            else
            {
                Debug.LogError(
                    "❌ Firebase initialization failed: " + task.Result
                );

                if (statusText != null)
                    statusText.text = "Firebase initialization failed.";
            }
        });
}

    public void OnLoginButtonPressed()
{
    string email = emailInput.text.Trim();
    string password = passwordInput.text;

    // Check if fields are empty
    if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
    {
        statusText.text = "Email and password required";
        return;
    }

    // Check if Firebase database is ready
    if (dbRoot == null)
    {
        statusText.text = "Firebase is still initializing...";
        Debug.LogWarning("⚠️ Login attempted before Firebase database was ready.");
        return;
    }

    statusText.text = "Logging in...";

    dbRoot.Child("users")
        .OrderByChild("email")
        .EqualTo(email)
        .GetValueAsync()
        .ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                ShowGenericError();
                return;
            }

            if (!task.Result.Exists || task.Result.ChildrenCount == 0)
            {
                statusText.text = "Incorrect email or password";
                return;
            }

            DataSnapshot snap = null;

            foreach (DataSnapshot child in task.Result.Children)
            {
                snap = child;
                break;
            }

            string uid = snap.Child("uid").Exists
                ? snap.Child("uid").Value.ToString()
                : snap.Key;

            // Your existing UID-based password check
            if (password != uid)
            {
                statusText.text = "Incorrect email or password";
                return;
            }

            PostLoginChecks(snap, uid);
        });
}
        /// <summary>
    /// Once the email/password(uid) match is confirmed, check whether the
    /// account is revoked and what role/scene to route to. Also respects
    /// config/maintenanceMode for non-admins.
    /// </summary>
    void PostLoginChecks(DataSnapshot snap, string uid)
    {
        dbRoot.Child("config").Child("maintenanceMode").GetValueAsync().ContinueWithOnMainThread(maintTask =>
        {
            if (maintTask.IsFaulted)
            {
                ShowGenericError();
                return;
            }

            bool maintenanceMode = maintTask.Result.Exists && (bool)maintTask.Result.Value;

            bool isRevoked = snap.Child("isRevoked").Exists && (bool)snap.Child("isRevoked").Value;
            string role = snap.Child("role").Exists ? snap.Child("role").Value.ToString() : "student";
            string name = snap.Child("name").Exists ? snap.Child("name").Value.ToString() : uid;
            string email = snap.Child("email").Exists ? snap.Child("email").Value.ToString() : "";

            if (isRevoked)
            {
                statusText.text = "This account has been revoked. Please contact support.";
                return;
            }

            if (maintenanceMode && role != "admin")
            {
                statusText.text = "The system is under maintenance. Please try again later.";
                return;
            }

            statusText.text = "Welcome, " + name;

            // Populate the shared session so every other scene (quiz, writing,
            // pronunciation, dashboards) knows which student is logged in
            // instead of each one hardcoding a uid.
            StudentSession.SignIn(uid, email, name, role);

            TouchSession(uid, role);
            RouteToScene(role);
        });
    }

    /// <summary>
    /// Stamps students/{uid}/session/lastActivity so idle-timeout logic
    /// downstream (session.timeoutMinutes) has a fresh reference point.
    /// Admin accounts don't carry a session node, so we skip them.
    /// </summary>
    void TouchSession(string uid, string role)
    {
        if (role != "student") return;

        dbRoot.Child("students").Child(uid).Child("session").Child("lastActivity")
            .SetValueAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    void RouteToScene(string role)
    {
        string scene = role == "admin" ? adminDashboardScene : studentLandingScene;
        SceneManager.LoadSceneAsync(scene);
    }

    void ShowGenericError()
    {
        statusText.text = "Login failed. Please try again.";
    }
}
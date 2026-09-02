using System;
using Firebase;
using Firebase.Auth;
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
    // to be addressed explicitly — Firebase.Auth won't route to it automatically.
    [SerializeField]
    private string databaseUrl = "https://unmei-nihongo-center-default-rtdb.asia-southeast1.firebasedatabase.app/";

    [Header("Scenes")]
    public string studentLandingScene = "landing";
    public string adminDashboardScene = "admin_dashboard";

    FirebaseAuth auth;
    DatabaseReference dbRoot;

    void Awake()
    {
        auth = FirebaseAuth.DefaultInstance;
        dbRoot = FirebaseDatabase.GetInstance(FirebaseApp.DefaultInstance, databaseUrl).RootReference;
    }

    public void OnLoginButtonPressed()
    {
        string email = emailInput.text.Trim();
        string password = passwordInput.text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            statusText.text = "Email and password required";
            return;
        }

        statusText.text = "Logging in...";

        auth.SignInWithEmailAndPasswordAsync(email, password)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    ShowError(task.Exception);
                    return;
                }

                FirebaseUser user = task.Result.User;
                PostLoginChecks(user);
            });
    }

    /// <summary>
    /// After Firebase Auth succeeds, cross-check against users/{uid} in the
    /// Realtime Database: is the account revoked, and what role/scene should
    /// we route to. Also respects config/maintenanceMode for non-admins.
    /// </summary>
    void PostLoginChecks(FirebaseUser user)
    {
        dbRoot.Child("config").Child("maintenanceMode").GetValueAsync().ContinueWithOnMainThread(maintTask =>
        {
            if (maintTask.IsFaulted)
            {
                ShowGenericError();
                return;
            }

            bool maintenanceMode = maintTask.Result.Exists && (bool)maintTask.Result.Value;

            dbRoot.Child("users").Child(user.UserId).GetValueAsync().ContinueWithOnMainThread(userTask =>
            {
                if (userTask.IsFaulted || !userTask.Result.Exists)
                {
                    // Auth succeeded but there's no matching users/{uid} record —
                    // treat as invalid rather than letting them through.
                    auth.SignOut();
                    statusText.text = "Account not found. Please contact support.";
                    return;
                }

                DataSnapshot snap = userTask.Result;
                bool isRevoked = snap.Child("isRevoked").Exists && (bool)snap.Child("isRevoked").Value;
                string role = snap.Child("role").Exists ? snap.Child("role").Value.ToString() : "student";
                string name = snap.Child("name").Exists ? snap.Child("name").Value.ToString() : user.Email;

                if (isRevoked)
                {
                    auth.SignOut();
                    statusText.text = "This account has been revoked. Please contact support.";
                    return;
                }

                if (maintenanceMode && role != "admin")
                {
                    auth.SignOut();
                    statusText.text = "The system is under maintenance. Please try again later.";
                    return;
                }

                statusText.text = "Welcome, " + name;
                TouchSession(user.UserId, role);
                RouteToScene(role);
            });
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

    void ShowError(AggregateException ex)
    {
        FirebaseException fbEx =
            ex.InnerExceptions[0] as FirebaseException;

        if (fbEx == null)
        {
            ShowGenericError();
            return;
        }

        AuthError code = (AuthError)fbEx.ErrorCode;

        switch (code)
        {
            case AuthError.WrongPassword:
                statusText.text = "Wrong password";
                break;
            case AuthError.UserNotFound:
                statusText.text = "User not found";
                break;
            default:
                ShowGenericError();
                break;
        }
    }

    void ShowGenericError()
    {
        statusText.text = "Login failed. Please try again.";
    }
}
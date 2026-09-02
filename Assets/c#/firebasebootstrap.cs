using UnityEngine;
using Firebase;
using Firebase.Extensions;

public static class FirebaseBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        Debug.Log("🔥 App started – initializing Firebase");

        FirebaseApp.CheckAndFixDependenciesAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.Result == DependencyStatus.Available)
                {
                    Debug.Log("✅ Firebase is ready");
                }
                else
                {
                    Debug.LogError("❌ Firebase dependencies missing");
                }
            });
    }
}

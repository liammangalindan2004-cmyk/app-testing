using UnityEngine;
using Firebase;
using Firebase.Extensions;
using System.Threading.Tasks;

public static class FirebaseBootstrap
{
    public static Task<DependencyStatus> InitializationTask { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        Debug.Log("🔥 App started – initializing Firebase");

        InitializationTask = FirebaseApp.CheckAndFixDependenciesAsync();

        InitializationTask.ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                Debug.Log("✅ Firebase is ready");
            }
            else
            {
                Debug.LogError(
                    "❌ Firebase dependencies missing: " + task.Result
                );
            }
        });
    }
}
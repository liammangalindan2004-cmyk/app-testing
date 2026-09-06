using UnityEngine;
using UnityEngine.Android;

/// <summary>
/// Handles runtime permission requests for Android 6.0+ (API level 23+).
/// 
/// On Android 6+, declaring permissions in AndroidManifest.xml is NOT enough.
/// You must explicitly request permissions at runtime and wait for user approval.
/// 
/// Without this, Microphone.devices will return an empty array even though the
/// app is installed, causing "microphone not initialized" errors in Vosk.
/// </summary>
public class PermissionManager : MonoBehaviour
{
    private static bool _microphonePermissionRequested = false;

    /// <summary>
    /// Request microphone permission. Safe to call multiple times.
    /// </summary>
    public static void RequestMicrophonePermission()
    {
#if UNITY_ANDROID
        if (_microphonePermissionRequested)
            return;

        _microphonePermissionRequested = true;

        if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Debug.Log("[PermissionManager] ✓ Microphone permission already granted.");
        }
        else
        {
            Debug.Log("[PermissionManager] ⚠ Microphone permission not granted. Requesting from user...");
            Permission.RequestUserPermission(Permission.Microphone);
        }
#else
        Debug.Log("[PermissionManager] Not on Android, skipping permission request.");
#endif
    }

    /// <summary>
    /// Check if microphone permission is currently granted.
    /// </summary>
    public static bool HasMicrophonePermission()
    {
#if UNITY_ANDROID
        return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
        return true;
#endif
    }

    /// <summary>
    /// Wait for permission to be granted. Use in a coroutine.
    /// Example:
    ///   StartCoroutine(PermissionManager.WaitForMicrophonePermission());
    /// </summary>
    public static System.Collections.IEnumerator WaitForMicrophonePermission()
    {
#if UNITY_ANDROID
        RequestMicrophonePermission();
        
        // Wait up to 30 seconds for user to grant/deny permission
        float elapsed = 0f;
        while (!HasMicrophonePermission() && elapsed < 30f)
        {
            yield return new WaitForSeconds(0.5f);
            elapsed += 0.5f;
        }

        if (HasMicrophonePermission())
        {
            Debug.Log("[PermissionManager] ✓ Microphone permission granted!");
        }
        else
        {
            Debug.LogError("[PermissionManager] ✗ Microphone permission denied or timed out.");
        }
#else
        yield return null;
#endif
    }
}

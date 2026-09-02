using UnityEngine;
using Firebase;
using Firebase.Auth;
using Unity.VisualScripting;

public class loginValidation : MonoBehaviour
{
    private void Awake()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task =>
        {
            var dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available)
            {

            }
            else
            {
                Debug.LogError("err:" + dependencyStatus);
            }
        });
    }
}

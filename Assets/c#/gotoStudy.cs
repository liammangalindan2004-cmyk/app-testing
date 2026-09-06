using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NewMonoBehaviourScript : MonoBehaviour
{
    public void gotoStudy(){
        SceneManager.LoadSceneAsync(1);
    }
    public void gotoWrite(){
        SceneManager.LoadSceneAsync(3);
    }
    public void gotoLanding()
    {
        SceneManager.LoadSceneAsync("landing");
    }
    public void gotoMultiChoices()
    {
        SceneManager.LoadSceneAsync(5);
    }
    public void gotoPronounce()
    {
        SceneManager.LoadSceneAsync(6);
    }
}

using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NewMonoBehaviourScript : MonoBehaviour
{
    public void gotoStudy(){
        SceneManager.LoadSceneAsync("definition");
    }
    public void gotoWrite(){
        SceneManager.LoadSceneAsync("KanjiWrite");
    }
    public void gotoLanding()
    {
        SceneManager.LoadSceneAsync("landing");
    }
    public void gotoMultiChoices()
    {
        SceneManager.LoadSceneAsync(4);
    }
    public void gotoPronounce()
    {
        SceneManager.LoadSceneAsync(5);
    }
    public void gotoGame()
    {
        SceneManager.LoadSceneAsync("game");
    }
}

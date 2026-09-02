using UnityEngine;

public class testing : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start() { Debug.Log("MIC COUNT: " + Microphone.devices.Length); foreach (var d in Microphone.devices) Debug.Log("Found mic: " + d); }

    // Update is called once per frame
    void Update()
    {
        
    }
}

// using UnityEngine;
// using TMPro;
// using Firebase;
// using Firebase.Database;
// using Firebase.Extensions;
// using System.Collections.Generic;

// public class KanjiRealtimeDB : MonoBehaviour
// {
//     [Header("TextMeshPro UI element")]
//     public TextMeshProUGUI letterTMP;

//     [Header("Firebase Realtime Database URL")]
//     public string databaseURL = "https://unmei-nihongo-center-default-rtdb.asia-southeast1.firebasedatabase.app/";
//     {
//         FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
//         {
//             if (task.Result == DependencyStatus.Available)
//             {
//                 FirebaseApp app = FirebaseApp.DefaultInstance;

//                 app.Options.DatabaseUrl = new System.Uri(databaseURL);

//                 Debug.Log("Firebase Realtime Database ready.");

//             }
//             else
//             {
//                 Debug.LogError("Firebase dependencies not available: " + task.Result);
//             }
//         });
//     }


//     public void FetchKanji(string kanjiKey)
//     {
//         DatabaseReference dbRef = FirebaseDatabase.DefaultInstance
//             .GetReference("kanji")
//             .Child(kanjiKey)
//             .Child("char"); 

//         dbRef.GetValueAsync().ContinueWithOnMainThread(task =>
//         {
//             if (task.IsCompleted)
//             {
//                 DataSnapshot snapshot = task.Result;

//                 if (snapshot.Exists)
//                 {
//                     string kanjiChar = snapshot.Value.ToString();
//                     letterTMP.text = kanjiChar; 
//                     Debug.Log("Kanji fetched: " + kanjiChar);
//                 }
//                 else
//                 {
//                     Debug.LogWarning("Kanji not found: " + kanjiKey);
//                 }
//             }
//             else
//             {
//                 Debug.LogError("Failed to fetch kanji: " + task.Exception);
//             }
//         });
//     }

    
//     public void PostMessage(string message)
//     {
//         DatabaseReference dbRef = FirebaseDatabase.DefaultInstance.RootReference;
//         string key = dbRef.Child("messages").Push().Key;

//         dbRef.Child("messages").Child(key).SetValueAsync(message)
//             .ContinueWithOnMainThread(task =>
//             {
//                 if (task.IsCompleted)
//                     Debug.Log("Message posted: " + message);
//                 else
//                     Debug.LogError("Failed to post message: " + task.Exception);
//             });
//     }

//     public void PostKanji(string kanjiChar, int strokes, int grade)
//     {
//         DatabaseReference dbRef = FirebaseDatabase.DefaultInstance.RootReference;

//         Dictionary<string, object> kanjiData = new Dictionary<string, object>();
//         kanjiData["char"] = kanjiChar;
//         kanjiData["strokes"] = strokes;
//         kanjiData["grade"] = grade;

//         string key = dbRef.Child("kanji").Push().Key;
//         dbRef.Child("kanji").Child(key).SetValueAsync(kanjiData)
//             .ContinueWithOnMainThread(task =>
//             {
//                 if (task.IsCompleted)
//                     Debug.Log("Kanji posted: " + kanjiChar);
//                 else
//                     Debug.LogError("Failed to post kanji: " + task.Exception);
//             });
//     }
// }

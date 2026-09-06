// using System;
// using System.Collections.Generic;
// using Firebase;
// using Firebase.Database;
// using Firebase.Extensions;
// using TMPro;
// using UnityEngine;
// using UnityEngine.UI;
// public class ClassKanjiResultItem : MonoBehaviour
// {
//     [SerializeField] TextMeshProUGUI kanjiLabel;
//     [SerializeField] TextMeshProUGUI infoLabel;
//     [SerializeField] TextMeshProUGUI statusBadge;   // optional "already in class" tag
//     [SerializeField] Button          addButton;
//     [SerializeField] Button          removeButton;  // optional

//     FirestoreKanjiData     _kanji;
//     string                 _classId;
//     bool                   _inClass;
//     Action<string>         _callback;

//     // ── Public init ───────────────────────────────────────────────────────────

//     public void Init(FirestoreKanjiData data, bool inClass, string classId, Action<string> callback)
//     {
//         _kanji    = data;
//         _classId  = classId;
//         _inClass  = inClass;
//         _callback = callback;

//         kanjiLabel.text = _kanji.Char;
//         infoLabel.text  = $"{_kanji.JlptDisplay}  •  {_kanji.MeaningDisplay}";

//         addButton.onClick.AddListener(OnAdd);
//         if (removeButton) removeButton.onClick.AddListener(OnRemove);

//         Refresh();
//     }

//     // ── Button handlers ───────────────────────────────────────────────────────

//     async void OnAdd()
//     {
//         SetInteractable(false);
//         try
//         {
//             string assignedKey = await ClassRosterService.AddKanjiToClass(_classId, _kanji);
//             _inClass = true;
//             _callback($"'{_kanji.Char}' added as {assignedKey}.");
//         }
//         catch (Exception e)
//         {
//             _callback($"Error: {e.Message}");
//         }
//         Refresh();
//     }

//     async void OnRemove()
//     {
//         SetInteractable(false);
//         try
//         {
//             await ClassRosterService.RemoveKanjiFromClass(_classId, _kanji.Char);
//             _inClass = false;
//             _callback($"'{_kanji.Char}' removed from class.");
//         }
//         catch (Exception e)
//         {
//             _callback($"Error: {e.Message}");
//         }
//         Refresh();
//     }

//     // ─── UI helpers ───────────────────────────────────────────────────────────

//     void Refresh()
//     {
//         addButton.interactable = !_inClass;
//         if (removeButton) removeButton.interactable = _inClass;

//         if (statusBadge)
//         {
//             statusBadge.text  = _inClass ? "✓ In class" : "";
//             statusBadge.color = new Color(0.2f, 0.85f, 0.35f);
//         }
//     }

//     void SetInteractable(bool v)
//     {
//         addButton.interactable = v;
//         if (removeButton) removeButton.interactable = v;
//     }
// }
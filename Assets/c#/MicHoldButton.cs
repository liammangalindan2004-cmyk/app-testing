using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// Turns a UI Button into a press-and-hold control: fires OnPressed when the
/// pointer goes down on it, and OnReleased when the pointer comes back up —
/// OR when it slides off the button while still held down, so a dragged-away
/// finger can't leave recording stuck "on" with no way to release it.
///
/// SETUP:
///   Attach this to the SAME GameObject as your mic Button.
///   In the Inspector, wire OnPressed -> PronunciationSceneController.OnMicButtonDown
///   and OnReleased -> PronunciationSceneController.OnMicButtonUp (or do it
///   from code, as PronunciationSceneController.Awake() below now does).
/// </summary>
public class MicHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public UnityEvent OnPressed;
    public UnityEvent OnReleased;

    private bool _isHeld;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_isHeld) return;
        _isHeld = true;
        OnPressed?.Invoke();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        Release();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // Finger/cursor dragged off the button while still held — end the
        // hold here rather than waiting for a pointer-up that may never
        // come on this object.
        if (_isHeld) Release();
    }

    private void Release()
    {
        if (!_isHeld) return;
        _isHeld = false;
        OnReleased?.Invoke();
    }
}

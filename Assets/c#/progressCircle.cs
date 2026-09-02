using UnityEngine;
using UnityEngine.UI;

public class ProgressCircle : MonoBehaviour
{
    [Header("Day identity")]
    [Tooltip("Which calendar day this circle represents, format yyyy-MM-dd. " +
             "Leave blank to have ActivityCalendarGridController assign it automatically " +
             "based on this object's position in its dayCircles list.")]
    public string dateKey;

    [Header("Circle Settings")]
    public Image circleImage;

    [Tooltip("Scale shown at currentProgress = 1.")]
    public float maxScale = 1f;

    [Tooltip("Scale shown at currentProgress = 0. NOTE: this was -10 before, " +
             "which flips the circle and blows it up to 10x size at zero progress. " +
             "Fixed to a small positive value so it shrinks instead - adjust to taste.")]
    public float minScale = 0.3f;

    [Header("Progress Settings")]
    [Range(0f, 1f)]
    public float currentProgress = 0f;

    void Start()
    {
        if (circleImage != null)
        {
            circleImage.fillAmount = 1f;
            circleImage.type = Image.Type.Simple;
        }

        UpdateCircle();
    }

    public void SetProgress(float progress)
    {
        currentProgress = Mathf.Clamp01(progress);
        UpdateCircle();
    }

    void UpdateCircle()
    {
        float scaleMultiplier = Mathf.Lerp(minScale, maxScale, currentProgress);
        transform.localScale = new Vector3(scaleMultiplier, scaleMultiplier, 1f);
    }

    void OnValidate()
    {
        UpdateCircle();
    }
}
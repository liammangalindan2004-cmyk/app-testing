using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace JapaneseLearning.Dashboard
{
    // A row like "Reading — 25% to 82% in 8 weeks — +57%"
    public class SkillDeltaRowUI : MonoBehaviour
    {
        [SerializeField] private Image iconBackground;
        [SerializeField] private TextMeshProUGUI skillNameText;
        [SerializeField] private TextMeshProUGUI rangeText;
        [SerializeField] private TextMeshProUGUI deltaText;

        public void SetData(SkillSummary summary, int weeksTracked)
        {
            iconBackground.color = summary.color;
            skillNameText.text = summary.skillName;
            rangeText.text = $"{summary.startPercent:0}% to {summary.endPercent:0}% in {weeksTracked} weeks";
            deltaText.text = $"+{summary.DeltaPercent:0}%";
        }
    }

    // A row in "Current Levels": name, percent, and a fill-bar Image
    // (set the Image's Image Type to Filled / Horizontal in the Inspector).
    public class SkillLevelBarUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI skillNameText;
        [SerializeField] private TextMeshProUGUI percentText;
        [SerializeField] private Image fillImage;

        public void SetData(string skillName, float percent, Color color)
        {
            skillNameText.text = skillName;
            percentText.text = $"{percent:0}%";
            fillImage.color = color;
            fillImage.fillAmount = percent / 100f;
        }
    }
}
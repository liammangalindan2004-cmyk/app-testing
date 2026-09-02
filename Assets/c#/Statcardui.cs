using UnityEngine;
using TMPro;

namespace JapaneseLearning.Dashboard
{
    // One of the three top cards: "+32% Overall Improvement", "8 wks Time Period", etc.
    public class StatCardUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI valueText;
        [SerializeField] private TextMeshProUGUI labelText;

        public void SetValue(string value, string label, Color? valueColor = null)
        {
            valueText.text = value;
            labelText.text = label;
            if (valueColor.HasValue)
                valueText.color = valueColor.Value;
        }
    }
}
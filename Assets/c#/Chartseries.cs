using System.Collections.Generic;
using UnityEngine;

namespace JapaneseLearning.Dashboard
{
    [System.Serializable]
    public class ChartSeries
    {
        public string label;
        public Color color;
        public List<float> values = new List<float>();
    }
}
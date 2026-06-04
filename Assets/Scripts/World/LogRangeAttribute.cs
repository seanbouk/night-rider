// Marks a float field to be edited with a logarithmic slider (see LogRangeDrawer).
// Useful when the useful range spans an order of magnitude (e.g. 0.001..0.01).

using UnityEngine;

namespace NightRider.World
{
    public class LogRangeAttribute : PropertyAttribute
    {
        public readonly float min, max;
        public LogRangeAttribute(float min, float max) { this.min = min; this.max = max; }
    }
}

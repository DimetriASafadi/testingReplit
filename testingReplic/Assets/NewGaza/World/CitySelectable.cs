using UnityEngine;

namespace NewGaza
{
    /// <summary>Hit-test metadata only. All actions and lock checks belong to the session.</summary>
    public sealed class CitySelectable : MonoBehaviour
    {
        public int districtIndex;
        public int plotIndex = -1;
    }
}
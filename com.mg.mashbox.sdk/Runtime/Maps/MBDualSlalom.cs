using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class MBDualSlalom : MonoBehaviour
    {
        [SerializeField] private string slalomName = "New Dual Slalom";

        public string SlalomName
        {
            get => slalomName;
            set
            {
                slalomName = string.IsNullOrWhiteSpace(value) ? "Dual Slalom" : value.Trim();
                SyncGameObjectName();
            }
        }

        public int CourseId => CalculateStableCourseId(slalomName);

        private void Reset()
        {
            slalomName = string.IsNullOrWhiteSpace(gameObject.name) ? "Dual Slalom" : gameObject.name;
            SyncGameObjectName();
        }

        private void OnValidate()
        {
            slalomName = string.IsNullOrWhiteSpace(slalomName) ? "Dual Slalom" : slalomName.Trim();
            SyncGameObjectName();
        }

        public List<MBDualSlalomLane> GetOrderedLanes()
        {
            return GetComponentsInChildren<MBDualSlalomLane>(true)
                .Where(lane => lane != null && lane.transform.parent == transform)
                .OrderBy(lane => lane.LaneNumber)
                .ThenBy(lane => lane.transform.GetSiblingIndex())
                .ToList();
        }

        public MBDualSlalomLane GetLane(int laneNumber)
        {
            return GetOrderedLanes().FirstOrDefault(lane => lane.LaneNumber == laneNumber);
        }

        public bool AreBothStartZonesOccupied
        {
            get
            {
                var lanes = GetOrderedLanes();
                return lanes.Count == 2 && lanes.All(lane => lane.StartZone != null && lane.StartZone.IsOccupied);
            }
        }

        public static int CalculateStableCourseId(string value)
        {
            unchecked
            {
                const uint offset = 2166136261;
                const uint prime = 16777619;
                uint hash = offset;
                string normalized = string.IsNullOrWhiteSpace(value) ? "Dual Slalom" : value.Trim();
                for (int i = 0; i < normalized.Length; i++)
                {
                    hash ^= char.ToUpperInvariant(normalized[i]);
                    hash *= prime;
                }

                return (int)(hash & 0x7fffffff);
            }
        }

        private void SyncGameObjectName()
        {
            if (gameObject.name != slalomName)
                gameObject.name = slalomName;
        }
    }
}

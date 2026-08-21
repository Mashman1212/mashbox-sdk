using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MBRace))]
    public class MBDualSlalomLane : MonoBehaviour
    {
        [SerializeField, Range(1, 2)] private int laneNumber = 1;

        public int LaneNumber
        {
            get => laneNumber;
            set => laneNumber = Mathf.Clamp(value, 1, 2);
        }

        public MBDualSlalom DualSlalom => GetComponentInParent<MBDualSlalom>();
        public MBRace Race => GetComponent<MBRace>();

        public MBRaceGate StartGate
        {
            get
            {
                List<MBRaceGate> gates = Race != null ? Race.GetOrderedGates() : null;
                return gates != null && gates.Count > 0 ? gates[0] : null;
            }
        }

        public MBRaceGate FinishGate
        {
            get
            {
                List<MBRaceGate> gates = Race != null ? Race.GetOrderedGates() : null;
                return gates != null && gates.Count > 1 ? gates[gates.Count - 1] : null;
            }
        }

        public MBDualSlalomStartZone StartZone =>
            StartGate != null ? StartGate.GetComponentInChildren<MBDualSlalomStartZone>(true) : null;

        private void Reset()
        {
            laneNumber = Mathf.Clamp(transform.GetSiblingIndex() + 1, 1, 2);
        }

        private void OnValidate()
        {
            laneNumber = Mathf.Clamp(laneNumber, 1, 2);
        }
    }
}

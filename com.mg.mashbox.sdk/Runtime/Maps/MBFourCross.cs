using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MBRace))]
    public class MBFourCross : MonoBehaviour
    {
        [SerializeField] private string fourCrossName = "New 4 Cross";
        [SerializeField, HideInInspector] private int minimumRiders = 1;
        [SerializeField, Range(1, 4)] private int maximumRiders = 4;

        public string FourCrossName
        {
            get => fourCrossName;
            set
            {
                fourCrossName = string.IsNullOrWhiteSpace(value) ? "4 Cross" : value.Trim();
                SyncNames();
            }
        }

        public int CourseId => MBDualSlalom.CalculateStableCourseId("4 Cross:" + fourCrossName);
        public int MinimumRiders => 1;
        public int MaximumRiders => maximumRiders;
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
            fourCrossName = string.IsNullOrWhiteSpace(gameObject.name) ? "4 Cross" : gameObject.name;
            minimumRiders = 1;
            maximumRiders = 4;
            SyncNames();
        }

        private void OnValidate()
        {
            fourCrossName = string.IsNullOrWhiteSpace(fourCrossName) ? "4 Cross" : fourCrossName.Trim();
            maximumRiders = Mathf.Clamp(maximumRiders, 1, 4);
            minimumRiders = 1;
            SyncNames();
        }

        private void SyncNames()
        {
            if (gameObject.name != fourCrossName)
                gameObject.name = fourCrossName;

            MBRace race = Race;
            if (race != null && race.RaceName != fourCrossName)
                race.RaceName = fourCrossName;
        }
    }
}

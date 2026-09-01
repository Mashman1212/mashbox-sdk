using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class MBRace : MonoBehaviour
    {
        public event System.Action OnPassed;
        public event System.Action OnFailed;
        public event System.Action OnStaged;
        public event System.Action OnInitiated;
        public event System.Action Started;
        public event System.Action Completed;
        public event System.Action Failed;
        public event System.Action Staged;
        public event System.Action Initiated;
        public event System.Action ResetOccurred;

        [SerializeField] private string raceName = "New Race";
        private readonly Color pathColor = new Color(1f, 0.45f, 0.15f, 0.9f);
        [SerializeField] private bool started;
        [SerializeField] private bool completed;
        [Header("Runtime Flow")]
        [SerializeField] private int checkpointsAheadToShow = 3;
        [SerializeField] private bool showAllCheckpoints = true;

        [Header("Events")]
        [SerializeField] private UnityEvent onStarted;
        [SerializeField] private UnityEvent onCompleted;
        [SerializeField] private UnityEvent onFailed;
        [SerializeField] private UnityEvent onStaged;
        [SerializeField] private UnityEvent onInitiated;
        [SerializeField] private UnityEvent onReset;

        private MBRaceGroup group;
        private readonly List<MBRaceGate> subscribedGates = new List<MBRaceGate>();
        private bool isRaceTimerStarted;
        private int activeGateIndex;

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void EnsureEditorRaceGatesActiveOnLoad()
        {
            if (Application.isPlaying)
                return;

            EditorApplication.delayCall += EnsureEditorRaceGatesActive;
        }

        private static void EnsureEditorRaceGatesActive()
        {
            if (Application.isPlaying)
                return;

            var races = Resources.FindObjectsOfTypeAll<MBRace>();
            for (var raceIndex = 0; raceIndex < races.Length; raceIndex++)
            {
                var race = races[raceIndex];
                if (race == null || EditorUtility.IsPersistent(race))
                    continue;

                race.ResyncRaceFlow();
            }
        }
#endif

        public string RaceName
        {
            get => raceName;
            set
            {
                raceName = string.IsNullOrWhiteSpace(value) ? "Race" : value.Trim();
                SyncGameObjectName();
            }
        }

        public bool HasStarted => started;
        public bool IsCompleted => completed;
        public Color GizmoColor => GetRaceColor();

        private void Awake()
        {
            CacheGroupReference();
            ResyncRaceFlow();
        }

        private void OnEnable()
        {
            ResyncRaceFlow();
        }

        private void OnDisable()
        {
            UnsubscribeFromGates();
        }

        private void OnValidate()
        {
            CacheGroupReference();
            raceName = string.IsNullOrWhiteSpace(raceName) ? "Race" : raceName.Trim();
            SyncGameObjectName();
            checkpointsAheadToShow = Mathf.Max(0, checkpointsAheadToShow);
            ResyncRaceFlow();
            group?.RegisterRace(this);
        }

        private void Reset()
        {
            raceName = string.IsNullOrWhiteSpace(raceName) ? gameObject.name : raceName;
            SyncGameObjectName();
            Stage();
        }

        private void OnTransformChildrenChanged()
        {
            ResyncRaceFlow();
        }

        private void OnDrawGizmos()
        {
            if (!MBGameplayGizmoVisibility.Visible)
                return;

            var gates = GetOrderedGates();
            if (gates.Count < 2)
                return;

            var previousColor = Gizmos.color;
            Gizmos.color = GetRaceColor();

            for (var index = 0; index < gates.Count - 1; index++)
                Gizmos.DrawLine(gates[index].transform.position, gates[index + 1].transform.position);

            Gizmos.color = previousColor;
        }

        public void StartRace()
        {
            if (started)
                return;

            started = true;
            group?.NotifyStarted(this);
            Started?.Invoke();
            onStarted?.Invoke();
        }

        /// <summary>
        /// Starts the timed portion of this race without consuming its first gate.
        /// Multiplayer activities use this to synchronize a teammate to a race
        /// that another rider has just initiated.
        /// </summary>
        public bool InitiateRace()
        {
            if (!MBGameplayStateGuard.IsGameplayActive || completed || isRaceTimerStarted)
                return false;

            isRaceTimerStarted = true;
            StartRace();
            // A synchronized team-race start activates this rider's course without
            // consuming their start gate. Refresh visibility from the initiated
            // state; gate progression still advances only through HandleGatePassed.
            UpdateGateVisibility(GetOrderedGates());
            Initiated?.Invoke();
            OnInitiated?.Invoke();
            onInitiated?.Invoke();
            return true;
        }

        public void Stage()
        {
            activeGateIndex = 0;
            isRaceTimerStarted = false;
            SetState(false, false);

            var gates = GetOrderedGates();
            for (var index = 0; index < gates.Count; index++)
                gates[index].ResetGate();

            UpdateGateVisibility(gates);

            if (gates.Count > 0)
                gates[0].Arm();

            Staged?.Invoke();
            OnStaged?.Invoke();
            onStaged?.Invoke();
        }

        public void Arm()
        {
            var gates = GetOrderedGates();
            if (gates.Count == 0)
                return;

            gates[0].Arm();
            UpdateGateVisibility(gates);
        }

        public void Fail()
        {
            if (!MBGameplayStateGuard.IsGameplayActive)
                return;

            if (!isRaceTimerStarted || completed)
                return;

            Failed?.Invoke();
            OnFailed?.Invoke();
            onFailed?.Invoke();
            Stage();
        }

        public void CompleteRace()
        {
            if (!MBGameplayStateGuard.IsGameplayActive)
                return;

            if (!started)
                started = true;

            if (completed)
                return;

            completed = true;
            group?.NotifyCompleted(this);
            Completed?.Invoke();
            onCompleted?.Invoke();
        }

        public void ResetRace()
        {
            Stage();
        }

        internal void SetState(bool hasStarted, bool isCompleted, bool notifyGroup = true)
        {
            if (started == hasStarted && completed == isCompleted)
                return;

            started = hasStarted;
            completed = isCompleted;

            if (notifyGroup)
                group?.RegisterRace(this);

            if (!started && !completed)
            {
                ResetOccurred?.Invoke();
                onReset?.Invoke();
            }
        }

        internal void AssignGroup(MBRaceGroup owningGroup)
        {
            group = owningGroup;
        }

        private void CacheGroupReference()
        {
            group = GetComponentInParent<MBRaceGroup>();
        }

        private void SyncGameObjectName()
        {
            if (gameObject.name != raceName)
                gameObject.name = raceName;
        }

        private void ResyncRaceFlow()
        {
            CacheGroupReference();
            SubscribeToGates();

            var gates = GetOrderedGates();
            if (gates.Count == 0)
                return;

            activeGateIndex = Mathf.Clamp(activeGateIndex, 0, Mathf.Max(0, gates.Count - 1));
            UpdateGateVisibility(gates);
        }

        private void SubscribeToGates()
        {
            UnsubscribeFromGates();

            var gates = GetOrderedGates();
            for (var index = 0; index < gates.Count; index++)
            {
                var gate = gates[index];
                if (gate == null)
                    continue;

                gate.Passed -= HandleGatePassed;
                gate.Passed += HandleGatePassed;
                subscribedGates.Add(gate);
            }
        }

        private void UnsubscribeFromGates()
        {
            for (var index = 0; index < subscribedGates.Count; index++)
            {
                var gate = subscribedGates[index];
                if (gate == null)
                    continue;

                gate.Passed -= HandleGatePassed;
            }

            subscribedGates.Clear();
        }

        private void HandleGatePassed()
        {
            var gates = GetOrderedGates();
            if (gates.Count == 0)
                return;

            if (!isRaceTimerStarted)
                InitiateRace();

            activeGateIndex = Mathf.Min(activeGateIndex + 1, gates.Count);

            if (activeGateIndex < gates.Count)
                gates[activeGateIndex].Arm();

            UpdateGateVisibility(gates);

            var allPassed = true;
            for (var index = 0; index < gates.Count; index++)
            {
                if (!gates[index].HasPassed)
                {
                    allPassed = false;
                    break;
                }
            }

            if (allPassed)
            {
                OnPassed?.Invoke();
                CompleteRace();
            }
        }

        private void UpdateGateVisibility(List<MBRaceGate> gates)
        {
            if (!Application.isPlaying)
            {
                for (var index = 0; index < gates.Count; index++)
                {
                    if (gates[index] != null && !gates[index].gameObject.activeSelf)
                        gates[index].gameObject.SetActive(true);
                }

                return;
            }

            for (var index = 0; index < gates.Count; index++)
            {
                var shouldShow = ShouldShowGate(index);
                if (gates[index] != null && gates[index].gameObject.activeSelf != shouldShow)
                    gates[index].gameObject.SetActive(shouldShow);
            }
        }

        private bool ShouldShowGate(int gateIndex)
        {
            if (!isRaceTimerStarted)
                return gateIndex == 0;

            if (showAllCheckpoints)
                return true;

            return gateIndex >= activeGateIndex && gateIndex <= activeGateIndex + checkpointsAheadToShow;
        }

        private Color GetRaceColor()
        {
            if (transform.parent == null)
                return pathColor;

            var raceSiblings = transform.parent
                .Cast<Transform>()
                .Where(child => child.GetComponent<MBRace>() != null)
                .OrderBy(child => child.GetSiblingIndex())
                .ToList();

            var raceIndex = raceSiblings.IndexOf(transform);
            if (raceIndex < 0)
                return pathColor;

            var hue = Mathf.Repeat(0.08f + raceIndex * 0.173f, 1f);
            var baseColor = Color.HSVToRGB(hue, 0.72f, 1f);
            baseColor.a = pathColor.a;
            return baseColor;
        }

        public List<MBRaceGate> GetOrderedGates()
        {
            return transform
                .GetComponentsInChildren<MBRaceGate>(true)
                .Where(gate => gate.transform.parent == transform)
                .OrderBy(gate => gate.transform.GetSiblingIndex())
                .ToList();
        }

        public int GetGateIndex(MBRaceGate gate)
        {
            if (gate == null)
                return 0;

            var gates = GetOrderedGates();
            var index = gates.IndexOf(gate);
            return index >= 0 ? index + 1 : 0;
        }

        public MBRaceGate GetNextGate(MBRaceGate gate)
        {
            if (gate == null)
                return null;

            var gates = GetOrderedGates();
            var index = gates.IndexOf(gate);
            if (index < 0 || index >= gates.Count - 1)
                return null;

            return gates[index + 1];
        }

        public float GetDistanceToNextGate(MBRaceGate gate)
        {
            var nextGate = GetNextGate(gate);
            if (gate == null || nextGate == null)
                return 0f;

            return Vector3.Distance(gate.transform.position, nextGate.transform.position);
        }

        public float GetDistanceFromStart(MBRaceGate gate)
        {
            if (gate == null)
                return 0f;

            var gates = GetOrderedGates();
            var targetIndex = gates.IndexOf(gate);
            if (targetIndex <= 0)
                return 0f;

            var distance = 0f;
            for (var index = 0; index < targetIndex; index++)
                distance += Vector3.Distance(gates[index].transform.position, gates[index + 1].transform.position);

            return distance;
        }

        public float GetTotalGatePathDistance()
        {
            var gates = GetOrderedGates();
            if (gates.Count < 2)
                return 0f;

            var distance = 0f;
            for (var index = 0; index < gates.Count - 1; index++)
                distance += Vector3.Distance(gates[index].transform.position, gates[index + 1].transform.position);

            return distance;
        }
    }
}

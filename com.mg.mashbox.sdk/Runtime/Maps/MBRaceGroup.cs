using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class MBRaceGroup : MonoBehaviour
    {
        [Header("Events")]
        [SerializeField] private UnityEvent onRaceStarted;
        [SerializeField] private UnityEvent onRaceCompleted;
        [SerializeField] private UnityEvent onAnyProgressChanged;
        [SerializeField] private UnityEvent onAllCompleted;
        [SerializeField] private UnityEvent onReset;

        private readonly HashSet<MBRace> started = new HashSet<MBRace>();
        private readonly HashSet<MBRace> completed = new HashSet<MBRace>();
        private bool hasCompleted;

        public int StartedCount => started.Count;
        public int CompletedCount => completed.Count;
        public int TotalCount => GetRaces().Count;
        public bool IsComplete => hasCompleted;

        private void Awake()
        {
            ResyncChildren();
        }

        private void OnValidate()
        {
            ResyncChildren();
        }

        private void OnTransformChildrenChanged()
        {
            ResyncChildren();
        }

        public void ResetGroup()
        {
            started.Clear();
            completed.Clear();
            hasCompleted = false;

            foreach (var race in GetRaces())
                race.SetState(false, false, notifyGroup: false);

            onReset?.Invoke();
            onAnyProgressChanged?.Invoke();
        }

        public void ResyncChildren()
        {
            started.Clear();
            completed.Clear();
            hasCompleted = false;

            foreach (var race in GetRaces())
            {
                race.AssignGroup(this);
                RegisterRace(race);
            }
        }

        internal void RegisterRace(MBRace race)
        {
            if (race == null)
                return;

            if (race.HasStarted)
                started.Add(race);
            else
                started.Remove(race);

            if (race.IsCompleted)
                completed.Add(race);
            else
                completed.Remove(race);

            hasCompleted = HasCompletedAll();
        }

        internal void NotifyStarted(MBRace race)
        {
            if (race == null || !started.Add(race))
                return;

            onRaceStarted?.Invoke();
            onAnyProgressChanged?.Invoke();
        }

        internal void NotifyCompleted(MBRace race)
        {
            if (race == null || !completed.Add(race))
                return;

            started.Add(race);
            onRaceCompleted?.Invoke();
            onAnyProgressChanged?.Invoke();

            if (!hasCompleted && HasCompletedAll())
            {
                hasCompleted = true;
                onAllCompleted?.Invoke();
            }
        }

        private bool HasCompletedAll()
        {
            var races = GetRaces();
            return races.Count > 0 && races.All(race => completed.Contains(race));
        }

        private List<MBRace> GetRaces()
        {
            return GetComponentsInChildren<MBRace>(true).ToList();
        }
    }
}

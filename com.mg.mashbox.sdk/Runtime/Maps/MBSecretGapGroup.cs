using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [DisallowMultipleComponent]
    public class MBSecretGapGroup : MonoBehaviour
    {
        [Header("Events")]
        [SerializeField] private UnityEvent onGapEntered;
        [SerializeField] private UnityEvent onGapCompleted;
        [SerializeField] private UnityEvent onAnyProgressChanged;
        [SerializeField] private UnityEvent onAllCompleted;
        [SerializeField] private UnityEvent onReset;

        private readonly HashSet<MBSecretGap> entered = new HashSet<MBSecretGap>();
        private readonly HashSet<MBSecretGap> completed = new HashSet<MBSecretGap>();
        private bool hasCompleted;

        public int EnteredCount => entered.Count;
        public int CompletedCount => completed.Count;
        public int TotalCount => GetSecretGaps().Count;
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
            entered.Clear();
            completed.Clear();
            hasCompleted = false;

            foreach (var gap in GetSecretGaps())
                gap.SetState(false, false, notifyGroup: false);

            onReset?.Invoke();
            onAnyProgressChanged?.Invoke();
        }

        internal void RegisterGap(MBSecretGap gap)
        {
            if (gap == null)
                return;

            if (gap.HasEntered)
                entered.Add(gap);
            else
                entered.Remove(gap);

            if (gap.IsCompleted)
                completed.Add(gap);
            else
                completed.Remove(gap);

            hasCompleted = HasCompletedAll();
        }

        internal void NotifyEntered(MBSecretGap gap)
        {
            if (gap == null || !entered.Add(gap))
                return;

            onGapEntered?.Invoke();
            onAnyProgressChanged?.Invoke();
        }

        internal void NotifyCompleted(MBSecretGap gap)
        {
            if (gap == null || !completed.Add(gap))
                return;

            entered.Add(gap);
            onGapCompleted?.Invoke();
            onAnyProgressChanged?.Invoke();

            if (!hasCompleted && HasCompletedAll())
            {
                hasCompleted = true;
                onAllCompleted?.Invoke();
            }
        }

        private bool HasCompletedAll()
        {
            var gaps = GetSecretGaps();
            return gaps.Count > 0 && gaps.All(gap => completed.Contains(gap));
        }

        private List<MBSecretGap> GetSecretGaps()
        {
            return GetComponentsInChildren<MBSecretGap>(true).ToList();
        }

        public void ResyncChildren()
        {
            entered.Clear();
            completed.Clear();
            hasCompleted = false;

            foreach (var gap in GetSecretGaps())
            {
                gap.AssignGroup(this);
                RegisterGap(gap);
            }
        }
    }
}

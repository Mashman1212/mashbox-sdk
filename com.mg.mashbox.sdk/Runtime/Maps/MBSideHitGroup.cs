using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [DisallowMultipleComponent]
    public class MBSideHitGroup : MonoBehaviour
    {
        [Header("Events")]
        [SerializeField] private UnityEvent onSideHitEntered;
        [SerializeField] private UnityEvent onSideHitCompleted;
        [SerializeField] private UnityEvent onAnyProgressChanged;
        [SerializeField] private UnityEvent onAllCompleted;
        [SerializeField] private UnityEvent onReset;

        private readonly HashSet<MBSideHit> entered = new HashSet<MBSideHit>();
        private readonly HashSet<MBSideHit> completed = new HashSet<MBSideHit>();
        private bool hasCompleted;

        public int EnteredCount => entered.Count;
        public int CompletedCount => completed.Count;
        public int TotalCount => GetSideHits().Count;
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

            foreach (var sideHit in GetSideHits())
                sideHit.SetState(false, false, notifyGroup: false);

            onReset?.Invoke();
            onAnyProgressChanged?.Invoke();
        }

        public void ResyncChildren()
        {
            entered.Clear();
            completed.Clear();
            hasCompleted = false;

            foreach (var sideHit in GetSideHits())
            {
                sideHit.AssignGroup(this);
                RegisterSideHit(sideHit);
            }
        }

        internal void RegisterSideHit(MBSideHit sideHit)
        {
            if (sideHit == null)
                return;

            if (sideHit.HasEntered)
                entered.Add(sideHit);
            else
                entered.Remove(sideHit);

            if (sideHit.IsCompleted)
                completed.Add(sideHit);
            else
                completed.Remove(sideHit);

            hasCompleted = HasCompletedAll();
        }

        internal void NotifyEntered(MBSideHit sideHit)
        {
            if (sideHit == null || !entered.Add(sideHit))
                return;

            onSideHitEntered?.Invoke();
            onAnyProgressChanged?.Invoke();
        }

        internal void NotifyCompleted(MBSideHit sideHit)
        {
            if (sideHit == null || !completed.Add(sideHit))
                return;

            entered.Add(sideHit);
            onSideHitCompleted?.Invoke();
            onAnyProgressChanged?.Invoke();

            if (!hasCompleted && HasCompletedAll())
            {
                hasCompleted = true;
                onAllCompleted?.Invoke();
            }
        }

        private bool HasCompletedAll()
        {
            var sideHits = GetSideHits();
            return sideHits.Count > 0 && sideHits.All(sideHit => completed.Contains(sideHit));
        }

        private List<MBSideHit> GetSideHits()
        {
            return GetComponentsInChildren<MBSideHit>(true).ToList();
        }
    }
}

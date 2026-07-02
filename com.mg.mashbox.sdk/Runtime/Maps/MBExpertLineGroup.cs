using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [DisallowMultipleComponent]
    public class MBExpertLineGroup : MonoBehaviour
    {
        [Header("Events")]
        [SerializeField] private UnityEvent onLineEntered;
        [SerializeField] private UnityEvent onLineCompleted;
        [SerializeField] private UnityEvent onLineFailed;
        [SerializeField] private UnityEvent onAnyProgressChanged;
        [SerializeField] private UnityEvent onAllCompleted;
        [SerializeField] private UnityEvent onReset;

        private readonly HashSet<MBExpertLine> entered = new HashSet<MBExpertLine>();
        private readonly HashSet<MBExpertLine> completed = new HashSet<MBExpertLine>();
        private bool hasCompleted;

        public int EnteredCount => entered.Count;
        public int CompletedCount => completed.Count;
        public int TotalCount => GetExpertLines().Count;
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

            foreach (MBExpertLine line in GetExpertLines())
                line.SetState(false, false, false, notifyGroup: false);

            onReset?.Invoke();
            onAnyProgressChanged?.Invoke();
        }

        public void ResyncChildren()
        {
            entered.Clear();
            completed.Clear();
            hasCompleted = false;

            foreach (MBExpertLine line in GetExpertLines())
            {
                line.AssignGroup(this);
                RegisterLine(line);
            }
        }

        internal void RegisterLine(MBExpertLine line)
        {
            if (line == null)
                return;

            if (line.HasEntered)
                entered.Add(line);
            else
                entered.Remove(line);

            if (line.IsCompleted)
                completed.Add(line);
            else
                completed.Remove(line);

            hasCompleted = HasCompletedAll();
        }

        internal void NotifyEntered(MBExpertLine line)
        {
            if (line == null || !entered.Add(line))
                return;

            onLineEntered?.Invoke();
            onAnyProgressChanged?.Invoke();
        }

        internal void NotifyCompleted(MBExpertLine line)
        {
            if (line == null || !completed.Add(line))
                return;

            entered.Add(line);
            onLineCompleted?.Invoke();
            onAnyProgressChanged?.Invoke();

            if (!hasCompleted && HasCompletedAll())
            {
                hasCompleted = true;
                onAllCompleted?.Invoke();
            }
        }

        internal void NotifyFailed(MBExpertLine line)
        {
            if (line == null)
                return;

            onLineFailed?.Invoke();
            onAnyProgressChanged?.Invoke();
        }

        private bool HasCompletedAll()
        {
            var lines = GetExpertLines();
            return lines.Count > 0 && lines.All(line => completed.Contains(line));
        }

        private List<MBExpertLine> GetExpertLines()
        {
            return GetComponentsInChildren<MBExpertLine>(true).ToList();
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [DisallowMultipleComponent]
    public class MBPhotoSpotGroup : MonoBehaviour
    {
        [Header("Events")]
        [SerializeField] private UnityEvent onPhotoSpotActivated;
        [SerializeField] private UnityEvent onPhotoSpotCompleted;
        [SerializeField] private UnityEvent onAnyProgressChanged;
        [SerializeField] private UnityEvent onAllCompleted;
        [SerializeField] private UnityEvent onReset;

        private readonly HashSet<MBPhotoSpot> activated = new HashSet<MBPhotoSpot>();
        private readonly HashSet<MBPhotoSpot> completed = new HashSet<MBPhotoSpot>();
        private bool hasCompleted;

        public int ActivatedCount => activated.Count;
        public int CompletedCount => completed.Count;
        public int TotalCount => GetPhotoSpots().Count;
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
            activated.Clear();
            completed.Clear();
            hasCompleted = false;

            foreach (var photoSpot in GetPhotoSpots())
                photoSpot.SetState(false, false, notifyGroup: false);

            onReset?.Invoke();
            onAnyProgressChanged?.Invoke();
        }

        internal void RegisterPhotoSpot(MBPhotoSpot photoSpot)
        {
            if (photoSpot == null)
                return;

            if (photoSpot.IsActivated)
                activated.Add(photoSpot);
            else
                activated.Remove(photoSpot);

            if (photoSpot.IsCompleted)
                completed.Add(photoSpot);
            else
                completed.Remove(photoSpot);

            hasCompleted = HasCompletedAll();
        }

        internal void NotifyActivated(MBPhotoSpot photoSpot)
        {
            if (photoSpot == null || !activated.Add(photoSpot))
                return;

            onPhotoSpotActivated?.Invoke();
            onAnyProgressChanged?.Invoke();
        }

        internal void NotifyCompleted(MBPhotoSpot photoSpot)
        {
            if (photoSpot == null || !completed.Add(photoSpot))
                return;

            activated.Add(photoSpot);
            onPhotoSpotCompleted?.Invoke();
            onAnyProgressChanged?.Invoke();

            if (!hasCompleted && HasCompletedAll())
            {
                hasCompleted = true;
                onAllCompleted?.Invoke();
            }
        }

        private bool HasCompletedAll()
        {
            var photoSpots = GetPhotoSpots();
            return photoSpots.Count > 0 && photoSpots.All(photoSpot => completed.Contains(photoSpot));
        }

        private List<MBPhotoSpot> GetPhotoSpots()
        {
            return GetComponentsInChildren<MBPhotoSpot>(true).ToList();
        }

        public void ResyncChildren()
        {
            activated.Clear();
            completed.Clear();
            hasCompleted = false;

            foreach (var photoSpot in GetPhotoSpots())
            {
                photoSpot.AssignGroup(this);
                RegisterPhotoSpot(photoSpot);
            }
        }
    }
}

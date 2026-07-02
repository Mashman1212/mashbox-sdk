using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [DisallowMultipleComponent]
    public class MBCollectibleGroup : MonoBehaviour
    {
        [Header("Events")]
        [SerializeField] private UnityEvent onCollectibleCollected;
        [SerializeField] private UnityEvent onAnyProgressChanged;
        [SerializeField] private UnityEvent onAllCollected;
        [SerializeField] private UnityEvent onReset;

        private readonly HashSet<MBCollectible> collected = new HashSet<MBCollectible>();
        private bool hasCompleted;

        public int CollectedCount => collected.Count;
        public int TotalCount => GetCollectibles().Count;
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
            collected.Clear();
            hasCompleted = false;

            foreach (var collectible in GetCollectibles())
                collectible.SetCollectedState(false, notifyGroup: false);

            onReset?.Invoke();
            onAnyProgressChanged?.Invoke();
        }

        internal void RegisterCollectible(MBCollectible collectible)
        {
            if (collectible == null)
                return;

            if (collectible.IsCollected)
                collected.Add(collectible);
            else
                collected.Remove(collectible);

            hasCompleted = HasCollectedAll();
        }

        internal void NotifyCollected(MBCollectible collectible)
        {
            if (collectible == null || !collected.Add(collectible))
                return;

            onCollectibleCollected?.Invoke();
            onAnyProgressChanged?.Invoke();

            if (!hasCompleted && HasCollectedAll())
            {
                hasCompleted = true;
                onAllCollected?.Invoke();
            }
        }

        private bool HasCollectedAll()
        {
            var collectibles = GetCollectibles();
            return collectibles.Count > 0 && collectibles.All(collectible => collected.Contains(collectible));
        }

        private List<MBCollectible> GetCollectibles()
        {
            return GetComponentsInChildren<MBCollectible>(true).ToList();
        }

        public void ResyncChildren()
        {
            collected.Clear();
            hasCompleted = false;

            foreach (var collectible in GetCollectibles())
            {
                collectible.AssignGroup(this);
                RegisterCollectible(collectible);
            }
        }
    }
}

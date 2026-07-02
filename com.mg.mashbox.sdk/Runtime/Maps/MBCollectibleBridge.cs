using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class MBCollectibleBridge : MonoBehaviour
    {
        [Tooltip("The SDK collectible proxy this spawned object should drive. If left empty, the bridge looks up the hierarchy.")]
        [SerializeField] private MBCollectible collectible;

        [Header("Events")]
        [SerializeField] private UnityEvent onBound;
        [SerializeField] private UnityEvent onCollected;
        [SerializeField] private UnityEvent onReset;

        public MBCollectible Collectible => collectible;

        private void Awake()
        {
            Rebind();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnTransformParentChanged()
        {
            Rebind();
        }

        public void Rebind()
        {
            Unsubscribe();

            if (collectible == null)
                collectible = GetComponentInParent<MBCollectible>();

            Subscribe();

            if (collectible != null)
                onBound?.Invoke();
        }

        public void Bind(MBCollectible target)
        {
            if (collectible == target)
                return;

            Unsubscribe();
            collectible = target;
            Subscribe();

            if (collectible != null)
                onBound?.Invoke();
        }

        public void Collect()
        {
            collectible?.Collect();
        }

        public void ResetCollected()
        {
            collectible?.ResetCollected();
        }

        private void Subscribe()
        {
            if (collectible == null)
                return;

            collectible.Collected -= HandleCollected;
            collectible.ResetOccurred -= HandleReset;
            collectible.Collected += HandleCollected;
            collectible.ResetOccurred += HandleReset;
        }

        private void Unsubscribe()
        {
            if (collectible == null)
                return;

            collectible.Collected -= HandleCollected;
            collectible.ResetOccurred -= HandleReset;
        }

        private void HandleCollected()
        {
            onCollected?.Invoke();
        }

        private void HandleReset()
        {
            onReset?.Invoke();
        }
    }
}

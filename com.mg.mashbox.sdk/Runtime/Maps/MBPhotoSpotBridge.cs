using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class MBPhotoSpotBridge : MonoBehaviour
    {
        [Tooltip("The SDK photo spot proxy this spawned object should drive. If left empty, the bridge looks up the hierarchy.")]
        [SerializeField] private MBPhotoSpot photoSpot;
        [Header("Runtime Trigger Sync")]
        [Tooltip("Optional deep child transform for the real runtime trigger zone. Its world position will be synced from the SDK photo spot proxy.")]
        [SerializeField] private Transform runtimeTriggerTransform;
        [Tooltip("Optional sphere collider for the real runtime trigger zone. Its radius will be synced from the SDK photo spot proxy.")]
        [SerializeField] private SphereCollider runtimeTriggerSphere;
        [Tooltip("Automatically push the SDK photo spot trigger zone settings into the runtime trigger references when the bridge binds.")]
        [SerializeField] private bool syncRuntimeTriggerOnBind = true;

        [Header("Events")]
        [SerializeField] private UnityEvent onBound;
        [SerializeField] private UnityEvent onActivated;
        [SerializeField] private UnityEvent onCompleted;
        [SerializeField] private UnityEvent onReset;

        public MBPhotoSpot PhotoSpot => photoSpot;

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

            if (photoSpot == null)
                photoSpot = GetComponentInParent<MBPhotoSpot>();

            Subscribe();

            if (photoSpot != null)
            {
                if (syncRuntimeTriggerOnBind)
                    SyncRuntimeTriggerFromProxy();

                onBound?.Invoke();
            }
        }

        public void Bind(MBPhotoSpot target)
        {
            if (photoSpot == target)
                return;

            Unsubscribe();
            photoSpot = target;
            Subscribe();

            if (photoSpot != null)
            {
                if (syncRuntimeTriggerOnBind)
                    SyncRuntimeTriggerFromProxy();

                onBound?.Invoke();
            }
        }

        public void Activate()
        {
            photoSpot?.Activate();
        }

        public void Complete()
        {
            photoSpot?.Complete();
        }

        public void ResetPhotoSpot()
        {
            photoSpot?.ResetPhotoSpot();
        }

        public void SyncRuntimeTriggerFromProxy()
        {
            if (photoSpot == null)
                return;

            if (runtimeTriggerTransform != null)
                runtimeTriggerTransform.position = photoSpot.TriggerZoneWorldPosition;

            if (runtimeTriggerSphere != null)
                runtimeTriggerSphere.radius = photoSpot.TriggerZoneRadius;
        }

        private void Subscribe()
        {
            if (photoSpot == null)
                return;

            photoSpot.Activated -= HandleActivated;
            photoSpot.Completed -= HandleCompleted;
            photoSpot.ResetOccurred -= HandleReset;
            photoSpot.Activated += HandleActivated;
            photoSpot.Completed += HandleCompleted;
            photoSpot.ResetOccurred += HandleReset;
        }

        private void Unsubscribe()
        {
            if (photoSpot == null)
                return;

            photoSpot.Activated -= HandleActivated;
            photoSpot.Completed -= HandleCompleted;
            photoSpot.ResetOccurred -= HandleReset;
        }

        private void HandleActivated()
        {
            onActivated?.Invoke();
        }

        private void HandleCompleted()
        {
            onCompleted?.Invoke();
        }

        private void HandleReset()
        {
            onReset?.Invoke();
        }
    }
}

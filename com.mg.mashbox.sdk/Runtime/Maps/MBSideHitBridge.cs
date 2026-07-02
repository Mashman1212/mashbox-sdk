using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class MBSideHitBridge : MonoBehaviour
    {
        [Tooltip("The SDK side hit proxy this spawned object should drive. If left empty, the bridge looks up the hierarchy.")]
        [SerializeField] private MBSideHit sideHit;

        [Header("Events")]
        [SerializeField] private UnityEvent onBound;
        [SerializeField] private UnityEvent onEntered;
        [SerializeField] private UnityEvent onCompleted;
        [SerializeField] private UnityEvent onReset;

        public MBSideHit SideHit => sideHit;

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

            if (sideHit == null)
                sideHit = GetComponentInParent<MBSideHit>();

            Subscribe();

            if (sideHit != null)
                onBound?.Invoke();
        }

        public void Bind(MBSideHit target)
        {
            if (sideHit == target)
                return;

            Unsubscribe();
            sideHit = target;
            Subscribe();

            if (sideHit != null)
                onBound?.Invoke();
        }

        public void EnterSideHit()
        {
            sideHit?.EnterSideHit();
        }

        public void CompleteSideHit()
        {
            sideHit?.CompleteSideHit();
        }

        public void ResetSideHit()
        {
            sideHit?.ResetSideHit();
        }

        private void Subscribe()
        {
            if (sideHit == null)
                return;

            sideHit.Entered -= HandleEntered;
            sideHit.Completed -= HandleCompleted;
            sideHit.ResetOccurred -= HandleReset;
            sideHit.Entered += HandleEntered;
            sideHit.Completed += HandleCompleted;
            sideHit.ResetOccurred += HandleReset;
        }

        private void Unsubscribe()
        {
            if (sideHit == null)
                return;

            sideHit.Entered -= HandleEntered;
            sideHit.Completed -= HandleCompleted;
            sideHit.ResetOccurred -= HandleReset;
        }

        private void HandleEntered()
        {
            onEntered?.Invoke();
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

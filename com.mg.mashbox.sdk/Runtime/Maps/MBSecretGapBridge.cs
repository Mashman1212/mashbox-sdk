using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class MBSecretGapBridge : MonoBehaviour
    {
        [Tooltip("The SDK secret gap proxy this spawned object should drive. If left empty, the bridge looks up the hierarchy.")]
        [SerializeField] private MBSecretGap secretGap;

        [Header("Events")]
        [SerializeField] private UnityEvent onBound;
        [SerializeField] private UnityEvent onEntered;
        [SerializeField] private UnityEvent onCompleted;
        [SerializeField] private UnityEvent onReset;

        public MBSecretGap SecretGap => secretGap;

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

            if (secretGap == null)
                secretGap = GetComponentInParent<MBSecretGap>();

            Subscribe();

            if (secretGap != null)
                onBound?.Invoke();
        }

        public void Bind(MBSecretGap target)
        {
            if (secretGap == target)
                return;

            Unsubscribe();
            secretGap = target;
            Subscribe();

            if (secretGap != null)
                onBound?.Invoke();
        }

        public void EnterGap()
        {
            secretGap?.EnterGap();
        }

        public void CompleteGap()
        {
            secretGap?.CompleteGap();
        }

        public void ResetGap()
        {
            secretGap?.ResetGap();
        }

        private void Subscribe()
        {
            if (secretGap == null)
                return;

            secretGap.Entered -= HandleEntered;
            secretGap.Completed -= HandleCompleted;
            secretGap.ResetOccurred -= HandleReset;
            secretGap.Entered += HandleEntered;
            secretGap.Completed += HandleCompleted;
            secretGap.ResetOccurred += HandleReset;
        }

        private void Unsubscribe()
        {
            if (secretGap == null)
                return;

            secretGap.Entered -= HandleEntered;
            secretGap.Completed -= HandleCompleted;
            secretGap.ResetOccurred -= HandleReset;
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

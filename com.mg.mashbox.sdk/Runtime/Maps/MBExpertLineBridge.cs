using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class MBExpertLineBridge : MonoBehaviour
    {
        [Tooltip("The SDK expert line proxy this spawned object should drive. If left empty, the bridge looks up the hierarchy.")]
        [SerializeField] private MBExpertLine expertLine;

        [Header("Events")]
        [SerializeField] private UnityEvent onBound;
        [SerializeField] private UnityEvent onEntered;
        [SerializeField] private UnityEvent onCompleted;
        [SerializeField] private UnityEvent onFailed;
        [SerializeField] private UnityEvent onReset;

        public MBExpertLine ExpertLine => expertLine;

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

            if (expertLine == null)
                expertLine = GetComponentInParent<MBExpertLine>();

            Subscribe();

            if (expertLine != null)
                onBound?.Invoke();
        }

        public void Bind(MBExpertLine target)
        {
            if (expertLine == target)
                return;

            Unsubscribe();
            expertLine = target;
            Subscribe();

            if (expertLine != null)
                onBound?.Invoke();
        }

        public void EnterLine()
        {
            expertLine?.EnterLine();
        }

        public void CompleteLine()
        {
            expertLine?.CompleteLine();
        }

        public void FailLine()
        {
            expertLine?.FailLine();
        }

        public void ResetLine()
        {
            expertLine?.ResetLine();
        }

        private void Subscribe()
        {
            if (expertLine == null)
                return;

            expertLine.Entered -= HandleEntered;
            expertLine.Completed -= HandleCompleted;
            expertLine.Failed -= HandleFailed;
            expertLine.ResetOccurred -= HandleReset;
            expertLine.Entered += HandleEntered;
            expertLine.Completed += HandleCompleted;
            expertLine.Failed += HandleFailed;
            expertLine.ResetOccurred += HandleReset;
        }

        private void Unsubscribe()
        {
            if (expertLine == null)
                return;

            expertLine.Entered -= HandleEntered;
            expertLine.Completed -= HandleCompleted;
            expertLine.Failed -= HandleFailed;
            expertLine.ResetOccurred -= HandleReset;
        }

        private void HandleEntered()
        {
            onEntered?.Invoke();
        }

        private void HandleCompleted()
        {
            onCompleted?.Invoke();
        }

        private void HandleFailed()
        {
            onFailed?.Invoke();
        }

        private void HandleReset()
        {
            onReset?.Invoke();
        }
    }
}

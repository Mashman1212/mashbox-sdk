using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class MBRaceGateBridge : MonoBehaviour
    {
        [SerializeField] private MBRaceGate raceGate;
        [Header("Events")]
        [SerializeField] private UnityEvent onBound;
        [SerializeField] private UnityEvent onArmed;
        [SerializeField] private UnityEvent onPassed;
        [SerializeField] private UnityEvent onReset;

        public MBRaceGate RaceGate => raceGate;

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

            if (raceGate == null)
                raceGate = GetComponentInParent<MBRaceGate>();

            Subscribe();

            if (raceGate != null)
                onBound?.Invoke();
        }

        public void Bind(MBRaceGate target)
        {
            if (raceGate == target)
                return;

            Unsubscribe();
            raceGate = target;
            Subscribe();

            if (raceGate != null)
                onBound?.Invoke();
        }

        public void Arm()
        {
            raceGate?.Arm();
        }

        public void Pass()
        {
            raceGate?.Pass();
        }

        public void ResetGate()
        {
            raceGate?.ResetGate();
        }

        private void Subscribe()
        {
            if (raceGate == null)
                return;

            raceGate.Armed -= HandleArmed;
            raceGate.Passed -= HandlePassed;
            raceGate.ResetOccurred -= HandleReset;
            raceGate.Armed += HandleArmed;
            raceGate.Passed += HandlePassed;
            raceGate.ResetOccurred += HandleReset;
        }

        private void Unsubscribe()
        {
            if (raceGate == null)
                return;

            raceGate.Armed -= HandleArmed;
            raceGate.Passed -= HandlePassed;
            raceGate.ResetOccurred -= HandleReset;
        }

        private void HandleArmed()
        {
            onArmed?.Invoke();
        }

        private void HandlePassed()
        {
            onPassed?.Invoke();
        }

        private void HandleReset()
        {
            onReset?.Invoke();
        }
    }
}

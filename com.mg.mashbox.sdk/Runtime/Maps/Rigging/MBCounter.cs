using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps.Rigging
{
    [AddComponentMenu("MashBox/Maps/Rigging/Counter")]
    [DisallowMultipleComponent]
    public class MBCounter : MonoBehaviour
    {
        [Header("State")]
        [Tooltip("The counter value to start with when the scene loads.")]
        [SerializeField] private int startingValue;
        [Tooltip("The value that will fire the target reached event once the counter is equal to or higher than it.")]
        [SerializeField] private int targetValue = 1;

        [Header("Events")]
        [SerializeField] private MBIntEvent onValueChanged;
        [SerializeField] private UnityEvent onIncremented;
        [SerializeField] private UnityEvent onDecremented;
        [SerializeField] private UnityEvent onReachedTarget;
        [SerializeField] private UnityEvent onDroppedBelowTarget;
        [SerializeField] private UnityEvent onBecameZero;
        [SerializeField] private UnityEvent onBecamePositive;
        [SerializeField] private UnityEvent onReset;

        public int CurrentValue { get; private set; }
        public int TargetValue => targetValue;

        private void Awake()
        {
            CurrentValue = startingValue;
        }

        public void Increment()
        {
            SetValue(CurrentValue + 1);
            onIncremented?.Invoke();
        }

        public void Decrement()
        {
            SetValue(CurrentValue - 1);
            onDecremented?.Invoke();
        }

        public void Add(int amount)
        {
            if (amount == 0)
                return;

            SetValue(CurrentValue + amount);

            if (amount > 0)
                onIncremented?.Invoke();
            else
                onDecremented?.Invoke();
        }

        public void ResetCounter()
        {
            SetValue(startingValue);
            onReset?.Invoke();
        }

        public void SetTargetValue(int newTargetValue)
        {
            var previousTargetReached = CurrentValue >= targetValue;
            targetValue = newTargetValue;
            EvaluateTargetTransitions(previousTargetReached, CurrentValue >= targetValue);
        }

        public void SetValue(int value)
        {
            if (CurrentValue == value)
                return;

            var previousValue = CurrentValue;
            var previousTargetReached = previousValue >= targetValue;

            CurrentValue = value;
            onValueChanged?.Invoke(CurrentValue);

            if (previousValue <= 0 && CurrentValue > 0)
                onBecamePositive?.Invoke();

            if (previousValue != 0 && CurrentValue == 0)
                onBecameZero?.Invoke();

            EvaluateTargetTransitions(previousTargetReached, CurrentValue >= targetValue);
        }

        private void EvaluateTargetTransitions(bool previousTargetReached, bool currentTargetReached)
        {
            if (!previousTargetReached && currentTargetReached)
                onReachedTarget?.Invoke();

            if (previousTargetReached && !currentTargetReached)
                onDroppedBelowTarget?.Invoke();
        }
    }
}

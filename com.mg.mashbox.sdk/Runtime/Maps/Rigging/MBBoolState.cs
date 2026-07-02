using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps.Rigging
{
    [AddComponentMenu("MashBox/Maps/Rigging/Bool State")]
    [DisallowMultipleComponent]
    public class MBBoolState : MonoBehaviour
    {
        [Tooltip("The value this state starts with when the scene loads or the object is enabled.")]
        [SerializeField] private bool initialValue;

        [Header("Events")]
        [SerializeField] private MBBoolEvent onValueChanged;
        [SerializeField] private UnityEvent onTrue;
        [SerializeField] private UnityEvent onFalse;
        [SerializeField] private UnityEvent onToggled;

        public bool Value { get; private set; }

        private void Awake()
        {
            Value = initialValue;
        }

        public void SetTrue()
        {
            SetValue(true);
        }

        public void SetFalse()
        {
            SetValue(false);
        }

        public void Toggle()
        {
            SetValue(!Value);
            onToggled?.Invoke();
        }

        public void SetValue(bool newValue)
        {
            if (Value == newValue)
                return;

            Value = newValue;
            onValueChanged?.Invoke(Value);

            if (Value)
                onTrue?.Invoke();
            else
                onFalse?.Invoke();
        }

        public void ResetState()
        {
            SetValue(initialValue);
        }
    }
}

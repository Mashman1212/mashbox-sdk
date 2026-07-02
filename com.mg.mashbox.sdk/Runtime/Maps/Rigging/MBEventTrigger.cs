using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps.Rigging
{
    [AddComponentMenu("MashBox/Maps/Rigging/Event Trigger")]
    [DisallowMultipleComponent]
    public class MBEventTrigger : MonoBehaviour
    {
        [Header("Behavior")]
        [Tooltip("Only allow this trigger to fire once until it is reset.")]
        [SerializeField] private bool oneShot;
        [Tooltip("Automatically fire the trigger when this object becomes enabled.")]
        [SerializeField] private bool triggerOnEnable;

        [Header("Events")]
        [SerializeField] private UnityEvent onTriggered;
        [SerializeField] private UnityEvent onFirstTriggered;
        [SerializeField] private UnityEvent onReset;
        [SerializeField] private MBIntEvent onTriggerCountChanged;

        public int TriggerCount { get; private set; }
        public bool HasTriggered => TriggerCount > 0;

        private void OnEnable()
        {
            if (triggerOnEnable)
                Trigger();
        }

        public void Trigger()
        {
            if (oneShot && TriggerCount > 0)
                return;

            TriggerCount++;

            if (TriggerCount == 1)
                onFirstTriggered?.Invoke();

            onTriggerCountChanged?.Invoke(TriggerCount);
            onTriggered?.Invoke();
        }

        public void ResetTrigger()
        {
            if (TriggerCount == 0)
                return;

            TriggerCount = 0;
            onTriggerCountChanged?.Invoke(TriggerCount);
            onReset?.Invoke();
        }
    }
}

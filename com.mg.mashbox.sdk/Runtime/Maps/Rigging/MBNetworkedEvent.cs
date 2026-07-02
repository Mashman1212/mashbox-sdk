using System;
using MashBoxSDK.Services;
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps.Rigging
{
    [Serializable]
    public class MBStringEvent : UnityEvent<string>
    {
    }

    [AddComponentMenu("MashBox/Maps/Networking/Networked Event")]
    [DisallowMultipleComponent]
    public class MBNetworkedEvent : MonoBehaviour, INetworkedEventListener
    {
        [Header("Network Key")]
        [Tooltip("Stable id shared by every player. Objects with the same key receive the same networked event.")]
        [SerializeField] private string eventKey;
        [Tooltip("Apply the latest saved state when this object is enabled. Use this for doors, switches, collectibles, and other stateful map objects.")]
        [SerializeField] private bool applyStoredStateOnEnable = true;

        [Header("Events")]
        [Tooltip("Invoked for both transient raises and saved state updates.")]
        [SerializeField] private UnityEvent onReceived;
        [Tooltip("Invoked only for one-shot transient raises.")]
        [SerializeField] private UnityEvent onRaised;
        [Tooltip("Invoked only when a saved state is applied.")]
        [SerializeField] private UnityEvent onStateApplied;
        [Tooltip("Invoked when saved state for this key is cleared.")]
        [SerializeField] private UnityEvent onStateCleared;
        [SerializeField] private MBBoolEvent onBool;
        [SerializeField] private MBIntEvent onInt;
        [SerializeField] private MBFloatEvent onFloat;
        [SerializeField] private MBStringEvent onString;

        public string EventKey => NetworkedEventService.NormalizeKey(eventKey);
        public bool ApplyStoredStateOnEnable => applyStoredStateOnEnable;
        string INetworkedEventListener.NetworkedEventKey => EventKey;

        private void Reset()
        {
            EnsureEventKey();
        }

        private void OnValidate()
        {
            EnsureEventKey();
        }

        private void OnEnable()
        {
            NetworkedEventService.Register(this);
        }

        private void OnDisable()
        {
            NetworkedEventService.Unregister(this);
        }

        public void Raise()
        {
            NetworkedEventService.Raise(MBNetworkedEventPayload.Empty(EventKey));
        }

        public void RaiseBool(bool value)
        {
            NetworkedEventService.Raise(MBNetworkedEventPayload.Bool(EventKey, value));
        }

        public void RaiseInt(int value)
        {
            NetworkedEventService.Raise(MBNetworkedEventPayload.Int(EventKey, value));
        }

        public void RaiseFloat(float value)
        {
            NetworkedEventService.Raise(MBNetworkedEventPayload.Float(EventKey, value));
        }

        public void RaiseString(string value)
        {
            NetworkedEventService.Raise(MBNetworkedEventPayload.String(EventKey, value));
        }

        public void SetState()
        {
            NetworkedEventService.SetState(MBNetworkedEventPayload.Empty(EventKey));
        }

        public void SetBoolState(bool value)
        {
            NetworkedEventService.SetState(MBNetworkedEventPayload.Bool(EventKey, value));
        }

        public void SetTrueState()
        {
            SetBoolState(true);
        }

        public void SetFalseState()
        {
            SetBoolState(false);
        }

        public void SetIntState(int value)
        {
            NetworkedEventService.SetState(MBNetworkedEventPayload.Int(EventKey, value));
        }

        public void SetFloatState(float value)
        {
            NetworkedEventService.SetState(MBNetworkedEventPayload.Float(EventKey, value));
        }

        public void SetStringState(string value)
        {
            NetworkedEventService.SetState(MBNetworkedEventPayload.String(EventKey, value));
        }

        public void ClearState()
        {
            NetworkedEventService.ClearState(EventKey);
        }

        public void ReceiveNetworkedEvent(MBNetworkedEventPayload payload, bool isStoredState)
        {
            if (!string.Equals(EventKey, payload.EventKey, StringComparison.Ordinal))
                return;

            onReceived?.Invoke();

            if (isStoredState)
                onStateApplied?.Invoke();
            else
                onRaised?.Invoke();

            InvokeTypedEvent(payload);
        }

        public void ReceiveNetworkedEventStateCleared(string clearedEventKey)
        {
            if (!string.Equals(EventKey, NetworkedEventService.NormalizeKey(clearedEventKey), StringComparison.Ordinal))
                return;

            onStateCleared?.Invoke();
        }

        private void InvokeTypedEvent(MBNetworkedEventPayload payload)
        {
            switch (payload.ValueType)
            {
                case MBNetworkedEventValueType.Bool:
                    onBool?.Invoke(payload.BoolValue);
                    break;
                case MBNetworkedEventValueType.Int:
                    onInt?.Invoke(payload.IntValue);
                    break;
                case MBNetworkedEventValueType.Float:
                    onFloat?.Invoke(payload.FloatValue);
                    break;
                case MBNetworkedEventValueType.String:
                    onString?.Invoke(payload.StringValue ?? string.Empty);
                    break;
            }
        }

        private void EnsureEventKey()
        {
            if (!string.IsNullOrWhiteSpace(eventKey))
                return;

            eventKey = Guid.NewGuid().ToString("N");
        }
    }
}

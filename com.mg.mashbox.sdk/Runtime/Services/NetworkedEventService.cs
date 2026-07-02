using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Services
{
    public enum MBNetworkedEventValueType
    {
        None,
        Bool,
        Int,
        Float,
        String
    }

    [Serializable]
    public struct MBNetworkedEventPayload
    {
        public string EventKey;
        public MBNetworkedEventValueType ValueType;
        public bool BoolValue;
        public int IntValue;
        public float FloatValue;
        public string StringValue;

        public bool HasEventKey => !string.IsNullOrWhiteSpace(EventKey);

        public static MBNetworkedEventPayload Empty(string eventKey)
        {
            return new MBNetworkedEventPayload
            {
                EventKey = eventKey,
                ValueType = MBNetworkedEventValueType.None,
                StringValue = string.Empty
            };
        }

        public static MBNetworkedEventPayload Bool(string eventKey, bool value)
        {
            return new MBNetworkedEventPayload
            {
                EventKey = eventKey,
                ValueType = MBNetworkedEventValueType.Bool,
                BoolValue = value,
                StringValue = string.Empty
            };
        }

        public static MBNetworkedEventPayload Int(string eventKey, int value)
        {
            return new MBNetworkedEventPayload
            {
                EventKey = eventKey,
                ValueType = MBNetworkedEventValueType.Int,
                IntValue = value,
                StringValue = string.Empty
            };
        }

        public static MBNetworkedEventPayload Float(string eventKey, float value)
        {
            return new MBNetworkedEventPayload
            {
                EventKey = eventKey,
                ValueType = MBNetworkedEventValueType.Float,
                FloatValue = value,
                StringValue = string.Empty
            };
        }

        public static MBNetworkedEventPayload String(string eventKey, string value)
        {
            return new MBNetworkedEventPayload
            {
                EventKey = eventKey,
                ValueType = MBNetworkedEventValueType.String,
                StringValue = value ?? string.Empty
            };
        }

        public bool SameValueAs(MBNetworkedEventPayload other)
        {
            if (!string.Equals(EventKey, other.EventKey, StringComparison.Ordinal))
                return false;

            if (ValueType != other.ValueType)
                return false;

            switch (ValueType)
            {
                case MBNetworkedEventValueType.Bool:
                    return BoolValue == other.BoolValue;
                case MBNetworkedEventValueType.Int:
                    return IntValue == other.IntValue;
                case MBNetworkedEventValueType.Float:
                    return Mathf.Approximately(FloatValue, other.FloatValue);
                case MBNetworkedEventValueType.String:
                    return string.Equals(StringValue ?? string.Empty, other.StringValue ?? string.Empty, StringComparison.Ordinal);
                default:
                    return true;
            }
        }
    }

    public interface INetworkedEventListener
    {
        string NetworkedEventKey { get; }
        bool ApplyStoredStateOnEnable { get; }
        void ReceiveNetworkedEvent(MBNetworkedEventPayload payload, bool isStoredState);
        void ReceiveNetworkedEventStateCleared(string eventKey);
    }

    public interface INetworkedEventService
    {
        bool IsAvailable { get; }
        void Raise(MBNetworkedEventPayload payload);
        void SetState(MBNetworkedEventPayload payload);
        void ClearState(string eventKey);
        bool TryGetState(string eventKey, out MBNetworkedEventPayload payload);
    }

    public static class NetworkedEventService
    {
        private static readonly Dictionary<string, List<INetworkedEventListener>> listenersByKey =
            new Dictionary<string, List<INetworkedEventListener>>();

        private static readonly Dictionary<string, MBNetworkedEventPayload> stateCache =
            new Dictionary<string, MBNetworkedEventPayload>();

        private static INetworkedEventService service;

        public static INetworkedEventService Service => service;

        public static void SetService(INetworkedEventService newService)
        {
            service = newService;
        }

        public static void ClearCachedStates()
        {
            stateCache.Clear();
        }

        public static string NormalizeKey(string eventKey)
        {
            return string.IsNullOrWhiteSpace(eventKey) ? string.Empty : eventKey.Trim();
        }

        public static void Register(INetworkedEventListener listener)
        {
            if (listener == null)
                return;

            string eventKey = NormalizeKey(listener.NetworkedEventKey);
            if (string.IsNullOrEmpty(eventKey))
                return;

            if (!listenersByKey.TryGetValue(eventKey, out List<INetworkedEventListener> listeners))
            {
                listeners = new List<INetworkedEventListener>();
                listenersByKey[eventKey] = listeners;
            }

            RemoveMissingListeners(listeners);

            if (!listeners.Contains(listener))
                listeners.Add(listener);

            if (listener.ApplyStoredStateOnEnable && TryGetState(eventKey, out MBNetworkedEventPayload payload))
                listener.ReceiveNetworkedEvent(payload, isStoredState: true);
        }

        public static void Unregister(INetworkedEventListener listener)
        {
            if (listener == null)
                return;

            string eventKey = NormalizeKey(listener.NetworkedEventKey);
            if (string.IsNullOrEmpty(eventKey))
                return;

            if (!listenersByKey.TryGetValue(eventKey, out List<INetworkedEventListener> listeners))
                return;

            listeners.Remove(listener);
            RemoveMissingListeners(listeners);

            if (listeners.Count == 0)
                listenersByKey.Remove(eventKey);
        }

        public static void Raise(MBNetworkedEventPayload payload)
        {
            if (!PreparePayload(ref payload))
                return;

            if (service != null && service.IsAvailable)
            {
                service.Raise(payload);
                return;
            }

            ReceiveRaise(payload);
        }

        public static void SetState(MBNetworkedEventPayload payload)
        {
            if (!PreparePayload(ref payload))
                return;

            if (service != null && service.IsAvailable)
            {
                service.SetState(payload);
                return;
            }

            ReceiveState(payload);
        }

        public static void ClearState(string eventKey)
        {
            eventKey = NormalizeKey(eventKey);
            if (string.IsNullOrEmpty(eventKey))
                return;

            if (service != null && service.IsAvailable)
            {
                service.ClearState(eventKey);
                return;
            }

            ReceiveStateCleared(eventKey);
        }

        public static bool TryGetState(string eventKey, out MBNetworkedEventPayload payload)
        {
            eventKey = NormalizeKey(eventKey);
            if (string.IsNullOrEmpty(eventKey))
            {
                payload = default;
                return false;
            }

            if (stateCache.TryGetValue(eventKey, out payload))
                return true;

            if (service != null && service.TryGetState(eventKey, out payload))
            {
                PreparePayload(ref payload);
                stateCache[eventKey] = payload;
                return true;
            }

            payload = default;
            return false;
        }

        public static void ReceiveRaise(MBNetworkedEventPayload payload)
        {
            if (!PreparePayload(ref payload))
                return;

            Dispatch(payload, isStoredState: false);
        }

        public static void ReceiveState(MBNetworkedEventPayload payload)
        {
            if (!PreparePayload(ref payload))
                return;

            if (stateCache.TryGetValue(payload.EventKey, out MBNetworkedEventPayload cached) &&
                cached.SameValueAs(payload))
            {
                return;
            }

            stateCache[payload.EventKey] = payload;
            Dispatch(payload, isStoredState: true);
        }

        public static void ReceiveStateCleared(string eventKey)
        {
            eventKey = NormalizeKey(eventKey);
            if (string.IsNullOrEmpty(eventKey))
                return;

            if (!stateCache.Remove(eventKey))
                return;

            if (!listenersByKey.TryGetValue(eventKey, out List<INetworkedEventListener> listeners))
                return;

            INetworkedEventListener[] snapshot = listeners.ToArray();
            foreach (INetworkedEventListener listener in snapshot)
            {
                if (!IsListenerAlive(listener))
                    continue;

                listener.ReceiveNetworkedEventStateCleared(eventKey);
            }

            RemoveMissingListeners(listeners);
        }

        private static bool PreparePayload(ref MBNetworkedEventPayload payload)
        {
            payload.EventKey = NormalizeKey(payload.EventKey);
            payload.StringValue = payload.StringValue ?? string.Empty;
            return payload.HasEventKey;
        }

        private static void Dispatch(MBNetworkedEventPayload payload, bool isStoredState)
        {
            if (!listenersByKey.TryGetValue(payload.EventKey, out List<INetworkedEventListener> listeners))
                return;

            INetworkedEventListener[] snapshot = listeners.ToArray();
            foreach (INetworkedEventListener listener in snapshot)
            {
                if (!IsListenerAlive(listener))
                    continue;

                listener.ReceiveNetworkedEvent(payload, isStoredState);
            }

            RemoveMissingListeners(listeners);
        }

        private static void RemoveMissingListeners(List<INetworkedEventListener> listeners)
        {
            for (int index = listeners.Count - 1; index >= 0; index--)
            {
                if (!IsListenerAlive(listeners[index]))
                    listeners.RemoveAt(index);
            }
        }

        private static bool IsListenerAlive(INetworkedEventListener listener)
        {
            if (listener == null)
                return false;

            if (listener is UnityEngine.Object unityObject)
                return unityObject != null;

            return true;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using MashBoxBridge.Common.Interfaces;

namespace MashBoxBridge.Common.Sys
{
  
    public interface ISmartDataService
    {
        //ISmartDataBase LookUpSmartData(string key);
        public void TriggerPrefSave();
    }
    
    public static class SmartDataService
    {
        private static ISmartDataService _service;
        private static Dictionary<string, ISmartDataBase> _smartDatas = new Dictionary<string, ISmartDataBase>();
        private static readonly HashSet<string> _warnedDuplicateIds = new HashSet<string>();
        
        public static void SetService(ISmartDataService service)
        {
            _service = service;
        }
        
        public static void Register(ISmartDataBase data)
        {
            if (data == null || IsDestroyed(data) || string.IsNullOrEmpty(data.ID))
            {
                Debug.LogWarning("[SmartDataService] Tried to register null or invalid SmartData.");
                return;
            }

            if (_smartDatas.TryGetValue(data.ID, out ISmartDataBase existing))
            {
                if (IsDestroyed(existing))
                {
                    _smartDatas[data.ID] = data;
                    return;
                }

                if (ReferenceEquals(existing, data))
                    return;

                if (_warnedDuplicateIds.Add(data.ID))
                    Debug.LogWarning($"[SmartDataService] Duplicate SmartData ID found: {data.ID}");
                return;
            }

            _smartDatas.Add(data.ID, data);
             //Debug.Log($"[SmartDataService] Registered: {data.ID}");
        }

        public static void TriggerPrefSave()
        {
            if (_service != null)
            {
                _service.TriggerPrefSave();
            }
        }
        
        public static void Unregister(ISmartDataBase data)
        {
            if (data == null)
                return;

            if (IsDestroyed(data))
            {
                RemoveDestroyedEntries();
                return;
            }

            if (_smartDatas.TryGetValue(data.ID, out ISmartDataBase existing) && ReferenceEquals(existing, data))
                _smartDatas.Remove(data.ID);
        }

        public static ISmartDataBase LookUp(string key)
        {
            if (key == null) return null;
            _smartDatas.TryGetValue(key.ToLowerInvariant(), out var data);

            if (IsDestroyed(data))
            {
                _smartDatas.Remove(key.ToLowerInvariant());
                return null;
            }

            return data;
        }
        

        public static T Get<T>(string key)
        {
            return (T)((ISmartData<T>)LookUp(key)).Value;
        }

        public static void Set<T>(string key, T value)
        {
            var data = LookUp(key) as ISmartData<T>;
            if (data != null) data.Value = value;
        }
        
        public static IEnumerable<ISmartDataBase> All
        {
            get
            {
                RemoveDestroyedEntries();
                return new List<ISmartDataBase>(_smartDatas.Values);
            }
        }

        private static bool IsDestroyed(ISmartDataBase data)
        {
            return data is Object unityObject && unityObject == null;
        }

        private static void RemoveDestroyedEntries()
        {
            List<string> deadKeys = null;
            foreach (var pair in _smartDatas)
            {
                if (!IsDestroyed(pair.Value))
                    continue;

                deadKeys ??= new List<string>();
                deadKeys.Add(pair.Key);
            }

            if (deadKeys == null)
                return;

            foreach (string key in deadKeys)
            {
                _smartDatas.Remove(key);
            }
        }
    }
}

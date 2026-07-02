using System;
using System.Collections.Generic;
using MashBoxBridge.Common.Interfaces.MashBoxBridge.Common.Interfaces;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxBridge.Common.Sys
{
    public interface IMapService
    {
        /// <summary>Loads a map by name (whatever your MapData.MapName is).</summary>
        bool TryLoadMap(string mapName);
        bool TryLoadMap(ulong uid);
        public void AddMap(Object mapDataObj);
        public void AddMap(IMapData mapData);

        public void RemoveAllMaps();
        public void RemoveMap(string name);
        
        /// <summary>Optional: for listing / autocomplete.</summary>
        IReadOnlyList<string> GetAllMapNames();

        public void RemoveInvalidMaps();

    }

    public static class MapService
    {
        private static IMapService _service;
        private static bool _warnedNoService;

        public static void SetService(IMapService service)
        {
            _service = service;
            _warnedNoService = false;
        }

        private static void WarnNoService(string message)
        {
            if (_warnedNoService)
                return;

            _warnedNoService = true;
            Debug.LogWarning(message);
        }

        public static bool TryLoadMap(string mapName)
        {
            if (_service == null)
            {
                WarnNoService($"[MapService] No IMapService registered. Tried to load '{mapName}'.");
                return false;
            }
            
            Debug.Log("[MapService] TryLoad string Name: " + mapName);

            return _service.TryLoadMap(mapName);
        }

        
        public static void TryLoadMap(IMapData mapData)
        {
            Debug.Log("[MapService] TryLoad Map UID: " + mapData.Uid);
            TryLoadMap(mapData.Uid);
        }
        public static void TryLoadMap(Object dataObj)
        {
            Debug.Log("[MapService] TryLoad Object: " + dataObj.name);
            if (dataObj is IMapData mapData)
            {
                TryLoadMap(mapData);
            }
        }
        public static bool TryLoadMap(ulong uid)
        {
            if (_service == null)
            {
                WarnNoService($"[MapService] No IMapService registered. Tried to load UID '{uid}'.");
                return false;
            }

            return _service.TryLoadMap(uid);
        }
        public static IReadOnlyList<string> GetAllMapNames()
        {
            if (_service == null)
            {
                WarnNoService("[MapService] No IMapService registered. Returning empty list.");
                return Array.Empty<string>();
            }

            return _service.GetAllMapNames();
        }
        
        public static void AddMap(Object dataObj)
        {
            if (dataObj == null)
            {
                return;
            }
            
            if (_service == null)
            {
                WarnNoService("[MapService] No IMapService registered. Cannot add map object.");
                return;
            }
            
            _service.AddMap(dataObj);
        }

        public static void AddMap(IMapData mapData)
        {
            if (mapData == null)
            {
                return;
            }
            
            if (_service == null)
            {
                WarnNoService("[MapService] No IMapService registered. Cannot add map data.");
                return;
            }

            _service.AddMap(mapData);
        }
        
        public static void RemoveAllMaps()
        {
            if (_service == null)
            {
                WarnNoService("[MapService] No IMapService registered. Cannot remove all maps.");
                return;
            }
            
            _service.RemoveAllMaps();
        }

        public static void RemoveMap(string name)
        {
            if (_service == null)
            {
                WarnNoService("[MapService] No IMapService registered. Cannot remove map.");
                return;
            }


            _service.RemoveMap(name);
        }

        public static void RemoveInvalidMaps()
        {
            if (_service == null)
            {
                WarnNoService("[MapService] No IMapService registered. Cannot remove invalid maps.");
                return;
            }

            _service.RemoveInvalidMaps();
        }
    }
}

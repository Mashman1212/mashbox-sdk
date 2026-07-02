
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxBridge.Common.Sys
{
    public interface IMasterActivityLog
    {
        // Event
        event Action<string> OnActivityLogged;
        
        // Methods
        void Log(string message);
        void Clear();
        List<string> GetLog(string filter = "");
        bool WasLogged(string keyword, float withinLastSeconds = -1f);
    }
    
    public static class MasterActivityLogService
    {
        public static IMasterActivityLog Service => _service;
        private static IMasterActivityLog _service;


        public static void SetService(IMasterActivityLog service)
        {
            Debug.Log("[MasterActivityLogService] SetService");
            _service = service;
            //_service.OnActivityLogged += OnActivityLogged.Invoke;
        }
        
        public static void Log(string message)
        {
            if(_service == null)
            {
                return;
            }
            _service.Log(message);
        }
        public static void Clear()
        {
            if(_service == null)
            {
                return;
            }
            _service.Clear();
        }
        public static bool WasLogged(string keyword, float withinLastSeconds = -1f)
        {
            if(_service == null)
            {
                return false;
            }

            return _service.WasLogged(keyword, withinLastSeconds);
        }
    }
}
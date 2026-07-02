using System.Collections.Generic;
using UnityEngine;

namespace MashBoxBridge.Common.Interfaces.GameEvent
{
    public static class GameEventsManager
    {
        private static IGameEventService _managerService;

        public static void SetService(IGameEventService service)
        {
            _managerService = service;
        }
        
        /// <summary>
        /// Can Be null. Dont have SkillIssues kay?
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static IGameEvent LookUpGameEvent(string key)
        {
            if (_managerService == null)
            {
                Debug.LogWarning($"[GameEventsManager] LookUpGameEvent({key}) _managerService == null");
                return null;
            }
            
            return _managerService.LookUpGameEvent(key);
        }

        public static void InvokeEvent(string name)
        {
            IGameEvent gameEvent = LookUpGameEvent(name);
            if (gameEvent != null)
            {
                gameEvent.Raise();
            }
        }
    }
}
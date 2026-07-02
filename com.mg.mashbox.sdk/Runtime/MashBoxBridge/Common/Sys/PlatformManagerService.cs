using System;
using MashBoxBridge.Achievements;
using UnityEngine;

namespace MashBoxBridge.Common.Sys
{
    public interface IPlatformManager
    {
        public void UnlockAchievement(string id);
    }
    
    public static class PlatformManagerService
    {
  
        public static IPlatformManager Service => _service;
        private static IPlatformManager _service;
        

        public static void SetService(IPlatformManager service)
        {
            _service = service;
        }

        public static void UnlockAchievement(string id)
        {
            if (_service != null)
            {
                _service.UnlockAchievement(AchievementDatabaseAccess.GetAchievementDatabase().GetPlatformIdByInternal(id));
            }
        }
        
    }
}
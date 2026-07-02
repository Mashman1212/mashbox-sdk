using System;
using UnityEngine;

namespace MashBoxBridge.Common.Sys
{
    public interface ITeamRegistryService
    {
        void PlayerScored(int playerID, int points);
        void PlayerLostLife(int teamID, int playerID);
    }
    
    public static class TeamRegistryService
    {
        //public static ITeamRegistryService Service => _service;
        private static ITeamRegistryService _service;
        public static Action<int, int> OnTeamScored { get; set; }//TeamID, PlayerID
        public static Action<int, int>  OnPlayerLostLife { get; set; }//TeamID, PlayerID
        public static void SetService(ITeamRegistryService service)
        {
            _service = service;

        }

        public static void PlayerScored(int playerID, int points)
        {
            if (_service != null)
            {
                #if UNITY_EDITOR
                    Debug.Log($"[TeamRegistryService] PlayerScored, player ID : {playerID}, points : {points}");
                #endif
                
                _service.PlayerScored(playerID,points);
            }
        }

        public static void PlayerLostLife(int teamID, int playerID)
        {
            if (_service != null)
            {
#if UNITY_EDITOR
                Debug.Log($"[TeamRegistryService] PlayerLostLife, teamID: {teamID}, playerID : {playerID}");
#endif
                
                _service.PlayerLostLife(teamID,playerID);
            }
        }
    }
}
using System;
using UnityEngine;

namespace MashBoxBridge.Common.Sys
{
    public enum GameState
    {
        TitleScreen,
        MainMenu,
        LoadingScreen,
        PauseMenu,
        Gameplay,
        Replay,
        Editor
    }

    
    public interface IGameLoopService
    {
        GameState State { get; }
        void SetState(GameState state);

        void ResetDynamics();
    }
    
    public static class GameLoopService
    {
        public static GameState State => _service?.State ?? GameState.Gameplay;

        public static GameState LastState;
        public static GameState CurrentState;
        public static IGameLoopService service => _service;
        private static IGameLoopService _service;
        
        public static Action<GameState> OnStateChanged;

        public static void SetService(IGameLoopService service)
        {
            _service = service;
        }

        public static void SetState(GameState gameState)
        {
            if (_service == null || (CurrentState == gameState))
                return;

            Debug.Log("[GameLoopService] Set State : " + gameState);

            LastState = CurrentState;
            CurrentState = gameState;

            _service.SetState(gameState);

            CleanNullSubscribers();      // ?? Add this!
            OnStateChanged?.Invoke(gameState);
        }
        
        private static void CleanNullSubscribers()
        {
            if (OnStateChanged == null) return;

            foreach (var d in OnStateChanged.GetInvocationList())
            {
                if (d.Target == null)
                {
                    OnStateChanged -= (Action<GameState>)d;
                }
            }
        }

        public static void ResetDynamics()
        {
            if (_service == null)
            {
                return;
            }

            _service.ResetDynamics();
        }
        
    }
}
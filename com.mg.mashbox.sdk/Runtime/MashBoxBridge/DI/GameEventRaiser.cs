using System;
using MashBoxBridge.Common.Interfaces.GameEvent;
using UnityEngine;

namespace MashBoxBridge.DI
{
    public interface IGameEventRaiser
    {
        void Raise(string lookUp, IGameEvent ge);
        void RegisterListener(Action<string, IGameEvent> listener);
        void UnregisterListener(Action<string, IGameEvent> listener);
    }
    
    [CreateAssetMenu(fileName = "New GameEvent Raiser", menuName = "GameEvent Raiser")]
    public class GameEventRaiser : ScriptableObject, IGameEventRaiser
    {
        private Action<string, IGameEvent> _onRaise;

        public void RegisterListener(Action<string, IGameEvent> listener) {
            _onRaise += listener;
        }

        public void UnregisterListener(Action<string, IGameEvent> listener) {
            _onRaise -= listener;
        }

        public void Raise(string lookUp, IGameEvent gameEvent) {
            _onRaise?.Invoke(lookUp, gameEvent);
        }
    }
}
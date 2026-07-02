using UnityEngine;

namespace MashBoxBridge.Common.Interfaces.GameEvent
{
    // This interface is used for any listener that needs to react to in-game events.
    public interface IGameEventListener
    {
        void OnEventRaised(); // Called when event is raised with no parameter
        void OnEventRaised(Object unityDataObject); // Called when event is raised with Unity object as parameter
    }
}
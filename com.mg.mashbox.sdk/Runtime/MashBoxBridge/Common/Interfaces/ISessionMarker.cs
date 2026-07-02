using UnityEngine;

namespace MashBoxBridge.Common.Interfaces
{
    public interface ISessionMarker : IInteractable
    {
        Transform Root { get; }
        void Place();
        void Place(Vector3 position,Quaternion rotation);
        void PlaceLocal(Vector3 position,Quaternion rotation);
        void Respawn();
        void HideVisual(bool off);
        
    }
}
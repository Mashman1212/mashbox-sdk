// ISurfaceProvider.cs

using UnityEngine;

namespace MashBoxBridge.Common.Interfaces
{
    public interface ISurfaceProvider
    {
        int CurrentSurfaceID { get; }
        int LastSurfaceID { get; }
        string CurrentSurfaceTag { get; }
        Collider CurrentCollider { get; }

        /// <summary>Raised whenever the resolved surface changes.</summary>
        event System.Action<int, string> OnSurfaceChanged;
    }
}
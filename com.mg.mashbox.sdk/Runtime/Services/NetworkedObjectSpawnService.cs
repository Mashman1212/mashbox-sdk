using System;
using UnityEngine;

namespace MashBoxSDK.Services
{
    [Serializable]
    public struct MBNetworkedObjectSpawnRequest
    {
        public string SpawnKey;
        public string RequestKey;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool SnapToGround;

        public bool HasSpawnKey => !string.IsNullOrWhiteSpace(SpawnKey);

        public static MBNetworkedObjectSpawnRequest Create(
            string spawnKey,
            Vector3 position,
            Quaternion rotation,
            bool snapToGround = true,
            string requestKey = null)
        {
            return new MBNetworkedObjectSpawnRequest
            {
                SpawnKey = spawnKey,
                RequestKey = requestKey,
                Position = position,
                Rotation = rotation,
                SnapToGround = snapToGround
            };
        }
    }

    public interface INetworkedObjectSpawnService
    {
        bool IsAvailable { get; }
        bool Spawn(MBNetworkedObjectSpawnRequest request);
    }

    public static class NetworkedObjectSpawnService
    {
        private static INetworkedObjectSpawnService service;

        public static INetworkedObjectSpawnService Service => service;
        public static bool IsAvailable => service != null && service.IsAvailable;

        public static void SetService(INetworkedObjectSpawnService newService)
        {
            service = newService;
        }

        public static string NormalizeKey(string key)
        {
            return string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();
        }

        public static bool Spawn(string spawnKey, Vector3 position, Quaternion rotation, bool snapToGround = true)
        {
            return Spawn(MBNetworkedObjectSpawnRequest.Create(spawnKey, position, rotation, snapToGround));
        }

        public static bool Spawn(MBNetworkedObjectSpawnRequest request)
        {
            if (!PrepareRequest(ref request))
                return false;

            return service != null && service.IsAvailable && service.Spawn(request);
        }

        public static bool PrepareRequest(ref MBNetworkedObjectSpawnRequest request)
        {
            request.SpawnKey = NormalizeKey(request.SpawnKey);
            request.RequestKey = NormalizeKey(request.RequestKey);

            if (IsZeroRotation(request.Rotation))
                request.Rotation = Quaternion.identity;

            return request.HasSpawnKey;
        }

        private static bool IsZeroRotation(Quaternion rotation)
        {
            return rotation.x == 0.0f
                   && rotation.y == 0.0f
                   && rotation.z == 0.0f
                   && rotation.w == 0.0f;
        }
    }
}

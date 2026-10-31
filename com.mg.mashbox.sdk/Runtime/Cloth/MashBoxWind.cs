using System;
using UnityEngine;
namespace MashBoxSDK.Cloth
{
    [Serializable]
    public struct MashBoxWindSettings
    {
        public Vector3 velocity;
        [Range(0,1)] public float gust;
        [Min(0)] public float frequency;
        public Vector3 Sample(float time) => velocity * Mathf.Max(0, 1 + Mathf.Clamp01(gust) * Mathf.Sin(time * Mathf.Max(0, frequency) * Mathf.PI * 2));
    }
    // Shared by runtime cloth and MG Clothing. Studio uses per-session/renderer overrides.
    public static class MashBoxWind
    {
        public static MashBoxWindSettings Current { get; private set; }
        public static void Set(MashBoxWindSettings settings)
        {
            Current = settings;
            Shader.SetGlobalVector("_MGWindVelocity", settings.velocity);
            Shader.SetGlobalVector("_MGWindGust", new Vector4(settings.gust,settings.frequency,0,0));
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Set(default);
    }
}

using System;
using UnityEngine;

namespace MashBoxSDK.Culling
{
    [Serializable]
    public sealed class CullingMask
    {
        public ClothingCullingSurface surface;
        public byte[] hidden = Array.Empty<byte>();
        public CullingMask Copy() => new CullingMask { surface=surface, hidden=(byte[])hidden.Clone() };
    }

    [CreateAssetMenu(menuName="MashBox/Clothing/Culling Profile")]
    public sealed class ClothingCullingProfile : ScriptableObject
    {
        // Stable legacy ClothingClipType value; zero for a user-authored profile.
        public int legacyClipType;
        public CullingMask[] masks = Array.Empty<CullingMask>();
    }
}

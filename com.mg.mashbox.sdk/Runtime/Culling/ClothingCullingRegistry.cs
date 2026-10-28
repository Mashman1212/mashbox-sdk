using System;
using UnityEngine;

namespace MashBoxSDK.Culling
{
    [CreateAssetMenu(menuName="MashBox/Clothing/Culling Registry")]
    public sealed class ClothingCullingRegistry : ScriptableObject
    {
        public ClothingCullingSurface[] surfaces = Array.Empty<ClothingCullingSurface>();
        public ClothingCullingProfile[] presets = Array.Empty<ClothingCullingProfile>();
        public ClothingCullingProfile Get(int legacyClipType)
        {
            foreach(var preset in presets) if(preset && preset.legacyClipType==legacyClipType) return preset;
            return null;
        }
    }
}

using System;
using MashBoxSDK.Cloth;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed class StudioClothColliderDefaults : ScriptableObject
    {
        internal const string AssetPath = "Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Cloth Collider Defaults.asset";
        public ClothBoneCapsule[] capsules = Array.Empty<ClothBoneCapsule>();
    }
}

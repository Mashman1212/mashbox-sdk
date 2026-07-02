using UnityEngine;
using UnityEngine.Scripting;

namespace MashBoxSDK.Clothing
{
    [Preserve]
    [DisallowMultipleComponent]
    public class ClothingClipSettings : MonoBehaviour
    {
        [Tooltip("Defines how this clothing item should clip with the character body")]
        public ClothingClipType clipType = ClothingClipType.None;
        [Tooltip("Character features that should be disabled when this item is equipped")]
        public CharacterFeatureFlags disableFeatures;
    }
}
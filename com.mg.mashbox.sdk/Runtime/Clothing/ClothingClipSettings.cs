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
    }
}
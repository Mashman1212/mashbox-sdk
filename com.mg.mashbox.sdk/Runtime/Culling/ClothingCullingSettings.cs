using UnityEngine;
using UnityEngine.Scripting;

namespace MashBoxSDK.Culling
{
    [Preserve, DisallowMultipleComponent]
    [AddComponentMenu("MashBox/Clothing/Culling Settings")]
    public sealed class ClothingCullingSettings : MonoBehaviour
    {
        // An explicit component, even with no profile, overrides the legacy preset.
        public ClothingCullingProfile profile;
    }
}

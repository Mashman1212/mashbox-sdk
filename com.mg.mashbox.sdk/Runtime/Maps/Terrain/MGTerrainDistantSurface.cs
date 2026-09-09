using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    /// <summary>Identifies a generated distant surface so subsequent captures can exclude it.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class MGTerrainDistantSurface : MonoBehaviour
    {
        [SerializeField] MGTerrain m_Source;
        public MGTerrain Source => m_Source;
        public void SetSource(MGTerrain source) => m_Source = source;
    }
}

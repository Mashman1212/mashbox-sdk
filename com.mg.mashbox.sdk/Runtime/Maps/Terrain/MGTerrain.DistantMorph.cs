using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        [NonSerialized] readonly List<Material> m_DistantMorphMaterials = new List<Material>();
        [NonSerialized] float m_DistantMorphTop = float.NegativeInfinity;
        [NonSerialized] bool m_DistantMorphBoundsSet;
        [NonSerialized] Bounds m_PreviousDistantMorphBounds;

        void RefreshDistantMorphBounds()
        {
            var renderer = MeshRenderer;
            if (renderer == null || MeshFilter == null || MeshFilter.sharedMesh == null) return;
            m_DistantMorphTop = float.NegativeInfinity;
            renderer.GetSharedMaterials(m_DistantMorphMaterials);
            foreach (var material in m_DistantMorphMaterials)
                if (material != null && material.HasProperty("_DistantSurfaceStrength")
                    && material.GetFloat("_DistantSurfaceStrength") > 0
                    && material.HasProperty("_DistantSurfaceMaxHeight")
                    && material.GetTexture("_DistantSurfaceHeightMap") != null)
                    m_DistantMorphTop = Mathf.Max(m_DistantMorphTop, material.GetFloat("_DistantSurfaceMaxHeight"));
            if (float.IsNegativeInfinity(m_DistantMorphTop)) { RestoreDistantMorphBounds(); return; }
            if (!m_DistantMorphBoundsSet) { m_PreviousDistantMorphBounds = renderer.localBounds; m_DistantMorphBoundsSet = true; }
            Bounds bounds = m_PreviousDistantMorphBounds;
            bounds.Encapsulate(MeshFilter.sharedMesh.bounds);
            renderer.localBounds = ExpandDistantMorphBounds(bounds);
        }

        Bounds ExpandDistantMorphBounds(Bounds bounds)
        {
            if (!float.IsNegativeInfinity(m_DistantMorphTop))
            {
                Vector3 max = bounds.max;
                max.y = Mathf.Max(max.y, m_DistantMorphTop + .1f);
                bounds.SetMinMax(bounds.min, max);
            }
            return bounds;
        }

        void RestoreDistantMorphBounds()
        {
            if (m_DistantMorphBoundsSet && MeshRenderer != null) MeshRenderer.localBounds = m_PreviousDistantMorphBounds;
            m_DistantMorphBoundsSet = false;
            m_DistantMorphTop = float.NegativeInfinity;
        }
    }
}

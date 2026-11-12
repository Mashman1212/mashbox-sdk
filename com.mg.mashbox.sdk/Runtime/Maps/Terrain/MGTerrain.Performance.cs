using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        /// <summary>Reads source parts without building detail cells or generated render resources.
        /// Meshes include all enabled LODs; draw/vertex counts use the largest single LOD.</summary>
        public void GetPerformanceSourceMeshes(int prototypeIndex, List<Mesh> meshes, out int maximumParts, out long maximumVertices)
        {
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));
            meshes.Clear();
            maximumParts = 0;
            maximumVertices = 0;
            if ((uint)prototypeIndex >= m_Prototypes.Count || m_Prototypes[prototypeIndex] == null) return;
            var prototype = m_Prototypes[prototypeIndex];
            for (int lod = 0; lod < prototype.TreeLodCount; lod++)
            {
                int parts = 0;
                long vertices = 0;
                foreach (var part in GetRenderParts(prototype, lod))
                {
                    if (part.mesh == null || part.material == null) continue;
                    if (!meshes.Contains(part.mesh)) meshes.Add(part.mesh);
                    parts++;
                    vertices += part.mesh.vertexCount;
                }
                maximumParts = Math.Max(maximumParts, parts);
                maximumVertices = Math.Max(maximumVertices, vertices);
            }
        }

        /// <summary>Returns generated renderers as well as the master surface. Unity object
        /// searches can omit the HideAndDontSave surface chunks.</summary>
        public void GetSurfacePerformanceRenderers(List<MeshRenderer> renderers)
        {
            if (renderers == null) throw new ArgumentNullException(nameof(renderers));
            renderers.Clear();
            if (MeshRenderer != null) renderers.Add(MeshRenderer);
            foreach (var tile in m_SurfaceTiles)
                if (tile.renderer != null && !renderers.Contains(tile.renderer)) renderers.Add(tile.renderer);
        }

        /// <summary>Includes inactive cached surface LODs that are still resident.</summary>
        public void GetCachedSurfaceMeshes(List<Mesh> meshes)
        {
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));
            meshes.Clear();
            if (MeshFilter != null && MeshFilter.sharedMesh != null) meshes.Add(MeshFilter.sharedMesh);
            foreach (var tile in m_SurfaceTiles)
                if (tile.mesh != null && !meshes.Contains(tile.mesh)) meshes.Add(tile.mesh);
            foreach (var lod in m_SurfaceLods)
                if (lod.mesh != null && !meshes.Contains(lod.mesh)) meshes.Add(lod.mesh);
        }
    }
}

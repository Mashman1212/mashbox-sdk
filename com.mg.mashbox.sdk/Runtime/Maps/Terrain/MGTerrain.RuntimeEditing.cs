using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        [NonSerialized] bool m_RuntimeEditPrepared;
        readonly List<Mesh> m_RuntimeEditOwnedMeshes = new List<Mesh>();
        public void PrepareRuntimeEditing()
        {
            if (m_RuntimeEditPrepared || !Application.isPlaying) return;
            Mesh source = MeshFilter ? MeshFilter.sharedMesh : null;
            if (!source || !source.isReadable) throw new InvalidOperationException("Terrain requires readable source geometry. Prepare the source map for runtime editing and export it again.");
            CacheSurfaceColliderVertexMaps(source);
            var copy = Instantiate(source); copy.name = source.name + " (runtime sculpt)";
            m_RuntimeEditOwnedMeshes.Add(copy); MeshFilter.sharedMesh = copy;
            foreach (var collider in m_SurfaceColliderChunks)
                if (collider && collider.sharedMesh)
                {
                    var collisionCopy = Instantiate(collider.sharedMesh);
                    m_RuntimeEditOwnedMeshes.Add(collisionCopy); collider.sharedMesh = collisionCopy;
                }
            m_RuntimeEditPrepared = true;
            NotifySurfaceMeshChanged(false, true);
            RefreshSurfaceCollidersFromMesh();
        }
        public void ReleaseRuntimeEditingMeshes()
        {
            foreach (var mesh in m_RuntimeEditOwnedMeshes) if (mesh) Destroy(mesh);
            m_RuntimeEditOwnedMeshes.Clear();
        }
    }
}

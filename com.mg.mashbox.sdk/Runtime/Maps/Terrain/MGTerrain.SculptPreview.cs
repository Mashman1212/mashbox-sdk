using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        [NonSerialized] readonly List<Vector3> m_SculptNormals = new List<Vector3>();
        [NonSerialized] readonly List<Vector3> m_SculptBorderNormals = new List<Vector3>();
        [NonSerialized] bool m_SculptTangentsDirty;

        public void RefreshSculptTangents(bool defer)
        {
            var mesh = MeshFilter.sharedMesh;
            if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) { m_SculptTangentsDirty = false; return; }
            if (defer) { m_SculptTangentsDirty = true; return; }
            mesh.RecalculateTangents(); m_SculptTangentsDirty = false;
        }

        public void FinalizeSculptTangents()
        {
            if (!m_SculptTangentsDirty || MeshFilter == null || MeshFilter.sharedMesh == null) return;
            RefreshSculptTangents(false);
            NotifySurfaceMeshChanged(topologyChanged: false, geometryOnly: true);
        }

        public bool HasRegularSculptGrid => SurfaceGridWidth >= 3 && SurfaceGridHeight >= 3
            && MeshFilter != null && MeshFilter.sharedMesh != null
            && (long)SurfaceGridWidth * SurfaceGridHeight == MeshFilter.sharedMesh.vertexCount;

        public bool IsSculptBorderVertex(int index)
        {
            int x = index % SurfaceGridWidth, z = index / SurfaceGridWidth;
            // One adjacent row affects the perimeter's triangle normals too.
            return x < 2 || x >= SurfaceGridWidth - 2 || z < 2 || z >= SurfaceGridHeight - 2;
        }

        public bool SculptBordersUnchanged(IReadOnlyList<Vector3> before, IReadOnlyList<Vector3> after)
        {
            if (!HasRegularSculptGrid || before.Count != after.Count) return false;
            int w = SurfaceGridWidth, h = SurfaceGridHeight;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    if (z >= 2 && z < h - 2 && x == 2) x = w - 2;
                    int i = z * w + x;
                    if (!before[i].Equals(after[i])) return false;
                }
            return true;
        }

        public void RecalculateSculptNormals(bool preserveBorder)
        {
            var mesh = MeshFilter.sharedMesh;
            preserveBorder &= HasRegularSculptGrid;
            int w = SurfaceGridWidth, h = SurfaceGridHeight;
            if (preserveBorder)
            {
                mesh.GetNormals(m_SculptNormals);
                preserveBorder = m_SculptNormals.Count == mesh.vertexCount;
                m_SculptBorderNormals.Clear();
                if (preserveBorder)
                    for (int z = 0; z < h; z++) for (int x = 0; x < w; x++)
                    {
                        if (z > 0 && z < h - 1 && x == 1) x = w - 1;
                        m_SculptBorderNormals.Add(m_SculptNormals[z * w + x]);
                    }
            }
            mesh.RecalculateNormals();
            if (!preserveBorder) return;
            mesh.GetNormals(m_SculptNormals);
            for (int z = 0, n = 0; z < h; z++) for (int x = 0; x < w; x++)
            {
                if (z > 0 && z < h - 1 && x == 1) x = w - 1;
                m_SculptNormals[z * w + x] = m_SculptBorderNormals[n++];
            }
            mesh.SetNormals(m_SculptNormals);
        }
    }
}

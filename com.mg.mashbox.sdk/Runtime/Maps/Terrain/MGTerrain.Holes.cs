using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        [Serializable]
        sealed class HoleSubMesh
        {
            public int[] triangles;
            public bool[] hidden;
        }

        [SerializeField, HideInInspector] HoleSubMesh[] m_HoleSubMeshes;
        [SerializeField, HideInInspector] int m_HoleVertexCount;
        public bool HasHoleData => m_HoleSubMeshes != null && m_HoleSubMeshes.Length > 0;

        public bool TryPrepareHoleColliderMappings()
        {
            if (m_SurfaceColliderChunks.Length == 0) return true;
            Mesh source = MeshFilter != null ? MeshFilter.sharedMesh : null;
            if (source == null || !source.isReadable) return false;
            CacheSurfaceColliderVertexMaps(source);
            if (m_SurfaceColliderVertexMaps.Length != m_SurfaceColliderChunks.Length
                || m_ColliderSourceVertexCount != source.vertexCount) return false;
            for (int i = 0; i < m_SurfaceColliderChunks.Length; i++)
            {
                var map = m_SurfaceColliderVertexMaps[i];
                if (map == null || map.collider == null || map.collider != m_SurfaceColliderChunks[i]
                    || map.sourceIndices == null) return false;
                Mesh mesh = map.collider.sharedMesh != null ? map.collider.sharedMesh : map.holeMesh;
                if (mesh == null || !mesh.isReadable || map.sourceIndices.Length != mesh.vertexCount) return false;
            }
            return true;
        }

        public void InitializeSurfaceHoles()
        {
            Mesh mesh = MeshFilter.sharedMesh;
            if (HasHoleData)
            {
                if (mesh.vertexCount != m_HoleVertexCount || mesh.subMeshCount != m_HoleSubMeshes.Length)
                    throw new InvalidOperationException("The terrain topology changed. Restore its original mesh before editing holes.");
                return;
            }
            CacheSurfaceColliderVertexMaps(mesh);
            if (m_SurfaceColliderChunks.Length != m_SurfaceColliderVertexMaps.Length)
                throw new InvalidOperationException("Rebuild the terrain child colliders before painting holes.");
            for (int s = 0; s < mesh.subMeshCount; s++)
                if (mesh.GetTopology(s) != MeshTopology.Triangles)
                    throw new InvalidOperationException("Hole painting requires triangle meshes.");
            m_HoleSubMeshes = new HoleSubMesh[mesh.subMeshCount];
            m_HoleVertexCount = mesh.vertexCount;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] triangles = mesh.GetTriangles(s);
                m_HoleSubMeshes[s] = new HoleSubMesh { triangles = triangles, hidden = new bool[triangles.Length / 3] };
            }
            foreach (var map in m_SurfaceColliderVertexMaps)
                if (map.collider != null && map.collider.sharedMesh != null)
                    map.holeSourceTriangles = map.collider.sharedMesh.triangles;
        }

        // Collider rebuilds include hidden faces so they can still be restored later.
        public int[] GetSurfaceTrianglesIncludingHoles()
        {
            if (!HasHoleData) return MeshFilter.sharedMesh.triangles;
            var result = new List<int>();
            foreach (var sub in m_HoleSubMeshes) result.AddRange(sub.triangles);
            return result.ToArray();
        }

        readonly List<int> m_HoleBrushFaces = new List<int>();

        public bool PaintSurfaceHoles(Vector3 worldCenter, float radius, bool restore, bool apply = true)
        {
            InitializeSurfaceHoles();
            EnsureHolePickTree(MeshFilter.sharedMesh);
            Transform surface = MeshFilter.transform;
            Matrix4x4 inverse = surface.worldToLocalMatrix;
            Vector3 extent = new Vector3(
                new Vector3(inverse.m00, inverse.m01, inverse.m02).magnitude,
                new Vector3(inverse.m10, inverse.m11, inverse.m12).magnitude,
                new Vector3(inverse.m20, inverse.m21, inverse.m22).magnitude) * radius;
            m_HolePickTree.Collect(new Bounds(surface.InverseTransformPoint(worldCenter), extent * 2f), m_HoleBrushFaces);
            bool changed = false;
            foreach (int face in m_HoleBrushFaces)
            {
                Vector3 center = surface.TransformPoint(m_HolePickTree.FaceCenter(face));
                if ((center - worldCenter).sqrMagnitude > radius * radius) continue;
                int localFace = face;
                foreach (var sub in m_HoleSubMeshes)
                {
                    if (localFace >= sub.hidden.Length) { localFace -= sub.hidden.Length; continue; }
                    if (sub.hidden[localFace] != !restore)
                    {
                        sub.hidden[localFace] = !restore;
                        changed = true;
                    }
                    break;
                }
            }
            if (changed && apply) ApplySurfaceHoles();
            return changed;
        }

        static (int, int, int) HoleTriangleKey(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return (a, b, c);
        }

        public IEnumerable<Mesh> GetHoleColliderMeshes()
        {
            foreach (var map in m_SurfaceColliderVertexMaps)
            {
                Mesh mesh = map.collider != null && map.collider.sharedMesh != null ? map.collider.sharedMesh : map.holeMesh;
                if (mesh != null) yield return mesh;
            }
        }

        readonly List<int> m_HoleColliderCurrentTriangles = new List<int>();

        public void ApplySurfaceHoles()
        {
            if (!HasHoleData) return;
            Mesh source = MeshFilter.sharedMesh;
            if (source.vertexCount != m_HoleVertexCount || source.subMeshCount != m_HoleSubMeshes.Length) return;
            var hidden = new HashSet<(int, int, int)>();
            for (int s = 0; s < m_HoleSubMeshes.Length; s++)
            {
                var sub = m_HoleSubMeshes[s];
                var visible = new List<int>(sub.triangles.Length);
                for (int t = 0; t < sub.triangles.Length; t += 3)
                {
                    int a = sub.triangles[t], b = sub.triangles[t + 1], c = sub.triangles[t + 2];
                    if (sub.hidden[t / 3]) hidden.Add(HoleTriangleKey(a, b, c));
                    else { visible.Add(a); visible.Add(b); visible.Add(c); }
                }
                source.SetTriangles(visible, s, false);
            }
            // Preserve bounds and vertex attributes, including the surface underneath holes.
            if (MeshCollider != null)
            {
                MeshCollider.sharedMesh = null;
                if (m_SurfaceColliderChunks.Length == 0)
                    for (int s = 0; s < source.subMeshCount; s++)
                        if (source.GetIndexCount(s) > 0) { MeshCollider.sharedMesh = source; break; }
            }
            foreach (var map in m_SurfaceColliderVertexMaps)
            {
                if (map.collider == null || map.holeSourceTriangles == null) continue;
                Mesh mesh = map.collider.sharedMesh != null ? map.collider.sharedMesh : map.holeMesh;
                if (mesh == null) continue;
                map.holeMesh = mesh;
                var visible = new List<int>();
                int[] triangles = map.holeSourceTriangles;
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    if (hidden.Contains(HoleTriangleKey(map.sourceIndices[a], map.sourceIndices[b], map.sourceIndices[c]))) continue;
                    visible.Add(a); visible.Add(b); visible.Add(c);
                }
                // Unchanged chunks need neither a mesh upload nor physics cooking.
                mesh.GetTriangles(m_HoleColliderCurrentTriangles, 0);
                bool differs = visible.Count != m_HoleColliderCurrentTriangles.Count;
                for (int i = 0; !differs && i < visible.Count; i++)
                    differs = visible[i] != m_HoleColliderCurrentTriangles[i];
                // The saved mesh can be current while the collider reference was
                // cleared by a previous hole edit. Restore that reference too.
                if (!differs && ((visible.Count == 0 && map.collider.sharedMesh == null)
                    || (visible.Count > 0 && map.collider.sharedMesh == mesh))) continue;
                if (differs) mesh.SetTriangles(visible, 0, false);
                map.collider.sharedMesh = null;
                if (visible.Count > 0) map.collider.sharedMesh = mesh;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(mesh);
#endif
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(source);
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            // Cutting changes visibility only; retained picking geometry remains valid.
            m_SurfaceTilesDirty = true;
            m_TiledSource = null;
            RefreshSurfaceTiles();
            InvalidateRenderCache();
        }

    }
}

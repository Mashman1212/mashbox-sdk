using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        [NonSerialized] HolePickTree m_HolePickTree;
        [NonSerialized] Mesh m_HolePickMesh;
        [NonSerialized] bool m_HolePickHadData;
        [NonSerialized] bool m_HolePickVerticesDirty;
#if UNITY_EDITOR
        [NonSerialized] HolePickTree m_SculptPickTree;
        [NonSerialized] Mesh m_SculptPickMesh;
        [NonSerialized] bool m_SculptPickVerticesDirty;
        [NonSerialized] int m_SculptPickDirtyCount;

        // Brush queries follow the visible mesh without cooking a physics collider.
        // Refit retained bounds after deformation; rebuild only for topology edits.
        public bool RaycastSculptSurface(Ray ray, out RaycastHit hit, float maximumDistance)
        {
            hit = default;
            var filter = MeshFilter;
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable || !isActiveAndEnabled) return false;
            var inverse = filter.transform.worldToLocalMatrix;
            var localDirection = inverse.MultiplyVector(ray.direction);
            float localUnitsPerWorldUnit = localDirection.magnitude;
            if (localUnitsPerWorldUnit <= 0) return false;
            var localRay = new Ray(inverse.MultiplyPoint3x4(ray.origin), localDirection);
            if (!mesh.bounds.IntersectRay(localRay, out float near) || near > maximumDistance * localUnitsPerWorldUnit) return false;
            int dirtyCount = UnityEditor.EditorUtility.GetDirtyCount(mesh);
            if (m_SculptPickTree == null || m_SculptPickMesh != mesh || m_SculptPickTree.VertexCount != mesh.vertexCount)
            {
                m_SculptPickTree = new HolePickTree(mesh.vertices, mesh.triangles);
                m_SculptPickMesh = mesh;
            }
            else if (m_SculptPickVerticesDirty || dirtyCount != m_SculptPickDirtyCount) m_SculptPickTree.Refit(mesh);
            m_SculptPickVerticesDirty = false;
            m_SculptPickDirtyCount = dirtyCount;
            if (!m_SculptPickTree.Raycast(localRay, out float distance, out var normal)) return false;
            distance /= localUnitsPerWorldUnit;
            if (distance > maximumDistance) return false;
            hit = new RaycastHit { point = ray.GetPoint(distance), normal = inverse.transpose.MultiplyVector(normal).normalized, distance = distance };
            return true;
        }
#endif

        void InvalidateSurfacePicking(bool topologyChanged)
        {
            if (topologyChanged) m_HolePickTree = null;
            m_HolePickVerticesDirty = true;
#if UNITY_EDITOR
            if (topologyChanged) m_SculptPickTree = null;
            m_SculptPickVerticesDirty = true;
#endif
        }

        static readonly Unity.Profiling.ProfilerMarker s_HolePickMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.HolePick");

        static readonly Unity.Profiling.ProfilerMarker s_HolePickBuildMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.HolePick.Build");

        // Retained faces participate in picking even after they have been cut.
        public bool RaycastSurfaceIncludingHoles(Ray worldRay, out Vector3 point, out Vector3 normal)
        {
            using var profile = s_HolePickMarker.Auto();
            point = normal = default;
            MeshFilter filter = MeshFilter;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) return false;
            EnsureHolePickTree(mesh);
            Transform surface = filter.transform;
            // Unity's Ray constructor normalizes direction; convert its local distance back to world units.
            var localDirection = surface.InverseTransformVector(worldRay.direction);
            float localUnitsPerWorldUnit = localDirection.magnitude;
            if (localUnitsPerWorldUnit <= 0) return false;
            Ray localRay = new Ray(surface.InverseTransformPoint(worldRay.origin), localDirection);
            if (!m_HolePickTree.Raycast(localRay, out float distance, out Vector3 localNormal)) return false;
            point = worldRay.GetPoint(distance / localUnitsPerWorldUnit);
            normal = surface.worldToLocalMatrix.transpose.MultiplyVector(localNormal).normalized;
            return true;
        }

        void EnsureHolePickTree(Mesh mesh)
        {
            bool rebuild = m_HolePickTree == null || m_HolePickMesh != mesh || m_HolePickHadData != HasHoleData || m_HolePickTree.VertexCount != mesh.vertexCount;

            if (rebuild)
            {
                using var buildProfile = s_HolePickBuildMarker.Auto();
                m_HolePickTree = new HolePickTree(mesh.vertices, GetSurfaceTrianglesIncludingHoles());
                m_HolePickMesh = mesh;
                m_HolePickHadData = HasHoleData;

            }
            else if (m_HolePickVerticesDirty) m_HolePickTree.Refit(mesh);
            m_HolePickVerticesDirty = false;
        }
        sealed class HolePickTree
        {
            struct Node
            {
                internal Bounds bounds;
                internal int first, count, left, right;
            }
            readonly Vector3[] vertices;
            readonly List<Vector3> readback = new List<Vector3>();
            internal int VertexCount => vertices.Length;
            readonly int[] vertexFaceOffsets, vertexFaces, faceLeaves;
            readonly bool[] dirtyFaces;
            bool[] dirtyNodes;
            internal void Refit(Mesh mesh)
            {
                using var profile = s_HolePickRefitMarker.Auto();
                mesh.GetVertices(readback);
                bool changed = false;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var next = readback[i];
                    if (vertices[i].Equals(next)) continue;
                    vertices[i] = next; changed = true;
                    for (int f = vertexFaceOffsets[i]; f < vertexFaceOffsets[i + 1]; f++) dirtyFaces[vertexFaces[f]] = true;
                }
                if (!changed) return;
                for (int face = 0; face < faceBounds.Length; face++)
                {
                    if (!dirtyFaces[face]) continue;
                    dirtyFaces[face] = false;
                    int t = face * 3;
                    var a = vertices[triangles[t]]; var b = vertices[triangles[t + 1]]; var c = vertices[triangles[t + 2]];
                    var bounds = new Bounds(); bounds.SetMinMax(Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)));
                    faceBounds[face] = bounds;
                    dirtyNodes[faceLeaves[face]] = true;
                }
                for (int i = nodes.Count - 1; i >= 0; i--)
                {
                    var node = nodes[i];
                    if (node.count == 0)
                    {
                        if (!dirtyNodes[node.left] && !dirtyNodes[node.right]) continue;
                        dirtyNodes[i] = true;
                        node.bounds = nodes[node.left].bounds; node.bounds.Encapsulate(nodes[node.right].bounds);
                    }
                    else
                    {
                        if (!dirtyNodes[i]) continue;
                        node.bounds = faceBounds[order[node.first]];
                        for (int f = 1; f < node.count; f++) node.bounds.Encapsulate(faceBounds[order[node.first + f]]);
                    }
                    nodes[i] = node;
                }
                Array.Clear(dirtyNodes, 0, dirtyNodes.Length);
            }
            static readonly Unity.Profiling.ProfilerMarker s_HolePickRefitMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.Picking.Refit");
            readonly int[] triangles, order;
            readonly Bounds[] faceBounds;
            readonly List<Node> nodes;

            internal HolePickTree(Vector3[] vertices, int[] triangles)
            {
                this.vertices = vertices;
                this.triangles = triangles;
                int count = triangles.Length / 3;
                order = new int[count];
                faceBounds = new Bounds[count];
                nodes = new List<Node>(Mathf.Max(1, count / 4));
                for (int i = 0; i < count; i++)
                {
                    order[i] = i;
                    var bounds = new Bounds(vertices[triangles[i * 3]], Vector3.zero);
                    bounds.Encapsulate(vertices[triangles[i * 3 + 1]]);
                    bounds.Encapsulate(vertices[triangles[i * 3 + 2]]);
                    faceBounds[i] = bounds;
                }
                if (count > 0) Build(0, count, 0);
                faceLeaves = new int[count]; dirtyFaces = new bool[count]; dirtyNodes = new bool[nodes.Count];
                for (int n = 0; n < nodes.Count; n++)
                {
                    var node = nodes[n];
                    for (int f = 0; f < node.count; f++) faceLeaves[order[node.first + f]] = n;
                }
                vertexFaceOffsets = new int[vertices.Length + 1];
                foreach (int vertex in triangles) vertexFaceOffsets[vertex + 1]++;
                for (int i = 1; i < vertexFaceOffsets.Length; i++) vertexFaceOffsets[i] += vertexFaceOffsets[i - 1];
                vertexFaces = new int[triangles.Length];
                var nextFace = (int[])vertexFaceOffsets.Clone();
                for (int t = 0; t < triangles.Length; t++) vertexFaces[nextFace[triangles[t]]++] = t / 3;
            }

            int Build(int first, int count, int depth)
            {
                Bounds bounds = faceBounds[order[first]];
                for (int i = first + 1; i < first + count; i++) bounds.Encapsulate(faceBounds[order[i]]);
                int index = nodes.Count;
                var node = new Node { bounds = bounds, first = first, count = count };
                nodes.Add(node);
                if (count <= 8 || depth >= 32) return index;
                Vector3 size = bounds.size;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                float split = bounds.center[axis];
                int middle = first;
                for (int i = first; i < first + count; i++)
                {
                    if (faceBounds[order[i]].center[axis] >= split) continue;
                    (order[middle], order[i]) = (order[i], order[middle]);
                    middle++;
                }
                if (middle == first || middle == first + count) middle = first + count / 2;
                node.left = Build(first, middle - first, depth + 1);
                node.right = Build(middle, first + count - middle, depth + 1);
                node.count = 0;
                nodes[index] = node;
                return index;
            }

            internal Vector3 FaceCenter(int face)
            {
                int t = face * 3;
                return (vertices[triangles[t]] + vertices[triangles[t + 1]] + vertices[triangles[t + 2]]) / 3f;
            }

            internal void Collect(Bounds bounds, List<int> result)
            {
                result.Clear();
                if (nodes.Count > 0) CollectNode(0, bounds, result);
            }

            void CollectNode(int index, Bounds bounds, List<int> result)
            {
                Node node = nodes[index];
                if (!node.bounds.Intersects(bounds)) return;
                if (node.count == 0)
                {
                    CollectNode(node.left, bounds, result);
                    CollectNode(node.right, bounds, result);
                    return;
                }
                for (int i = node.first; i < node.first + node.count; i++)
                    if (faceBounds[order[i]].Intersects(bounds)) result.Add(order[i]);
            }
            internal bool Raycast(Ray ray, out float distance, out Vector3 normal)
            {
                distance = float.PositiveInfinity;
                normal = default;
                if (nodes.Count > 0) Visit(0, ray, ref distance, ref normal);
                return !float.IsPositiveInfinity(distance);
            }

            void Visit(int index, Ray ray, ref float closest, ref Vector3 normal)
            {
                Node node = nodes[index];
                if (!Intersects(node.bounds, ray, closest, out _)) return;
                if (node.count == 0)
                {
                    bool left = Intersects(nodes[node.left].bounds, ray, closest, out float ld);
                    bool right = Intersects(nodes[node.right].bounds, ray, closest, out float rd);
                    if (left && right)
                    {
                        Visit(ld <= rd ? node.left : node.right, ray, ref closest, ref normal);
                        Visit(ld <= rd ? node.right : node.left, ray, ref closest, ref normal);
                    }
                    else if (left) Visit(node.left, ray, ref closest, ref normal);
                    else if (right) Visit(node.right, ray, ref closest, ref normal);
                    return;
                }
                for (int i = node.first; i < node.first + node.count; i++)
                {
                    int t = order[i] * 3;
                    Vector3 a = vertices[triangles[t]], ab = vertices[triangles[t + 1]] - a, ac = vertices[triangles[t + 2]] - a;
                    Vector3 p = Vector3.Cross(ray.direction, ac);
                    float det = Vector3.Dot(ab, p);
                    if (Mathf.Abs(det) < 1e-8f) continue;
                    Vector3 offset = ray.origin - a;
                    float u = Vector3.Dot(offset, p) / det;
                    if (u < 0 || u > 1) continue;
                    Vector3 q = Vector3.Cross(offset, ab);
                    float v = Vector3.Dot(ray.direction, q) / det;
                    if (v < 0 || u + v > 1) continue;
                    float distance = Vector3.Dot(ac, q) / det;
                    if (distance < 0 || distance >= closest) continue;
                    closest = distance;
                    normal = Vector3.Cross(ab, ac);
                }
            }

            static bool Intersects(Bounds bounds, Ray ray, float limit, out float near)
            {
                near = 0;
                Vector3 min = bounds.min, max = bounds.max;
                for (int axis = 0; axis < 3; axis++)
                {
                    float direction = ray.direction[axis], origin = ray.origin[axis];
                    if (direction == 0f)
                    {
                        if (origin < min[axis] || origin > max[axis]) return false;
                        continue;
                    }
                    float a = (min[axis] - origin) / direction, b = (max[axis] - origin) / direction;
                    if (a > b) (a, b) = (b, a);
                    near = Mathf.Max(near, a);
                    limit = Mathf.Min(limit, b);
                    if (near > limit) return false;
                }
                return true;
            }
        }
    }
}

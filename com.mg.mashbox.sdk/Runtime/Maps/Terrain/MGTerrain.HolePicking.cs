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
            // Do not normalize: the ray parameter must remain a world-space distance.
            Ray localRay = new Ray(surface.InverseTransformPoint(worldRay.origin), surface.InverseTransformVector(worldRay.direction));
            if (!m_HolePickTree.Raycast(localRay, out float distance, out Vector3 localNormal)) return false;
            point = worldRay.GetPoint(distance);
            normal = surface.worldToLocalMatrix.transpose.MultiplyVector(localNormal).normalized;
            return true;
        }

        void EnsureHolePickTree(Mesh mesh)
        {
            bool rebuild = m_HolePickTree == null || m_HolePickMesh != mesh || m_HolePickHadData != HasHoleData;

            if (rebuild)
            {
                using var buildProfile = s_HolePickBuildMarker.Auto();
                m_HolePickTree = new HolePickTree(mesh.vertices, GetSurfaceTrianglesIncludingHoles());
                m_HolePickMesh = mesh;
                m_HolePickHadData = HasHoleData;

            }

        }
        sealed class HolePickTree
        {
            struct Node
            {
                internal Bounds bounds;
                internal int first, count, left, right;
            }
            readonly Vector3[] vertices;
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

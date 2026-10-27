using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Collider-free picking in Studio's preview scene. The BVH is built once per mesh,
    // not once per dab; rays retain world-distance parameterization under nonuniform scale.
    internal sealed class StudioPaintingSurface : IDisposable
    {
        internal struct Hit
        {
            internal Renderer renderer;
            internal int slot, triangle;
            internal Vector2 uv;
            internal Vector3 point;
            internal float distance;
            internal bool hasUv;
        }
        private struct Triangle { internal int a, b, c, slot; internal Bounds bounds; }
        private struct Node { internal Bounds bounds; internal int start, count, left, right; }
        private sealed class Part
        {
            internal Renderer renderer;
            internal Mesh baked;
            internal Vector3[] vertices;
            internal readonly List<Vector2>[] uv = { new List<Vector2>(), new List<Vector2>(), new List<Vector2>(), new List<Vector2>() };
            internal Triangle[] triangles;
            internal readonly List<Node> nodes = new List<Node>();
        }
        private readonly List<Part> parts = new List<Part>();

        internal StudioPaintingSurface(IEnumerable<Renderer> renderers)
        {
            try { foreach (var renderer in renderers) Add(renderer); }
            catch { Dispose(); throw; }
        }

        private void Add(Renderer renderer)
        {
            Mesh mesh = null;
            var part = new Part { renderer = renderer };
            if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh)
            {
                part.baked = new Mesh { name = "Painting pick pose", hideFlags = HideFlags.HideAndDontSave };
                parts.Add(part); // Own the bake even if mesh extraction throws.
                skin.BakeMesh(part.baked);
                mesh = part.baked;
            }
            else if (renderer.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
            if (!mesh) return;
            if (!parts.Contains(part)) parts.Add(part);
            part.vertices = mesh.vertices;
            for (int channel = 0; channel < 4; channel++) mesh.GetUVs(channel, part.uv[channel]);
            var triangles = new List<Triangle>();
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                var topology = mesh.GetTopology(slot);
                if (topology != MeshTopology.Triangles && topology != MeshTopology.Quads) continue;
                var indices = mesh.GetIndices(slot, true);
                int stride = topology == MeshTopology.Quads ? 4 : 3;
                for (int i = 0; i + stride - 1 < indices.Length; i += stride)
                {
                    AddTriangle(indices[i], indices[i + 1], indices[i + 2], slot);
                    if (stride == 4) AddTriangle(indices[i], indices[i + 2], indices[i + 3], slot);
                }
            }
            part.triangles = triangles.ToArray();
            if (triangles.Count > 0) Build(part, 0, triangles.Count);

            void AddTriangle(int a, int b, int c, int slot)
            {
                var bounds = new Bounds(part.vertices[a], Vector3.zero);
                bounds.Encapsulate(part.vertices[b]); bounds.Encapsulate(part.vertices[c]);
                bounds.Expand(.000001f);
                triangles.Add(new Triangle { a = a, b = b, c = c, slot = slot, bounds = bounds });
            }
        }

        private static int Build(Part part, int start, int count)
        {
            var bounds = part.triangles[start].bounds;
            for (int i = start + 1; i < start + count; i++) bounds.Encapsulate(part.triangles[i].bounds);
            int index = part.nodes.Count;
            var node = new Node { bounds = bounds, start = start, count = count };
            part.nodes.Add(node);
            if (count > 12)
            {
                int axis = bounds.size.x > bounds.size.y ? 0 : 1;
                if (bounds.size.z > bounds.size[axis]) axis = 2;
                Array.Sort(part.triangles, start, count, Comparer<Triangle>.Create((a, b) => a.bounds.center[axis].CompareTo(b.bounds.center[axis])));
                int half = count / 2;
                node.left = Build(part, start, half); node.right = Build(part, start + half, count - half); node.count = 0;
                part.nodes[index] = node;
            }
            return index;
        }

        internal bool Raycast(Ray worldRay, int channel, out Hit hit)
        {
            hit = new Hit { distance = float.PositiveInfinity };
            foreach (var part in parts)
            {
                if (!part.renderer || !part.renderer.enabled || !part.renderer.gameObject.activeInHierarchy || part.nodes.Count == 0) continue;
                var matrix = part.renderer.localToWorldMatrix.inverse;
                var direction = matrix.MultiplyVector(worldRay.direction);
                float rayScale = direction.magnitude;
                if (rayScale < 1e-12f) continue;
                // Unity's Ray constructor normalizes direction. Convert every local
                // distance back before comparing hits from differently scaled objects.
                var ray = new Ray(matrix.MultiplyPoint3x4(worldRay.origin), direction);
                Traverse(part, 0, ray, worldRay, rayScale, Mathf.Clamp(channel, 0, 3), ref hit);
            }
            return hit.renderer;
        }

        internal bool BrushAxes(Hit hit, int channel, out Vector3 u, out Vector3 v)
        {
            u = v = Vector3.zero;
            foreach (var part in parts)
            {
                if (part.renderer != hit.renderer || !hit.hasUv) continue;
                var triangle = part.triangles[hit.triangle];
                var uv = part.uv[Mathf.Clamp(channel, 0, 3)];
                var ab = uv[triangle.b] - uv[triangle.a]; var ac = uv[triangle.c] - uv[triangle.a];
                float determinant = ab.x * ac.y - ab.y * ac.x;
                if (Mathf.Abs(determinant) < 1e-10f) return false;
                var p = part.vertices[triangle.b] - part.vertices[triangle.a];
                var q = part.vertices[triangle.c] - part.vertices[triangle.a];
                u = part.renderer.localToWorldMatrix.MultiplyVector((p * ac.y - q * ab.y) / determinant);
                v = part.renderer.localToWorldMatrix.MultiplyVector((q * ab.x - p * ac.x) / determinant);
                return true;
            }
            return false;
        }

        private static void Traverse(Part part, int index, Ray ray, Ray worldRay, float rayScale, int channel, ref Hit hit)
        {
            var node = part.nodes[index];
            if (!node.bounds.IntersectRay(ray, out float near) || near / rayScale > hit.distance) return;
            if (node.count == 0)
            {
                Traverse(part, node.left, ray, worldRay, rayScale, channel, ref hit);
                Traverse(part, node.right, ray, worldRay, rayScale, channel, ref hit);
                return;
            }
            for (int i = node.start; i < node.start + node.count; i++)
            {
                var triangle = part.triangles[i];
                if (!Intersect(ray, part.vertices[triangle.a], part.vertices[triangle.b], part.vertices[triangle.c], out float distance, out Vector2 barycentric)) continue;
                distance /= rayScale;
                if (distance >= hit.distance) continue;
                var uv = part.uv[channel];
                bool hasUv = uv.Count == part.vertices.Length;
                hit = new Hit
                {
                    renderer = part.renderer, slot = triangle.slot, triangle = i, distance = distance,
                    point = worldRay.GetPoint(distance), hasUv = hasUv,
                    uv = hasUv ? uv[triangle.a] * (1 - barycentric.x - barycentric.y) + uv[triangle.b] * barycentric.x + uv[triangle.c] * barycentric.y : Vector2.zero
                };
            }
        }

        internal static bool Intersect(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance, out Vector2 barycentric)
        {
            distance = 0; barycentric = default;
            var ab = b - a; var ac = c - a; var p = Vector3.Cross(ray.direction, ac);
            float determinant = Vector3.Dot(ab, p);
            if (Mathf.Abs(determinant) <= 1e-10f) return false;
            float inverse = 1 / determinant;
            var offset = ray.origin - a;
            float u = Vector3.Dot(offset, p) * inverse;
            if (u < 0 || u > 1) return false;
            var q = Vector3.Cross(offset, ab);
            float v = Vector3.Dot(ray.direction, q) * inverse;
            if (v < 0 || u + v > 1) return false;
            distance = Vector3.Dot(ac, q) * inverse;
            barycentric = new Vector2(u, v);
            return distance >= 0;
        }

        public void Dispose()
        {
            foreach (var part in parts) if (part.baked) UnityEngine.Object.DestroyImmediate(part.baked);
            parts.Clear();
        }
    }
}

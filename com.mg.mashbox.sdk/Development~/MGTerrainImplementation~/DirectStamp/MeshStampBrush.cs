using System.Collections.Generic;
using MashBoxSDK.Maps.Sculpting;
using UnityEditor;
using UnityEngine;
using Unity.Collections;

namespace MashBoxSDK.MapTools
{
    // Cache projected triangles in spatial bins. Sampling is exact within each
    // triangle, independent of physics colliders and the source object's placement.
    internal sealed class MeshStampBrush
    {
        const int BinCount = 32;
        readonly Vector3[] vertices;
        readonly int[] triangles;
        readonly List<int>[] bins = new List<int>[BinCount * BinCount];
        readonly Vector3[] previewLine = new Vector3[2];
        public Mesh Source { get; }

        public MeshStampBrush(Mesh source)
        {
            Source = source;
            // Editor snapshots can read imported meshes without changing their
            // Read/Write flag. Keep only the sampling arrays, not native handles.
            using (var snapshot = MeshUtility.AcquireReadOnlyMeshData(source))
            {
                var data = snapshot[0];
                using (var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                {
                    data.GetVertices(positions);
                    vertices = positions.ToArray();
                }
                var indices = new List<int>();
                for (int submesh = 0; submesh < data.subMeshCount; submesh++)
                {
                    var descriptor = data.GetSubMesh(submesh);
                    if (descriptor.topology != MeshTopology.Triangles) continue;
                    using (var buffer = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                    {
                        data.GetIndices(buffer, submesh, true);
                        indices.AddRange(buffer.ToArray());
                    }
                }
                triangles = indices.ToArray();
            }
            if (triangles.Length == 0) throw new System.InvalidOperationException("The stamp mesh has no triangle surfaces.");
            Bounds bounds = source.bounds;
            float radius = Mathf.Max(0.00001f, new Vector2(bounds.extents.x, bounds.extents.z).magnitude);
            float height = Mathf.Max(0.00001f, bounds.size.y);
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                vertices[i] = new Vector3((v.x - bounds.center.x) / radius,
                    (v.y - bounds.min.y) / height, (v.z - bounds.center.z) / radius);
            }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                for (int z = Bin(Mathf.Min(a.z, b.z, c.z)); z <= Bin(Mathf.Max(a.z, b.z, c.z)); z++)
                    for (int x = Bin(Mathf.Min(a.x, b.x, c.x)); x <= Bin(Mathf.Max(a.x, b.x, c.x)); x++)
                    {
                        int index = z * BinCount + x;
                        (bins[index] ??= new List<int>()).Add(i);
                    }
            }
        }

        static int Bin(float coordinate) => Mathf.Clamp((int)((coordinate + 1f) * 0.5f * BinCount), 0, BinCount - 1);

        internal bool TryHeight(float x, float z, out float height)
        {
            return TrySurface(x, z, out height, out _, out _);
        }
        internal bool TrySurface(float x, float z, out float height, out int triangle, out Vector3 barycentric)
        {
            height = 0f; triangle = -1; barycentric = Vector3.zero;
            if (x < -1f || x > 1f || z < -1f || z > 1f) return false;
            var candidates = bins[Bin(z) * BinCount + Bin(x)];
            if (candidates == null) return false;
            bool found = false;
            foreach (int i in candidates)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                float determinant = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(determinant) < 0.00000001f) continue;
                float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / determinant;
                float v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / determinant;
                float w = 1f - u - v;
                if (u < -0.00001f || v < -0.00001f || w < -0.00001f) continue;
                float y = u * a.y + v * b.y + w * c.y;
                if (!found || y > height) { height = y; triangle = i / 3; barycentric = new Vector3(u, v, w); }
                found = true;
            }
            return found;
        }

        public MeshSculptModifier.SeamVertex[] Sample(MeshFilter target, Vector3 center, float radius,
            float height, float rotation, float strength, float falloff)
        {
            var result = new List<MeshSculptModifier.SeamVertex>();
            if (radius <= 0f) return result.ToArray();
            var inverseRotation = Quaternion.Euler(0, -rotation, 0);
            var points = target.sharedMesh.vertices;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 point = inverseRotation * (target.transform.TransformPoint(points[i]) - center) / radius;
                float distance = new Vector2(point.x, point.z).magnitude;
                if (distance >= 1f || !TryHeight(point.x, point.z, out float sample)) continue;
                float delta = sample * height * strength * Mathf.Pow(1f - distance, falloff);
                if (Mathf.Abs(delta) < 0.0000001f) continue;
                result.Add(new MeshSculptModifier.SeamVertex { index = i,
                    delta = target.transform.InverseTransformVector(Vector3.up * delta) });
            }
            return result.ToArray();
        }

        Vector3[] previewLines;
        readonly List<MeshFilter> previewTargets = new List<MeshFilter>();
        readonly List<Mesh> previewMeshes = new List<Mesh>();
        readonly List<Matrix4x4> previewTransforms = new List<Matrix4x4>();
        Vector3 previewCenter;
        float previewRadius, previewHeight, previewRotation, previewStrength, previewFalloff;

        public void InvalidatePreview() => previewLines = null;

        internal Vector3[] BuildPreviewLines(MeshFilter target, Vector3 center, float radius,
            float height, float rotation, float strength, float falloff)
        {
            var changes = Sample(target, center, radius, height, rotation, strength, falloff);
            if (changes.Length == 0) return System.Array.Empty<Vector3>();
            var points = target.sharedMesh.vertices;
            var changed = new bool[points.Length];
            foreach (var change in changes) { points[change.index] += change.delta; changed[change.index] = true; }
            for (int i = 0; i < points.Length; i++) points[i] = target.transform.TransformPoint(points[i]);
            var indices = target.sharedMesh.triangles;
            var edges = new HashSet<ulong>();
            var lines = new List<Vector3>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                if (!changed[a] && !changed[b] && !changed[c]) continue;
                Edge(a, b); Edge(b, c); Edge(c, a);
            }
            return lines.ToArray();

            void Edge(int a, int b)
            {
                ulong key = ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                if (!edges.Add(key)) return;
                lines.Add(points[a]); lines.Add(points[b]);
            }
        }

        public void DrawPreview(List<MeshFilter> targets, Vector3 center, float radius, float height,
            float rotation, float strength, float falloff)
        {
            bool rebuild = previewLines == null || center != previewCenter || radius != previewRadius
                || height != previewHeight || rotation != previewRotation || strength != previewStrength
                || falloff != previewFalloff || targets.Count != previewTargets.Count;
            if (!rebuild)
                for (int i = 0; i < targets.Count; i++)
                    if (targets[i] != previewTargets[i] || targets[i].sharedMesh != previewMeshes[i]
                        || targets[i].transform.localToWorldMatrix != previewTransforms[i]) { rebuild = true; break; }
            if (rebuild)
            {
                var lines = new List<Vector3>();
                previewTargets.Clear(); previewMeshes.Clear(); previewTransforms.Clear();
                foreach (var target in targets)
                {
                    lines.AddRange(BuildPreviewLines(target, center, radius, height, rotation, strength, falloff));
                    previewTargets.Add(target); previewMeshes.Add(target.sharedMesh);
                    previewTransforms.Add(target.transform.localToWorldMatrix);
                }
                previewLines = lines.ToArray();
                previewCenter = center; previewRadius = radius; previewHeight = height;
                previewRotation = rotation; previewStrength = strength; previewFalloff = falloff;
            }
            Color previous = Handles.color;
            var previousDepth = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = strength < 0f ? new Color(1f, .4f, .2f, .65f) : new Color(.1f, .9f, 1f, .65f);
            try { if (previewLines.Length > 0) Handles.DrawLines(previewLines); }
            finally { Handles.color = previous; Handles.zTest = previousDepth; }
        }
    }
}
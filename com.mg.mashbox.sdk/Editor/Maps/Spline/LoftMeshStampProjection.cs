using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    // Project the fully transformed mesh along world Y. Inverse-rotating a loft
    // point and reading local Y would give the wrong footprint when tilted.
    internal sealed class LoftMeshStampProjection
    {
        const int BinCount = 32;
        readonly Mesh source;
        readonly Matrix4x4 matrix;
        readonly bool carve;
        readonly Vector3[] vertices;
        readonly Vector2[] footprint;
        readonly int[] triangles;
        readonly List<int>[] bins = new List<int>[BinCount * BinCount];
        internal Bounds Bounds { get; }

        internal bool Matches(Mesh mesh, Matrix4x4 transform, bool lowerSurface) =>
            source == mesh && matrix == transform && carve == lowerSurface;

        internal LoftMeshStampProjection(Mesh mesh, Matrix4x4 transform, bool lowerSurface)
        {
            source = mesh;
            matrix = transform;
            carve = lowerSurface;
            using (var snapshot = MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                var data = snapshot[0];
                using (var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                {
                    data.GetVertices(positions);
                    vertices = positions.ToArray();
                }
                var indices = new List<int>();
                for (int sub = 0; sub < data.subMeshCount; sub++)
                {
                    var desc = data.GetSubMesh(sub);
                    if (desc.topology != MeshTopology.Triangles) continue;
                    using (var buffer = new NativeArray<int>(desc.indexCount, Allocator.Temp))
                    {
                        data.GetIndices(buffer, sub, true);
                        indices.AddRange(buffer.ToArray());
                    }
                }
                triangles = indices.ToArray();
            }
            if (vertices.Length == 0 || triangles.Length == 0)
                throw new InvalidOperationException("The stamp mesh has no triangle surfaces.");
            footprint = new Vector2[vertices.Length];
            Bounds original = mesh.bounds;
            var bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                footprint[i] = new Vector2((v.x - original.center.x) / original.extents.x,
                    (v.z - original.center.z) / original.extents.z);
                vertices[i] = matrix.MultiplyPoint3x4(v);
                bounds.Encapsulate(vertices[i]);
            }
            Bounds = bounds;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                for (int z = BinZ(Mathf.Min(a.z, b.z, c.z)); z <= BinZ(Mathf.Max(a.z, b.z, c.z)); z++)
                    for (int x = BinX(Mathf.Min(a.x, b.x, c.x)); x <= BinX(Mathf.Max(a.x, b.x, c.x)); x++)
                        (bins[z * BinCount + x] ??= new List<int>()).Add(i);
            }
        }

        int BinX(float x) => Mathf.Clamp((int)((x - Bounds.min.x) / Mathf.Max(.000001f, Bounds.size.x) * BinCount), 0, BinCount - 1);
        int BinZ(float z) => Mathf.Clamp((int)((z - Bounds.min.z) / Mathf.Max(.000001f, Bounds.size.z) * BinCount), 0, BinCount - 1);

        internal bool TryHeight(float x, float z, out float height, out Vector2 localFootprint)
        {
            height = 0;
            localFootprint = Vector2.zero;
            if (x < Bounds.min.x || x > Bounds.max.x || z < Bounds.min.z || z > Bounds.max.z) return false;
            var candidates = bins[BinZ(z) * BinCount + BinX(x)];
            if (candidates == null) return false;
            bool found = false;
            foreach (int triangle in candidates)
            {
                int ia = triangles[triangle], ib = triangles[triangle + 1], ic = triangles[triangle + 2];
                Vector3 a = vertices[ia], b = vertices[ib], c = vertices[ic];
                // Use relative edges for precision when the source has an offset pivot.
                double bx = b.x - a.x, bz = b.z - a.z, cx = c.x - a.x, cz = c.z - a.z;
                double determinant = bx * cz - bz * cx;
                double extent = Math.Max(Math.Max(Math.Abs(bx), Math.Abs(bz)), Math.Max(Math.Abs(cx), Math.Abs(cz)));
                if (Math.Abs(determinant) <= 1e-8 * extent * extent) continue; // Vertical/degenerate face.
                double px = x - a.x, pz = z - a.z;
                float v = (float)((px * cz - pz * cx) / determinant);
                float w = (float)((bx * pz - bz * px) / determinant);
                float u = 1 - v - w;
                if (u < -.00001f || v < -.00001f || w < -.00001f) continue;
                float y = u * a.y + v * b.y + w * c.y;
                // Mirrored carve geometry samples its lower envelope, preserving
                // the former carve result at zero pitch and roll.
                if (!found || (carve ? y < height : y > height))
                {
                    height = y;
                    localFootprint = u * footprint[ia] + v * footprint[ib] + w * footprint[ic];
                }
                found = true;
            }
            return found;
        }
    }
}

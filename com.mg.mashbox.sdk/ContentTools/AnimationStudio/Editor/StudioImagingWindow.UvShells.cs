using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow
    {
        private sealed class UvShellWire
        {
            internal Vector2[] uvs;
            internal int[] indices;
            internal MeshTopology topology;
            internal Vector2[] lines;
            internal Vector3[] guiLines;
        }
        private readonly Dictionary<(Mesh mesh, int subMesh, int channel), UvShellWire> uvShellWires =
            new Dictionary<(Mesh, int, int), UvShellWire>();

        private Vector3[] QuadUvShellLines(Mesh mesh, int subMesh, List<Vector2> uvs, Rect canvas)
        {
            var topology = mesh.GetTopology(subMesh);
            int size = topology == MeshTopology.Quads ? 4 : topology == MeshTopology.Triangles ? 3 : 0;
            if (size == 0) return Array.Empty<Vector3>();
            var indices = mesh.GetIndices(subMesh);
            var key = (mesh, subMesh, (int)uvChannel);
            if (!uvShellWires.TryGetValue(key, out var cache) || cache.topology != topology ||
                !cache.uvs.SequenceEqual(uvs) || !cache.indices.SequenceEqual(indices))
            {
                var hidden = new HashSet<ulong>();
                if (size == 3)
                {
                    // Infer display quads in UV space, preserving split seam vertices
                    // and material boundaries. Source geometry and UVs stay untouched.
                    var projected = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = mesh.indexFormat };
                    try
                    {
                        projected.vertices = uvs.Select(uv => new Vector3(uv.x, uv.y, 0)).ToArray();
                        projected.SetIndices(indices, MeshTopology.Triangles, 0);
                        hidden = StudioQuadWire.Diagonals(projected);
                    }
                    finally { DestroyImmediate(projected); }
                }
                var used = new HashSet<ulong>(); var lines = new List<Vector2>();
                for (int i = 0; i + size <= indices.Length; i += size)
                    for (int edge = 0; edge < size; edge++)
                    {
                        int a = indices[i + edge], b = indices[i + (edge + 1) % size];
                        ulong id = ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                        if (a == b || hidden.Contains(id) || !used.Add(id)) continue;
                        lines.Add(uvs[a]); lines.Add(uvs[b]);
                    }
                cache = new UvShellWire { uvs = uvs.ToArray(), indices = indices, topology = topology,
                    lines = lines.ToArray(), guiLines = new Vector3[lines.Count] };
                // Bound the cache while browsing many objects or channels.
                if (uvShellWires.Count >= 32) uvShellWires.Clear();
                uvShellWires[key] = cache;
            }
            for (int i = 0; i < cache.lines.Length; i++) cache.guiLines[i] = UvToGui(cache.lines[i], canvas);
            return cache.guiLines;
        }
    }
}

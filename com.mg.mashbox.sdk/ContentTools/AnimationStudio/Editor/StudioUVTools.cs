using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioUVTools
    {
        internal static Rect Bounds(IList<Vector2> uv, IEnumerable<int> indices)
        {
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
            foreach (int i in indices) { min = Vector2.Min(min, uv[i]); max = Vector2.Max(max, uv[i]); }
            return float.IsInfinity(min.x) ? new Rect() : Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        internal static HashSet<int>[] Adjacency(Mesh mesh, IList<Vector2> uv)
        {
            var adjacent = Enumerable.Range(0, uv.Count).Select(i => new HashSet<int>()).ToArray();
            var triangles = mesh.triangles; var vertices = mesh.vertices;
            for (int i = 0; i < triangles.Length; i += 3)
                for (int j = 0; j < 3; j++) { int a = triangles[i + j], b = triangles[i + (j + 1) % 3]; adjacent[a].Add(b); adjacent[b].Add(a); }
            foreach (var group in Enumerable.Range(0, uv.Count).GroupBy(i => (vertices[i], uv[i])))
            {
                int first = group.First();
                foreach (int i in group) if (i != first) { adjacent[first].Add(i); adjacent[i].Add(first); }
            }
            return adjacent;
        }
        internal static List<List<int>> Shells(Mesh mesh, IList<Vector2> uv)
        {
            var shells = new List<List<int>>(); if (uv.Count != mesh.vertexCount) return shells;
            var adjacent = Adjacency(mesh, uv); var visited = new bool[uv.Count];
            for (int i = 0; i < uv.Count; i++)
            {
                if (visited[i]) continue;
                var shell = new List<int>(); var queue = new Queue<int>(); queue.Enqueue(i); visited[i] = true;
                while (queue.Count > 0) { int v = queue.Dequeue(); shell.Add(v); foreach (int n in adjacent[v]) if (!visited[n]) { visited[n] = true; queue.Enqueue(n); } }
                shells.Add(shell);
            }
            return shells;
        }
        internal static HashSet<int> Border(Mesh mesh, IList<Vector2> uv)
        {
            var vertices = mesh.vertices; var canonical = new int[uv.Count];
            foreach (var group in Enumerable.Range(0, uv.Count).GroupBy(i => (vertices[i], uv[i])))
                foreach (int i in group) canonical[i] = group.First();
            var edges = new Dictionary<(int, int), int>(); var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
                for (int j = 0; j < 3; j++)
                {
                    int a = canonical[triangles[i + j]], b = canonical[triangles[i + (j + 1) % 3]];
                    var key = (Math.Min(a, b), Math.Max(a, b)); edges.TryGetValue(key, out int count); edges[key] = count + 1;
                }
            var border = new HashSet<int>();
            foreach (var edge in edges) if (edge.Value == 1) { border.Add(edge.Key.Item1); border.Add(edge.Key.Item2); }
            return new HashSet<int>(Enumerable.Range(0, uv.Count).Where(i => border.Contains(canonical[i])));
        }
        internal static void Pack(List<Vector2> uv, List<List<int>> shells, float padding)
        {
            if (shells.Count == 0) return;
            padding = Mathf.Clamp(padding, 0, .2f);
            int columns = Mathf.CeilToInt(Mathf.Sqrt(shells.Count)), rows = Mathf.CeilToInt(shells.Count / (float)columns);
            var bounds = shells.Select(s => Bounds(uv, s)).ToArray();
            float cellWidth = 1f / columns, cellHeight = 1f / rows;
            // Padding is proportional to each cell so many shells cannot produce negative usable area.
            float factor = Mathf.Min(cellWidth * (1 - 2 * padding) / Mathf.Max(.00001f, bounds.Max(b => b.width)), cellHeight * (1 - 2 * padding) / Mathf.Max(.00001f, bounds.Max(b => b.height)));
            for (int n = 0; n < shells.Count; n++)
            {
                var center = new Vector2((n % columns + .5f) * cellWidth, (n / columns + .5f) * cellHeight);
                foreach (int i in shells[n]) uv[i] = (uv[i] - bounds[n].center) * factor + center;
            }
        }
        internal static void Project(Mesh mesh, List<Vector2> uv, int[] indices, int kind, int axis)
        {
            var vertices = mesh.vertices; if (indices.Length == 0) return;
            var bounds = new UnityEngine.Bounds(vertices[indices[0]], Vector3.zero);
            foreach (int i in indices) bounds.Encapsulate(vertices[i]);
            int u = axis == 0 ? 2 : 0, v = axis == 1 ? 2 : 1;
            foreach (int i in indices)
            {
                var p = vertices[i]; var d = p - bounds.center;
                if (kind == 0) uv[i] = new Vector2(Mathf.InverseLerp(bounds.min[u], bounds.max[u], p[u]), Mathf.InverseLerp(bounds.min[v], bounds.max[v], p[v]));
                else
                {
                    int a = (axis + 1) % 3, b = (axis + 2) % 3;
                    float longitude = Mathf.Atan2(d[b], d[a]) / (2 * Mathf.PI) + .5f;
                    float latitude = kind == 1 ? Mathf.InverseLerp(bounds.min[axis], bounds.max[axis], p[axis]) : .5f + Mathf.Asin(d.magnitude > 1e-8f ? Mathf.Clamp(d[axis] / d.magnitude, -1, 1) : 0) / Mathf.PI;
                    uv[i] = new Vector2(longitude, latitude);
                }
            }
        }
        internal static void Sew(Mesh mesh, List<Vector2> uv, IEnumerable<int> selected, float tolerance)
        {
            var indices = selected.ToArray(); var vertices = mesh.vertices;
            var parents = Enumerable.Range(0, indices.Length).ToArray();
            int Root(int i) { while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; } return i; }
            float cell = Mathf.Max(tolerance, 1e-7f);
            var buckets = new Dictionary<Vector3Int, List<int>>();
            for (int n = 0; n < indices.Length; n++)
            {
                var p = vertices[indices[n]]; var key = Vector3Int.FloorToInt(p / cell);
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    if (buckets.TryGetValue(key + new Vector3Int(x, y, z), out var neighbors))
                        foreach (int m in neighbors) if ((p - vertices[indices[m]]).sqrMagnitude <= tolerance * tolerance) parents[Root(n)] = Root(m);
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>(); list.Add(n);
            }
            foreach (var group in Enumerable.Range(0, indices.Length).GroupBy(Root))
            {
                Vector2 average = Vector2.zero; int count = 0;
                foreach (int n in group) { average += uv[indices[n]]; count++; }
                average /= count; foreach (int n in group) uv[indices[n]] = average;
            }
        }
        internal static void Relax(Mesh mesh, List<Vector2> uv, HashSet<int> selection, HashSet<int> pins, int iterations, bool preserveBorder)
        {
            var vertices = mesh.vertices;
            // Treat normal-split copies as a single UV point to avoid opening cracks while smoothing.
            var groups = Enumerable.Range(0, uv.Count).GroupBy(i => (vertices[i], uv[i])).Select(g => g.ToArray()).ToArray();
            var groupOf = new int[uv.Count]; for (int g = 0; g < groups.Length; g++) foreach (int i in groups[g]) groupOf[i] = g;
            var neighbors = groups.Select(g => new HashSet<int>()).ToArray(); var tr = mesh.triangles;
            for (int n = 0; n < tr.Length; n += 3) for (int j = 0; j < 3; j++)
            { int a = groupOf[tr[n + j]], b = groupOf[tr[n + (j + 1) % 3]]; if (a != b) { neighbors[a].Add(b); neighbors[b].Add(a); } }
            var fixedUVs = new HashSet<int>(pins); if (preserveBorder) fixedUVs.UnionWith(Border(mesh, uv));
            var movable = groups.Select(g => g.All(selection.Contains) && !g.Any(fixedUVs.Contains)).ToArray();
            var positions = groups.Select(g => uv[g[0]]).ToArray();
            for (int n = 0; n < iterations; n++)
            {
                var next = (Vector2[])positions.Clone();
                for (int g = 0; g < groups.Length; g++) if (movable[g] && neighbors[g].Count > 0)
                    next[g] = Vector2.Lerp(positions[g], neighbors[g].Aggregate(Vector2.zero, (sum, i) => sum + positions[i]) / neighbors[g].Count, .5f);
                positions = next;
            }
            for (int g = 0; g < groups.Length; g++) foreach (int i in groups[g]) uv[i] = positions[g];
        }
        // UV seams require separate vertices in Unity. Rebuild only on a temporary mesh,
        // then copy after every stream has been remapped successfully so failures are atomic.
        internal static void ApplyCornerUVs(Mesh mesh, int channel, Vector2[] corners)
        {
            var triangles = mesh.triangles;
            if (corners == null || corners.Length != triangles.Length || corners.Any(v => float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.x) || float.IsInfinity(v.y)))
                throw new InvalidOperationException("Unwrap did not produce valid UV coordinates.");
            if (channel < 0 || channel > 7) throw new ArgumentOutOfRangeException(nameof(channel));
            for (int s = 0; s < mesh.subMeshCount; s++) if (mesh.GetTopology(s) != MeshTopology.Triangles) throw new InvalidOperationException("UV unwrapping requires triangle meshes.");
            var remap = new List<int>(); var values = new List<Vector2>(); var rebuiltIndices = new int[triangles.Length];
            var lookup = new Dictionary<(int, Vector2), int>();
            for (int i = 0; i < triangles.Length; i++)
            {
                var key = (triangles[i], corners[i]);
                if (!lookup.TryGetValue(key, out int index)) { index = remap.Count; lookup[key] = index; remap.Add(triangles[i]); values.Add(corners[i]); }
                rebuiltIndices[i] = index;
            }
            // Keep unused source vertices, including their blend-shape/skin data.
            var used = new HashSet<int>(remap); var oldUV = new List<Vector2>(); mesh.GetUVs(channel, oldUV);
            for (int i = 0; i < mesh.vertexCount; i++) if (!used.Contains(i)) { remap.Add(i); values.Add(oldUV.Count == mesh.vertexCount ? oldUV[i] : Vector2.zero); }
            var rebuilt = new Mesh { name = mesh.name, indexFormat = remap.Count > 65535 ? IndexFormat.UInt32 : mesh.indexFormat };
            try
            {
                T[] Map<T>(T[] data) => remap.Select(i => data[i]).ToArray();
                rebuilt.vertices = Map(mesh.vertices);
                if (mesh.normals.Length > 0) rebuilt.normals = Map(mesh.normals);
                if (mesh.tangents.Length > 0) rebuilt.tangents = Map(mesh.tangents);
                if (mesh.colors.Length > 0) rebuilt.colors = Map(mesh.colors);
                for (int c = 0; c < 8; c++)
                {
                    if (c == channel) { rebuilt.SetUVs(c, values); continue; }
                    var attr = (VertexAttribute)((int)VertexAttribute.TexCoord0 + c);
                    if (!mesh.HasVertexAttribute(attr)) continue;
                    int dimension = mesh.GetVertexAttributeDimension(attr);
                    if (dimension <= 2) { var data = new List<Vector2>(); mesh.GetUVs(c, data); rebuilt.SetUVs(c, remap.Select(i => data[i]).ToList()); }
                    else if (dimension == 3) { var data = new List<Vector3>(); mesh.GetUVs(c, data); rebuilt.SetUVs(c, remap.Select(i => data[i]).ToList()); }
                    else { var data = new List<Vector4>(); mesh.GetUVs(c, data); rebuilt.SetUVs(c, remap.Select(i => data[i]).ToList()); }
                }
                rebuilt.bindposes = mesh.bindposes;
                using (var counts = mesh.GetBonesPerVertex())
                using (var weights = mesh.GetAllBoneWeights())
                {
                    if (weights.Length > 0)
                    {
                        var starts = new int[counts.Length]; int offset = 0;
                        for (int i = 0; i < counts.Length; i++) { starts[i] = offset; offset += counts[i]; }
                        var newCounts = remap.Select(i => counts[i]).ToArray(); var newWeights = new List<BoneWeight1>();
                        foreach (int i in remap) for (int n = 0; n < counts[i]; n++) newWeights.Add(weights[starts[i] + n]);
                        using (var mappedCounts = new NativeArray<byte>(newCounts, Allocator.Temp))
                        using (var mappedWeights = new NativeArray<BoneWeight1>(newWeights.ToArray(), Allocator.Temp)) rebuilt.SetBoneWeights(mappedCounts, mappedWeights);
                    }
                }
                rebuilt.subMeshCount = mesh.subMeshCount; int cursor = 0;
                for (int s = 0; s < mesh.subMeshCount; s++) { int count = mesh.GetTriangles(s).Length; rebuilt.SetTriangles(rebuiltIndices.Skip(cursor).Take(count).ToArray(), s, false); cursor += count; }
                for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                    for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                    {
                        var dv = new Vector3[mesh.vertexCount]; var dn = new Vector3[mesh.vertexCount]; var dt = new Vector3[mesh.vertexCount];
                        mesh.GetBlendShapeFrameVertices(shape, frame, dv, dn, dt);
                        rebuilt.AddBlendShapeFrame(mesh.GetBlendShapeName(shape), mesh.GetBlendShapeFrameWeight(shape, frame), Map(dv), Map(dn), Map(dt));
                    }
                rebuilt.bounds = mesh.bounds;
                if (channel == 0 && rebuilt.normals.Length > 0) rebuilt.RecalculateTangents();
                Undo.RegisterCompleteObjectUndo(mesh, "Unwrap UVs"); EditorUtility.CopySerialized(rebuilt, mesh); EditorUtility.SetDirty(mesh);
            }
            finally { UnityEngine.Object.DestroyImmediate(rebuilt); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        internal sealed class ExtrusionResult
        {
            internal int[] Faces;
            internal (int a, int b)[] Edges;
            internal int[] MovingVertices;
            internal Vector3[] SourceVertices;
        }

        // Work in temporary lists and validate before touching the owned mesh.
        // Render-vertex splits retain their attributes; coincident seams share displacement.
        internal ExtrusionResult Extrude(StudioModelDocument.Node node, int[] selectedFaces,
            (int a, int b)[] selectedEdges, float distance, bool together, bool keepSeams,
            Func<Vector3, Vector3> transform = null)
        {
            var mesh = node.mesh;
            if (node.skinned || node.blendShape != null || mesh.blendShapeCount > 0 || mesh.boneWeights.Length > 0)
                throw new InvalidOperationException("Bake the blend pose or use a static mesh before extruding.");
            if (float.IsNaN(distance) || float.IsInfinity(distance) || Mathf.Abs(distance) < .00001f)
                throw new InvalidOperationException("Extrusion distance must be finite and non-zero.");
            bool faceMode = selectedFaces != null;
            var selected = new HashSet<int>(selectedFaces ?? Array.Empty<int>());
            if (faceMode && (selected.Count == 0 || selected.Any(f => f < 0 || f >= Faces.Count)))
                throw new InvalidOperationException("Select one or more faces to extrude.");
            if (!faceMode && (selectedEdges == null || selectedEdges.Length == 0))
                throw new InvalidOperationException("Select one or more boundary edges to extrude.");

            int Canonical(int vertex) => keepSeams ? welded[vertex] : vertex;
            (int, int) EdgeKey(int a, int b) => Canonical(a) < Canonical(b) ? (Canonical(a), Canonical(b)) : (Canonical(b), Canonical(a));
            var uses = new Dictionary<(int, int), List<(int face, int a, int b)>>();
            var faceNormals = new Vector3[Faces.Count];
            for (int f = 0; f < Faces.Count; f++)
            {
                var v = Faces[f].Vertices; var normal = Vector3.zero;
                for (int e = 0; e < v.Count; e++)
                {
                    int a = v[e], b = v[(e + 1) % v.Count];
                    normal += Vector3.Cross(Positions[a] - Positions[v[0]], Positions[b] - Positions[v[0]]);
                    var key = EdgeKey(a, b);
                    if (!uses.TryGetValue(key, out var list)) uses[key] = list = new List<(int, int, int)>();
                    list.Add((f, a, b));
                }
                faceNormals[f] = normal.normalized;
            }
            var boundary = new List<(int face, int a, int b)>();
            if (faceMode)
            {
                foreach (int f in selected.OrderBy(f => f))
                {
                    if (faceNormals[f].sqrMagnitude < .5f) throw new InvalidOperationException("Cannot extrude a zero-area face.");
                    var v = Faces[f].Vertices;
                    for (int e = 0; e < v.Count; e++)
                    {
                        int a = v[e], b = v[(e + 1) % v.Count]; var list = uses[EdgeKey(a, b)];
                        if (list.Count > 2) throw new InvalidOperationException("Extrusion requires manifold edges. Split or repair the selected region first.");
                        if (!together || list.Count(u => selected.Contains(u.face)) == 1) boundary.Add((f, a, b));
                        else if (list.Count == 2 && Canonical(list[0].a) == Canonical(list[1].a))
                            throw new InvalidOperationException("Selected faces have inconsistent winding. Correct their normals first.");
                    }
                }
            }
            else
            {
                var seen = new HashSet<(int, int)>();
                foreach (var edge in selectedEdges)
                {
                    if (edge.a < 0 || edge.b < 0 || edge.a >= Positions.Length || edge.b >= Positions.Length)
                        throw new InvalidOperationException("The edge selection is out of date. Select the edges again.");
                    var key = EdgeKey(edge.a, edge.b);
                    if (!seen.Add(key)) continue;
                    if (!uses.TryGetValue(key, out var list) || list.Count != 1)
                        throw new InvalidOperationException("Edge extrusion needs open boundary edges; an interior edge would create a non-manifold surface.");
                    if (faceNormals[list[0].face].sqrMagnitude < .5f) throw new InvalidOperationException("Cannot extrude an edge of a zero-area face.");
                    boundary.Add(list[0]);
                }
            }

            int Group(int f) => faceMode && !together ? f + 1 : 0;
            var directions = new Dictionary<(int group, int vertex), Vector3>();
            var movingFaces = faceMode ? selected : new HashSet<int>(boundary.Select(e => e.face));
            foreach (int f in movingFaces)
                foreach (int v in Faces[f].Vertices.Select(Canonical).Distinct())
                {
                    var key = (Group(f), v); directions.TryGetValue(key, out var sum);
                    directions[key] = sum + faceNormals[f];
                }
            var positions = Positions.ToList(); var colors = mesh.colors.ToList();
            var sources = Positions.ToList(); var moving = new HashSet<int>();
            var uvs = new List<Vector4>[8]; var dimensions = new int[8];
            for (int c = 0; c < 8; c++)
            {
                uvs[c] = new List<Vector4>(); mesh.GetUVs(c, uvs[c]);
                dimensions[c] = mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + c);
            }
            // New walls always receive usable rectangular UV0; all other data is copied.
            if (uvs[0].Count == 0) { uvs[0].AddRange(Enumerable.Repeat(Vector4.zero, positions.Count)); dimensions[0] = 2; }
            int Copy(int source, Vector3 position, Vector2? wallUV = null)
            {
                int id = positions.Count; positions.Add(position);
                sources.Add(Positions[source]);
                if (colors.Count > 0) colors.Add(colors[source]);
                for (int c = 0; c < 8; c++) if (uvs[c].Count > 0)
                {
                    var uv = uvs[c][source];
                    if (c == 0 && wallUV.HasValue) { uv.x = wallUV.Value.x; uv.y = wallUV.Value.y; }
                    uvs[c].Add(uv);
                }
                return id;
            }
            var top = new Dictionary<(int group, int vertex), int>();
            int Top(int f, int v)
            {
                var key = (Group(f), v);
                if (top.TryGetValue(key, out int existing)) return existing;
                var normal = directions[(Group(f), Canonical(v))].normalized;
                if (normal.sqrMagnitude < .5f) throw new InvalidOperationException("The selected normals cancel out. Extrude the faces individually.");
                int id = Copy(v, Positions[v] + normal * distance);
                moving.Add(id);
                return top[key] = id;
            }
            var result = Faces.Select((f, index) => new Face { Submesh = f.Submesh,
                Vertices = faceMode && selected.Contains(index) ? f.Vertices.Select(v => Top(index, v)).ToList() : new List<int>(f.Vertices) }).ToList();
            var rim = new List<(int a, int b)>();
            foreach (var edge in boundary)
            {
                int a = edge.a, b = edge.b, ta = Top(edge.face, a), tb = Top(edge.face, b);
                // Opposite winding at the original edge for extending an open surface.
                if (!faceMode) { int swap = a; a = b; b = swap; swap = ta; ta = tb; tb = swap; }
                float length = Vector3.Distance(Positions[a], Positions[b]);
                if (length < .00001f) throw new InvalidOperationException("Cannot extrude a zero-length edge.");
                var wall = new List<int> {
                    Copy(a, Positions[a], new Vector2(0, 0)), Copy(b, Positions[b], new Vector2(length, 0)),
                    Copy(b, positions[tb], new Vector2(length, Mathf.Abs(distance))), Copy(a, positions[ta], new Vector2(0, Mathf.Abs(distance))) };
                result.Add(new Face { Submesh = Faces[edge.face].Submesh, Vertices = wall });
                moving.Add(wall[2]); moving.Add(wall[3]);
                if (!faceMode) rim.Add((wall[3], wall[2]));
            }
            // Remove unreferenced old interior vertices and temporary edge tips.
            var used = result.SelectMany(f => f.Vertices).Distinct().OrderBy(i => i).ToArray();
            var remap = new Dictionary<int, int>(); for (int i = 0; i < used.Length; i++) remap[used[i]] = i;
            var compact = used.Select(i => positions[i]).ToList();
            foreach (var f in result) f.Vertices = f.Vertices.Select(i => remap[i]).ToList();
            var indices = Enumerable.Range(0, mesh.subMeshCount).Select(_ => new List<int>()).ToArray();
            foreach (var f in result) Triangulate(f.Vertices, compact, indices[f.Submesh]);
            // Build connectivity from a valid normal extrusion, then place only its
            // new tips/caps using the handle's world/object-space transformation.
            if (transform != null) foreach (int id in used.Where(moving.Contains))
            {
                var point = transform(sources[id]);
                if (float.IsNaN(point.sqrMagnitude) || float.IsInfinity(point.sqrMagnitude))
                    throw new InvalidOperationException("The extrusion would create invalid vertex positions.");
                compact[remap[id]] = point;
            }
            mesh.Clear(); mesh.indexFormat = compact.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(compact);
            if (colors.Count > 0) mesh.SetColors(used.Select(i => colors[i]).ToList());
            for (int c = 0; c < 8; c++) if (uvs[c].Count > 0)
            {
                var data = used.Select(i => uvs[c][i]);
                if (dimensions[c] == 2) mesh.SetUVs(c, data.Select(v => new Vector2(v.x, v.y)).ToList());
                else if (dimensions[c] == 3) mesh.SetUVs(c, data.Select(v => new Vector3(v.x, v.y, v.z)).ToList());
                else mesh.SetUVs(c, data.ToList());
            }
            mesh.subMeshCount = indices.Length;
            for (int s = 0; s < indices.Length; s++) mesh.SetTriangles(indices[s], s);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); mesh.UploadMeshData(false);
            node.cutPolygons = result.SelectMany(f => new[] { f.Submesh, f.Vertices.Count }.Concat(f.Vertices)).ToArray();
            node.cutTriangles = mesh.triangles;
            return new ExtrusionResult { Faces = selected.OrderBy(f => f).ToArray(), Edges = rim.Select(e => (remap[e.a], remap[e.b])).ToArray(),
                MovingVertices = used.Where(moving.Contains).Select(i => remap[i]).ToArray(),
                SourceVertices = used.Select(i => sources[i]).ToArray() };
        }
    }
}

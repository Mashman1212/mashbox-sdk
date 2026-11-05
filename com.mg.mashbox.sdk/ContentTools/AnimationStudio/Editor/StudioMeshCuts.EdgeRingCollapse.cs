using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // IDs belong to this immutable authored-edge table, not render diagonals.
        internal sealed class EdgeRingTopology
        {
            internal readonly List<(int a, int b)> Edges = new List<(int, int)>();
            internal readonly Dictionary<(int, int), int> Index = new Dictionary<(int, int), int>();
            private readonly List<List<(int face, int side)>> uses = new List<List<(int, int)>>();
            private readonly int[][] sides;

            internal EdgeRingTopology(StudioMeshCuts source)
            {
                sides = new int[source.Faces.Count][];
                for (int f = 0; f < sides.Length; f++)
                {
                    var vertices = source.Faces[f].Vertices;
                    sides[f] = new int[vertices.Count];
                    for (int i = 0; i < vertices.Count; i++)
                    {
                        int a = vertices[i], b = vertices[(i + 1) % vertices.Count];
                        var key = source.Key(a, b);
                        if (!Index.TryGetValue(key, out int edge))
                        {
                            Index.Add(key, edge = Edges.Count);
                            Edges.Add((a, b)); uses.Add(new List<(int, int)>());
                        }
                        sides[f][i] = edge; uses[edge].Add((f, i));
                    }
                }
            }

            // Phase 1: walk both adjacent directions, using only (i + 2) % 4.
            internal int[] Collect(int startingEdgeId)
            {
                if (startingEdgeId < 0 || startingEdgeId >= Edges.Count)
                    throw new InvalidOperationException("Select a valid authored edge to collapse its ring.");
                var result = new List<int> { startingEdgeId };
                var visited = new HashSet<int>(result);
                void Validate(int edge)
                {
                    if (uses[edge].Count > 2)
                        throw new InvalidOperationException("Collapse Edge Ring cannot cross non-manifold edges.");
                }
                void Walk(int face, int entered)
                {
                    while (sides[face].Length == 4)
                    {
                        int i = Array.IndexOf(sides[face], entered);
                        int opposite = sides[face][(i + 2) % 4];
                        Validate(opposite);
                        if (!visited.Add(opposite)) return;
                        result.Add(opposite);
                        if (uses[opposite].Count != 2) return;
                        var next = uses[opposite][0].face == face ? uses[opposite][1] : uses[opposite][0];
                        face = next.face; entered = opposite;
                    }
                }
                Validate(startingEdgeId);
                foreach (var use in uses[startingEdgeId]) Walk(use.face, startingEdgeId);
                return result.ToArray();
            }
        }

        internal EdgeRingTopology CaptureEdgeRings() => new EdgeRingTopology(this);

        private sealed class RingFusion
        {
            internal Vector3[] Positions;
            internal int[] Geometry, Render;
            internal bool[] Affected;
        }

        // Phase 2: fuse independent endpoint pairs at their captured midpoints.
        // Separate render corners may remain for UV/color/hard-normal seams;
        // every corner nevertheless receives the same fused geometric identity.
        private RingFusion FuseRingMidpoints(StudioModelDocument.Node node, EdgeRingTopology topology, IEnumerable<int> edges)
        {
            var result = new RingFusion { Positions = (Vector3[])Positions.Clone(), Geometry = (int[])welded.Clone(),
                Render = Enumerable.Range(0, Positions.Length).ToArray(), Affected = new bool[Positions.Length] };
            var destination = new Dictionary<int, (int root, Vector3 midpoint)>();
            foreach (int id in edges)
            {
                var edge = topology.Edges[id]; int a = welded[edge.a], b = welded[edge.b];
                if (a == b || destination.ContainsKey(a) || destination.ContainsKey(b))
                    throw new InvalidOperationException("The collected rings share endpoints. Select non-intersecting quad rings.");
                // Halve before adding to avoid overflow for large finite coordinates.
                var midpoint = Positions[edge.a] * .5f + Positions[edge.b] * .5f;
                if (!BevelFinite(midpoint)) throw new InvalidOperationException("Edge collapse requires finite endpoints.");
                destination.Add(a, (a, midpoint)); destination.Add(b, (a, midpoint));
            }
            var source = BevelSource(node.mesh);
            bool grouped = node.normalGroups != null && node.normalGroups.Length == Positions.Length;
            int AttributeHash(int v)
            {
                unchecked
                {
                    int h = source.Normals.Length > 0 ? source.Normals[v].GetHashCode() : 0;
                    if (grouped) h = h * 31 + node.normalGroups[v];
                    if (source.Tangents.Length > 0) h = h * 31 + source.Tangents[v].GetHashCode();
                    if (source.Colors.Length > 0) h = h * 31 + source.Colors[v].GetHashCode();
                    for (int c = 0; c < 8; c++) if (source.UV[c] != null) h = h * 31 + source.UV[c][v].GetHashCode();
                    return h;
                }
            }
            bool SameAttributes(int a, int b)
            {
                if (grouped && node.normalGroups[a] != node.normalGroups[b]) return false;
                if (source.Normals.Length > 0 && !source.Normals[a].Equals(source.Normals[b])) return false;
                if (source.Tangents.Length > 0 && !source.Tangents[a].Equals(source.Tangents[b])) return false;
                if (source.Colors.Length > 0 && !source.Colors[a].Equals(source.Colors[b])) return false;
                for (int c = 0; c < 8; c++) if (source.UV[c] != null && !source.UV[c][a].Equals(source.UV[c][b])) return false;
                return true;
            }
            var variants = new Dictionary<(int root, int hash), List<int>>();
            // Visit A before B so compatible B pointers always map to A.
            foreach (int v in Enumerable.Range(0, Positions.Length).OrderBy(v => destination.TryGetValue(welded[v], out var d) && d.root != welded[v] ? 1 : 0))
            {
                if (!destination.TryGetValue(welded[v], out var target)) continue;
                if (!BevelFinite(Positions[v])) throw new InvalidOperationException("Edge collapse requires finite vertices.");
                result.Positions[v] = target.midpoint; result.Geometry[v] = target.root; result.Affected[v] = true;
                var key = (target.root, AttributeHash(v));
                if (!variants.TryGetValue(key, out var candidates)) variants.Add(key, candidates = new List<int>());
                int match = candidates.FindIndex(other => SameAttributes(v, other));
                if (match >= 0) result.Render[v] = candidates[match]; else candidates.Add(v);
            }
            return result;
        }

        // Phase 3: delete whole crushed authored faces, retain source tessellation,
        // and compact all streams together. Never turn a crushed quad into a triangle.
        private int[] PurgeCollapsedRing(StudioModelDocument.Node node, RingFusion fusion)
        {
            var mesh = node.mesh; var source = BevelSource(mesh); var captured = BevelSourceTriangles(mesh);
            var faces = new List<Face>();
            var triangles = Enumerable.Range(0, mesh.subMeshCount).Select(_ => new List<int>()).ToArray();
            for (int f = 0; f < Faces.Count; f++)
            {
                var face = Faces[f]; var vertices = face.Vertices;
                if (vertices.Count < 3 || vertices.Select(v => fusion.Geometry[v]).Distinct().Count() != vertices.Count) continue;
                faces.Add(new Face { Submesh = face.Submesh, Vertices = vertices.Select(v => fusion.Render[v]).ToList() });
                var input = captured[f]; var output = triangles[face.Submesh];
                for (int t = 0; t < input.Count; t += 3)
                {
                    int a = fusion.Render[input[t]], b = fusion.Render[input[t + 1]], c = fusion.Render[input[t + 2]];
                    if (fusion.Geometry[a] == fusion.Geometry[b] || fusion.Geometry[b] == fusion.Geometry[c] || fusion.Geometry[c] == fusion.Geometry[a]) continue;
                    output.Add(a); output.Add(b); output.Add(c);
                }
            }
            var used = new HashSet<int>(faces.SelectMany(f => f.Vertices).Concat(triangles.SelectMany(t => t)));
            var compact = used.OrderBy(v => v).ToArray(); var remap = new int[Positions.Length];
            for (int i = 0; i < compact.Length; i++) remap[compact[i]] = i;
            var groups = node.normalGroups;
            var format = mesh.indexFormat == IndexFormat.UInt32 || compact.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.Clear(); mesh.indexFormat = format; mesh.SetVertices(compact.Select(v => fusion.Positions[v]).ToList());
            if (source.Normals.Length > 0) mesh.SetNormals(compact.Select(v => source.Normals[v]).ToList());
            if (source.Tangents.Length > 0) mesh.SetTangents(compact.Select(v => source.Tangents[v]).ToList());
            if (source.Colors.Length > 0) mesh.SetColors(compact.Select(v => source.Colors[v]).ToList());
            for (int c = 0; c < 8; c++) if (source.UV[c] != null)
            {
                var uv = compact.Select(v => source.UV[c][v]);
                if (source.Dimensions[c] == 1) StudioSubdivisionData.SetUV1(mesh, c, uv);
                else if (source.Dimensions[c] == 2) mesh.SetUVs(c, uv.Select(v => new Vector2(v.x, v.y)).ToList());
                else if (source.Dimensions[c] == 3) mesh.SetUVs(c, uv.Select(v => new Vector3(v.x, v.y, v.z)).ToList());
                else mesh.SetUVs(c, uv.ToList());
            }
            mesh.subMeshCount = triangles.Length;
            for (int s = 0; s < triangles.Length; s++) mesh.SetTriangles(triangles[s].Select(v => remap[v]).ToList(), s, false);
            mesh.RecalculateBounds(); mesh.UploadMeshData(false);
            node.cutPolygons = faces.SelectMany(f => new[] { f.Submesh, f.Vertices.Count }.Concat(f.Vertices.Select(v => remap[v]))).ToArray();
            node.cutTriangles = mesh.triangles; node.geometryVertices = GeometricIds(compact.Select(v => fusion.Geometry[v]));
            node.normalGroups = groups != null && groups.Length == Positions.Length ? compact.Select(v => groups[v]).ToArray() : null;
            return compact.Select((v, i) => (v, i)).Where(p => fusion.Affected[p.v]).Select(p => p.i).ToArray();
        }

        internal int[] CollapseEdgeRings(StudioModelDocument.Node node, IEnumerable<(int a, int b)> seeds)
        {
            var mesh = node.mesh;
            if (!mesh || !mesh.isReadable || node.skinned || node.blendShape != null || mesh.blendShapeCount > 0 || mesh.bindposes.Length > 0 || mesh.boneWeights.Length > 0)
                throw new InvalidOperationException("Collapse Edge Ring requires a readable static mesh without skinning or blend shapes.");
            var topology = CaptureEdgeRings(); var collected = new SortedSet<int>();
            foreach (var edge in seeds ?? Enumerable.Empty<(int a, int b)>())
            {
                if (edge.a < 0 || edge.b < 0 || edge.a >= Positions.Length || edge.b >= Positions.Length ||
                    !topology.Index.TryGetValue(Key(edge.a, edge.b), out int id))
                    throw new InvalidOperationException("Select valid authored edges to collapse their rings.");
                if (!collected.Contains(id)) collected.UnionWith(topology.Collect(id));
            }
            if (collected.Count == 0) throw new InvalidOperationException("Select edges to collapse their rings.");
            return PurgeCollapsedRing(node, FuseRingMidpoints(node, topology, collected));
        }
    }
}

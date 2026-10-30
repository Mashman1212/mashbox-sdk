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
            internal Vector3[] SourceVertices, NormalDirections, InsetDirections;
            internal float PositiveInsetLimit = float.PositiveInfinity, NegativeInsetLimit = float.PositiveInfinity;
            internal int[] CapTriangles;
            internal void ValidateInset(float inset)
            {
                if(float.IsNaN(inset)||float.IsInfinity(inset))throw new InvalidOperationException("Inset must be finite.");
                float limit=inset>0?PositiveInsetLimit:NegativeInsetLimit;
                if(Mathf.Abs(inset)>0 && Mathf.Abs(inset)>=limit-1e-6f)
                    throw new InvalidOperationException("Inset reaches a boundary collision. Reduce the inset amount.");
                if(inset==0||InsetDirections==null)return;
                for(int t=0;t<CapTriangles.Length;t+=3)
                {
                    int a=CapTriangles[t],b=CapTriangles[t+1],c=CapTriangles[t+2];
                    var original=Vector3.Cross(SourceVertices[b]-SourceVertices[a],SourceVertices[c]-SourceVertices[a]);
                    var ab=SourceVertices[b]-SourceVertices[a]+(InsetDirections[b]-InsetDirections[a])*inset;
                    var ac=SourceVertices[c]-SourceVertices[a]+(InsetDirections[c]-InsetDirections[a])*inset;
                    if(original.sqrMagnitude>1e-20f&&Vector3.Dot(Vector3.Cross(ab,ac),original)<=original.sqrMagnitude*1e-6f)
                        throw new InvalidOperationException("Inset collapses or reverses a cap triangle. Reduce the inset amount.");
                }
            }
        }

        // Work in temporary lists and validate before touching the owned mesh.
        // Render-vertex splits retain their attributes; coincident seams share displacement.
        internal ExtrusionResult Extrude(StudioModelDocument.Node node, int[] selectedFaces,
            (int a, int b)[] selectedEdges, float distance, bool together, bool keepSeams,
            Func<Vector3, Vector3> transform = null, float inset = 0)
        {
            var mesh = node.mesh;
            if (node.skinned || node.blendShape != null || mesh.blendShapeCount > 0 || mesh.boneWeights.Length > 0)
                throw new InvalidOperationException("Bake the blend pose or use a static mesh before extruding.");
            if (float.IsNaN(distance) || float.IsInfinity(distance) || float.IsNaN(inset) || float.IsInfinity(inset) || Mathf.Abs(distance) < .00001f && Mathf.Abs(inset) < .00001f)
                throw new InvalidOperationException("Extrude distance and inset must be finite, with at least one non-zero value.");
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
            var insetPlan = inset != 0 ? PrepareExtrusionInset(boundary, faceNormals, Group, Canonical, faceMode) : null;
            Vector3 InsetDirection(int f,int v) => insetPlan != null && insetPlan.Directions.TryGetValue((Group(f),Canonical(v)),out var direction) ? direction : Vector3.zero;
            var normalDirections = Enumerable.Repeat(Vector3.zero,Positions.Length).ToList();
            var insetDirections = Enumerable.Repeat(Vector3.zero,Positions.Length).ToList();
            var positions = Positions.ToList(); var colors = mesh.colors.ToList();var geometry=welded.ToList();int nextGeo=sourceGeometry.Length;var topGeometry=new Dictionary<(int,int),int>();
            var sources = Positions.ToList(); var moving = new HashSet<int>();
            var uvs = new List<Vector4>[8]; var dimensions = new int[8];
            for (int c = 0; c < 8; c++)
            {
                uvs[c] = new List<Vector4>(); mesh.GetUVs(c, uvs[c]);
                dimensions[c] = mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + c);
            }
            // New walls always receive usable rectangular UV0; all other data is copied.
            if (uvs[0].Count == 0) { uvs[0].AddRange(Enumerable.Repeat(Vector4.zero, positions.Count)); dimensions[0] = 2; }
            int Copy(int source, Vector3 position, Vector2? wallUV = null, Vector3 normalDirection = default, Vector3 insetDirection = default)
            {
                int id = positions.Count; positions.Add(position);geometry.Add(welded[source]); normalDirections.Add(normalDirection); insetDirections.Add(insetDirection);
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
                var offset = InsetDirection(f,v);
                int id = Copy(v, Positions[v] + normal * distance + offset * inset, normalDirection: normal, insetDirection: offset);
                var geoKey=(Group(f),Canonical(v));if(!topGeometry.TryGetValue(geoKey,out int geo))topGeometry.Add(geoKey,geo=nextGeo++);geometry[id]=geo;
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
                    Copy(b, positions[tb], new Vector2(length, Vector3.Distance(positions[tb],Positions[b])), normalDirections[tb], insetDirections[tb]), Copy(a, positions[ta], new Vector2(0, Vector3.Distance(positions[ta],Positions[a])), normalDirections[ta], insetDirections[ta]) };
                geometry[wall[2]]=geometry[tb];geometry[wall[3]]=geometry[ta];
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
            // Extrusion changes connectivity only at the new walls. Retain authored
            // render diagonals on original faces and remap selected caps onto their
            // copied vertices. Retriangulating unrelated imported faces can reject
            // valid small/warped quads and change their piecewise surface.
            var sourceTriangles = BevelSourceTriangles(mesh); var capTriangles = new List<int>();
            for (int f = 0; f < Faces.Count; f++)
            {
                var triangles = sourceTriangles[f];
                if (triangles.Count != (Faces[f].Vertices.Count - 2) * 3)
                    throw new InvalidOperationException("An extrusion source face has no matching render triangulation.");
                bool cap = faceMode && selected.Contains(f);
                foreach (int vertex in triangles)
                {
                    int mapped=remap[cap ? top[(Group(f), vertex)] : vertex];
                    indices[Faces[f].Submesh].Add(mapped); if(cap)capTriangles.Add(mapped);
                }
            }
            for (int f = Faces.Count; f < result.Count; f++)
                Triangulate(result[f].Vertices, compact, indices[result[f].Submesh]);
            // Build connectivity from a valid normal extrusion, then place only its
            // new tips/caps using the handle's world/object-space transformation.
            if (transform != null) foreach (int id in used.Where(moving.Contains))
            {
                var point = transform(sources[id]);
                if (float.IsNaN(point.sqrMagnitude) || float.IsInfinity(point.sqrMagnitude))
                    throw new InvalidOperationException("The extrusion would create invalid vertex positions.");
                compact[remap[id]] = point;
            }
            var extrusion = new ExtrusionResult { Faces = selected.OrderBy(f => f).ToArray(), Edges = rim.Select(e => (remap[e.a], remap[e.b])).ToArray(),
                MovingVertices = used.Where(moving.Contains).Select(i => remap[i]).ToArray(), SourceVertices = used.Select(i => sources[i]).ToArray(),
                NormalDirections = used.Select(i => normalDirections[i]).ToArray(),
                InsetDirections = insetPlan == null ? null : used.Select(i => insetDirections[i]).ToArray(), CapTriangles = capTriangles.ToArray(),
                PositiveInsetLimit = insetPlan?.PositiveInsetLimit ?? float.PositiveInfinity, NegativeInsetLimit = insetPlan?.NegativeInsetLimit ?? float.PositiveInfinity };
            extrusion.ValidateInset(inset);
            mesh.Clear(); mesh.indexFormat = compact.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(compact);
            if (colors.Count > 0) mesh.SetColors(used.Select(i => colors[i]).ToList());
            for (int c = 0; c < 8; c++) if (uvs[c].Count > 0)
            {
                var data = used.Select(i => uvs[c][i]);
                if (dimensions[c] == 1) StudioSubdivisionData.SetUV1(mesh,c,data);
                else if (dimensions[c] == 2) mesh.SetUVs(c, data.Select(v => new Vector2(v.x, v.y)).ToList());
                else if (dimensions[c] == 3) mesh.SetUVs(c, data.Select(v => new Vector3(v.x, v.y, v.z)).ToList());
                else mesh.SetUVs(c, data.ToList());
            }
            mesh.subMeshCount = indices.Length;
            for (int s = 0; s < indices.Length; s++) mesh.SetTriangles(indices[s], s);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); mesh.UploadMeshData(false);
            node.cutPolygons = result.SelectMany(f => new[] { f.Submesh, f.Vertices.Count }.Concat(f.Vertices)).ToArray();
            node.cutTriangles = mesh.triangles;node.geometryVertices=GeometricIds(used.Select(i=>geometry[i]));
            return extrusion;
        }
    }
}

using System;

using System.Collections.Generic;

using UnityEngine;

using UnityEngine.Rendering;



namespace MashBoxSDK.AnimationStudio

{

    internal sealed partial class StudioMeshCuts

    {

        internal enum BevelJunction { [InspectorName("Grid (Uniform)")] Uniform, [InspectorName("Cap (N-gon)")] Patch }

        internal struct BevelOptions

        {

            internal float Width, Tension;

            internal int Subdivisions;

            internal BevelJunction Junction;

        }

        internal sealed class BevelResult

        {

            internal float AppliedWidth;

            internal int Edges, AddedFaces;
            internal int VertexUpdateStart;
            internal IList<Vector3> PublishedPositions;
            internal BevelVertexMap Welds;
            internal BevelVertexUpdate VertexUpdate;
            internal readonly List<(int,int)> RenderSelection = new List<(int,int)>();

            internal readonly HashSet<(Vector3, Vector3)> Selection = new HashSet<(Vector3, Vector3)>();

        }



        // Geometric IDs join coincident render vertices, while directed face uses

        // retain their original material/UV corners. No triangulation diagonals

        // enter the bevel selection or its adjacency graph.

        private sealed class BevelEdge

        {

            internal int A, B;

            internal readonly List<(int face, int corner)> Uses = new List<(int, int)>(2);

        }

        private sealed class BevelOverlap : Exception { internal BevelOverlap(string text) : base(text) { } }

        private struct BevelSample { internal int A, B, C; internal Vector3 Weights; }

        private struct BevelRenderVertex

        {

            internal int Geo, SourceFace;

            internal BevelSample Sample;

            internal Vector3 Normal;

        }

        private sealed class BevelPolygon

        {

            internal int Material;
            internal int OriginalFace = -1;
            internal bool SourceSurface;

            internal readonly List<int> Vertices = new List<int>();

        }

        private sealed class BevelDraft

        {

            internal readonly List<Vector3> Positions = new List<Vector3>();

            internal readonly List<int> Owners = new List<int>();

            internal readonly List<BevelRenderVertex> Vertices = new List<BevelRenderVertex>();

            internal readonly List<BevelPolygon> Polygons = new List<BevelPolygon>();

            internal readonly HashSet<int> JunctionAnchors = new HashSet<int>();

            internal readonly Dictionary<int, Vector3> NormalHints = new Dictionary<int, Vector3>();

            internal readonly Dictionary<int, int> SourceHints = new Dictionary<int, int>();

            internal readonly HashSet<(int, int)> Selection = new HashSet<(int, int)>();

            internal List<int>[][] Corners;
            internal void Reset(BevelWavefrontPlan next)
            {
                if (!ReferenceEquals(Plan,next))
                {
                    Positions.Clear(); Positions.AddRange(next.Originals); Owners.Clear();
                    for (int i=0;i<next.Originals.Length;i++) Owners.Add(i);
                    Corners=null;
                }
                else
                {
                    TrimBevelTail(Positions,next.Originals.Length); TrimBevelTail(Owners,next.Originals.Length);
                    if (Corners!=null) foreach (int face in next.AffectedFaces) Corners[face]=null;
                }
                Vertices.Clear(); Polygons.Clear();
                JunctionAnchors.Clear(); NormalHints.Clear(); SourceHints.Clear(); Selection.Clear(); pointBins.Clear();
            }
            internal float Width;
            internal BevelWavefrontPlan Plan;

            internal double Precision;

            internal Vector3[] Originals;

            private readonly Dictionary<(int owner, long x, long y, long z), List<int>> pointBins = new Dictionary<(int, long, long, long), List<int>>();

            internal int AddPoint(Vector3 point, int owner)

            {

                var delta = point - Originals[owner];
                if (delta.sqrMagnitude <= Precision * Precision) return owner;

                long x = (long)Math.Floor(delta.x / Precision), y = (long)Math.Floor(delta.y / Precision), z = (long)Math.Floor(delta.z / Precision);

                for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++) for (int c = -1; c <= 1; c++)

                    if (pointBins.TryGetValue((owner, x + a, y + b, z + c), out var candidates))

                        foreach (int candidate in candidates)

                            if ((Positions[candidate] - point).sqrMagnitude <= Precision * Precision) return candidate;

                int id = Positions.Count; Positions.Add(point); Owners.Add(owner);

                if (!pointBins.TryGetValue((owner, x, y, z), out var bucket)) pointBins.Add((owner, x, y, z), bucket = new List<int>());

                bucket.Add(id); return id;

            }

        }

        private static (int, int) BevelKey(int a, int b) => a < b ? (a, b) : (b, a);

        private static bool BevelFinite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        private static bool BevelFinite(Vector3 p) => BevelFinite(p.x) && BevelFinite(p.y) && BevelFinite(p.z);

        private static Vector3 BevelUnit(Vector3 p)

        {

            double length = Math.Sqrt((double)p.x * p.x + (double)p.y * p.y + (double)p.z * p.z);

            return length > 1e-30 ? p / (float)length : Vector3.zero;

        }



        private Dictionary<(int, int), BevelEdge> bevelEdges;

        private Dictionary<int, List<BevelEdge>> bevelIncident;

        private Vector3[] bevelNormals;

        private double bevelEpsilon;

        private void PrepareBevelTopology(Mesh mesh)

        {

            if (bevelEdges != null) return;

            var edges = new Dictionary<(int, int), BevelEdge>();

            var incident = new Dictionary<int, List<BevelEdge>>();

            var normals = new Vector3[Faces.Count];

            float scale = mesh.bounds.size.magnitude;

            double epsilon = Math.Max(scale * 1e-7, 1e-10);

            for (int f = 0; f < Faces.Count; f++)

            {

                var v = Faces[f].Vertices;

                if (v.Count < 3) throw new InvalidOperationException("Bevel found a polygon with fewer than three corners.");

                Vector3 normal = Vector3.zero, origin = Positions[v[0]];

                for (int i = 0; i < v.Count; i++)

                {

                    int a = welded[v[i]], b = welded[v[(i + 1) % v.Count]];

                    if (a == b) throw new InvalidOperationException("Remove zero-length polygon edges before beveling.");

                    normal += Vector3.Cross(Positions[v[i]] - origin, Positions[v[(i + 1) % v.Count]] - origin);

                    var key = BevelKey(a, b);

                    if (!edges.TryGetValue(key, out var edge))

                    {

                        edge = new BevelEdge { A = key.Item1, B = key.Item2 }; edges.Add(key, edge);

                        foreach (int point in new[] { edge.A, edge.B })

                        { if (!incident.TryGetValue(point, out var list)) incident.Add(point, list = new List<BevelEdge>()); list.Add(edge); }

                    }

                    edge.Uses.Add((f, i));

                }

                if (normal.sqrMagnitude < epsilon * epsilon * epsilon * epsilon)

                    throw new InvalidOperationException("Remove degenerate polygons before beveling.");

                normals[f] = BevelUnit(normal);

            }

            bevelEdges = edges; bevelIncident = incident; bevelNormals = normals; bevelEpsilon = epsilon;

        }



        private List<int>[] bevelSourceTriangles;
        private int bevelInputFaceCount;
        private readonly HashSet<(int, int)> bevelChartEdges = new HashSet<(int, int)>();
        internal List<int>[] BevelSourceTriangles(Mesh mesh)
        {
            if (bevelSourceTriangles != null) return bevelSourceTriangles;
            var originalTriangles = new List<int>[Faces.Count];
            var memberships = new List<int>[Positions.Length];
            for (int f = 0; f < Faces.Count; f++)
            {
                originalTriangles[f] = new List<int>();
                foreach (int vertex in Faces[f].Vertices)
                {
                    if (memberships[vertex] == null) memberships[vertex] = new List<int>();
                    memberships[vertex].Add(f);
                }
            }
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var triangles = mesh.GetTriangles(sub);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var candidates = memberships[triangles[i]];
                    if (candidates == null) continue;
                    foreach (int f in candidates)
                    {
                        var face = Faces[f];
                        if (face.Submesh != sub || originalTriangles[f].Count >= (face.Vertices.Count - 2) * 3 ||
                            !face.Vertices.Contains(triangles[i + 1]) || !face.Vertices.Contains(triangles[i + 2])) continue;
                        originalTriangles[f].Add(triangles[i]); originalTriangles[f].Add(triangles[i + 1]); originalTriangles[f].Add(triangles[i + 2]);
                        break;
                    }
                }
            }
            return bevelSourceTriangles = originalTriangles;
        }
        private void PrepareBevelSurfaceCharts(Mesh mesh, IEnumerable<(int a, int b)> selected)
        {
            var split = new HashSet<int>();
            foreach (var pair in selected)
            {
                if (pair.a < 0 || pair.b < 0 || pair.a >= Positions.Length || pair.b >= Positions.Length) continue;
                var key = Key(pair.a, pair.b);
                if (!bevelChartEdges.Add(key) || !bevelEdges.TryGetValue(key, out var edge)) continue;
                foreach (var use in edge.Uses)
                {
                    var face = Faces[use.face].Vertices;
                    if (face.Count <= 3) continue;
                    var origin = Positions[face[0]]; float scale = 0;
                    foreach (int v in face) scale = Mathf.Max(scale, Vector3.Distance(origin, Positions[v]));
                    foreach (int v in face)
                        if (Math.Abs(Vector3.Dot(Positions[v] - origin, bevelNormals[use.face])) > Math.Max(bevelEpsilon * 8, scale * 1e-6))
                        { split.Add(use.face); break; }
                }
            }
            if (split.Count == 0) return;
            if (bevelInputFaceCount == 0) bevelInputFaceCount = Faces.Count;
            var triangles = BevelSourceTriangles(mesh);
            // A warped polygon has no single supporting plane. Use its authored
            // render facets as local planar charts; never flatten the surface or
            // partition unrelated faces. These boundaries publish only on commit.
            foreach (int f in split)
            {
                var face = Faces[f]; var indices = triangles[f];
                if (indices.Count != (face.Vertices.Count - 2) * 3)
                    throw new InvalidOperationException("The non-planar bevel face has no matching source triangulation.");
                for (int i = 0; i < indices.Count; i += 3)
                {
                    var chart = new Face { Submesh = face.Submesh, Vertices = new List<int> { indices[i], indices[i + 1], indices[i + 2] } };
                    if (i == 0) Faces[f] = chart; else Faces.Add(chart);
                }
            }
            bevelSourceTriangles = null; bevelEdges = null; bevelIncident = null; bevelNormals = null; bevelWavefront = null;
            PrepareBevelTopology(mesh);
        }

#if MASHBOX_STUDIO_TESTS
        internal static Action<string> BevelProfileStage;
        internal bool UseReferenceBevelWriter;
#endif
        internal BevelResult BevelEdges(StudioModelDocument.Node node, IEnumerable<(int a, int b)> selected,

            BevelOptions options, Action beforeWrite = null)

        {

            var mesh = node.mesh;

            if (!mesh || !mesh.isReadable || node.skinned || node.blendShape != null || mesh.blendShapeCount != 0 || mesh.bindposes.Length != 0 || mesh.HasVertexAttribute(VertexAttribute.BlendWeight))

                throw new InvalidOperationException("Bevel requires an editable static mesh. Bake skinning and blend shapes first.");

            if (!BevelFinite(options.Width) || options.Width < 0 || options.Width > 1 ||

                !BevelFinite(options.Tension) || options.Tension < -1 || options.Tension > 1 ||

                options.Subdivisions < 0 || options.Subdivisions > 32 || !Enum.IsDefined(typeof(BevelJunction), options.Junction))

                throw new InvalidOperationException("Bevel needs width 0-1, tension -1 to 1, and 0-32 internal subdivisions.");

#if MASHBOX_STUDIO_TESTS
            BevelProfileStage?.Invoke("bevel.start");
#endif
            selected = new List<(int a, int b)>(selected);
            PrepareBevelTopology(mesh);
            PrepareBevelSurfaceCharts(mesh, selected);

            var edges = bevelEdges; var incident = bevelIncident; var normals = bevelNormals; double epsilon = bevelEpsilon;

            var targets = new HashSet<(int, int)>();

            var affected = new HashSet<int>();

            foreach (var pair in selected)

            {

                if (pair.a < 0 || pair.b < 0 || pair.a >= Positions.Length || pair.b >= Positions.Length)

                    throw new InvalidOperationException("The edge selection changed. Select the edges again.");

                var key = Key(pair.a, pair.b);

                if (!edges.TryGetValue(key, out var edge)) throw new InvalidOperationException("Select polygon edges rather than triangulation diagonals.");

                if (edge.Uses.Count != 2) throw new InvalidOperationException("Bevel needs two faces per selected edge. Boundary and non-manifold edges cannot be beveled.");

                targets.Add(key); affected.Add(edge.A); affected.Add(edge.B);

            }

            if (targets.Count == 0) throw new InvalidOperationException("Select edges to bevel.");

            if (options.Width == 0) return null;

            foreach (int vertex in affected)

            {

                int boundaries = 0;

                foreach (var edge in incident[vertex])

                {

                    if (edge.Uses.Count > 2) throw new InvalidOperationException("Repair non-manifold vertices touching the bevel selection first.");

                    if (edge.Uses.Count == 1) { boundaries++; continue; }

                    var x = edge.Uses[0]; var y = edge.Uses[1];

                    if (welded[Faces[x.face].Vertices[x.corner]] == welded[Faces[y.face].Vertices[y.corner]])

                        throw new InvalidOperationException("Adjacent bevel faces have inconsistent winding. Fix their orientation first.");

                }

                if (boundaries != 0 && boundaries != 2) throw new InvalidOperationException("Bevel found a disconnected or non-manifold vertex fan.");

            }

            long estimate = (long)targets.Count * (options.Subdivisions + 1) * 4 + (long)affected.Count * (options.Subdivisions + 2) * 12;

            if (estimate > 8000000) throw new InvalidOperationException("This bevel would generate too much geometry. Reduce the selection or subdivisions.");

            var plan = PrepareBevelWavefront(targets, affected);
            float width = Mathf.Min(options.Width, plan.Limit);
            if (width <= epsilon) throw new InvalidOperationException("No room remains for a nondegenerate bevel before the first skeleton collision.");
            // One analytic wavefront evaluation, followed by atomic validation.
            // Never change the requested shape through repeated half-width retries.
#if MASHBOX_STUDIO_TESTS
            BevelProfileStage?.Invoke("bevel.prepare");
#endif
            BevelDraft draft;
            try { draft = BuildBevelDraft(node, edges, incident, targets, affected, normals, options, width, epsilon); }
            catch (BevelOverlap e) { throw new InvalidOperationException("The bevel junction cannot be constructed safely: " + e.Message); }

#if MASHBOX_STUDIO_TESTS
            BevelProfileStage?.Invoke("bevel.build");
#endif
#if MASHBOX_STUDIO_TESTS
            var result = UseReferenceBevelWriter ? WriteBevelDraftReference(node, draft, targets.Count, beforeWrite, epsilon) : WriteBevelDraft(node, draft, targets.Count, beforeWrite, epsilon);
#else
            var result = WriteBevelDraft(node, draft, targets.Count, beforeWrite, epsilon);
#endif
#if MASHBOX_STUDIO_TESTS
            BevelProfileStage?.Invoke("bevel.write");
#endif
            return result;

        }



        private BevelDraft reusableBevelDraft;
        private BevelDraft BuildBevelDraft(StudioModelDocument.Node node, Dictionary<(int, int), BevelEdge> edges,

            Dictionary<int, List<BevelEdge>> incident, HashSet<(int, int)> targets, HashSet<int> affected,

            Vector3[] faceNormals, BevelOptions options, float width, double epsilon)

        {

            var plan = PrepareBevelWavefront(targets, affected);
            var originals = plan.Originals;
            var draft = reusableBevelDraft ?? (reusableBevelDraft = new BevelDraft());
            draft.Reset(plan); draft.Width = width; draft.Originals = originals; draft.Precision = epsilon * 2; draft.Plan = plan;
            var distances = plan.Distances;
            var cuts = new Dictionary<(int vertex, int other), int>();
            var boundaryCuts = new Dictionary<(int,int), List<int>>();
            var waveCorners = new Dictionary<(int,int),int>();
            void BoundaryCut(int vertex, int other, int point)
            {
                var key = BevelKey(vertex, other);
                if (!boundaryCuts.TryGetValue(key, out var points)) boundaryCuts.Add(key, points = new List<int>());
                if (!points.Contains(point)) points.Add(point);
                var cutKey = (vertex, other);
                if (!cuts.TryGetValue(cutKey, out int previous) ||
                    (draft.Positions[point] - originals[vertex]).sqrMagnitude < (draft.Positions[previous] - originals[vertex]).sqrMagnitude)
                    cuts[cutKey] = point;
            }
            // A shared edge may carry several distinct face-plane intersections.
            // Keep their identities, then split its retained spans consistently.
            // Choosing max/min travel for both incident faces would skew one rail.
            foreach (int f in plan.SelectedFaces)
            {
                var v = Faces[f].Vertices;
                for (int c = 0; c < v.Count; c++)
                {
                    int vertex = welded[v[c]], previous = welded[v[(c + v.Count - 1) % v.Count]], next = welded[v[(c + 1) % v.Count]];
                    bool incoming = targets.Contains(BevelKey(previous, vertex)), outgoing = targets.Contains(BevelKey(vertex, next));
                    if (!incoming && !outgoing) continue;
                    int point = draft.AddPoint(originals[vertex] + plan.Velocities[f][c] * width, vertex);
                    waveCorners.Add((f,c),point);
                    if (!incoming) BoundaryCut(vertex, previous, point);
                    if (!outgoing) BoundaryCut(vertex, next, point);
                }
            }
            int Cut(int vertex, int other)
            {
                if (cuts.TryGetValue((vertex, other), out int id)) return id;
                id = draft.AddPoint(originals[vertex] + BevelUnit(originals[other] - originals[vertex]) * (distances[vertex] * width), vertex);
                BoundaryCut(vertex, other, id);return id;
            }
            foreach (int vertex in affected) foreach (var edge in incident[vertex])
            {
                if (targets.Contains(BevelKey(edge.A,edge.B))) continue;
                Cut(vertex, edge.A == vertex ? edge.B : edge.A);
            }

            var corners = draft.Corners ?? (draft.Corners = new List<int>[Faces.Count][]);

            var sourceNormals = BevelSource(node.mesh).Normals;

            var sourceTriangles = new Dictionary<int, List<int>>();

            BevelSample Sample(int face, Vector3 position)

            {

                var v = Faces[face].Vertices;

                for (int i = 0; i < v.Count; i++) if ((Positions[v[i]] - position).sqrMagnitude <= epsilon * epsilon)

                    return new BevelSample { A = v[i], B = v[i], C = v[i], Weights = Vector3.right };

                if (!sourceTriangles.TryGetValue(face, out var triangles))

                { triangles = BevelTriangulate(v, Positions, epsilon); sourceTriangles.Add(face, triangles); }

                double best = double.PositiveInfinity; BevelSample sample = default;

                for (int i = 0; i < triangles.Count; i += 3)

                {

                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];

                    var ab = Positions[b] - Positions[a]; var ac = Positions[c] - Positions[a]; var p = position - Positions[a];

                    double aa = Vector3.Dot(ab, ab), bb = Vector3.Dot(ab, ac), cc = Vector3.Dot(ac, ac), ap = Vector3.Dot(p, ab), cp = Vector3.Dot(p, ac);

                    double determinant = aa * cc - bb * bb;

                    if (determinant <= epsilon * epsilon * epsilon * epsilon) continue;

                    float y = (float)((cc * ap - bb * cp) / determinant), z = (float)((aa * cp - bb * ap) / determinant), x = 1 - y - z;

                    var weights = new Vector3(Mathf.Max(0, x), Mathf.Max(0, y), Mathf.Max(0, z)); weights /= weights.x + weights.y + weights.z;

                    var projected = Positions[a] * weights.x + Positions[b] * weights.y + Positions[c] * weights.z;

                    double error = (projected - position).sqrMagnitude;

                    if (error < best) { best = error; sample = new BevelSample { A = a, B = b, C = c, Weights = weights }; }

                }

                if (double.IsPositiveInfinity(best)) throw new BevelOverlap("A source polygon could not be sampled.");

                return sample;

            }

            bool buildingSourceFaces=true;
            void AddFace(List<int> geometry, int sourceFace, int material, List<Vector3> normals = null)

            {

                if (geometry.Count < 3) return;

                var polygon = new BevelPolygon { Material = material, SourceSurface = buildingSourceFaces };

                for (int i = 0; i < geometry.Count; i++)

                {

                    int geo = geometry[i]; var sample = Sample(sourceFace, draft.Positions[geo]);

                    Vector3 normal = normals != null ? normals[i] : sourceNormals.Length == Positions.Length ?

                        sourceNormals[sample.A] * sample.Weights.x + sourceNormals[sample.B] * sample.Weights.y + sourceNormals[sample.C] * sample.Weights.z : faceNormals[sourceFace];

                    if (normal.sqrMagnitude < 1e-12f) normal = faceNormals[sourceFace]; normal.Normalize();

                    polygon.Vertices.Add(draft.Vertices.Count);

                    draft.Vertices.Add(new BevelRenderVertex { Geo = geo, SourceFace = sourceFace, Sample = sample, Normal = normal });

                    if (draft.NormalHints.TryGetValue(geo, out var hint)) draft.NormalHints[geo] = hint + normal; else draft.NormalHints.Add(geo, normal);

                    if (!draft.SourceHints.ContainsKey(geo)) draft.SourceHints.Add(geo, sourceFace);

                }

                draft.Polygons.Add(polygon);

            }

            foreach (int f in plan.AffectedFaces)
            {
                var face=Faces[f]; var v=face.Vertices;
                corners[f] = new List<int>[v.Count];
                var polygon = new List<int>(); bool changed = false;

                for (int c = 0; c < v.Count; c++)

                {

                    int vertex = welded[v[c]], previous = welded[v[(c + v.Count - 1) % v.Count]], next = welded[v[(c + 1) % v.Count]];

                    var path = corners[f][c] = new List<int>(2);

                    if (!affected.Contains(vertex)) path.Add(vertex);

                    else

                    {

                        changed = true;

                        bool incoming = targets.Contains(BevelKey(previous, vertex)), outgoing = targets.Contains(BevelKey(vertex, next));

                        if (!incoming && !outgoing) { path.Add(Cut(vertex, previous)); path.Add(Cut(vertex, next)); }

                        else path.Add(waveCorners[(f,c)]);

                    }

                    if (affected.Contains(vertex)) foreach (int point in path) draft.JunctionAnchors.Add(point);

                }
                for (int c = 0; c < v.Count; c++)
                {
                    var path = corners[f][c]; polygon.AddRange(path);
                    int nextCorner = (c + 1) % v.Count;
                    int from = path[path.Count - 1], to = corners[f][nextCorner][0];
                    var key = BevelKey(welded[v[c]], welded[v[nextCorner]]);
                    if (!targets.Contains(key) && boundaryCuts.TryGetValue(key, out var candidates))
                    {
                        var direction = draft.Positions[to] - draft.Positions[from]; double square = direction.sqrMagnitude;
                        if (square > epsilon * epsilon)
                        {
                            var inserts = new List<(float t,int point)>();
                            foreach (int point in candidates)
                            {
                                if (point == from || point == to) continue;
                                var delta = draft.Positions[point] - draft.Positions[from]; float t = (float)(Vector3.Dot(delta,direction) / square);
                                if (t <= 0 || t >= 1 || (delta - direction*t).sqrMagnitude > epsilon*epsilon*4) continue;
                                inserts.Add((t,point));
                            }
                            inserts.Sort((a,b)=>a.t.CompareTo(b.t)); foreach (var insert in inserts) polygon.Add(insert.point);
                        }
                    }
                }

                if (changed) BevelValidateInset(polygon, draft.Positions, v, Positions, faceNormals[f], epsilon);

                AddFace(polygon, f, face.Submesh);
                if (!changed) draft.Polygons[draft.Polygons.Count - 1].OriginalFace = f;

            }

            buildingSourceFaces=false;
            // A terminal profile is constrained by the untouched vertex fan.
            // A shared miter profile has one identity and one control for both
            // incident strips. Projecting each control independently along its
            // edge creates two different curves across the same boundary.
            var profileUses = new Dictionary<(int,int),int>();
            var selectedDegree = new Dictionary<int,int>();
            void ProfileUse(int a, int b)
            {
                var key = BevelKey(a,b);
                profileUses.TryGetValue(key,out int count); profileUses[key]=count+1;
            }
            foreach (var key in targets)
            {
                selectedDegree.TryGetValue(key.Item1,out int da); selectedDegree[key.Item1]=da+1;
                selectedDegree.TryGetValue(key.Item2,out int db); selectedDegree[key.Item2]=db+1;
                var edge=edges[key]; var first=edge.Uses[0]; var second=edge.Uses[1];
                int ca=first.corner, cb=(ca+1)%Faces[first.face].Vertices.Count;
                int sa=second.corner, sb=(sa+1)%Faces[second.face].Vertices.Count;
                ProfileUse(corners[first.face][ca][corners[first.face][ca].Count-1],corners[second.face][sb][0]);
                ProfileUse(corners[first.face][cb][0],corners[second.face][sa][corners[second.face][sa].Count-1]);
            }
            Vector3 ProfileControl(int owner, int a, int b, Vector3 tangent)
            {
                // At an edge termination the original edge intersects every
                // unchanged incident supporting plane at the original vertex.
                // Keeping that control preserves the end plane for all profile
                // samples. Coincident miter boundaries likewise share this control.
                if (selectedDegree[owner]==1 || profileUses[BevelKey(a,b)]>1) return originals[owner];
                // A multi-way junction trims each independent profile in its
                // cross-section; the vertex mesh joins the remaining boundary.
                return originals[owner] - tangent * Vector3.Dot(originals[owner] -
                    (draft.Positions[a]+draft.Positions[b])*.5f,tangent);
            }
            int steps = options.Subdivisions + 1;

            foreach (var key in targets)

            {

                var edge = edges[key]; var first = edge.Uses[0]; var second = edge.Uses[1];

                var fv = Faces[first.face].Vertices; int ca = first.corner, cb = (ca + 1) % fv.Count;

                int va = welded[fv[ca]], vb = welded[fv[cb]];

                int sa = second.corner, sb = (sa + 1) % Faces[second.face].Vertices.Count;

                int pa = corners[first.face][ca][corners[first.face][ca].Count - 1], pb = corners[first.face][cb][0];

                int qa = corners[second.face][sb][0], qb = corners[second.face][sa][corners[second.face][sa].Count - 1];

                var a = new int[steps + 1]; var b = new int[steps + 1]; var na = new Vector3[steps + 1]; var nb = new Vector3[steps + 1];

                // A corner inset moves each strip endpoint along its source edge.

                // Keep the profile control in that endpoint's cross-section instead

                // of pulling it back toward the old vertex along the strip.

                var tangent = BevelUnit(originals[vb] - originals[va]);

                var controlA = ProfileControl(va,pa,qa,tangent);

                var controlB = ProfileControl(vb,pb,qb,tangent);

                for (int k = 0; k <= steps; k++)

                {

                    float t = k / (float)steps;

                    a[k] = k == 0 ? pa : k == steps ? qa : draft.AddPoint(BevelProfile(draft.Positions[pa], draft.Positions[qa], controlA, t, options.Tension), va);

                    b[k] = k == 0 ? pb : k == steps ? qb : draft.AddPoint(BevelProfile(draft.Positions[pb], draft.Positions[qb], controlB, t, options.Tension), vb);

                    var along = draft.Positions[b[k]] - draft.Positions[a[k]];

                    Vector3 expected = Vector3.Lerp(faceNormals[first.face], faceNormals[second.face], t);

                    na[k] = BevelProfileNormal(draft.Positions[pa], draft.Positions[qa], controlA, along, expected, t, options.Tension);

                    nb[k] = BevelProfileNormal(draft.Positions[pb], draft.Positions[qb], controlB, along, expected, t, options.Tension);

                }

                for (int k = 0; k < steps; k++)

                {

                    // Reverse the first face's rail, then follow the second. The

                    // paired half-edges are opposite by construction.

                    int source = k * 2 < steps ? first.face : second.face;

                    var strip = new List<int> { b[k], a[k], a[k + 1], b[k + 1] };
                    var stripNormals = new List<Vector3> { nb[k], na[k], na[k + 1], nb[k + 1] };
                    // A single-segment chamfer is a flat face, independent of the
                    // curved profile's tension. Do not shade it like a round bevel.
                    if (steps == 1)
                    {
                        var flat = BevelPolygonNormal(strip, draft.Positions);
                        for (int i = 0; i < stripNormals.Count; i++) stripNormals[i] = flat;
                    }
                    AddFace(strip, source, Faces[source].Submesh, stripNormals);

                }

                int middle = steps / 2; draft.Selection.Add(BevelKey(a[middle], b[middle]));

            }

            SealBevelCorners(draft, affected, options, AddFace, epsilon);

            return draft;

        }



        private static Vector3 BevelProfile(Vector3 a, Vector3 b, Vector3 original, float t, float tension) =>

            Vector3.Lerp(a, b, t) + (original - (a + b) * .5f) * (2 * tension * t * (1 - t));

        private static Vector3 BevelProfileNormal(Vector3 a, Vector3 b, Vector3 original, Vector3 along, Vector3 expected, float t, float tension)

        {

            Vector3 derivative = b - a + (original - (a + b) * .5f) * (2 * tension * (1 - 2 * t));

            var normal = BevelUnit(Vector3.Cross(along, derivative));

            if (normal.sqrMagnitude < .5f) return expected.normalized;

            return Vector3.Dot(normal, expected) < 0 ? -normal : normal;

        }



        private void SealBevelCorners(BevelDraft draft, HashSet<int> affected, BevelOptions options,

            Action<List<int>, int, int, List<Vector3>> addFace, double epsilon)

        {

            var uses = new Dictionary<(int, int), (int count, int a, int b)>();

            var cornerPolygons = new Dictionary<int, HashSet<int>>();

            for (int p = 0; p < draft.Polygons.Count; p++)

            {

                var polygon = draft.Polygons[p];

                for (int i = 0; i < polygon.Vertices.Count; i++)

                {

                    int a = draft.Vertices[polygon.Vertices[i]].Geo, b = draft.Vertices[polygon.Vertices[(i + 1) % polygon.Vertices.Count]].Geo;

                    int owner = draft.Owners[a];

                    if (affected.Contains(owner))

                    { if (!cornerPolygons.TryGetValue(owner, out var polygons)) cornerPolygons.Add(owner, polygons = new HashSet<int>()); polygons.Add(p); }

                    var key = BevelKey(a, b);

                    if (uses.TryGetValue(key, out var use))

                    {

                        if (use.count != 1 || use.a != b) throw new BevelOverlap("The expanded rails intersect or have inconsistent winding.");

                        uses[key] = (2, use.a, use.b);

                    }

                    else uses.Add(key, (1, a, b));

                }

            }

            var boundaries = new Dictionary<int, List<(int a, int b)>>();

            foreach (var use in uses.Values)

            {

                int vertex = draft.Owners[use.a];

                if (use.count != 1 || vertex != draft.Owners[use.b] || !affected.Contains(vertex)) continue;

                if (!boundaries.TryGetValue(vertex, out var list)) boundaries.Add(vertex, list = new List<(int, int)>());

                list.Add((use.a, use.b));

            }

            foreach (var pair in boundaries)

            {

                int vertex = pair.Key; var links = new Dictionary<int, int>(); var incoming = new HashSet<int>();

                foreach (var edge in pair.Value)

                { if (links.ContainsKey(edge.a) || !incoming.Add(edge.b)) throw new BevelOverlap("A bevel corner contains overlapping vertex fans."); links.Add(edge.a, edge.b); }

                int start = pair.Value[0].a;

                foreach (int point in links.Keys) if (!incoming.Contains(point)) { start = point; break; }

                var ring = new List<int>(); int current = start;

                do

                {

                    ring.Add(current);

                    if (!links.TryGetValue(current, out int next)) break;

                    current = next;

                    if (ring.Count > pair.Value.Count + 1) throw new BevelOverlap("A bevel corner could not be ordered.");

                } while (current != start);

                if (current != start)

                {

                    // A selected interior edge may end on an open mesh boundary.

                    // Retain that boundary's original endpoint while sealing its fan.

                    if (ring.Count != pair.Value.Count + 1) throw new BevelOverlap("A disconnected corner fan cannot be healed.");

                    ring.Add(vertex);

                }

                else if (ring.Count != pair.Value.Count) throw new BevelOverlap("A bevel corner contains disconnected loops.");

                ring.Reverse();

                if (ring.Count < 3) continue;

                Vector3 normal = Vector3.zero;

                foreach (int point in ring) if (draft.NormalHints.TryGetValue(point, out var hint)) normal += hint.normalized;

                var geometric = BevelPolygonNormal(ring, draft.Positions);

                if (BevelRingIsLinear(ring, draft.Positions, epsilon))

                {

                    HealFlatBevelCorner(draft, ring, cornerPolygons[vertex], addFace, epsilon);

                    continue;

                }

                if (geometric.sqrMagnitude < .5f) throw new BevelOverlap("A corner patch has no stable surface normal.");

                if (AbsorbPlanarBevelEnd(draft, ring, geometric, cornerPolygons[vertex], addFace, epsilon)) continue;

                if (normal.sqrMagnitude < 1e-10f) normal = geometric; normal.Normalize();

                int source = -1;

                foreach (int point in ring) if (draft.SourceHints.TryGetValue(point, out source)) break;

                if (source < 0) throw new BevelOverlap("The corner has no incident attribute source.");

                int material = Faces[source].Submesh;

                var normals = new List<Vector3>(ring.Count);

                foreach (int point in ring)

                {

                    Vector3 hint = draft.NormalHints.TryGetValue(point, out var h) ? h.normalized : normal;

                    normals.Add(options.Tension == 0 ? geometric : Vector3.Lerp(geometric, hint, Mathf.Abs(options.Tension)).normalized);

                }

                if (options.Junction == BevelJunction.Uniform && options.Subdivisions > 0 &&

                    AddBevelGridJunction(draft, ring, vertex, source, material, options, addFace, epsilon)) continue;

                // The cap modeling topology contains one corner n-gon. Triangulation

                // happens only when building Unity's render index buffer; no center

                // vertices, radial rings or triangle diagonals become editable edges.

                addFace(ring, source, material, normals);

            }

        }



        private bool AbsorbPlanarBevelEnd(BevelDraft draft, List<int> ring, Vector3 normal, IEnumerable<int> candidates,

            Action<List<int>, int, int, List<Vector3>> addFace, double epsilon)

        {

            foreach (int index in candidates)

            {

                // Only absorb a newly generated cap into an original inset face.

                // Never dissolve unrelated source faces or the curved bevel strips.

                if (!draft.Polygons[index].SourceSurface) continue;

                var polygon = draft.Polygons[index];

                var geometry = new List<int>(polygon.Vertices.Count);

                foreach (int vertex in polygon.Vertices) geometry.Add(draft.Vertices[vertex].Geo);

                // A concave profile makes the old cap fold back over this face.

                // Replacing the shared chord fixes that case too; the ring may

                // therefore have the opposite geometric normal before merging.

                // Test against the original supporting plane, not the normal of
                // a small generated cap. Normalizing a tiny cap magnifies binary32
                // error, so testing a distant face corner against it rejects truly
                // coplanar ends and retains an artificial divider.
                int receivingSource = draft.Vertices[polygon.Vertices[0]].SourceFace;
                var planeNormal = bevelNormals[receivingSource];
                var planeOrigin = Positions[Faces[receivingSource].Vertices[0]];
                bool coplanar = true;
                foreach (int point in geometry)
                    if (Math.Abs(Vector3.Dot(draft.Positions[point]-planeOrigin,planeNormal))>epsilon*4)
                    { coplanar=false; break; }
                if (coplanar) foreach (int point in ring)
                    if (Math.Abs(Vector3.Dot(draft.Positions[point]-planeOrigin,planeNormal))>epsilon*4)
                    { coplanar=false; break; }
                if (!coplanar) continue;

                for (int edge = 0; edge < ring.Count; edge++)

                    for (int other = 0; other < geometry.Count; other++)

                    {

                        if (ring[edge] != geometry[(other + 1) % geometry.Count] || ring[(edge + 1) % ring.Count] != geometry[other]) continue;

                        var merged = new List<int>(ring.Count + geometry.Count - 2) { geometry[other] };

                        for (int k = 1; k < ring.Count; k++) merged.Add(ring[(edge + 1 + k) % ring.Count]);

                        for (int k = 2; k < geometry.Count; k++) merged.Add(geometry[(other + k) % geometry.Count]);

                        if (new HashSet<int>(merged).Count != merged.Count) continue;

                        int source = draft.Vertices[polygon.Vertices[0]].SourceFace;

                        // Resample the receiving face so its UVs and normal seams

                        // remain continuous across the former artificial divider.

                        addFace(merged, source, polygon.Material, null);

                        draft.Polygons[draft.Polygons.Count-1].SourceSurface=polygon.SourceSurface;
                        draft.Polygons[index] = draft.Polygons[draft.Polygons.Count - 1];

                        draft.Polygons.RemoveAt(draft.Polygons.Count - 1);

                        return true;

                    }

            }

            return false;

        }



        private bool AddBevelGridJunction(BevelDraft draft, List<int> ring, int owner, int source, int material,

            BevelOptions options, Action<List<int>, int, int, List<Vector3>> addFace, double epsilon)

        {

            int steps = options.Subdivisions + 1;

            var anchors = new List<int>();

            for (int i = 0; i < ring.Count; i++) if (draft.JunctionAnchors.Contains(ring[i])) anchors.Add(i);

            if (anchors.Count < 2) return false;

            // A single selected edge ends on an unsubdivided face boundary. That

            // planar end is an n-gon; only matching profile paths form a grid.

            for (int i = 0; i < anchors.Count; i++)

                if ((anchors[(i + 1) % anchors.Count] - anchors[i] + ring.Count) % ring.Count != steps) return false;

            int Arc(int side, int index) => ring[(anchors[(side + anchors.Count) % anchors.Count] + index) % ring.Count];

            var faces = new List<List<int>>();

            void Face(params int[] points)

            {

                var polygon = new List<int>();

                foreach (int p in points) if (polygon.Count == 0 || polygon[polygon.Count - 1] != p) polygon.Add(p);

                if (polygon.Count > 1 && polygon[0] == polygon[polygon.Count - 1]) polygon.RemoveAt(polygon.Count - 1);

                if (polygon.Count >= 3) faces.Add(polygon);

            }

            if (anchors.Count == 2)

            {

                // Two beveled edges form two opposed profile paths. Connect

                // corresponding samples directly, with real triangular end faces.

                for (int k = 0; k < steps; k++) Face(Arc(0, k), Arc(0, k + 1), Arc(1, steps - k - 1), Arc(1, steps - k));

            }

            else

            {

                var mean = Vector3.zero; var meanNormal = Vector3.zero;

                var positions = new Vector3[anchors.Count]; var directions = new Vector3[anchors.Count];

                for (int i = 0; i < anchors.Count; i++)

                {

                    int point = Arc(i, 0); positions[i] = draft.Positions[point]; mean += positions[i];

                    int face = draft.SourceHints[point];

                    directions[i] = BevelPolygonNormal(Faces[face].Vertices, Positions); meanNormal += directions[i];

                }

                mean /= anchors.Count; meanNormal /= anchors.Count;

                // Fit the offset-face normal rays to a common fillet center.

                // At an orthogonal cube corner this is the exact sphere center.

                double numerator = 0, denominator = 0;

                for (int i = 0; i < anchors.Count; i++)

                {

                    var normal = directions[i] - meanNormal;

                    numerator += Vector3.Dot(positions[i] - mean, normal); denominator += normal.sqrMagnitude;

                }

                float radius = denominator > 1e-12 ? (float)(numerator / denominator) : 0;

                bool fitted = radius > epsilon && !float.IsInfinity(radius) && !float.IsNaN(radius);

                var sphere = mean - meanNormal * radius;

                var direction = BevelUnit(draft.Originals[owner] - sphere);

                var rounded = fitted && direction.sqrMagnitude > .5f ? sphere + direction * radius : mean + (draft.Originals[owner] - mean) * .35f;

                var center = Vector3.LerpUnclamped(mean, rounded, options.Tension);

                Vector3 Radial(Vector3 a, Vector3 b, float t)

                {

                    var p = Vector3.Lerp(a, b, t);

                    if (!fitted || options.Tension <= 0) return p;

                    var n = BevelUnit(p - sphere);

                    float length = Mathf.Lerp((a - sphere).magnitude, (b - sphere).magnitude, t);

                    return Vector3.Lerp(p, sphere + n * length, options.Tension);

                }

                int half = steps / 2;

                bool odd = (steps & 1) != 0;

                var inner = new int[anchors.Count];

                var rightRays = new int[anchors.Count][];

                var leftRays = new int[anchors.Count][];

                for (int side = 0; side < anchors.Count; side++)

                {

                    var p = odd ? Radial(center, positions[side], 1f / steps) : center;

                    inner[side] = draft.AddPoint(p, owner);

                }

                for (int side = 0; side < anchors.Count; side++)

                {

                    var grid = new int[half + 1, half + 1];

                    var a = positions[side]; var b = draft.Positions[Arc(side, half)];

                    var d = draft.Positions[Arc(side - 1, steps - half)]; var c = draft.Positions[inner[side]];

                    for (int v = 0; v <= half; v++) for (int u = 0; u <= half; u++)

                    {

                        if (v == 0) { grid[u, v] = Arc(side, u); continue; }

                        if (u == 0) { grid[u, v] = Arc(side - 1, steps - v); continue; }

                        float x = u / (float)half, y = v / (float)half;

                        Vector3 right = Radial(b, c, y), top = Radial(d, c, x);

                        Vector3 p;

                        if (u == half) p = right;

                        else if (v == half) p = top;

                        else

                        {

                            // Coons interpolation reproduces the profile boundary

                            // exactly, including concave and linear tension values.

                            var bottom = draft.Positions[Arc(side, u)];

                            var left = draft.Positions[Arc(side - 1, steps - v)];

                            var bilinear = a * ((1-x)*(1-y)) + b * (x*(1-y)) + c * (x*y) + d * ((1-x)*y);

                            p = bottom * (1-y) + top * y + left * (1-x) + right * x - bilinear;

                        }

                        grid[u, v] = draft.AddPoint(p, owner);

                    }

                    for (int v = 0; v < half; v++) for (int u = 0; u < half; u++)

                        Face(grid[u,v], grid[u+1,v], grid[u+1,v+1], grid[u,v+1]);

                    rightRays[side] = new int[half + 1]; leftRays[side] = new int[half + 1];

                    for (int k = 0; k <= half; k++)

                    { rightRays[side][k] = grid[half,k]; leftRays[side][k] = grid[k,half]; }

                }

                if (odd)

                {

                    // An odd boundary count cannot be filled entirely by quads.

                    // Keep one central n-gon and join the remaining strips with quads.

                    for (int side = 0; side < anchors.Count; side++)

                        for (int k = 0; k < half; k++)

                        {

                            var a = rightRays[side]; var b = leftRays[(side + 1) % anchors.Count];

                            Face(a[k], b[k], b[k + 1], a[k + 1]);

                        }

                    Face(inner);

                }

            }

            var normals = new Dictionary<int, Vector3>();

            foreach (var face in faces)

            {

                var normal = BevelPolygonNormal(face, draft.Positions);

                if (normal.sqrMagnitude < .5f) throw new BevelOverlap("The junction grid contains a collapsed face.");

                foreach (int point in face)

                    normals[point] = normals.TryGetValue(point, out var sum) ? sum + normal : normal;

            }

            foreach (int point in ring)

                if (draft.NormalHints.TryGetValue(point, out var hint) && hint.sqrMagnitude > 1e-10f) normals[point] = hint;

            foreach (var face in faces)

            {

                var faceNormals = new List<Vector3>(face.Count);

                foreach (int point in face) faceNormals.Add(BevelUnit(normals[point]));

                addFace(face, source, material, faceNormals);

            }

            return true;

        }



        private static bool BevelRingIsLinear(List<int> ring, List<Vector3> positions, double epsilon)

        {

            int x = ring[0], y = x; float length = 0;

            foreach (int a in ring) foreach (int b in ring)

            { float d = (positions[a] - positions[b]).sqrMagnitude; if (d > length) { length = d; x = a; y = b; } }

            if (length <= epsilon * epsilon) return true;

            var line = positions[y] - positions[x];

            foreach (int point in ring)

            { var delta = positions[point] - positions[x]; if ((delta - line * (Vector3.Dot(delta, line) / length)).sqrMagnitude > epsilon * epsilon * 4) return false; }

            return true;

        }



        // A straight chamfer with extra profile loops has a zero-area end cap.

        // Split the neighboring face boundary at those loop endpoints instead of

        // emitting degenerate cap triangles or leaving render-time T junctions.

        private static void HealFlatBevelCorner(BevelDraft draft, List<int> ring, IEnumerable<int> candidates,

            Action<List<int>, int, int, List<Vector3>> addFace, double epsilon)

        {

            int x = ring[0], y = x; float length = 0;

            foreach (int a in ring) foreach (int b in ring)

            { float d = (draft.Positions[a] - draft.Positions[b]).sqrMagnitude; if (d > length) { length = d; x = a; y = b; } }

            if (length <= epsilon * epsilon) throw new BevelOverlap("A corner collapsed to one point.");

            Vector3 line = draft.Positions[y] - draft.Positions[x];

            foreach (int point in ring)

            {

                var delta = draft.Positions[point] - draft.Positions[x];

                if ((delta - line * (Vector3.Dot(delta, line) / length)).sqrMagnitude > epsilon * epsilon * 4)

                    throw new BevelOverlap("A corner boundary intersects itself.");

            }

            foreach (int p in candidates)

            {

                var polygon = draft.Polygons[p]; var geometry = new List<int>(); var normals = new List<Vector3>(); bool changed = false;

                for (int i = 0; i < polygon.Vertices.Count; i++)

                {

                    var a = draft.Vertices[polygon.Vertices[i]]; var b = draft.Vertices[polygon.Vertices[(i + 1) % polygon.Vertices.Count]];

                    geometry.Add(a.Geo); normals.Add(a.Normal);

                    if (!ring.Contains(a.Geo) || !ring.Contains(b.Geo)) continue;

                    Vector3 ab = draft.Positions[b.Geo] - draft.Positions[a.Geo]; float squared = ab.sqrMagnitude;

                    var inserts = new List<(float t, int point)>();

                    foreach (int point in ring)

                    {

                        if (point == a.Geo || point == b.Geo) continue;

                        var delta = draft.Positions[point] - draft.Positions[a.Geo]; float t = Vector3.Dot(delta, ab) / squared;

                        if (t <= 0 || t >= 1 || (delta - ab * t).sqrMagnitude > epsilon * epsilon * 4) continue;

                        inserts.Add((t, point));

                    }

                    inserts.Sort((u, v) => u.t.CompareTo(v.t));

                    foreach (var insert in inserts)

                    { geometry.Add(insert.point); normals.Add(Vector3.Lerp(a.Normal, b.Normal, insert.t).normalized); changed = true; }

                }

                if (!changed) continue;

                int source = draft.Vertices[polygon.Vertices[0]].SourceFace;

                addFace(geometry, source, polygon.Material, normals);

                draft.Polygons[p] = draft.Polygons[draft.Polygons.Count - 1]; draft.Polygons.RemoveAt(draft.Polygons.Count - 1);

            }

        }



        private static Vector3 BevelPolygonNormal(IList<int> polygon, IList<Vector3> positions)

        {

            Vector3 normal = Vector3.zero, origin = positions[polygon[0]];

            for (int i = 0; i < polygon.Count; i++) normal += Vector3.Cross(positions[polygon[i]] - origin, positions[polygon[(i + 1) % polygon.Count]] - origin);

            return BevelUnit(normal);

        }

        private static Vector2 BevelProject(Vector3 p, int axis) => axis == 0 ? new Vector2(p.y, p.z) : axis == 1 ? new Vector2(p.z, p.x) : new Vector2(p.x, p.y);

        private static double BevelCross(Vector2 a, Vector2 b, Vector2 c) => ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);

        private static int BevelAxis(Vector3 normal) => Mathf.Abs(normal.x) > Mathf.Abs(normal.y) ? Mathf.Abs(normal.x) > Mathf.Abs(normal.z) ? 0 : 2 : Mathf.Abs(normal.y) > Mathf.Abs(normal.z) ? 1 : 2;



        private static void BevelValidateInset(List<int> polygon, List<Vector3> positions, List<int> original,

            Vector3[] source, Vector3 normal, double epsilon)

        {

            if (Vector3.Dot(BevelPolygonNormal(polygon, positions), normal) < .01f) throw new BevelOverlap("An inset face collapsed or reversed.");

            int axis = BevelAxis(normal); var points = new Vector2[polygon.Count];

            for (int i = 0; i < polygon.Count; i++) points[i] = BevelProject(positions[polygon[i]], axis);

            double tolerance = epsilon * epsilon;

            for (int i = 0; i < points.Length; i++)

            {

                int j = (i + 1) % points.Length;

                if ((points[i] - points[j]).sqrMagnitude <= tolerance) throw new BevelOverlap("Inset boundaries met on a small face.");

                for (int k = i + 2; k < points.Length; k++)

                {

                    int l = (k + 1) % points.Length; if (l == i) continue;

                    double a = BevelCross(points[i], points[j], points[k]), b = BevelCross(points[i], points[j], points[l]);

                    double c = BevelCross(points[k], points[l], points[i]), d = BevelCross(points[k], points[l], points[j]);

                    if (a * b < -tolerance * tolerance && c * d < -tolerance * tolerance) throw new BevelOverlap("Inset paths cross inside a polygon.");

                }

                // Outside miters on concave polygons must not cut across the notch.

                bool inside = false, onEdge = false; Vector2 p = points[i];

                for (int k = 0, l = original.Count - 1; k < original.Count; l = k++)

                {

                    Vector2 a = BevelProject(source[original[l]], axis), b = BevelProject(source[original[k]], axis);

                    Vector2 ab = b - a; float t = ab.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0;

                    onEdge |= (p - (a + ab * t)).sqrMagnitude <= tolerance * 4;

                    if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;

                }

                if (!inside && !onEdge) throw new BevelOverlap("An inset miter leaves its source polygon.");

            }

        }

        // Filtered orient2d: use the standard double determinant error bound,
        // then exact integer arithmetic for ambiguous binary32 input coordinates.
        // This separates numerical sign tests from geometric offset tolerances.
        // Background: J. Shewchuk, https://www.cs.cmu.edu/~quake/robust.html
        private static int BevelOrientation(Vector2 a, Vector2 b, Vector2 c)
        {
            double left = ((double)a.x - c.x) * ((double)b.y - c.y);
            double right = ((double)a.y - c.y) * ((double)b.x - c.x);
            double determinant = left - right;
            double error = (Math.Abs(left) + Math.Abs(right)) * 3.3306690738754716e-16;
            if (Math.Abs(determinant) > error) return Math.Sign(determinant);
            var ax = BevelFloatInteger(a.x); var ay = BevelFloatInteger(a.y);
            var bx = BevelFloatInteger(b.x); var by = BevelFloatInteger(b.y);
            var cx = BevelFloatInteger(c.x); var cy = BevelFloatInteger(c.y);
            return ((ax - cx) * (by - cy) - (ay - cy) * (bx - cx)).Sign;
        }
        private static System.Numerics.BigInteger BevelFloatInteger(float value)
        {
            int bits = BitConverter.SingleToInt32Bits(value);
            int exponent = (bits >> 23) & 255;
            int mantissa = bits & 0x7fffff;
            if (exponent != 0) mantissa |= 1 << 23;
            // Every finite float is an integer multiple of 2^-149.
            return new System.Numerics.BigInteger(bits < 0 ? -mantissa : mantissa) << (exponent == 0 ? 0 : exponent - 1);
        }

        private static List<int> BevelTriangulate(IList<int> polygon, IList<Vector3> positions, double epsilon)

        {

            var normal = BevelPolygonNormal(polygon, positions); int axis = BevelAxis(normal);

            double sign = axis == 0 ? normal.x : axis == 1 ? normal.y : normal.z; sign = sign >= 0 ? 1 : -1;

            var remaining = new List<int>(polygon); var triangles = new List<int>((polygon.Count - 2) * 3);

            while (remaining.Count > 3)

            {

                bool found = false;

                for (int i = 0; i < remaining.Count; i++)

                {

                    int a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i], c = remaining[(i + 1) % remaining.Count];

                    var pa = BevelProject(positions[a], axis); var pb = BevelProject(positions[b], axis); var pc = BevelProject(positions[c], axis);

                    if (BevelOrientation(pa, pb, pc) * sign <= 0) continue;

                    bool occupied = false;

                    foreach (int point in remaining)

                    {

                        if (point == a || point == b || point == c) continue;

                        var p = BevelProject(positions[point], axis);

                        if (BevelOrientation(pa, pb, p) * sign >= 0 && BevelOrientation(pb, pc, p) * sign >= 0 && BevelOrientation(pc, pa, p) * sign >= 0) { occupied = true; break; }

                    }

                    if (occupied) continue;

                    triangles.Add(a); triangles.Add(b); triangles.Add(c); remaining.RemoveAt(i); found = true; break;

                }

                if (!found) throw new BevelOverlap("The expanded polygon cannot be triangulated safely.");

            }

            if (remaining.Count == 3)

            {

                if (BevelOrientation(BevelProject(positions[remaining[0]], axis), BevelProject(positions[remaining[1]], axis), BevelProject(positions[remaining[2]], axis)) == 0)

                    throw new BevelOverlap("The bevel created a zero-area triangle.");

                triangles.AddRange(remaining);

            }

            return triangles;

        }



#if MASHBOX_STUDIO_TESTS
        private BevelResult WriteBevelDraftReference(StudioModelDocument.Node node, BevelDraft draft, int edgeCount, Action beforeWrite, double epsilon)

        {
            var polygons=new List<BevelPolygon>();
            foreach (int face in draft.Plan.StaticFaces) polygons.Add(new BevelPolygon { OriginalFace=face, Material=Faces[face].Submesh });
            polygons.AddRange(draft.Polygons);


            var mesh = node.mesh; var colors = mesh.colors; var tangents = mesh.tangents;
            var originalNormals = mesh.normals;
            if (originalNormals.Length != Positions.Length)
            {
                // No authored normals: use area-weighted normals for retained
                // source vertices rather than publishing zero-valued normals.
                originalNormals = new Vector3[Positions.Length];
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var triangles = mesh.GetTriangles(sub);
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                        var normal = Vector3.Cross(Positions[b] - Positions[a], Positions[c] - Positions[a]);
                        originalNormals[a] += normal; originalNormals[b] += normal; originalNormals[c] += normal;
                    }
                }
                for (int i = 0; i < originalNormals.Length; i++) originalNormals[i] = BevelUnit(originalNormals[i]);
            }

            var sourceUV = new List<Vector4>[8]; var uv = new List<Vector4>[8]; var dimensions = new int[8];

            for (int c = 0; c < 8; c++) { sourceUV[c] = new List<Vector4>(); mesh.GetUVs(c, sourceUV[c]); if (sourceUV[c].Count > 0) { uv[c] = new List<Vector4>(); dimensions[c] = mesh.GetVertexAttributeDimension((VertexAttribute)((int)VertexAttribute.TexCoord0 + c)); } }

            var positions = new List<Vector3>(); var normals = new List<Vector3>(); var outputColors = new List<Color>(); var outputTangents = new List<Vector4>();

            var groups = new List<int>(); var smoothing = new Dictionary<(int, Vector3), int>();

            var variants = new Dictionary<(int geo, int group, int hash), List<int>>();

            var render = new int[draft.Vertices.Count]; var values = new Vector4[8];

            var used = new bool[draft.Vertices.Count];

            foreach (var polygon in polygons)
                if (polygon.OriginalFace < 0) foreach (int vertex in polygon.Vertices) used[vertex] = true;

            for (int v = 0; v < draft.Vertices.Count; v++)

            {

                if (!used[v]) continue;

                var vertex = draft.Vertices[v]; var s = vertex.Sample; var normal = vertex.Normal.normalized;

                if (!BevelFinite(draft.Positions[vertex.Geo]) || !BevelFinite(normal)) throw new BevelOverlap("The bevel produced non-finite coordinates.");

                var key = (vertex.Geo, normal); if (!smoothing.TryGetValue(key, out int group)) { group = smoothing.Count; smoothing.Add(key, group); }

                Color color = colors.Length == Positions.Length ? colors[s.A] * s.Weights.x + colors[s.B] * s.Weights.y + colors[s.C] * s.Weights.z : Color.white;

                Vector4 tangent = tangents.Length == Positions.Length ? tangents[s.A] * s.Weights.x + tangents[s.B] * s.Weights.y + tangents[s.C] * s.Weights.z : Vector4.zero;

                int hash = color.GetHashCode();

                for (int c = 0; c < 8; c++) if (uv[c] != null)

                { values[c] = sourceUV[c][s.A] * s.Weights.x + sourceUV[c][s.B] * s.Weights.y + sourceUV[c][s.C] * s.Weights.z; unchecked { hash = hash * 31 + values[c].GetHashCode(); } }

                if (uv[0] == null) unchecked { hash = hash * 31 + tangent.GetHashCode(); }

                var variant = (vertex.Geo, group, hash);

                if (!variants.TryGetValue(variant, out var candidates)) variants.Add(variant, candidates = new List<int>());

                int match = -1;

                foreach (int candidate in candidates)

                {

                    if (colors.Length > 0 && !outputColors[candidate].Equals(color)) continue;

                    if (uv[0] == null && tangents.Length > 0 && !outputTangents[candidate].Equals(tangent)) continue;

                    bool same = true; for (int c = 0; c < 8; c++) if (uv[c] != null && !uv[c][candidate].Equals(values[c])) { same = false; break; }

                    if (same) { match = candidate; break; }

                }

                if (match < 0)

                {

                    match = positions.Count; candidates.Add(match); positions.Add(draft.Positions[vertex.Geo]); normals.Add(normal); groups.Add(group);

                    if (colors.Length > 0) outputColors.Add(color); if (tangents.Length > 0) outputTangents.Add(tangent);

                    for (int c = 0; c < 8; c++) if (uv[c] != null) uv[c].Add(values[c]);

                }

                render[v] = match;

            }

            var indices = new List<int>[mesh.subMeshCount]; for (int i = 0; i < indices.Length; i++) indices[i] = new List<int>();

            var metadata = new List<int>();
            // Untouched faces retain their authored triangle diagonals and exact
            // vertex attributes. An unrelated tiny/degenerate source triangle must
            // not invalidate a local bevel elsewhere on an imported production mesh.
            var originalTriangles = BevelSourceTriangles(mesh);
            var originalRender = new Dictionary<int, int>();
            int OriginalVertex(int source)
            {
                if (originalRender.TryGetValue(source, out int result)) return result;
                result = positions.Count; originalRender.Add(source, result); positions.Add(Positions[source]);
                var normal = originalNormals.Length == Positions.Length ? originalNormals[source] : Vector3.zero;
                normals.Add(normal);
                var key = (welded[source], normal.normalized);
                if (!smoothing.TryGetValue(key, out int group)) { group = smoothing.Count; smoothing.Add(key, group); }
                groups.Add(group);
                if (colors.Length > 0) outputColors.Add(colors[source]);
                if (tangents.Length > 0) outputTangents.Add(tangents[source]);
                for (int c = 0; c < 8; c++) if (uv[c] != null) uv[c].Add(sourceUV[c][source]);
                return result;
            }

            foreach (var polygon in polygons)

            {

                if (polygon.OriginalFace >= 0)
                {
                    var face = Faces[polygon.OriginalFace]; var triangles = originalTriangles[polygon.OriginalFace];
                    if (triangles.Count != (face.Vertices.Count - 2) * 3)
                        throw new BevelOverlap("An untouched source face has no matching render triangulation.");
                    metadata.Add(face.Submesh); metadata.Add(face.Vertices.Count);
                    foreach (int vertex in face.Vertices) metadata.Add(OriginalVertex(vertex));
                    foreach (int vertex in triangles) indices[face.Submesh].Add(OriginalVertex(vertex));
                    continue;
                }
                var vertices = new List<int>(polygon.Vertices.Count); foreach (int v in polygon.Vertices) vertices.Add(render[v]);

                metadata.Add(polygon.Material); metadata.Add(vertices.Count); metadata.AddRange(vertices);

                indices[polygon.Material].AddRange(BevelTriangulate(vertices, positions, epsilon));

            }

            // All topology, interpolation and triangulation is complete before Undo

            // registration and the first mutation of the live mesh.

            beforeWrite?.Invoke();

            mesh.Clear(); mesh.indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            mesh.SetVertices(positions); mesh.SetNormals(normals); if (colors.Length > 0) mesh.SetColors(outputColors);

            for (int c = 0; c < 8; c++) if (uv[c] != null)

            {

                if (dimensions[c] == 2) { var data = new List<Vector2>(positions.Count); foreach (var value in uv[c]) data.Add(new Vector2(value.x, value.y)); mesh.SetUVs(c, data); }

                else if (dimensions[c] == 3) { var data = new List<Vector3>(positions.Count); foreach (var value in uv[c]) data.Add(new Vector3(value.x, value.y, value.z)); mesh.SetUVs(c, data); }

                else mesh.SetUVs(c, uv[c]);

            }

            mesh.subMeshCount = indices.Length; for (int i = 0; i < indices.Length; i++) mesh.SetTriangles(indices[i], i, false);

            if (uv[0] != null)
            {
                mesh.RecalculateTangents();
                if (tangents.Length == Positions.Length && originalRender.Count > 0)
                {
                    mesh.GetTangents(outputTangents);
                    foreach (var pair in originalRender) outputTangents[pair.Value] = tangents[pair.Key];
                    mesh.SetTangents(outputTangents);
                }
            }
            else if (tangents.Length > 0) mesh.SetTangents(outputTangents);

            mesh.RecalculateBounds(); mesh.UploadMeshData(false);

            node.cutPolygons = metadata.ToArray(); node.cutTriangles = mesh.triangles; node.normalGroups = groups.ToArray();

            var result = new BevelResult { AppliedWidth = draft.Width, Edges = edgeCount, AddedFaces = draft.Polygons.Count + draft.Plan.StaticFaces.Count - (bevelInputFaceCount > 0 ? bevelInputFaceCount : Faces.Count) };

            foreach (var edge in draft.Selection) result.Selection.Add(NormalEdgePositionKeyForBevel(draft.Positions[edge.Item1], draft.Positions[edge.Item2]));

            return result;

        }

#endif
        internal static (Vector3, Vector3) NormalEdgePositionKeyForBevel(Vector3 a, Vector3 b)

        {

            int c = a.x.CompareTo(b.x); if (c == 0) c = a.y.CompareTo(b.y); if (c == 0) c = a.z.CompareTo(b.z);

            return c <= 0 ? (a, b) : (b, a);

        }

    }

}


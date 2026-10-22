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

            internal float Width;

            internal double Precision;

            internal Vector3[] Originals;

            private readonly Dictionary<(int owner, long x, long y, long z), List<int>> pointBins = new Dictionary<(int, long, long, long), List<int>>();

            internal int AddPoint(Vector3 point, int owner)

            {

                var delta = point - Originals[owner];

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



        internal BevelResult BevelEdges(StudioModelDocument.Node node, IEnumerable<(int a, int b)> selected,

            BevelOptions options, Action beforeWrite = null)

        {

            var mesh = node.mesh;

            if (!mesh || !mesh.isReadable || node.skinned || node.blendShape != null || mesh.blendShapeCount != 0 || mesh.bindposes.Length != 0 || mesh.HasVertexAttribute(VertexAttribute.BlendWeight))

                throw new InvalidOperationException("Bevel requires an editable static mesh. Bake skinning and blend shapes first.");

            if (!BevelFinite(options.Width) || options.Width < 0 || options.Width > 1 ||

                !BevelFinite(options.Tension) || options.Tension < -1 || options.Tension > 1 ||

                options.Subdivisions < 0 || options.Subdivisions > 32 || !Enum.IsDefined(typeof(BevelJunction), options.Junction))

                throw new InvalidOperationException("Bevel needs width 0–1, tension −1–1, and 0–32 internal subdivisions.");

            PrepareBevelTopology(mesh);

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

            BevelDraft draft = null;

            string overlap = null;

            // Resolve local overlap before touching the live mesh. Normalized width

            // reaches its useful maximum without silently inverting small faces.

            for (int attempt = 0; attempt < 12; attempt++)

            {

                try { draft = BuildBevelDraft(node, edges, incident, targets, affected, normals, options, options.Width / (1 << attempt), epsilon); break; }

                catch (BevelOverlap e) { overlap = e.Message; }

            }

            if (draft == null) throw new InvalidOperationException("Cannot bevel this selection without overlap: " + overlap);

            return WriteBevelDraft(node, draft, targets.Count, beforeWrite, epsilon);

        }



        private BevelDraft BuildBevelDraft(StudioModelDocument.Node node, Dictionary<(int, int), BevelEdge> edges,

            Dictionary<int, List<BevelEdge>> incident, HashSet<(int, int)> targets, HashSet<int> affected,

            Vector3[] faceNormals, BevelOptions options, float width, double epsilon)

        {

            int count = 0; foreach (int id in welded) count = Math.Max(count, id + 1);

            var originals = new Vector3[count];

            for (int i = 0; i < Positions.Length; i++) originals[welded[i]] = Positions[i];

            var draft = new BevelDraft { Width = width, Originals = originals, Precision = epsilon * 2 };

            for (int i = 0; i < count; i++) draft.AddPoint(originals[i], i);

            var distances = new Dictionary<int, float>();

            foreach (int vertex in affected)

            {

                float length = float.PositiveInfinity;

                foreach (var edge in incident[vertex]) length = Mathf.Min(length, Vector3.Distance(originals[edge.A], originals[edge.B]));

                float distance = length * width * .45f;

                if (distance <= epsilon) throw new BevelOverlap("Width is below the mesh's geometric precision.");

                distances.Add(vertex, distance);

            }

            var cuts = new Dictionary<(int vertex, int other), int>();

            int Cut(int vertex, int other)

            {

                if (cuts.TryGetValue((vertex, other), out int id)) return id;

                var direction = originals[other] - originals[vertex];

                id = draft.AddPoint(originals[vertex] + BevelUnit(direction) * distances[vertex], vertex);

                cuts.Add((vertex, other), id); return id;

            }

            var corners = new List<int>[Faces.Count][];

            var sourceNormals = node.mesh.normals;

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

            void AddFace(List<int> geometry, int sourceFace, int material, List<Vector3> normals = null)

            {

                if (geometry.Count < 3) return;

                var polygon = new BevelPolygon { Material = material };

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

            for (int f = 0; f < Faces.Count; f++)

            {

                var face = Faces[f]; var v = face.Vertices; corners[f] = new List<int>[v.Count];

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

                        else if (!incoming) path.Add(Cut(vertex, previous));

                        else if (!outgoing) path.Add(Cut(vertex, next));

                        else

                        {

                            Vector3 u = BevelUnit(originals[vertex] - originals[previous]), w = BevelUnit(originals[next] - originals[vertex]);

                            Vector3 inwardA = Vector3.Cross(faceNormals[f], u), inwardB = Vector3.Cross(faceNormals[f], w);

                            float cosine = Vector3.Dot(inwardA, inwardB), denominator = 1 + cosine;

                            if (denominator < 1e-5f) throw new BevelOverlap("An acute corner leaves no room for the inset rails.");

                            Vector3 point = originals[vertex] + (inwardA + inwardB) * (distances[vertex] / denominator);

                            if (Vector3.Distance(point, originals[vertex]) > distances[vertex] / width * 1.1f)

                                throw new BevelOverlap("The inset miter runs past an adjacent edge.");

                            path.Add(draft.AddPoint(point, vertex));

                        }

                    }

                    if (affected.Contains(vertex)) foreach (int point in path) draft.JunctionAnchors.Add(point);

                    polygon.AddRange(path);

                }

                if (changed) BevelValidateInset(polygon, draft.Positions, v, Positions, faceNormals[f], epsilon);

                AddFace(polygon, f, face.Submesh);

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

                var controlA = originals[va] - tangent * Vector3.Dot(originals[va] - (draft.Positions[pa] + draft.Positions[qa]) * .5f, tangent);

                var controlB = originals[vb] - tangent * Vector3.Dot(originals[vb] - (draft.Positions[pb] + draft.Positions[qb]) * .5f, tangent);

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

                    AddFace(new List<int> { b[k], a[k], a[k + 1], b[k + 1] }, source, Faces[source].Submesh,

                        new List<Vector3> { nb[k], na[k], na[k + 1], nb[k + 1] });

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

            var origin = draft.Positions[ring[0]];

            foreach (int point in ring)

                if (Math.Abs(Vector3.Dot(draft.Positions[point] - origin, normal)) > epsilon * 4) return false;

            foreach (int index in candidates)

            {

                // Only absorb a newly generated cap into an original inset face.

                // Never dissolve unrelated source faces or the curved bevel strips.

                if (index >= Faces.Count) continue;

                var polygon = draft.Polygons[index];

                var geometry = new List<int>(polygon.Vertices.Count);

                foreach (int vertex in polygon.Vertices) geometry.Add(draft.Vertices[vertex].Geo);

                // A concave profile makes the old cap fold back over this face.

                // Replacing the shared chord fixes that case too; the ring may

                // therefore have the opposite geometric normal before merging.

                if (Math.Abs(Vector3.Dot(BevelPolygonNormal(geometry, draft.Positions), normal)) < .9999f) continue;

                bool coplanar = true;

                foreach (int point in geometry)

                    if (Math.Abs(Vector3.Dot(draft.Positions[point] - origin, normal)) > epsilon * 4) { coplanar = false; break; }

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

        private static List<int> BevelTriangulate(IList<int> polygon, IList<Vector3> positions, double epsilon)

        {

            var normal = BevelPolygonNormal(polygon, positions); int axis = BevelAxis(normal);

            double sign = axis == 0 ? normal.x : axis == 1 ? normal.y : normal.z; sign = sign >= 0 ? 1 : -1;

            float longest = 0;

            for (int i = 0; i < polygon.Count; i++) longest = Mathf.Max(longest, Vector3.Distance(positions[polygon[i]], positions[polygon[(i + 1) % polygon.Count]]));

            double tolerance = epsilon * Math.Max(epsilon, longest);

            var remaining = new List<int>(polygon); var triangles = new List<int>((polygon.Count - 2) * 3);

            while (remaining.Count > 3)

            {

                bool found = false;

                for (int i = 0; i < remaining.Count; i++)

                {

                    int a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i], c = remaining[(i + 1) % remaining.Count];

                    var pa = BevelProject(positions[a], axis); var pb = BevelProject(positions[b], axis); var pc = BevelProject(positions[c], axis);

                    if (BevelCross(pa, pb, pc) * sign <= tolerance) continue;

                    bool occupied = false;

                    foreach (int point in remaining)

                    {

                        if (point == a || point == b || point == c) continue;

                        var p = BevelProject(positions[point], axis);

                        if (BevelCross(pa, pb, p) * sign >= -tolerance && BevelCross(pb, pc, p) * sign >= -tolerance && BevelCross(pc, pa, p) * sign >= -tolerance) { occupied = true; break; }

                    }

                    if (occupied) continue;

                    triangles.Add(a); triangles.Add(b); triangles.Add(c); remaining.RemoveAt(i); found = true; break;

                }

                if (!found) throw new BevelOverlap("The expanded polygon cannot be triangulated safely.");

            }

            if (remaining.Count == 3)

            {

                if (Vector3.Cross(positions[remaining[1]] - positions[remaining[0]], positions[remaining[2]] - positions[remaining[0]]).sqrMagnitude <= tolerance * tolerance)

                    throw new BevelOverlap("The bevel created a zero-area triangle.");

                triangles.AddRange(remaining);

            }

            return triangles;

        }



        private BevelResult WriteBevelDraft(StudioModelDocument.Node node, BevelDraft draft, int edgeCount, Action beforeWrite, double epsilon)

        {

            var mesh = node.mesh; var colors = mesh.colors; var tangents = mesh.tangents;

            var sourceUV = new List<Vector4>[8]; var uv = new List<Vector4>[8]; var dimensions = new int[8];

            for (int c = 0; c < 8; c++) { sourceUV[c] = new List<Vector4>(); mesh.GetUVs(c, sourceUV[c]); if (sourceUV[c].Count > 0) { uv[c] = new List<Vector4>(); dimensions[c] = mesh.GetVertexAttributeDimension((VertexAttribute)((int)VertexAttribute.TexCoord0 + c)); } }

            var positions = new List<Vector3>(); var normals = new List<Vector3>(); var outputColors = new List<Color>(); var outputTangents = new List<Vector4>();

            var groups = new List<int>(); var smoothing = new Dictionary<(int, Vector3), int>();

            var variants = new Dictionary<(int geo, int group, int hash), List<int>>();

            var render = new int[draft.Vertices.Count]; var values = new Vector4[8];

            var used = new bool[draft.Vertices.Count];

            foreach (var polygon in draft.Polygons) foreach (int vertex in polygon.Vertices) used[vertex] = true;

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

            foreach (var polygon in draft.Polygons)

            {

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

            if (uv[0] != null) mesh.RecalculateTangents(); else if (tangents.Length > 0) mesh.SetTangents(outputTangents);

            mesh.RecalculateBounds(); mesh.UploadMeshData(false);

            node.cutPolygons = metadata.ToArray(); node.cutTriangles = mesh.triangles; node.normalGroups = groups.ToArray();

            var result = new BevelResult { AppliedWidth = draft.Width, Edges = edgeCount, AddedFaces = draft.Polygons.Count - Faces.Count };

            foreach (var edge in draft.Selection) result.Selection.Add(NormalEdgePositionKeyForBevel(draft.Positions[edge.Item1], draft.Positions[edge.Item2]));

            return result;

        }

        internal static (Vector3, Vector3) NormalEdgePositionKeyForBevel(Vector3 a, Vector3 b)

        {

            int c = a.x.CompareTo(b.x); if (c == 0) c = a.y.CompareTo(b.y); if (c == 0) c = a.z.CompareTo(b.z);

            return c <= 0 ? (a, b) : (b, a);

        }

    }

}


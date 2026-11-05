using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioEdgeRingCollapseValidation
    {
        static string Out(string file) => Path.GetFullPath(Path.Combine(Application.dataPath, "../../EdgeRingCollapse/" + file));
        [InitializeOnLoadMethod] static void Boot()
        {
            if (Application.dataPath.Replace('\\', '/').EndsWith("/ThemeIsolationProject/Assets") && File.Exists(Out("request.txt")))
                EditorApplication.update += Next;
        }
        static void Next() { EditorApplication.update -= Next; Run(); }
        static StudioModelDocument.Node Make(Vector3[] points, params int[][] faces)
        {
            var mesh = new Mesh(); mesh.vertices = points;
            var triangles = new List<int>();
            foreach (var face in faces) for (int i = 1; i + 1 < face.Length; i++) triangles.AddRange(new[] { face[0], face[i], face[i + 1] });
            mesh.triangles = triangles.ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return new StudioModelDocument.Node { name = "Ring check", mesh = mesh, cutTriangles = mesh.triangles,
                cutPolygons = faces.SelectMany(f => new[] { 0, f.Length }.Concat(f)).ToArray(), geometryVertices = Enumerable.Range(0, points.Length).ToArray() };
        }
        static StudioModelDocument.Node Grid()
        {
            var p = Enumerable.Range(0, 16).Select(v => new Vector3(v % 4, v / 4, 0)).ToArray();
            var f = new List<int[]>(); for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
            { int v = y * 4 + x; f.Add(new[] { v, v + 1, v + 5, v + 4 }); }
            return Make(p, f.ToArray());
        }
        static object Call(StudioModelEditor editor, string name) => typeof(StudioModelEditor).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editor, null);
        static int Edge(StudioModelEditor editor, int a, int b)
        {
            var edges = (List<(int a, int b)>)Call(editor, "Edges");
            return edges.FindIndex(e => (e.a == a && e.b == b) || (e.a == b && e.b == a));
        }
        public static void Run()
        {
            if (!Application.dataPath.Replace('\\', '/').EndsWith("/ThemeIsolationProject/Assets")) return;
            File.Delete(Out("request.txt")); var log = new List<string>(); var owned = new List<Mesh>();
            StudioModelEditor editor = null; StudioModelDocument doc = null;
            void Check(bool ok, string label) { if (!ok) throw new Exception(label); log.Add("PASS " + label); }
            StudioModelDocument.Node Track(StudioModelDocument.Node n) { owned.Add(n.mesh); return n; }
            void Reject(Action action, StudioModelDocument.Node n, string label)
            {
                var before = StudioModifierSource.Capture(n); bool rejected = false;
                try { action(); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected && before.mesh == StudioModifierSource.Capture(n).mesh && before.polygons.SequenceEqual(n.cutPolygons), label);
            }
            try
            {
                var n = Track(Grid()); var source = StudioModifierSource.Capture(n); var topology = new StudioMeshCuts(n); var rings = topology.CaptureEdgeRings();
                int seed = rings.Index[(4, 5)]; var ring = rings.Collect(seed);
                Check(ring.Length == 4 && ring.Select(id => rings.Edges[id]).All(e => Math.Abs(e.b - e.a) == 1), "Opposite-index traversal walks both directions to boundaries");
                topology.CollapseEdgeRings(n, new[] { (4, 5), (8, 9), (5, 4) });
                Check(new StudioMeshCuts(n).Faces.Count == 6 && n.mesh.vertexCount == 12, "Repeated seeds collapse each ring edge once and compact orphan vertices");
                Check(n.mesh.vertices.Count(p => p.x == .5f) == 4 && n.mesh.vertices.All(p => p.x == .5f || p.x == 2 || p.x == 3), "Each endpoint pair reaches its own midpoint; unrelated columns stay fixed");
                Check(new StudioMeshCuts(n).Faces.All(f => f.Vertices.Count == 4), "Crushed quads are deleted as whole faces; retained quads stay genuine quads");
                Check(n.mesh.triangles.Length == 36 && n.mesh.triangles.All(v => v >= 0 && v < n.mesh.vertexCount), "Retained render tessellation has valid compacted indices");
                var replay = Track(source.Restore()); var operation = new StudioModifierOperation { kind = "EdgeRingCollapse", edges = new[] { 4, 5 }, name = "Ring" }; operation.Bind(replay);
                StudioModifierEvaluator.Apply(replay, operation);
                Check(replay.mesh.vertices.SequenceEqual(n.mesh.vertices) && replay.cutPolygons.SequenceEqual(n.cutPolygons), "Compact saved seed-edge modifier replays identical topology and positions");

                // A six-sided closed band, bounded by non-quad caps and neighboring bands.
                var points = Enumerable.Range(0, 24).Select(v => { float a = (v % 6) * Mathf.PI / 3; return new Vector3(Mathf.Cos(a), v / 6, Mathf.Sin(a)); }).ToArray();
                var faces = new List<int[]>(); for (int y = 0; y < 3; y++) for (int x = 0; x < 6; x++)
                { int next = (x + 1) % 6; faces.Add(new[] { y * 6 + x, y * 6 + next, (y + 1) * 6 + next, (y + 1) * 6 + x }); }
                faces.Add(Enumerable.Range(0, 6).Reverse().ToArray()); faces.Add(Enumerable.Range(18, 6).ToArray());
                var tube = Track(Make(points, faces.ToArray())); var tubeTopology = new StudioMeshCuts(tube); var tubeRings = tubeTopology.CaptureEdgeRings();
                Check(tubeRings.Collect(tubeRings.Index[(6, 12)]).Length == 6, "Closed ring terminates at its start without collecting twice");
                tubeTopology.CollapseEdgeRings(tube, new[] { (6, 12) });
                Check(new StudioMeshCuts(tube).Faces.Count == 14 && tube.mesh.vertexCount == 18 && tube.mesh.vertices.Count(p => p.y == 1.5f) == 6, "Closed quad band fuses at midpoints and preserves both adjacent bands and caps");

                var stop = Track(Make(new[] { Vector3.zero, Vector3.right, Vector3.one, Vector3.up, new Vector3(2, 0, 0) }, new[] { 0, 1, 2, 3 }, new[] { 1, 4, 2 }));
                var stopRings = new StudioMeshCuts(stop).CaptureEdgeRings();
                Check(stopRings.Collect(stopRings.Index[(0, 3)]).Length == 2, "Traversal collects the exit edge and stops at a non-quad");
                var invalid = Track(Grid());
                Reject(() => new StudioMeshCuts(invalid).CollapseEdgeRings(invalid, new[] { (0, 99) }), invalid, "Invalid edge rejection leaves source untouched");
                Reject(() => new StudioMeshCuts(invalid).CollapseEdgeRings(invalid, new[] { (4, 5), (5, 6) }), invalid, "Intersecting midpoint pairs reject atomically");
                Reject(() => new StudioMeshCuts(invalid).CollapseEdgeRings(invalid, Array.Empty<(int, int)>()), invalid, "Empty selection rejects without mutation");
                var manifold = Track(Make(new[] { Vector3.zero, Vector3.right, Vector3.one, Vector3.up, Vector3.forward }, new[] { 0, 1, 2, 3 }, new[] { 1, 0, 4 }, new[] { 0, 1, 4 }));
                Reject(() => new StudioMeshCuts(manifold).CollapseEdgeRings(manifold, new[] { (0, 1) }), manifold, "Non-manifold ring rejects without mutation");

                var seam = Track(Grid()); var uv = seam.mesh.vertices.Select(p => new Vector4(p.x, p.y, 7, 9)).ToList();
                seam.mesh.SetUVs(0, uv); seam.mesh.SetUVs(3, uv); seam.mesh.colors = seam.mesh.vertices.Select(p => new Color(p.x / 3, p.y / 3, .5f)).ToArray();
                new StudioMeshCuts(seam).CollapseEdgeRings(seam, new[] { (4, 5) }); var keptUV = new List<Vector4>(); seam.mesh.GetUVs(3, keptUV);
                Check(keptUV.Count == seam.mesh.vertexCount && keptUV.All(v => v.z == 7 && v.w == 9) && seam.mesh.colors.Length == seam.mesh.vertexCount, "Compaction preserves colors and all UV dimensions/channels");
                Check(seam.mesh.uv.Where((v, i) => seam.mesh.vertices[i].x == .5f).All(v => v.x == 1), "Surviving seam corners keep their own source attributes");

                var baseGrid = Track(Grid()); var baseFaces = new StudioMeshCuts(baseGrid).Faces;
                var ids = baseFaces.SelectMany(f => f.Vertices).ToArray();
                var split = Track(Make(ids.Select(v => baseGrid.mesh.vertices[v]).ToArray(), Enumerable.Range(0, 9).Select(f => Enumerable.Range(f * 4, 4).ToArray()).ToArray()));
                split.geometryVertices = ids; split.mesh.uv = Enumerable.Range(0, 36).Select(v => new Vector2(v / 4, v % 4)).ToArray();
                var splitSourceUV = split.mesh.uv;
                var splitRings = new StudioMeshCuts(split).CaptureEdgeRings();
                Check(splitRings.Collect(0).Length == 4, "Ring traversal crosses render/UV seams using authored geometric IDs");
                new StudioMeshCuts(split).CollapseEdgeRings(split, new[] { (0, 1) });
                Check(new StudioMeshCuts(split).Faces.Count == 6 && split.geometryVertices.Distinct().Count() == 12 && split.mesh.uv.All(v => splitSourceUV.Contains(v)), "Fusion shares geometric identities while preserving surviving UV seam variants");
                var empty = Track(Make(new[] { Vector3.zero, Vector3.right, Vector3.one, Vector3.up }, new[] { 0, 1, 2, 3 }));
                new StudioMeshCuts(empty).CollapseEdgeRings(empty, new[] { (0, 1) });
                Check(empty.mesh.vertexCount == 0 && empty.mesh.triangles.Length == 0 && empty.cutPolygons.Length == 0, "Fully crushed faces leave no orphaned vertices");

                doc = ScriptableObject.CreateInstance<StudioModelDocument>(); var live = Track(Grid()); doc.nodes.Add(live);
                doc.nodes.Add(new StudioModelDocument.Node { name = "Linked", mesh = live.mesh, cutPolygons = live.cutPolygons, cutTriangles = live.cutTriangles, geometryVertices = live.geometryVertices });
                editor = new StudioModelEditor(() => { }); editor.Load(doc); editor.SelectObject(0); editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge); editor.Components.Add(Edge(editor, 4, 5));
                Call(editor, "CollapseEdgeRings"); live = doc.nodes[0];
                Check(live.mesh.vertexCount == 12 && live.modifiers.operations.Last().kind == "EdgeRingCollapse" && editor.Components.Count == 0, "Editor commits the command with compact history and clears deleted edge IDs");
                Check(doc.nodes[1].mesh == live.mesh && doc.nodes[1].cutPolygons.SequenceEqual(live.cutPolygons) && doc.nodes[1].modifiers == live.modifiers, "Linked mesh topology and history update together");
                UnityEditor.Undo.FlushUndoRecordObjects(); StudioUndo.IncrementCurrentGroup(); StudioUndo.PerformUndo();
                Check(doc.nodes[0].mesh.vertexCount == 16 && new StudioMeshCuts(doc.nodes[0]).Faces.Count == 9 && editor.Components.Count == 1, "Native StudioUndo restores original mesh, topology and seed selection");
                StudioUndo.PerformRedo();
                Check(doc.nodes[0].mesh.vertexCount == 12 && editor.Components.Count == 0, "Native Redo restores collapsed topology and cleared selection");
                var evaluated = Track(StudioModifierEvaluator.Evaluate(doc.nodes[0].modifiers));
                Check(evaluated.mesh.vertices.SequenceEqual(doc.nodes[0].mesh.vertices) && evaluated.cutPolygons.SequenceEqual(doc.nodes[0].cutPolygons), "Editor history evaluates the same final mesh");
                editor.Components.Add(Edge(editor, 1, 2)); Call(editor, "RepeatLastAction");
                Check(doc.nodes[0].mesh.vertexCount == 8 && doc.nodes[0].modifiers.operations.Count == 2, "G repeats the completed operation against a fresh current edge selection");
            }
            catch (Exception e) { log.Add("FAIL " + e); }
            finally
            {
                editor?.Dispose(); foreach (var mesh in owned.Where(m => m).Distinct()) Object.DestroyImmediate(mesh); if (doc) Object.DestroyImmediate(doc);
                File.WriteAllLines(Out("result.txt"), log);
            }
        }
    }
}

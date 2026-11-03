using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioRangeWeldValidation
    {
        private static StudioModelDocument.Node Make()
        {
            var points = new List<Vector3>(); var faces = new List<int>(); var triangles = new List<int>();
            foreach (float x in new[] { 0f, .02f, 1f, 1.02f })
            {
                int a = points.Count;
                points.Add(new Vector3(x, 0, 0)); points.Add(new Vector3(x, 1, .1f)); points.Add(new Vector3(x, 1, -.1f));
                triangles.AddRange(new[] { a, a + 1, a + 2 }); faces.AddRange(new[] { 0, 3, a, a + 1, a + 2 });
            }
            points.Add(new Vector3(.005f, 0, 0));
            var mesh = new Mesh(); mesh.SetVertices(points); mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, points.Select((p, i) => new Vector2(i, p.y)).ToList()); mesh.RecalculateNormals();
            return new StudioModelDocument.Node { mesh = mesh, cutTriangles = mesh.triangles, cutPolygons = faces.ToArray(), geometryVertices = Enumerable.Range(0, points.Count).ToArray() };
        }
        public static void Run()
        {
            if (!Application.dataPath.EndsWith("BevelJunctionProject/Assets")) return;
            var log = new List<string>(); var selection = new[] { 0, 3, 6, 9 };
            void Check(bool value, string message) { if (!value) throw new Exception(message); log.Add("PASS " + message); }
            try
            {
                var doc = ScriptableObject.CreateInstance<StudioModelDocument>(); var node = Make(); doc.nodes.Add(node);
                using (var preview = new StudioMeshToolPreview(doc, new[] { 0 }))
                {
                    var target = preview.Targets[0]; var original = node.mesh.vertices;
                    Check(target.Topology.MergeVertices(target.Work, selection, false, .015f, Matrix4x4.identity) == null, "Below-range pairs remain separate");
                    target.Reset();
                    var result = target.Topology.MergeVertices(target.Work, selection, false, .03f, Matrix4x4.identity);
                    preview.Publish(true);
                    Check(result.Removed == 2 && node.mesh.vertices.Any(p => Mathf.Abs(p.x - .01f) < 1e-6f && p.y == 0) && node.mesh.vertices.Any(p => Mathf.Abs(p.x - 1.01f) < 1e-6f && p.y == 0), "Two nearby groups weld independently");
                    Check(node.mesh.vertices.Contains(new Vector3(.005f, 0, 0)) && new StudioMeshCuts(node).Faces.Count == 4, "Unselected point and authored faces remain");
                    preview.Restore(); Check(original.SequenceEqual(node.mesh.vertices), "Cancel restores the captured source");
                    target.Reset(); Check(target.Topology.MergeVertices(target.Work, selection, false, .03f, Matrix4x4.Scale(Vector3.one * 2)) == null, "Range measures world-space scale");
                    target.Reset();
                    StudioModifierEvaluator.Apply(target.Work, new StudioModifierOperation { kind = "Weld", vertices = selection, parameters = new Vector4(.05f, 0, 0, 0), world = Matrix4x4.Scale(Vector3.one * 2) });
                    Check(target.Work.geometryVertices.Distinct().Count() == original.Length - 2, "Saved range modifier replays the weld");
                }
                UnityEngine.Object.DestroyImmediate(node.mesh); UnityEngine.Object.DestroyImmediate(doc);
            }
            catch (Exception e) { log.Add("FAIL " + e); }
            File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath, "../../range-weld-result.txt")), log);
        }
    }
}

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
    public static class StudioQuadEdgesValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> Results = new List<string>();
        static AnimationStudioWindow controller; static AnimationStudioViewport view; static StudioModelEditor editor;
        static string oldModel, errors = ""; static bool hadModel; static int step; static double next;
        static string ModelKey => "MashBox.LastModel." + Application.dataPath;
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        static List<(int a, int b)> Edges(StudioModelEditor ed) => (List<(int, int)>)typeof(StudioModelEditor).GetMethod("Edges", Private).Invoke(ed, null);
        public static void Run()
        {
            hadModel = EditorPrefs.HasKey(ModelKey); oldModel = EditorPrefs.GetString(ModelKey);
            try
            {
                var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Quad Edges.asset"));
                int cube = doc.AddPrimitive(PrimitiveType.Cube), quad = doc.AddPrimitive(PrimitiveType.Quad);
                var triangle = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                AssetDatabase.AddObjectToAsset(triangle, doc); doc.nodes.Add(new StudioModelDocument.Node { name = "Triangle", mesh = triangle });
                using (var ed = new StudioModelEditor(() => {}))
                {
                    ed.Load(doc); ed.SelectObject(cube);
                    var verts = ed.ActiveMesh.vertices; var edges = Edges(ed);
                    Check(edges.Count > 0 && edges.All(e => Mathf.Approximately((verts[e.a] - verts[e.b]).sqrMagnitude, 1)), "Cube edge-mode list excludes every face diagonal");
                    Check(edges.Select(e => (verts[e.a] + verts[e.b]) * .5f).Distinct().Count() == 12, "Cube has exactly twelve geometric boundary edges");
                    Check(ReferenceEquals(edges, Edges(ed)), "Edge drawing and picking reuse the same cached list");
                    ed.SelectObject(quad); Check(Edges(ed).Count == 4, "Quad exposes four boundary edges rather than five triangle edges");
                    ed.SelectObject(2); Check(Edges(ed).Count == 3, "Genuine triangle retains all three edges");
                    var node = doc.nodes[quad]; node.cutPolygons = new[] { 0, 4, 0, 1, 2, 3 }; node.cutTriangles = node.mesh.triangles;
                    var bent = node.mesh.vertices; bent[0].z += .2f; node.mesh.vertices = bent; node.mesh.RecalculateBounds();
                    ed.Load(doc); ed.SelectObject(quad);
                    Check(Edges(ed).Count == 4, "Authored quad boundaries survive non-planar deformation");
                }
                AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
                typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Private).Invoke(controller, new object[] { true });
                typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Private).Invoke(controller, new object[] { doc });
                editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Private).GetValue(controller); editor.SelectObject(0);
                editor.Transforms[1].gameObject.SetActive(false); editor.Transforms[2].gameObject.SetActive(false);
                view = Resources.FindObjectsOfTypeAll<AnimationStudioViewport>().First(); view.position = new Rect(100, 100, 1400, 800);
                view.pivot = Vector3.zero; view.rotation = Quaternion.identity; view.orthographic = true; view.size = 2; view.Focus();
                Application.logMessageReceived += Log; EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
            }
            catch(Exception e) { Finish(e.ToString()); }
        }
        static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack; }
        static void Click(Vector3 world)
        {
            var p = view.camera.WorldToScreenPoint(world); var mouse = new Vector2(p.x, view.camera.pixelHeight - p.y) / EditorGUIUtility.pixelsPerPoint;
            view.SendEvent(new Event { type = EventType.MouseMove, mousePosition = mouse });
            view.SendEvent(new Event { type = EventType.MouseDown, mousePosition = mouse, button = 0 });
            view.SendEvent(new Event { type = EventType.MouseUp, mousePosition = mouse, button = 0 });
        }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .5;
            try
            {
                switch(step++)
                {
                    case 0:
                        view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Alpha3 });
                        view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Q }); break;
                    case 1: Click(new Vector3(0, 0, -.5f)); break;
                    case 2:
                        Check(editor.Components.Count == 0, "Clicking the face-center diagonal does not select a hidden render edge");
                        Click(new Vector3(0, .5f, -.5f)); break;
                    case 3:
                        Check(editor.Components.Count == 1, "Clicking a visible quad boundary selects an edge");
                        var vertices = editor.ActiveMesh.vertices;
                        Check(editor.SelectedVertices().Select(i => vertices[i]).Distinct().Count() == 2, "Edge transform targets the visible boundary's two geometric endpoints");
                        Finish(null); break;
                }
            }
            catch(Exception e) { Finish(e.ToString()); }
        }
        static void Finish(string error)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            if (!string.IsNullOrEmpty(errors)) error += errors;
            if (!string.IsNullOrEmpty(error)) Results.Add("FAIL: " + error);
            if (view) view.Close(); if (controller) Object.DestroyImmediate(controller);
            if (hadModel) EditorPrefs.SetString(ModelKey, oldModel); else EditorPrefs.DeleteKey(ModelKey);
            File.WriteAllLines("quad-edges-result.txt", Results); EditorApplication.delayCall += () => EditorApplication.Exit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
}

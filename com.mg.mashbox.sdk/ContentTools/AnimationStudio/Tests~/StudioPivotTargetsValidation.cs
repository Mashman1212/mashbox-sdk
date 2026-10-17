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
    public static class StudioPivotTargetsValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> Results = new List<string>();
        static AnimationStudioWindow controller; static AnimationStudioViewport view; static StudioModelEditor editor;
        static string oldModel, errors = ""; static bool hadModel; static int step; static double next;
        static Vector3 beforePosition; static Quaternion beforeOrientation;
        static Vector2 mouse;
        static string ModelKey => "MashBox.LastModel." + Application.dataPath;
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        static Vector2 Screen(Camera camera, Vector3 world, float dpi) { var p = camera.WorldToScreenPoint(world); return new Vector2(p.x, camera.pixelHeight - p.y) / dpi; }
        static void CheckShape(StudioModelEditor ed, Vector3[] original, string label) => Check(original.Zip(ed.ActiveMesh.vertices, (a, b) => Vector3.Distance(a, ed.Transforms[ed.Active].TransformPoint(b)) < .0002f).All(x => x), label);
        public static void Run()
        {
            hadModel = EditorPrefs.HasKey(ModelKey); oldModel = EditorPrefs.GetString(ModelKey);
            try
            {
                var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Pivot Targets.asset"));
                int source = doc.AddPrimitive(PrimitiveType.Cube), target = doc.AddPrimitive(PrimitiveType.Cube);
                doc.nodes[source].position = new Vector3(-1.5f, 0, 0); doc.nodes[source].scale = new Vector3(1, 1.5f, .7f);
                doc.nodes[target].position = new Vector3(1.5f, 0, 0);
                doc.nodes.Add(new StudioModelDocument.Node { name = "Child", parent = source, position = Vector3.up });
                using (var ed = new StudioModelEditor(() => {}))
                {
                    ed.Load(doc); ed.SelectObject(source);
                    var camObject = new GameObject("Pivot target test camera"); var camera = camObject.AddComponent<Camera>();
                    camera.transform.position = new Vector3(0, 0, -6); camera.orthographic = true; camera.orthographicSize = 3; camera.pixelRect = new Rect(0, 0, 900, 600);
                    try
                    {
                        var face = ed.PickPivotTarget(camera, Screen(camera, new Vector3(1.5f, 0, -.5f), 1), 1);
                        var edge = ed.PickPivotTarget(camera, Screen(camera, new Vector3(1.5f, .49f, -.5f), 1), 1);
                        var vertex = ed.PickPivotTarget(camera, Screen(camera, new Vector3(1.99f, .49f, -.5f), 1), 1);
                        Check(face != null && face.kind == StudioModelEditor.PivotComponent.Face && face.outline.Length == 4, "Face hover resolves the whole cube polygon without triangulation diagonals");
                        Check(edge != null && edge.kind == StudioModelEditor.PivotComponent.Edge, "Edge hover wins over face within the pixel threshold");
                        Check(vertex != null && vertex.kind == StudioModelEditor.PivotComponent.Vertex, "Vertex hover wins over edge at a corner");
                        var outside = ed.PickPivotTarget(camera, Screen(camera, new Vector3(2.01f, .51f, -.5f), 1), 1);
                        Check(outside != null && outside.kind == StudioModelEditor.PivotComponent.Vertex, "Vertex hover includes the tolerance just outside the silhouette");
                        Check(ed.PickPivotTarget(camera, new Vector2(-100, -100), 1) == null, "Empty space has no pivot target");
                        var hidpi = ed.PickPivotTarget(camera, Screen(camera, new Vector3(1.99f, .49f, -.5f), 2), 2);
                        Check(hidpi != null && hidpi.kind == StudioModelEditor.PivotComponent.Vertex, "Component hover works with display scaling");
                        foreach (StudioModelEditor.PivotAction action in Enum.GetValues(typeof(StudioModelEditor.PivotAction)))
                        {
                            var pos = ed.Transforms[source].position; var rot = ed.PivotOrientation; var child = ed.Transforms[2].position;
                            var world = ed.ActiveMesh.vertices.Select(v => ed.Transforms[source].TransformPoint(v)).ToArray();
                            var objectRotation = ed.Transforms[source].rotation;
                            Undo.IncrementCurrentGroup(); ed.ApplyPivotTarget(face, action);
                            CheckShape(ed, world, action + " preserves every world-space vertex");
                            Check(Vector3.Distance(child, ed.Transforms[2].position) < .0002f && Quaternion.Angle(objectRotation, ed.Transforms[source].rotation) < .001f, action + " preserves child placement and object rotation");
                            if (action == StudioModelEditor.PivotAction.Position || action == StudioModelEditor.PivotAction.Align)
                                Check(Vector3.Distance(ed.Transforms[source].position, face.position) < .0002f, action + " moves the pivot to the component");
                            else Check(Vector3.Distance(ed.Transforms[source].position, pos) < .0002f, action + " leaves pivot position fixed");
                            if (action == StudioModelEditor.PivotAction.Position) Check(Quaternion.Angle(ed.PivotOrientation, rot) < .001f, "Position leaves orientation fixed");
                            else if (action == StudioModelEditor.PivotAction.Aim) Check(Vector3.Dot(ed.PivotOrientation * Vector3.right, (face.aimPoint - pos).normalized) > .9999f, "Aim points the pivot X axis toward the target");
                            else Check(Quaternion.Angle(ed.PivotOrientation, face.orientation) < .001f, action + " matches component orientation");
                            Undo.PerformUndo();
                            Check(Vector3.Distance(ed.Transforms[source].position, pos) < .0002f && Quaternion.Angle(ed.PivotOrientation, rot) < .001f, action + " supports atomic Undo");
                            Undo.PerformRedo(); CheckShape(ed, world, action + " Redo preserves geometry");
                            Undo.PerformUndo();
                        }
                        Undo.IncrementCurrentGroup(); ed.ApplyPivotTarget(edge, StudioModelEditor.PivotAction.Orient);
                        string json = EditorJsonUtility.ToJson(doc); var restored = ScriptableObject.CreateInstance<StudioModelDocument>();
                        EditorJsonUtility.FromJsonOverwrite(json, restored);
                        Check(Quaternion.Angle(doc.nodes[source].pivotRotation, restored.nodes[source].pivotRotation) < .001f, "Custom pivot orientation serializes with the workspace");
                        Object.DestroyImmediate(restored); Undo.PerformUndo();
                    }
                    finally { Object.DestroyImmediate(camObject); }
                }
                AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
                typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Private).Invoke(controller, new object[] { true });
                typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Private).Invoke(controller, new object[] { doc });
                editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Private).GetValue(controller); editor.SelectObject(0);
                view = Resources.FindObjectsOfTypeAll<AnimationStudioViewport>().First();
                view.position = new Rect(100, 100, 1800, 900); view.pivot = Vector3.zero; view.rotation = Quaternion.identity; view.size = 4; view.orthographic = true; view.Focus();
                Application.logMessageReceived += Log; EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
            }
            catch (Exception e) { Finish(e.ToString()); }
        }
        static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack; }
        static void Mouse(EventType type, EventModifiers modifiers = EventModifiers.None) => view.SendEvent(new Event { type = type, button = 0, mousePosition = mouse, modifiers = modifiers });
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .5;
            try
            {
                switch(step++)
                {
                    case 0:
                        view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.D });
                        mouse = Screen(view.camera, new Vector3(1.5f, 0, -.5f), EditorGUIUtility.pixelsPerPoint);
                        Check(view.ModelCanvasRect.Contains(mouse), "Native hover target is inside the viewport");
                        Mouse(EventType.MouseMove); break;
                    case 1:
                        var hover = (StudioModelEditor.PivotTarget)typeof(StudioModelEditor).GetField("pivotHover", Private).GetValue(editor);
                        Check(hover != null && hover.kind == StudioModelEditor.PivotComponent.Face, "Mouse movement highlights a face in pivot mode");
                        beforePosition = editor.Transforms[0].position;
                        Mouse(EventType.MouseDown, EventModifiers.Control); Mouse(EventType.MouseUp, EventModifiers.Control); break;
                    case 2:
                        Check(editor.Active == 0 && Vector3.Distance(editor.Transforms[0].position, beforePosition) < .0002f, "Ctrl-click orients without selecting or moving the target object");
                        Check(Vector3.Dot(editor.PivotOrientation * Vector3.right, Vector3.back) > .9999f, "Ctrl-click applies the hovered face orientation");
                        beforeOrientation = editor.PivotOrientation;
                        Mouse(EventType.MouseMove); Mouse(EventType.MouseDown, EventModifiers.Shift); Mouse(EventType.MouseUp, EventModifiers.Shift); break;
                    case 3:
                        Check(Vector3.Distance(editor.Transforms[0].position, new Vector3(1.5f, 0, -.5f)) < .0002f && Quaternion.Angle(beforeOrientation, editor.PivotOrientation) < .001f, "Shift-click positions without changing orientation");
                        Undo.PerformUndo(); Undo.PerformUndo();
                        Mouse(EventType.MouseMove); Mouse(EventType.MouseDown, EventModifiers.Control | EventModifiers.Shift); Mouse(EventType.MouseUp, EventModifiers.Control | EventModifiers.Shift); break;
                    case 4:
                        Check(Vector3.Dot(editor.PivotOrientation * Vector3.right, (new Vector3(1.5f, 0, -.5f) - editor.Transforms[0].position).normalized) > .999f, "Ctrl+Shift-click aims X at the hovered surface");
                        Undo.PerformUndo(); Mouse(EventType.MouseMove); Mouse(EventType.MouseDown); Mouse(EventType.MouseUp); break;
                    case 5:
                        Check(Vector3.Distance(editor.Transforms[0].position, new Vector3(1.5f, 0, -.5f)) < .0002f && Vector3.Dot(editor.PivotOrientation * Vector3.right, Vector3.back) > .999f, "Plain click aligns pivot position and orientation");
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
            File.WriteAllLines("pivot-targets-result.txt", Results); EditorApplication.delayCall += () => EditorApplication.Exit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
}

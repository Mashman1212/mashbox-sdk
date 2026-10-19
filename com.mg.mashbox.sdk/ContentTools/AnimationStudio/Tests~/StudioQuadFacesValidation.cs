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
    public static class StudioQuadFacesValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> Results = new List<string>();
        static AnimationStudioWindow controller;
        static MashBoxStudioViewport view;
        static StudioModelEditor editor;
        static string oldModel, oldSettings, errors = "";
        static bool hadModel, hadSettings;
        static int step, face;
        static double next;
        static float baseSize;
        static Vector2 pointer;
        static Vector3[] before;
        static HashSet<int> selected;
        static string ModelKey => "MashBox.LastModel." + Application.dataPath;
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        static object Call(string name, params object[] args) => typeof(StudioModelEditor).GetMethod(name, Private).Invoke(editor, args);
        static void Key(KeyCode key) => view.SendEvent(new Event { type = EventType.KeyDown, keyCode = key });
        static Vector2 Screen(Vector3 world) { var p = view.camera.WorldToScreenPoint(world); return new Vector2(p.x, view.camera.pixelHeight - p.y) / EditorGUIUtility.pixelsPerPoint; }
        static void Mouse(EventType type, Vector2 p, EventModifiers modifiers = EventModifiers.None) => view.SendEvent(new Event { type = type, button = 0, mousePosition = p, modifiers = modifiers });
        static void Click(Vector3 p, EventModifiers modifiers = EventModifiers.None) { var screen = Screen(p); Mouse(EventType.MouseMove, screen); Mouse(EventType.MouseDown, screen, modifiers); Mouse(EventType.MouseUp, screen, modifiers); }
        public static void Run()
        {
            hadModel = EditorPrefs.HasKey(ModelKey); oldModel = EditorPrefs.GetString(ModelKey);
            hadSettings = EditorPrefs.HasKey(StudioSettings.PreferenceKey); oldSettings = EditorPrefs.GetString(StudioSettings.PreferenceKey);
            StudioSettings.Current.handleSize = 1; StudioSettings.Save();
            var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Quad Faces Test.asset")); doc.AddPrimitive(PrimitiveType.Cube);
            AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Private).Invoke(controller, new object[] { true });
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Private).Invoke(controller, new object[] { doc });
            editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Private).GetValue(controller); editor.SelectObject(0);
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First();
            view.position = new Rect(100,100,1600,900); view.pivot = Vector3.zero; view.rotation = Quaternion.identity; view.size = 2; view.orthographic = true; view.Focus();
            Application.logMessageReceived += Log; SceneView.duringSceneGui += Measure; EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
        }
        static void Measure(SceneView v) { if (v == view && Event.current.type == EventType.Repaint) baseSize = HandleUtility.GetHandleSize(new Vector3(0,0,-.5f)); }
        static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack; }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .4;
            try
            {
                switch(step++)
                {
                    case 0:
                        Key(KeyCode.Alpha4); Key(KeyCode.Q);
                        Check((int)Call("ComponentCount") == 6, "Cube exposes six selectable polygon faces");
                        Click(new Vector3(.25f,.15f,-.5f));
                        Check(editor.Components.Count == 1, "Clicking a cube surface selects one face"); face = editor.Components.Single();
                        Click(new Vector3(-.25f,-.15f,-.5f));
                        Check(editor.Components.Count == 1 && editor.Components.Single() == face, "Both rendered triangles select the same quad");
                        selected = editor.SelectedVertices(); before = editor.ActiveMesh.vertices;
                        Check(selected.Select(i => before[i]).Distinct().Count() == 4, "Face transform includes all four quad corners");
                        var triangles = (List<int>[])typeof(StudioModelEditor).GetField("cachedFaceTriangles", Private).GetValue(editor);
                        Check(triangles[face].Count == 6 && triangles[face].Distinct().Count() == 4, "Highlight covers both actual render triangles");
                        Click(new Vector3(.25f,.15f,-.5f), EventModifiers.Control);
                        Check(editor.Components.Count == 0, "Subtract selection removes the whole quad");
                        var a = Screen(new Vector3(-.2f,.2f,-.5f)); var b = Screen(new Vector3(.2f,-.2f,-.5f));
                        Mouse(EventType.MouseDown,a); Mouse(EventType.MouseDrag,b); Mouse(EventType.MouseUp,b);
                        Check(editor.Components.Count == 2, "Through marquee selects front and back quads as two faces");
                        Click(new Vector3(.25f,.15f,-.5f)); Key(KeyCode.W); break;
                    case 1:
                        pointer = Screen(new Vector3(baseSize * .8f,0,-.5f));
                        Mouse(EventType.MouseMove,pointer); Mouse(EventType.MouseDown,pointer); Mouse(EventType.MouseDrag,pointer + Vector2.right * 40); break;
                    case 2:
                        Mouse(EventType.MouseUp,pointer + Vector2.right * 40);
                        var after = editor.ActiveMesh.vertices; var delta = after[selected.First()] - before[selected.First()];
                        Check(delta.magnitude > .01f && selected.All(i => Vector3.Distance(after[i] - before[i],delta) < .0001f), "Native face drag moves every selected corner equally");
                        Check(Enumerable.Range(0,before.Length).Where(i => !selected.Contains(i)).All(i => after[i] == before[i]), "Other corners remain unchanged");
                        Undo.PerformUndo(); break;
                    case 3:
                        Check(editor.ActiveMesh.vertices.SequenceEqual(before), "Undo restores all face vertices"); Undo.PerformRedo(); break;
                    case 4:
                        Check((int)Call("ComponentCount") == 6, "Redo preserves quad identity");
                        // Warp a corner using the same metadata preservation as component handles.
                        Call("PreserveFaceTopology"); var warped = editor.ActiveMesh.vertices; warped[0] += new Vector3(.1f,.2f,.3f); editor.ActiveMesh.vertices = warped;
                        Call("Rebuild");
                        Check((int)Call("ComponentCount") == 6, "Non-planar edits and preview rebuild retain the original quads");
                        var triangle = new Mesh { vertices = new[] { Vector3.zero,Vector3.right,Vector3.up }, triangles = new[] { 0,1,2 } };
                        var node = new StudioModelDocument.Node { mesh = triangle };
                        var topology = new StudioMeshCuts(node);
                        Check(topology.Faces.Count == 1 && topology.Faces[0].Vertices.Count == 3, "Genuine triangle remains a triangular face"); Object.DestroyImmediate(triangle);
                        Finish(null); break;
                }
            }
            catch (Exception e) { Finish(e.ToString()); }
        }
        static void Finish(string error)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log; SceneView.duringSceneGui -= Measure;
            if (!string.IsNullOrEmpty(errors)) error += errors;
            if (!string.IsNullOrEmpty(error)) Results.Add("FAIL: " + error);
            if (view) view.Close(); if (controller) Object.DestroyImmediate(controller);
            if (hadSettings) EditorPrefs.SetString(StudioSettings.PreferenceKey, oldSettings); else EditorPrefs.DeleteKey(StudioSettings.PreferenceKey); StudioSettings.Reload();
            if (hadModel) EditorPrefs.SetString(ModelKey, oldModel); else EditorPrefs.DeleteKey(ModelKey);
            File.WriteAllLines("quad-faces-result.txt", Results); EditorApplication.delayCall += () => EditorApplication.Exit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
}

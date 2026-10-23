using EditorPrefs = MashBoxSDK.AnimationStudio.StudioPreferenceStore;
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
    public static class StudioPivotGizmoValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> Results = new List<string>();
        static AnimationStudioWindow controller;
        static MashBoxStudioViewport view;
        static StudioModelEditor editor;
        static string oldSettings, oldModel, errors = "";
        static bool hadSettings, hadModel;
        static int step, drag;
        static double next;
        static float baseSize;
        static Vector2 pointer, direction, origin;
        static Vector3[] worldVertices;
        static readonly Vector3[] Axes = { Vector3.right, Vector3.up, Vector3.forward };
        static string ModelKey => "MashBox.LastModel." + Application.dataPath;
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        static void Key(KeyCode key) => view.SendEvent(new Event { type = EventType.KeyDown, keyCode = key });
        static Vector2 Screen(Vector3 world) { var p = view.camera.WorldToScreenPoint(world); return new Vector2(p.x, view.camera.pixelHeight - p.y) / EditorGUIUtility.pixelsPerPoint; }
        static void Mouse(EventType type, Vector2 p) => view.SendEvent(new Event { type = type, button = 0, mousePosition = p });
        public static void Run()
        {
            hadSettings = EditorPrefs.HasKey(StudioSettings.PreferenceKey); oldSettings = EditorPrefs.GetString(StudioSettings.PreferenceKey);
            hadModel = EditorPrefs.HasKey(ModelKey); oldModel = EditorPrefs.GetString(ModelKey);
            StudioSettings.Current.handleSize = 1; StudioSettings.Save();
            var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Pivot Gizmo Test.asset")); doc.AddPrimitive(PrimitiveType.Cube);
            AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Private).Invoke(controller, new object[] { true });
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Private).Invoke(controller, new object[] { doc });
            editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Private).GetValue(controller); editor.SelectObject(0);
            worldVertices = doc.nodes[0].mesh.vertices.Select(editor.Transforms[0].TransformPoint).ToArray();
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First();
            view.position = new Rect(100, 100, 1600, 900); view.pivot = Vector3.zero; view.rotation = Quaternion.Euler(20, 30, 0); view.size = 2; view.orthographic = true; view.Focus();
            Application.logMessageReceived += Log; SceneView.duringSceneGui += Measure; EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
        }
        static void Measure(SceneView v) { if (v == view && Event.current.type == EventType.Repaint) baseSize = HandleUtility.GetHandleSize(editor.Transforms[0].position); }
        static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack; }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .35;
            try
            {
                if (step == 0)
                {
                    Key(KeyCode.Q); Key(KeyCode.D);
                    Check((bool)typeof(StudioModelEditor).GetField("pivot", Private).GetValue(editor), "D activates pivot handles even with the selection tool active");
                    Key(KeyCode.Plus); Check(Mathf.Approximately(StudioSettings.Current.handleSize, 1.2f), "Plus enlarges the gizmo in pivot mode");
                    Key(KeyCode.Minus); Check(Mathf.Approximately(StudioSettings.Current.handleSize, 1), "Minus shrinks the gizmo in pivot mode");
                    Key(KeyCode.KeypadPlus); Key(KeyCode.KeypadMinus);
                    Check(Mathf.Approximately(StudioSettings.Current.handleSize, 1), "Numpad plus and minus work in pivot mode");
                    StudioSettings.Current.handleSize = .5f; StudioSettings.Save(); step++; return;
                }
                if (step == 1)
                {
                    var center = editor.Transforms[0].position;
                    origin = Screen(center);
                    pointer = Screen(center + Axes[drag % 3] * baseSize * StudioSettings.Current.handleSize * 1.25f * .8f);
                    direction = (pointer - origin).normalized;
                    Check(view.ModelCanvasRect.Contains(pointer), "Axis " + drag + " is inside the viewport");
                    Mouse(EventType.MouseMove, pointer); Mouse(EventType.MouseDown, pointer);
                    Check(!(bool)typeof(StudioModelEditor).GetField("marquee", Private).GetValue(editor), "Pivot arrow " + drag + " captures its drag");
                    Mouse(EventType.MouseDrag, pointer + direction * 30); step++; return;
                }
                if (step == 2)
                {
                    Mouse(EventType.MouseUp, pointer + direction * 30);
                    var delta = Screen(editor.Transforms[0].position) - origin;
                    Check(Vector2.Distance(delta, direction * 30) < 2, "Pivot arrow " + drag + " follows a 30-pixel drag: " + delta);
                    var after = editor.Document.nodes[0].mesh.vertices.Select(editor.Transforms[0].TransformPoint).ToArray();
                    Check(after.Zip(worldVertices, (a,b) => Vector3.Distance(a,b)).Max() < .0001f, "Pivot drag leaves rendered geometry in place");
                    Undo.PerformUndo(); step++; return;
                }
                Check(editor.Transforms[0].position.sqrMagnitude < .0001f, "Undo restores the pivot after arrow " + drag);
                if (++drag == 6) { Finish(null); return; }
                if (drag == 3) { StudioSettings.Current.handleSize = 2.5f; StudioSettings.Save(); }
                step = 1;
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
            File.WriteAllLines("pivot-gizmo-result.txt", Results); EditorApplication.delayCall += () => EditorApplication.Exit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
}

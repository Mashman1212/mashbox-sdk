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
    public static class StudioGridSnapValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> Results = new List<string>();
        static AnimationStudioWindow controller;
        static MashBoxStudioViewport view;
        static StudioModelEditor editor;
        static string oldSettings, oldModel, errors = "";
        static bool hadSettings, hadModel;
        static int step, mode;
        static double next;
        static float baseSize;
        static Vector2 pointer;
        static Vector3 center, snapped;
        static Vector3[] before;
        static string ModelKey => "MashBox.LastModel." + Application.dataPath;
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        static void Key(KeyCode key, bool up = false) => view.SendEvent(new Event { type = up ? EventType.KeyUp : EventType.KeyDown, keyCode = key });
        static Vector2 Screen(Vector3 world) { var p = view.camera.WorldToScreenPoint(world); return new Vector2(p.x, view.camera.pixelHeight - p.y) / EditorGUIUtility.pixelsPerPoint; }
        static void Mouse(EventType type, Vector2 p) => view.SendEvent(new Event { type = type, button = 0, mousePosition = p });
        static Vector3 Center() => mode == 2 ? editor.Transforms[0].TransformPoint(editor.ActiveMesh.vertices[0]) : editor.Transforms[0].position;
        static Vector3[] Vertices() => editor.ActiveMesh.vertices.Select(editor.Transforms[0].TransformPoint).ToArray();
        public static void Run()
        {
            hadSettings = EditorPrefs.HasKey(StudioSettings.PreferenceKey); oldSettings = EditorPrefs.GetString(StudioSettings.PreferenceKey);
            hadModel = EditorPrefs.HasKey(ModelKey); oldModel = EditorPrefs.GetString(ModelKey);
            StudioSettings.Current.handleSize = 1; StudioSettings.Current.gridSpacing = .5f; StudioSettings.Save();
            var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Grid Snap Test.asset")); doc.AddPrimitive(PrimitiveType.Cube);
            doc.nodes[0].position = new Vector3(.13f,.17f,.11f);
            AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Private).Invoke(controller, new object[] { true });
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Private).Invoke(controller, new object[] { doc });
            editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Private).GetValue(controller); editor.SelectObject(0);
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First();
            view.position = new Rect(100,100,1600,900); view.pivot = Vector3.zero; view.rotation = Quaternion.identity; view.size = 2; view.orthographic = true; view.Focus();
            Application.logMessageReceived += Log; SceneView.duringSceneGui += Measure; EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
        }
        static void Measure(SceneView v) { if (v == view && Event.current.type == EventType.Repaint) baseSize = HandleUtility.GetHandleSize(Center()); }
        static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack; }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .4;
            try
            {
                if (step == 0)
                {
                    Check(StudioModelEditor.SnapMoveToGrid(new Vector3(.13f,.17f,.11f), new Vector3(.43f,.17f,.11f), .5f) == new Vector3(.5f,.17f,.11f), "Absolute grid snapping preserves locked axes");
                    Check(StudioModelEditor.SnapMoveToGrid(Vector3.zero, new Vector3(-.42f,0,.38f), .25f) == new Vector3(-.5f,0,.5f), "Negative coordinates and plane movement honor custom spacing");
                    step++; return;
                }
                if (step == 1)
                {
                    center = Center(); before = Vertices();
                    pointer = Screen(center + Vector3.right * baseSize * (mode == 1 ? 1.25f : 1) * .8f);
                    Key(KeyCode.X);
                    Mouse(EventType.MouseMove, pointer); Mouse(EventType.MouseDown, pointer);
                    Check(!(bool)typeof(StudioModelEditor).GetField("marquee", Private).GetValue(editor), "Mode " + mode + " captures the move axis");
                    Mouse(EventType.MouseDrag, pointer + Vector2.right * 60); step++; return;
                }
                if (step == 2)
                {
                    snapped = Center();
                    Check(Mathf.Abs(snapped.x / .5f - Mathf.Round(snapped.x / .5f)) < .0001f && Mathf.Abs(snapped.x - center.x) > .01f, "Mode " + mode + " snaps to world-grid coordinates");
                    Check(Mathf.Abs(snapped.y - center.y) < .0001f && Mathf.Abs(snapped.z - center.z) < .0001f, "Axis snap preserves perpendicular coordinates");
                    if (mode == 1) Check(Vertices().Zip(before, (a,b) => Vector3.Distance(a,b)).Max() < .0001f, "Pivot grid snapping preserves visible geometry");
                    Key(KeyCode.X, true);
                    Mouse(EventType.MouseDrag, pointer + Vector2.right * 73); step++; return;
                }
                if (step == 3)
                {
                    Mouse(EventType.MouseUp, pointer + Vector2.right * 73);
                    Check(Mathf.Abs(Center().x / .5f - Mathf.Round(Center().x / .5f)) > .001f, "Releasing X restores free movement during the same drag");
                    Undo.PerformUndo(); step++; return;
                }
                Check(Vector3.Distance(Center(), center) < .0001f && Vertices().Zip(before, (a,b) => Vector3.Distance(a,b)).Max() < .0001f, "One Undo restores the complete drag");
                if (++mode == 3)
                {
                    Key(KeyCode.X); Key(KeyCode.Escape);
                    Check(!(bool)typeof(StudioModelEditor).GetField("gridSnapHeld", Private).GetValue(editor), "Escape releases grid snapping");
                    Finish(null); return;
                }
                if (mode == 1) Key(KeyCode.D);
                else { Key(KeyCode.D); Key(KeyCode.Alpha2); editor.Components.Add(0); }
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
            File.WriteAllLines("grid-snap-result.txt", Results); EditorApplication.delayCall += () => EditorApplication.Exit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
}

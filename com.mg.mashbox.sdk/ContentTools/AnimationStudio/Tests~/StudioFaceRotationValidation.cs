using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public static class StudioFaceRotationValidation
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static AnimationStudioWindow controller;
    static MashBoxStudioViewport view;
    static StudioModelEditor editor;
    static Vector3[] beforeDrag;
    static HashSet<int> selected;
    static Vector3 center;
    static Quaternion initialOrientation;
    static Vector2 screenCenter, mouse;
    static float radius;
    static int step, scenario;
    static double next;
    static string errors = "";
    static readonly List<string> results = new List<string>();
    static T Field<T>(string name) => (T)typeof(StudioModelEditor).GetField(name, Flags).GetValue(editor);
    static void Set(string name, object value) => typeof(StudioModelEditor).GetField(name, Flags).SetValue(editor, value);
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        AnimationStudioWindow.Open();
        controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
        typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Flags).Invoke(controller, new object[] { true });
        var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/FaceRotation.asset"));
        doc.AddPrimitive(PrimitiveType.Cube);
        typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Flags).Invoke(controller, new object[] { doc });
        editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Flags).GetValue(controller);
        editor.SelectObject(0); Set("mode", StudioModelEditor.ComponentMode.Face); editor.Components.Add(0);
        editor.ActivateTool(Tool.Rotate);
        selected = editor.SelectedVertices();
        Require(selected.Count >= 4, "Test requires a selected quad including seam vertices");
        view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First();
        view.position = new Rect(100, 100, 1400, 850);
        Setup(); view.Focus();
        SceneView.duringSceneGui += Capture;
        Application.logMessageReceived += Log;
        EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
    }
    static void Setup()
    {
        var transform = editor.Transforms[0];
        transform.position = new Vector3(1, 2, 3);
        transform.rotation = Quaternion.Euler(15, 25, 35);
        transform.localScale = new Vector3(1.1f, .8f, 1.3f);
        editor.Document.ReadTransform(0, transform);
        Set("local", scenario == 0);
        initialOrientation = scenario == 0 ? editor.PivotOrientation : Quaternion.identity;
        beforeDrag = editor.ActiveMesh.vertices;
        center = selected.Aggregate(Vector3.zero, (sum, i) => sum + transform.TransformPoint(beforeDrag[i])) / selected.Count;
        view.pivot = center; view.rotation = Quaternion.Euler(15, 25, 0); view.size = 3; view.orthographic = true;
        view.Repaint();
    }
    static void Capture(SceneView scene)
    {
        if (scene != view || Event.current.type != EventType.Repaint) return;
        using (new Handles.DrawingScope(Matrix4x4.identity))
        {
            screenCenter = HandleUtility.WorldToGUIPoint(center);
            float size = StudioSettings.Current.handleSize;
            float worldRadius = HandleUtility.GetHandleSize(center / size) * size * 1.1f;
            radius = Vector2.Distance(screenCenter, HandleUtility.WorldToGUIPoint(center + view.camera.transform.right * worldRadius));
        }
    }
    static void Mouse(EventType type, float angle)
    {
        var point = screenCenter + new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
        view.SendEvent(new Event { type = type, button = 0, mousePosition = point, delta = point - mouse });
        mouse = point;
    }
    static void CheckGeometry()
    {
        var rotation = Field<Quaternion>("componentHandleRotation");
        Require(Quaternion.Angle(rotation, initialOrientation) > .5f, "Mouse drag must actually rotate the handle");
        var delta = rotation * Quaternion.Inverse(initialOrientation);
        var actual = editor.ActiveMesh.vertices;
        var transform = editor.Transforms[0];
        for (int i = 0; i < actual.Length; i++)
        {
            var expected = selected.Contains(i)
                ? transform.InverseTransformPoint(center + delta * (transform.TransformPoint(beforeDrag[i]) - center)) : beforeDrag[i];
            Require(Vector3.Distance(actual[i], expected) < .0002f, "Face diverged from handle at update " + step + ", vertex " + i + ": " + Vector3.Distance(actual[i], expected));
        }
    }
    static void Log(string text, string stack, LogType type)
    { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors += text + "\n" + stack; }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .25;
        try
        {
            switch (step++)
            {
                case 0: Mouse(EventType.MouseMove, 40); break;
                case 1: Mouse(EventType.MouseDown, 40); break;
                case 2: Mouse(EventType.MouseDrag, 50); CheckGeometry(); break;
                case 3: Mouse(EventType.MouseDrag, 65); CheckGeometry(); break;
                case 4: Mouse(EventType.MouseDrag, 80); CheckGeometry(); break;
                case 5: Mouse(EventType.MouseDrag, 55); CheckGeometry(); break;
                case 6:
                    Mouse(EventType.MouseUp, 55);
                    Require(Field<int>("componentRotationControl") == 0, "Mouse release must clear the rotation gesture");
                    Undo.PerformUndo(); break;
                case 7:
                    var restored = editor.ActiveMesh.vertices;
                    Require(restored.Zip(beforeDrag, (a,b) => Vector3.Distance(a,b)).Max() < .0002f, "One Undo must restore entire drag");
                    results.Add("PASS: " + (scenario == 0 ? "Local" : "World") + " quad rotation matches native gizmo through four drag updates and reversal; unselected vertices stay fixed; one Undo restores the face.");
                    if (++scenario < 2) { Setup(); step = 0; break; }
                    Setup(); step = 8; break;
                case 8: Mouse(EventType.MouseMove, 40); break;
                case 9: Mouse(EventType.MouseDown, 40); Mouse(EventType.MouseDrag, 65); CheckGeometry(); break;
                case 10:
                    view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }); break;
                case 11:
                    Require(editor.ActiveMesh.vertices.Zip(beforeDrag, (a,b) => Vector3.Distance(a,b)).Max() < .0002f, "Escape must cancel a fresh rotation gesture");
                    results.Add("PASS: A fresh drag resets its rotation baseline and Escape restores the face.");
                    Require(string.IsNullOrEmpty(errors), errors); Finish(null); break;
            }
        }
        catch (Exception e) { Finish(e.ToString()); }
    }
    static void Finish(string error)
    {
        EditorApplication.update -= Tick; SceneView.duringSceneGui -= Capture; Application.logMessageReceived -= Log;
        if (error != null) results.Add("FAIL: " + error + "\n" + errors);
        if (view) view.Close(); if (controller) UnityEngine.Object.DestroyImmediate(controller);
        File.WriteAllLines("face-rotation-result.txt", results);
        EditorApplication.delayCall += () => EditorApplication.Exit(error == null ? 0 : 1);
    }
}

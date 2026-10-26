using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public static class StudioComponentPivotValidation
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static AnimationStudioWindow controller;
    static MashBoxStudioViewport view;
    static StudioModelEditor editor;
    static Vector3[] beforeDrag;
    static HashSet<int> selected;
    static Vector3 center, selectionCenter, snapTarget;
    static Quaternion initialOrientation;
    static Vector2 screenCenter, mouse, snapScreen;
    static float radius, originalHandleSize;
    static Vector3 gesturePivot;
    static Vector2 gestureMouse;
    static int step, scenario;
    static double next;
    static string errors = "";
    static readonly List<string> results = new List<string>();
    static T Field<T>(string name) => (T)typeof(StudioModelEditor).GetField(name, Flags).GetValue(editor);
    static void Set(string name, object value) => typeof(StudioModelEditor).GetField(name, Flags).SetValue(editor, value);
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void Near(Vector3 a, Vector3 b, string message) => Require(Vector3.Distance(a,b) < .0002f, message + ": " + a + " expected " + b);
    static void Key(KeyCode key)
    {
        view.SendEvent(new Event { type = EventType.KeyDown, keyCode = key });
        if (key != KeyCode.V && key != KeyCode.X) view.SendEvent(new Event { type = EventType.KeyUp, keyCode = key });
    }
    public static void Run()
    {
        try
        {
            AnimationStudioWindow.Open();
            controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Flags).Invoke(controller, new object[] { true });
            var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/ComponentPivot.asset"));
            doc.AddPrimitive(PrimitiveType.Cube);
            doc.nodes[0].position = new Vector3(1, 2, 3);
            doc.nodes[0].rotation = Quaternion.Euler(15, 25, 35);
            doc.nodes[0].scale = new Vector3(1.1f, .8f, 1.3f);
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Flags).Invoke(controller, new object[] { doc });
            editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Flags).GetValue(controller);
            editor.SelectObject(0);
            originalHandleSize = StudioSettings.Current.handleSize;
            editor.SetToolbarComponentAxes();
            ValidateActions();
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First();
            view.position = new Rect(100, 100, 1400, 850);
            Setup(); view.Focus();
            SceneView.duringSceneGui += Capture; Application.logMessageReceived += Log;
            EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
        }
        catch (Exception e) { Finish(e.ToString()); }
    }
    static void ValidateActions()
    {
        var modes = new[] { StudioModelEditor.ComponentMode.Face, StudioModelEditor.ComponentMode.Edge, StudioModelEditor.ComponentMode.Vertex };
        foreach (var mode in modes)
        {
            Set("mode", mode); editor.Components.Clear(); editor.Components.Add(0);
            var originalPosition = editor.EditingPivotPosition; var originalOrientation = editor.PivotOrientation;
            var model = EditorJsonUtility.ToJson(editor.Document);
            var vertices = editor.ActiveMesh.vertices;
            var disk = File.ReadAllBytes(AssetDatabase.GetAssetPath(editor.Document));
            var target = new StudioModelEditor.PivotTarget { position = originalPosition + new Vector3(2, 1, -.5f),
                aimPoint = originalPosition + new Vector3(-2, 3, 1), orientation = Quaternion.Euler(30, 45, 60) };
            foreach (StudioModelEditor.PivotAction action in Enum.GetValues(typeof(StudioModelEditor.PivotAction)))
            {
                Undo.IncrementCurrentGroup(); editor.ApplyPivotTarget(target, action);
                Near(editor.EditingPivotPosition, action == StudioModelEditor.PivotAction.Align || action == StudioModelEditor.PivotAction.Position ? target.position : originalPosition, mode + " " + action + " position");
                if (action == StudioModelEditor.PivotAction.Aim)
                    Require(Vector3.Dot(editor.PivotOrientation * Vector3.right, (target.aimPoint-originalPosition).normalized) > .9999f, "Aim uses selection pivot");
                else Require(Quaternion.Angle(editor.PivotOrientation, action == StudioModelEditor.PivotAction.Position ? originalOrientation : target.orientation) < .05f, mode + " " + action + " orientation");
                Require(EditorJsonUtility.ToJson(editor.Document) == model, "Component pivot must not alter document or object pivot");
                Require(vertices.SequenceEqual(editor.ActiveMesh.vertices), "Component pivot must not move vertices");
                Require(disk.SequenceEqual(File.ReadAllBytes(AssetDatabase.GetAssetPath(editor.Document))), "Pivot adjustment must not write the workspace");
                Undo.PerformUndo(); Near(editor.EditingPivotPosition, originalPosition, "Undo pivot position");
                Require(Quaternion.Angle(editor.PivotOrientation, originalOrientation) < .05f, "Undo pivot orientation");
                Undo.PerformRedo(); Near(editor.EditingPivotPosition, action == StudioModelEditor.PivotAction.Align || action == StudioModelEditor.PivotAction.Position ? target.position : originalPosition, "Redo pivot");
                Undo.PerformUndo();
            }
            editor.ApplyPivotTarget(target, StudioModelEditor.PivotAction.Align);
            editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.CenterPivot, null);
            Near(editor.EditingPivotPosition, originalPosition, "Center Pivot uses selected components");
            editor.Components.Clear(); editor.Components.Add(1);
            Require(!Field<StudioComponentPivotState>("componentPivot").selection.SequenceEqual(editor.Components), "Changed selection test setup");
            var changedCenter = editor.SelectedVertices().Aggregate(Vector3.zero, (sum,i) => sum + editor.Transforms[0].TransformPoint(vertices[i])) / editor.SelectedVertices().Count;
            Near(editor.EditingPivotPosition, changedCenter, "Changing selection resets custom pivot");
            results.Add("PASS: " + mode + " Align/Position/Orient/Aim, Undo/Redo, Center Pivot and selection reset; no geometry, object-pivot, document or disk changes.");
        }
        editor.SelectObject(0);
    }
    static void Setup()
    {
        editor.SelectObject(0);
        Set("mode", (StudioModelEditor.ComponentMode)(1 + scenario / 2));
        editor.Components.Add(0); editor.ActivateTool(Tool.Move);
        Set("local", scenario % 2 == 0);
        selected = editor.SelectedVertices(); beforeDrag = editor.ActiveMesh.vertices;
        selectionCenter = editor.EditingPivotPosition;
        snapTarget = editor.Transforms[0].TransformPoint(beforeDrag.First(v => Vector3.Distance(editor.Transforms[0].TransformPoint(v), selectionCenter) > .2f));
        view.pivot = editor.Transforms[0].position; view.rotation = Quaternion.Euler(20, 145, 0); view.size = 3; view.orthographic = true;
        view.Repaint();
    }
    static void Capture(SceneView scene)
    {
        if (scene != view || (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout)) return;
        using (new Handles.DrawingScope(Matrix4x4.identity))
        {
            center = editor.EditingPivotPosition;
            if (Event.current.type == EventType.Repaint)
            {
                snapScreen = GUIUtility.GUIToScreenPoint(HandleUtility.WorldToGUIPoint(snapTarget)) - view.position.position;
                return;
            }
            var guiCenter = HandleUtility.WorldToGUIPoint(center);
            screenCenter = GUIUtility.GUIToScreenPoint(guiCenter) - view.position.position;
            float size = StudioSettings.Current.handleSize;
            float worldRadius = HandleUtility.GetHandleSize(center / size) * size * 1.1f;
            radius = Vector2.Distance(guiCenter, HandleUtility.WorldToGUIPoint(center + view.camera.transform.right * worldRadius));
        }
    }
    static void Mouse(EventType type, float angle)
    {
        var point = screenCenter + new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
        view.SendEvent(new Event { type = type, button = 0, mousePosition = point, delta = point - mouse }); mouse = point;
    }
    static void CheckGeometry()
    {
        var rotation = Field<Quaternion>("componentHandleRotation");
        Require(Quaternion.Angle(rotation, initialOrientation) > .5f, "Native drag must rotate handle");
        var delta = rotation * Quaternion.Inverse(initialOrientation);
        var actual = editor.ActiveMesh.vertices; var transform = editor.Transforms[0];
        for (int i = 0; i < actual.Length; i++)
        {
            var expected = selected.Contains(i) ? transform.InverseTransformPoint(center + delta * (transform.TransformPoint(beforeDrag[i]) - center)) : beforeDrag[i];
            Near(actual[i], expected, "Rotation must use custom pivot exactly, update " + step + ", vertex " + i);
        }
    }
    static void Log(string text, string stack, LogType type)
    { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors += text + "\n" + stack; }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .25;
        try
        {
            switch (step++)
            {
                case 0:
                    Key(KeyCode.D); Require(Field<bool>("pivot"), "D enters component pivot editing");
                    Key(KeyCode.Equals); Require(StudioSettings.Current.handleSize > originalHandleSize, "Plus enlarges component pivot gizmo");
                    Key(KeyCode.Minus); StudioSettings.Current.handleSize = originalHandleSize;
                    break;
                case 1:
                    var screen = snapScreen;
                    Key(KeyCode.V);
                    Require(Field<bool>("pivotSnapHeld"), "V is held in component mode");
                    Require(view.ModelCanvasRect.Contains(screen), "Snap target inside canvas");
                    view.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = screen });
                    Require(Field<int>("pivotSnapControl") != 0, "V click captures component snap");
                    view.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = screen });
                    view.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = screen });
                    view.SendEvent(new Event { type = EventType.KeyUp, keyCode = KeyCode.V });
                    Near(editor.EditingPivotPosition, snapTarget, "V snaps component pivot to vertex");
                    Require(editor.ActiveMesh.vertices.SequenceEqual(beforeDrag), "V snap must preserve geometry");
                    Require(Field<bool>("pivot"), "Snap preserves pivot mode; selection=" + editor.Components.Count);
                    break;
                case 2:
                    Key(KeyCode.D); Require(!Field<bool>("pivot"), "D locks component pivot; selection=" + editor.Components.Count + " mode=" + Field<StudioModelEditor.ComponentMode>("mode"));
                    editor.ActivateTool(Tool.Rotate); initialOrientation = scenario % 2 == 0 ? editor.PivotOrientation : Quaternion.identity;
                    Mouse(EventType.MouseMove, 40);
                    break;
                case 3: Mouse(EventType.MouseDown, 40); break;
                case 4: Mouse(EventType.MouseDrag, 50); CheckGeometry(); break;
                case 5: Mouse(EventType.MouseDrag, 65); CheckGeometry(); break;
                case 6: Mouse(EventType.MouseDrag, 80); CheckGeometry(); break;
                case 7: Mouse(EventType.MouseDrag, 55); CheckGeometry(); break;
                case 8: Mouse(EventType.MouseUp, 55); Undo.PerformUndo(); break;
                case 9:
                    Require(editor.ActiveMesh.vertices.Zip(beforeDrag,(a,b)=>Vector3.Distance(a,b)).Max() < .0002f, "Undo restores component rotation");
                    Near(editor.EditingPivotPosition, snapTarget, "Rotation Undo retains edited pivot");
                    results.Add("PASS: Native " + Field<StudioModelEditor.ComponentMode>("mode") + " " + (scenario % 2 == 0 ? "Local" : "World") + " D toggle, gizmo sizing, V snap, custom-origin rotation/reversal, and Undo.");
                    if (++scenario < 6) { Setup(); step = 0; break; }
                    break;
                case 10:
                    Set("local", false); Key(KeyCode.D); gesturePivot = editor.EditingPivotPosition;
                    view.SendEvent(new Event { type = EventType.MouseMove, mousePosition = screenCenter }); break;
                case 11:
                    gestureMouse = screenCenter; Key(KeyCode.X);
                    view.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = gestureMouse }); break;
                case 12:
                    view.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = gestureMouse + new Vector2(53, 37), delta = new Vector2(53,37) });
                    var gridPivot = editor.EditingPivotPosition;
                    Require(Vector3.Distance(gridPivot, gesturePivot) > .01f, "Dragging pivot handle moves the component pivot");
                    for (int axis = 0; axis < 3; axis++) if (Mathf.Abs(gridPivot[axis]-gesturePivot[axis]) > .00001f)
                        Require(Mathf.Abs(gridPivot[axis] / StudioSettings.Current.gridSpacing - Mathf.Round(gridPivot[axis] / StudioSettings.Current.gridSpacing)) < .0002f, "X snaps dragged component pivot to grid");
                    Require(editor.ActiveMesh.vertices.SequenceEqual(beforeDrag), "Pivot drag preserves mesh");
                    break;
                case 13:
                    Key(KeyCode.Escape); view.SendEvent(new Event { type = EventType.KeyUp, keyCode = KeyCode.X }); break;
                case 14:
                    Near(editor.EditingPivotPosition, gesturePivot, "Escape cancels component pivot drag");
                    editor.ActivateTool(Tool.Move);
                    view.SendEvent(new Event { type = EventType.MouseMove, mousePosition = screenCenter }); break;
                case 15:
                    gestureMouse = screenCenter;
                    view.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = gestureMouse }); break;
                case 16:
                    view.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = gestureMouse + new Vector2(42, 28), delta = new Vector2(42,28) });
                    view.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = gestureMouse + new Vector2(42, 28) });
                    var translation = editor.EditingPivotPosition - gesturePivot;
                    Require(translation.sqrMagnitude > .0001f, "Component move carries the custom pivot");
                    var moved = editor.ActiveMesh.vertices;
                    for (int i = 0; i < moved.Length; i++) Near(editor.Transforms[0].TransformPoint(moved[i]),
                        editor.Transforms[0].TransformPoint(beforeDrag[i]) + (selected.Contains(i) ? translation : Vector3.zero), "Move carries only selected vertices with pivot");
                    Undo.PerformUndo(); break;
                case 17:
                    Near(editor.EditingPivotPosition, gesturePivot, "Move Undo restores custom pivot");
                    Require(editor.ActiveMesh.vertices.Zip(beforeDrag,(a,b)=>Vector3.Distance(a,b)).Max() < .0002f, "Move Undo restores mesh");
                    results.Add("PASS: Native component-pivot handle drag, X grid snap and Escape; normal Move carries the custom pivot with selected geometry and Undo restores both.");
                    Require(string.IsNullOrEmpty(errors), errors); Finish(null); break;
            }
        }
        catch (Exception e) { Finish(e.ToString()); }
    }
    static void Finish(string error)
    {
        EditorApplication.update -= Tick; SceneView.duringSceneGui -= Capture; Application.logMessageReceived -= Log;
        StudioSettings.Current.handleSize = originalHandleSize;
        if (error != null) results.Add("FAIL: " + error + "\n" + errors);
        if (view) view.Close(); if (controller) UnityEngine.Object.DestroyImmediate(controller);
        File.WriteAllLines("component-pivot-result.txt", results);
        EditorApplication.delayCall += () => EditorApplication.Exit(error == null ? 0 : 1);
    }
}

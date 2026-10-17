using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public static class StudioChannelBoxValidation
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static AnimationStudioWindow controller;
    private static AnimationStudioViewport view;
    private static StudioModelEditor editor;
    private static double next;
    private static int step;
    private static bool done;
    private static string errors = "";
    public static void Run()
    {
        AnimationStudioWindow.Open();
        controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
        view = Resources.FindObjectsOfTypeAll<AnimationStudioViewport>().First();
        view.position = new Rect(100, 100, 1300, 850);
        typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Flags).Invoke(controller, new object[] { true });
        typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Flags).Invoke(controller, new object[] { null });
        editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Flags).GetValue(controller);
        editor.CreatePrimitive(PrimitiveType.Cube, view, document => typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Flags).Invoke(controller, new object[] { document }));
        Require(editor.Document && editor.Document.nodes.Count == 1 && editor.ActiveMesh, "Create from an empty studio did not create a working document and mesh");
        editor.SetObjectChannels(new Vector3(2, 3, 4), new Vector3(0, 30, 0), new Vector3(2, 2, 2), false);
        Require(editor.Transforms[0].localPosition == new Vector3(2, 3, 4) && !editor.Transforms[0].gameObject.activeSelf, "Channels did not apply position and visibility");
        Require(Quaternion.Angle(editor.Transforms[0].localRotation, Quaternion.Euler(0, 30, 0)) < .01f && editor.Transforms[0].localScale == Vector3.one * 2, "Channels did not apply rotation and scale");
        Undo.PerformUndo();
        Require(editor.Transforms[0].localPosition == Vector3.zero && editor.Transforms[0].localScale == Vector3.one && editor.Transforms[0].gameObject.activeSelf, "Channel Undo failed");
        Undo.PerformRedo();
        Require(editor.Transforms[0].localPosition == new Vector3(2, 3, 4) && !editor.Transforms[0].gameObject.activeSelf, "Channel Redo failed");
        editor.SetObjectChannels(Vector3.zero, Vector3.zero, Vector3.one, true);
        editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.EditPivot, view);
        var point = editor.Transforms[0].TransformPoint(editor.ActiveMesh.vertices[0]);
        editor.SetObjectChannels(Vector3.right, Vector3.one * 90, Vector3.one * 5, true);
        Require(Vector3.Distance(point, editor.Transforms[0].TransformPoint(editor.ActiveMesh.vertices[0])) < .001f, "Pivot channel edits moved geometry");
        Require(editor.Transforms[0].localScale == Vector3.one && editor.Transforms[0].localRotation == Quaternion.identity, "Pivot editing changed rotation or scale");
        editor.ActivateTool(Tool.None);
        Require(Field<Tool>("tool") == Tool.None && !Field<bool>("pivot"), "Select tool did not clear pivot editing");
        editor.ActivateTool(Tool.Move);
        view.Focus(); view.Repaint();
        next = EditorApplication.timeSinceStartup + 1;
        Application.logMessageReceived += Log;
        EditorApplication.update += Tick;
    }
    private static void Log(string text, string stack, LogType type)
    { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors += text + "\n" + stack + "\n"; }
    private static T Field<T>(string name) => (T)typeof(StudioModelEditor).GetField(name, Flags).GetValue(editor);
    private static void Require(bool test, string message) { if (!test) throw new Exception(message); }
    private static void Tick()
    {
        if (done || EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .6;
        try
        {
            switch (step++)
            {
                case 0:
                    var tabs = (UnityEngine.UIElements.IMGUIContainer)typeof(AnimationStudioViewport).GetField("modeTabs", Flags).GetValue(view);
                    var inspector = (UnityEngine.UIElements.IMGUIContainer)typeof(AnimationStudioViewport).GetField("inspector", Flags).GetValue(view);
                    Require(tabs.resolvedStyle.height >= 59 && inspector.resolvedStyle.top >= 67, "Menu bar or inspector did not reserve the second toolbar row");
                    view.SendEvent(new Event { type = EventType.MouseDown, button = 1, mousePosition = new Vector2(600, 50) });
                    Require(!Field<bool>("markingOpen"), "Marking menu intercepted the menu bar");
                    view.SendEvent(new Event { type = EventType.MouseUp, button = 1, mousePosition = new Vector2(600, 50) });
                    view.SendEvent(new Event { type = EventType.MouseDown, button = 1, mousePosition = new Vector2(600, 350) });
                    Require(Field<bool>("markingOpen"), "Native viewport RMB event failed to open marking menu");
                    break;
                case 1:
                    Require(Field<bool>("markingOpen"), "Menu lost capture/focus while awaiting drag");
                    view.SendEvent(new Event { type = EventType.MouseDrag, button = 1, mousePosition = new Vector2(734, 412) });
                    Require(Field<StudioModelEditor.MarkingLevel>("markingLevel") == StudioModelEditor.MarkingLevel.UV, "Native drag did not enter UV submenu");
                    break;
                case 2:
                    view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
                    Require(!Field<bool>("markingOpen"), "Native Escape did not dismiss menu");
                    Require(!view.drawGizmos, "Preview gizmo isolation was lost");
                    Require(string.IsNullOrEmpty(errors), errors);
                    view.modelGridVisible = false; view.Repaint();
                    var strip = (UnityEngine.UIElements.IMGUIContainer)typeof(AnimationStudioViewport).GetField("modelTools", Flags).GetValue(view);
                    Require(strip.resolvedStyle.width == 32 && strip.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.Flex, "Tool strip is not visible");
                    // Dispatch a real UIElements/IMGUI double-click to the Move icon.
                    var toolPoint = new Vector2(strip.worldBound.x + 16, strip.worldBound.y + 48);
                    view.SendEvent(new Event { type = EventType.MouseDown, button = 0, clickCount = 2, mousePosition = toolPoint });
                    view.SendEvent(new Event { type = EventType.MouseUp, button = 0, clickCount = 2, mousePosition = toolPoint });
                    Require(Resources.FindObjectsOfTypeAll<StudioModelToolOptions>().Length == 1, "Double-click Move did not open Tool Options");
                    break;
                case 3:
                    Require(string.IsNullOrEmpty(errors), errors);
                    Require(!view.modelGridVisible, "Grid visibility did not retain its setting");
                    editor.ActivateTool(Tool.Rotate);
                    Require(Resources.FindObjectsOfTypeAll<StudioModelToolOptions>().Length == 1 && Field<Tool>("tool") == Tool.Rotate, "Tool Options did not remain bound to the active tool");
                    typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Flags).Invoke(controller, new object[] { false });
                    var modes = (UnityEngine.UIElements.IMGUIContainer)typeof(AnimationStudioViewport).GetField("modeTabs", Flags).GetValue(view);
                    Require(modes.style.height.value.value == 36, "Animation mode did not restore its toolbar height");
                    Require(Resources.FindObjectsOfTypeAll<StudioModelToolOptions>().Length == 0, "Tool Options remained open after leaving Modeling");
                    var hiddenStrip = (UnityEngine.UIElements.IMGUIContainer)typeof(AnimationStudioViewport).GetField("modelTools", Flags).GetValue(view);
                    Require(hiddenStrip.style.display.value == UnityEngine.UIElements.DisplayStyle.None, "Model tools remained visible in Animation");
                    Finish(0, "PASS: Channel translation/rotation/scale/visibility, Undo/Redo, pivot geometry preservation, Select tool, toolbar layout, marking menus, actual double-click Tool Options, tool switching, grid state and animation cleanup; no GUI exceptions.");
                    break;
            }
        }
        catch (Exception e) { Finish(1, "FAIL: " + e + "\n" + errors); }
    }
    private static void Finish(int code, string result)
    {
        done = true; EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        var documentPath = AssetDatabase.GetAssetPath(editor.Document);
        editor.CancelMarkingMenu(); view.Close(); UnityEngine.Object.DestroyImmediate(controller);
        if (!string.IsNullOrEmpty(documentPath)) AssetDatabase.DeleteAsset(documentPath);
        File.WriteAllText("channel-box-result.txt", result);
        EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}



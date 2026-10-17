using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public static class StudioModelMenusIntegrationValidation
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
                    typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Flags).Invoke(controller, new object[] { false });
                    var modes = (UnityEngine.UIElements.IMGUIContainer)typeof(AnimationStudioViewport).GetField("modeTabs", Flags).GetValue(view);
                    Require(modes.style.height.value.value == 36, "Animation mode did not restore its toolbar height");
                    Finish(0, "PASS: Empty MashBox Studio creates a saved cube, renders the modeling menus with clear panel spacing, excludes the toolbar from RMB gestures, preserves nested marking menus and gizmo isolation, and restores the animation layout without GUI exceptions.");
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
        File.WriteAllText("model-menus-integration-result.txt", result);
        EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}

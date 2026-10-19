using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class StudioPanelResizeValidation
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static AnimationStudioWindow controller;
    private static MashBoxStudioViewport view;
    private static bool hadLeft, hadRight, preview;
    private static float oldLeft, oldRight;
    private static int step;
    private static double next;
    private static string errors = "";
    private static readonly List<string> Results = new List<string>();
    public static void Preview() { preview = true; Run(); }
    public static void Run()
    {
        hadLeft = EditorPrefs.HasKey(MashBoxStudioViewport.LeftWidthKey); oldLeft = EditorPrefs.GetFloat(MashBoxStudioViewport.LeftWidthKey);
        hadRight = EditorPrefs.HasKey(MashBoxStudioViewport.RightWidthKey); oldRight = EditorPrefs.GetFloat(MashBoxStudioViewport.RightWidthKey);
        EditorPrefs.SetFloat(MashBoxStudioViewport.LeftWidthKey, 260); EditorPrefs.SetFloat(MashBoxStudioViewport.RightWidthKey, 350);
        AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First(); view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First();
        typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Fields).Invoke(controller, new object[] { true });
        var main = EditorGUIUtility.GetMainWindowPosition(); view.position = new Rect(main.x + 50, main.y + 80, 1400, 800); view.Focus();
        Application.logMessageReceived += Log; EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
    }
    private static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack; }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); Results.Add("PASS: " + label); }
    private static IMGUIContainer Element(string name) => (IMGUIContainer)typeof(MashBoxStudioViewport).GetField(name, Fields).GetValue(view);
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .5;
        try
        {
            switch (step++)
            {
                case 0:
                    Check(Mathf.Approximately(Element("modelMenuBar").layout.yMin, 0) && Mathf.Approximately(Element("modelMenuBar").layout.height, 24), "Modeling menu starts at the top without a separate mode bar");
                    Check(Mathf.Approximately(view.ModelCanvasRect.yMin, 32) && Mathf.Approximately(Element("rigControls").layout.yMin, 32), "Viewport and sidebars reclaim 36 pixels of height");
                    Check(Element("modeTabs").layout.size == new Vector2(64, 24) && Mathf.Abs(Element("modeTabs").layout.center.x - view.ModelCanvasRect.center.x) <= 1, "Compact mode icons are centered over the scene canvas: " + Element("modeTabs").layout + " canvas " + view.ModelCanvasRect);
                    Check(Element("viewportOptions").layout.xMin > view.ModelCanvasRect.xMin && !Element("viewportOptions").layout.Overlaps(Element("modeTabs").layout), "Local and Grid sit inside the left of the canvas without overlapping modes");
                    Check(view.IsOverViewportControls(Element("modeTabs").layout.center) && view.IsOverViewportControls(Element("viewportOptions").layout.center), "Scene marking menus exclude floating controls");
                    Check(Mathf.Approximately(Element("modelTools").layout.xMin, 8) && Mathf.Approximately(Element("modelTools").layout.xMax, Element("rigControls").layout.xMin), "Tool strip is at the far left with no gap before the hierarchy");
                    Check(Mathf.Approximately(view.rootVisualElement.layout.width - Element("inspector").layout.xMax, 8), "Channels use the right edge without a tool strip reservation");
                    Check(Element("leftDivider").resolvedStyle.width == 8 && Element("rightDivider").resolvedStyle.width == 8, "Both inner edges have resize handles");
                    view.ResizePanel(1, 380); view.ResizePanel(2, 420); break;
                case 1:
                    Check(Mathf.Approximately(Element("rigControls").resolvedStyle.width, 380) && Mathf.Approximately(Element("inspector").resolvedStyle.width, 420), "Both panels independently resize");
                    Check(Mathf.Approximately(view.ModelCanvasRect.xMin, 428) && Mathf.Approximately(view.ModelCanvasRect.xMax, 964), "Viewport hit bounds follow the panel edges");
                    view.SavePanelWidths();
                    Check(EditorPrefs.GetFloat(MashBoxStudioViewport.LeftWidthKey) == 380 && EditorPrefs.GetFloat(MashBoxStudioViewport.RightWidthKey) == 420, "Widths persist in editor preferences");
                    view.position = new Rect(view.position.position, new Vector2(1050, 800)); break;
                case 2:
                    Check(!Element("viewportOptions").layout.Overlaps(Element("modeTabs").layout), "Narrow viewport keeps overlay controls separate");
                    Check(view.ModelCanvasRect.width >= 319.9f && view.PanelWidths.x >= 180 && view.PanelWidths.y >= 220, "Narrow windows retain usable panels and a center viewport");
                    view.position = new Rect(view.position.position, new Vector2(1400, 800)); break;
                case 3:
                    Check(view.PanelWidths == new Vector2(380, 420), "Expanding the window restores the preferred widths");
                    view.ResizePanel(1, -100); view.ResizePanel(2, 10000); break;
                case 4:
                    Check(view.PanelWidths.x == 180 && view.PanelWidths.y <= 600 && view.ModelCanvasRect.width >= 320, "Drag limits prevent collapsed panels or an obstructed viewport");
                    view.ResizePanel(1, 260); view.ResizePanel(2, 350);
                    typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Fields).Invoke(controller, new object[] { false }); break;
                case 5:
                    Check(Mathf.Approximately(Element("rigControls").layout.xMin, 8) && Element("modelTools").resolvedStyle.display == DisplayStyle.None, "Animation mode restores the left hierarchy margin and hides modeling tools");
                    Check(Mathf.Approximately(Element("leftDivider").resolvedStyle.top, 8) && Mathf.Approximately(view.rootVisualElement.layout.height - Element("rightDivider").layout.yMax, 148), "Animation mode keeps handles clear of its timeline");
                    Check(Element("modelMenuBar").resolvedStyle.display == DisplayStyle.None && Element("viewportOptions").resolvedStyle.display == DisplayStyle.None && Element("studioSettings").resolvedStyle.display == DisplayStyle.Flex, "Animation hides modeling controls and retains settings");
                    typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Fields).Invoke(controller, new object[] { true });
                    view.Focus(); break;
                case 6:
                    if (preview) { File.WriteAllText("panel-preview-ready.txt", "ready"); break; }
                    // Route real Unity mouse events through the SceneView and its overlay containers.
                    view.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = new Vector2(304, 220) });
                    Check((int)typeof(MashBoxStudioViewport).GetField("resizingSide", Fields).GetValue(view) == 1, "Hierarchy divider captures mouse-down"); break;
                case 7:
                    if (preview) { step--; if (File.Exists("panel-preview-finish.txt")) { Check(view.PanelWidths.x > 300 && view.PanelWidths.y > 390, "Real mouse drags resized both side panels"); Finish(0, null); } break; }
                    view.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = new Vector2(404, 220) });
                    view.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = new Vector2(404, 220) });
                    Check(Mathf.Approximately(view.PanelWidths.x, 360), "Hierarchy width follows mouse drag"); break;
                case 8:
                    view.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = new Vector2(1038, 220) });
                    Check((int)typeof(MashBoxStudioViewport).GetField("resizingSide", Fields).GetValue(view) == 2, "Channels divider captures mouse-down"); break;
                case 9:
                    view.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = new Vector2(938, 220) });
                    view.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = new Vector2(938, 220) });
                    Check(Mathf.Approximately(view.PanelWidths.y, 450), "Dragging the channels divider left expands the panel");
                    view.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = new Vector2(404, 220) }); break;
                case 10:
                    view.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = new Vector2(484, 220) });
                    view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
                    Check(Mathf.Approximately(view.PanelWidths.x, 360) && (int)typeof(MashBoxStudioViewport).GetField("resizingSide", Fields).GetValue(view) == 0, "Escape restores the width and releases resize capture");
                    var reopened = ScriptableObject.CreateInstance<MashBoxStudioViewport>();
                    reopened.position = new Rect(0, 0, 1400, 800); reopened.BindModes(() => { }, true);
                    Check(reopened.PanelWidths == new Vector2(360, 450), "New studio view restores saved sidebar widths");
                    UnityEngine.Object.DestroyImmediate(reopened);
                    break;
                case 11:
                    bool grid = view.modelGridVisible;
                    var gridPoint = Element("viewportOptions").layout.position + new Vector2(105, 10);
                    view.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = gridPoint });
                    view.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = gridPoint });
                    Check(view.modelGridVisible != grid, "Grid toggle responds in its floating overlay");
                    view.modelGridVisible = grid;
                    ClickMode(false); break;
                case 12:
                    Check(!(bool)typeof(AnimationStudioWindow).GetField("modelingMode", Fields).GetValue(controller), "Animation icon switches mode");
                    ClickMode(true); break;
                case 13:
                    Check((bool)typeof(AnimationStudioWindow).GetField("modelingMode", Fields).GetValue(controller), "Modeling icon switches mode");
                    Finish(0, null); break;
            }
        }
        catch (Exception e) { Finish(1, e.ToString()); }
    }
    private static void ClickMode(bool modeling)
    {
        var point = Element("modeTabs").layout.position + new Vector2(modeling ? 48 : 16, 12);
        view.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = point });
        view.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = point });
    }
    private static void Finish(int code, string error)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        if (error != null) Results.Add("FAIL: " + error);
        if (!string.IsNullOrEmpty(errors)) { Results.Add("FAIL: " + errors); code = 1; }
        if (view) view.Close(); if (controller) UnityEngine.Object.DestroyImmediate(controller);
        if (hadLeft) EditorPrefs.SetFloat(MashBoxStudioViewport.LeftWidthKey, oldLeft); else EditorPrefs.DeleteKey(MashBoxStudioViewport.LeftWidthKey);
        if (hadRight) EditorPrefs.SetFloat(MashBoxStudioViewport.RightWidthKey, oldRight); else EditorPrefs.DeleteKey(MashBoxStudioViewport.RightWidthKey);
        File.WriteAllLines("panel-resize-result.txt", Results); EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}

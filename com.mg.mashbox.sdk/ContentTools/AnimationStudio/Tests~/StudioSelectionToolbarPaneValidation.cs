using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

[Overlay(typeof(MashBoxStudioViewport), "MashBox/RegistryFixture", "Registry Fixture", false)]
public sealed class RegistryFixtureToolbar : ToolbarOverlay { public RegistryFixtureToolbar() : base() { } }

public static class StudioSelectionToolbarPaneValidation
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static AnimationStudioWindow controller;
    static MashBoxStudioViewport view;
    static StudioModelEditor editor;
    static StudioModelDocument document;
    static ToolChannelPane pane;
    static double next;
    static int step, checks, operations;
    static string errors = "";
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, F).Invoke(o, args);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static void Check(bool pass, string message) { if (!pass) throw new Exception(message); checks++; }
    static StudioMenu Menu(string title) { var menu = new StudioMenu(); Call(editor, "PopulateModelMenu", title, menu, view, null); return menu; }
    public static void Run() { EditorApplication.delayCall += Begin; }
    static void Begin()
    {
        try
        {
            document = ScriptableObject.CreateInstance<StudioModelDocument>();
            var mesh = StudioModelDocument.BuildPrimitive(PrimitiveType.Cube, out var polygons);
            document.nodes.Add(new StudioModelDocument.Node { name = "Toolbar cube", mesh = mesh, cutPolygons = polygons, cutTriangles = mesh.triangles });
            controller = ScriptableObject.CreateInstance<AnimationStudioWindow>(); Set(controller, "modelingMode", true); Set(controller, "modelDocument", document);
            Call(controller, "OpenViewport");
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Single(v => v.Studio == controller);
            view.position = new Rect(0, 0, 1400, 900);
            editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", F).GetValue(controller);
            editor.SelectObject(0);
            Application.logMessageReceived += Log;
            EditorApplication.update += Tick;
            next = EditorApplication.timeSinceStartup + 2;
        }
        catch (Exception e) { Finish(e); }
    }
    static void Log(string text, string stack, LogType type)
    { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors += text + "\n" + stack + "\n"; }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .5;
        try
        {
            switch (step++)
            {
                case 0:
                    var toolbar = view.overlayCanvas.overlays.Single(o => o.id == StudioSelectionToolbar.Id);
                    Check(toolbar is ToolbarOverlay && toolbar.displayed && !toolbar.floating, "Selection toolbar starts docked and visible");
                    Check(toolbar.rootVisualElement.ClassListContains("overlay-layout--toolbar-horizontal"), "Selection toolbar starts horizontal");
                    var grid = view.overlayCanvas.overlays.Single(o => o.id == StudioGridToolbar.Id);
                    Check(toolbar.rootVisualElement.worldBound.xMin < grid.rootVisualElement.worldBound.xMin, "Selection toolbar precedes Grid on default layout");
                    var buttons = toolbar.rootVisualElement.Query<EditorToolbarToggle>().ToList();
                    Check(buttons.Count == 4, "Object, vertex, edge and face buttons");
                    foreach (var mode in new[] { StudioModelEditor.ComponentMode.Object, StudioModelEditor.ComponentMode.Vertex, StudioModelEditor.ComponentMode.Edge, StudioModelEditor.ComponentMode.Face })
                    {
                        var button = toolbar.rootVisualElement.Q<EditorToolbarToggle>("studio-toolbar-mode-" + mode.ToString().ToLowerInvariant());
                        Check(button != null && button.enabledSelf, "Selection button enabled");
                        button.value = true;
                        Check(editor.ToolbarComponentMode == mode, "Button changes actual component mode");
                        Check(buttons.Count(b => b.value) == 1, "Selection mode highlights are exclusive");
                        button.value = false; Check(button.value, "Selected mode cannot be untoggled");
                    }
                    view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Alpha2 });
                    Check(editor.ToolbarComponentMode == StudioModelEditor.ComponentMode.Vertex, "Native keyboard shortcut changes mode");
                    Check(toolbar.rootVisualElement.Q<EditorToolbarToggle>("studio-toolbar-mode-vertex").value, "Keyboard synchronizes toolbar highlight");
                    Check(!Menu("Edit Mesh").Items.Any(i => i.Text.Contains("Extrude") || i.Text.Contains("Seams") || i.Text.Contains("Mode")), "Old component menu removed");
                    Check(Menu("Mesh Tools").Items.Any(i => i.Text == "Extrude"), "Extrude lives in Mesh Tools");
                    var display = Menu("Display");
                    var own = view.overlayCanvas.overlays.Where(o => o is ToolbarOverlay && o.GetType().Assembly == typeof(MashBoxStudioViewport).Assembly).ToList();
                    foreach (var overlay in own) Check(display.Items.Any(i => i.Text == (overlay.id == StudioViewportToolbar.Id ? StudioViewportToolbar.DisplayName : overlay.displayName)), "Every registered Studio toolbar has a display toggle");
                    var toggle = display.Items.Single(i => i.Text == "Selection Modes"); toggle.Action(); Check(!toolbar.displayed, "Display hides selection toolbar");
                    toggle = Menu("Display").Items.Single(i => i.Text == "Selection Modes"); Check(!toggle.Checked, "Hidden toolbar remains in registry menu"); toggle.Action(); Check(toolbar.displayed, "Display restores selection toolbar");
                    Check(Menu("Display").Items.Any(i => i.Text == "Registry Fixture"), "A future registered toolbar needs no menu code");
                    pane = ToolChannelPane.Open("Command test", () => GUILayout.Label("Reusable tool content"), execute: () => operations++, canExecute: () => true);
                    pane.ExecuteCommand(false); Check(pane && operations == 1, "Apply executes and keeps pane open");
                    view.Focus(); view.Repaint();
                    break;
                case 1:
                    Check(pane && operations == 1, "Pane survives focus moving to viewport");
                    view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.W });
                    Check(pane, "W does not close the pane");
                    pane.Focus();
                    var executePoint = new Vector2(30, pane.position.height - 20);
                    pane.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = executePoint });
                    pane.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = executePoint });
                    Check(!pane && operations == 2, "Native Execute button runs and closes pane");
                    pane = ToolChannelPane.Open("Failure test", () => GUILayout.Label("Fails safely"), execute: () => throw new InvalidOperationException("Expected failure"), failed: _ => operations++);
                    Check(!pane.ExecuteCommand(true) && pane && operations == 3, "Failed Execute leaves pane open"); pane.Close();
                    pane = ToolChannelPane.Open("Disabled test", () => GUILayout.Label("Invalid selection"), execute: () => operations++, canExecute: () => false);
                    Check(!pane.ExecuteCommand(true) && pane && operations == 3, "Disabled command cannot execute or close"); pane.Close();
                    editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge); editor.Components.Add(0);
                    Menu("Mesh Tools").Items.Single(i => i.Text == "Bevel…").Action();
                    pane = Resources.FindObjectsOfTypeAll<ToolChannelPane>().Single(p => p.titleContent.text == "Bevel");
                    int vertices = editor.ActiveMesh.vertexCount;
                    Check(pane.ExecuteCommand(false) && pane && editor.ActiveMesh.vertexCount > vertices, "Actual bevel Apply changes mesh and keeps pane open");
                    vertices = editor.ActiveMesh.vertexCount; pane.Close(); Check(editor.ActiveMesh.vertexCount == vertices, "Closing pane retains previously applied geometry");
                    Undo.PerformUndo(); Check(editor.ActiveMesh.vertexCount < vertices, "Applied tool remains undoable"); Undo.PerformRedo();
                    editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge); editor.Components.Add(0);
                    Menu("Mesh Tools").Items.Single(i => i.Text == "Bevel…").Action();
                    pane = Resources.FindObjectsOfTypeAll<ToolChannelPane>().Single(p => p.titleContent.text == "Bevel");
                    Check(pane.ExecuteCommand(true) && !pane, "Actual bevel Execute closes pane");
                    view.Repaint();
                    break;
                case 2:
                    Check(string.IsNullOrEmpty(errors), "No native UI errors: " + errors);
                    Snapshot(); Finish(null); break;
            }
        }
        catch (Exception e) { Finish(e); }
    }
    static void Snapshot()
    {
        var parent = typeof(EditorWindow).GetField("m_Parent", F).GetValue(view);
        var rt = new RenderTexture(1400, 900, 0); var pixels = new Texture2D(1400, 900, TextureFormat.RGBA32, false); var old = RenderTexture.active;
        try
        {
            rt.Create(); parent.GetType().GetMethod("GrabPixels", F).Invoke(parent, new object[] { rt, new Rect(0, 0, 1400, 900) });
            RenderTexture.active = rt; pixels.ReadPixels(new Rect(0, 0, 1400, 900), 0, 0); pixels.Apply();
            var colors = pixels.GetPixels(); var flipped = new Color[colors.Length];
            for (int y = 0; y < 900; y++) Array.Copy(colors, y * 1400, flipped, (899-y)*1400, 1400);
            pixels.SetPixels(flipped); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Application.dataPath, "../selection-toolbar-pane.png"), pixels.EncodeToPNG());
        }
        finally { RenderTexture.active = old; Object.DestroyImmediate(pixels); Object.DestroyImmediate(rt); }
    }
    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        File.WriteAllText(Path.Combine(Application.dataPath, "../selection-toolbar-pane-result.txt"), error == null ? "PASS " + checks + " native toolbar/pane checks" : error + "\n" + errors);
        if (pane) pane.Close();
        if (editor != null) editor.Load(null);
        if (document) { foreach (var n in document.nodes) if (n.mesh) Object.DestroyImmediate(n.mesh); Object.DestroyImmediate(document); }
        if (view) view.Close();
        if (controller) Object.DestroyImmediate(controller);
        Undo.ClearAll(); EditorApplication.Exit(error == null ? 0 : 1);
    }
}

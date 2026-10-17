using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object = UnityEngine.Object;

public class StudioMarkingMenuValidation : EditorWindow
{
    private static readonly List<string> Results = new List<string>();
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private bool ran;
    public static void Run()
    {
        var window = CreateInstance<StudioMarkingMenuValidation>();
        window.position = new Rect(100, 100, 1100, 800); window.Show();
        window.Repaint();
    }
    private static T Read<T>(StudioModelEditor editor, string name) => (T)typeof(StudioModelEditor).GetField(name, Fields).GetValue(editor);
    private static void Check(bool value, string label)
    { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
    private void OnGUI()
    {
        if (ran || Event.current.type != EventType.Repaint) return;
        ran = true; int code = 0;
        var original = Event.current;
        StudioModelEditor editor = null; SceneView view = null; GameObject cube = null;
        string path = "Assets/MarkingValidation.asset";
        try
        {
            editor = new StudioModelEditor(() => { });
            view = CreateInstance<SceneView>(); view.position = new Rect(GUIUtility.GUIToScreenPoint(Vector2.zero), new Vector2(1100, 800));
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            AssetDatabase.DeleteAsset(path);
            editor.Load(StudioModelDocument.Import(cube, path)); editor.SelectObject(0);
            var sourceVertices = cube.GetComponent<MeshFilter>().sharedMesh.vertices;
            Event lastEvent = null;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            Action<EventType, Vector2, EventModifiers> send = (type, point, modifiers) => {
                lastEvent = new Event { type = type, button = 1, mousePosition = point, modifiers = modifiers };
                editor.HandleModelMarkingEvent(view, lastEvent, control);
            };
            Action<EventModifiers> open = modifiers => send(EventType.MouseDown, new Vector2(550, 350), modifiers);
            Action<int, EventType> sector = (index, type) => {
                var offsets = (Vector2[])typeof(StudioModelEditor).GetField("MarkingOffsets", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                send(type, GUIUtility.ScreenToGUIPoint(Read<Vector2>(editor, "markingOrigin") + offsets[index]), EventModifiers.None);
            };
            Func<bool> opened = () => Read<bool>(editor, "markingOpen");
            open(EventModifiers.Alt); Check(!opened(), "Alt-right mouse passes through to camera navigation");
            GUIUtility.hotControl = 999;
            open(EventModifiers.None); Check(!opened(), "An existing handle drag keeps mouse ownership"); GUIUtility.hotControl = 0;
            open(EventModifiers.None); Check(opened() && lastEvent.type == EventType.Used && GUIUtility.hotControl != 0, "RMB opens and captures the gesture");
            sector(6, EventType.MouseUp);
            Check(!opened() && GUIUtility.hotControl == 0 && Read<StudioModelEditor.ComponentMode>(editor, "mode") == StudioModelEditor.ComponentMode.Vertex, "Drag left and release selects Vertex and releases mouse");
            editor.Components.Add(0);
            open(EventModifiers.None); send(EventType.MouseUp, new Vector2(550, 350), EventModifiers.None);
            Check(editor.Components.Contains(0), "Neutral release cancels without clearing selection");
            open(EventModifiers.None); sector(0, EventType.MouseUp);
            Check(Read<StudioModelEditor.ComponentMode>(editor, "mode") == StudioModelEditor.ComponentMode.Edge && editor.Components.Count == 0, "Drag up selects Edge and clears incompatible components");
            open(EventModifiers.Shift); Check(Read<StudioModelEditor.MarkingLevel>(editor, "markingLevel") == StudioModelEditor.MarkingLevel.Components, "Shift-RMB retains the component menu");
            sector(4, EventType.MouseUp); Check(Read<StudioModelEditor.ComponentMode>(editor, "mode") == StudioModelEditor.ComponentMode.Face, "Drag down selects Face");
            open(EventModifiers.Control); Check(Read<StudioModelEditor.MarkingLevel>(editor, "markingLevel") == StudioModelEditor.MarkingLevel.Components, "Ctrl-RMB retains the component menu");
            sector(1, EventType.MouseUp); Check(Read<StudioModelEditor.ComponentMode>(editor, "mode") == StudioModelEditor.ComponentMode.Object, "Upper-right selects Object Mode");
            open(EventModifiers.None); sector(2, EventType.MouseDrag);
            Check(opened() && Read<StudioModelEditor.MarkingLevel>(editor, "markingLevel") == StudioModelEditor.MarkingLevel.UV, "Dragging onto UV opens first submenu without releasing RMB");
            sector(2, EventType.MouseDrag);
            Check(Read<StudioModelEditor.MarkingLevel>(editor, "markingLevel") == StudioModelEditor.MarkingLevel.UVSets, "Dragging onto UV Sets opens the second nested submenu");
            sector(4, EventType.MouseUp);
            var uvWindows = Resources.FindObjectsOfTypeAll<StudioUVEditor>();
            Check(!opened() && uvWindows.Length == 1 && (int)typeof(StudioUVEditor).GetField("channel", Fields).GetValue(uvWindows[0]) == 4, "Releasing over UV 4 opens UV editor on channel 4");
            foreach (var window in uvWindows) window.Close();
            open(EventModifiers.None); sector(2, EventType.MouseDrag);
            var back = Read<Vector2>(editor, "markingOrigin") + new Vector2(0, -84);
            send(EventType.MouseDrag, GUIUtility.ScreenToGUIPoint(back), EventModifiers.None);
            Check(Read<StudioModelEditor.MarkingLevel>(editor, "markingLevel") == StudioModelEditor.MarkingLevel.Components, "Dragging onto Back restores parent menu");
            lastEvent = new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }; editor.HandleModelMarkingEvent(view, lastEvent, control);
            Check(!opened() && GUIUtility.hotControl == 0 && lastEvent.type == EventType.Used, "Escape cancels and releases capture");
            // Root near top edge is clamped for visibility. Stationary release must still cancel.
            var priorMode = Read<StudioModelEditor.ComponentMode>(editor, "mode");
            send(EventType.MouseDown, new Vector2(550, 45), EventModifiers.None);
            send(EventType.MouseUp, new Vector2(550, 45), EventModifiers.None);
            Check(!opened() && priorMode == Read<StudioModelEditor.ComponentMode>(editor, "mode"), "Clamped menu does not execute a command on stationary release");
            open(EventModifiers.None); sector(2, EventType.MouseDrag);
            var pointer = Read<Vector2>(editor, "markingPointer");
            send(EventType.MouseUp, GUIUtility.ScreenToGUIPoint(pointer), EventModifiers.None);
            Check(Resources.FindObjectsOfTypeAll<StudioUVEditor>().Length == 0, "Releasing immediately after entering a submenu does not trigger a leaf");
            open(EventModifiers.None); editor.Load(null);
            Check(!opened() && GUIUtility.hotControl == 0, "Changing documents cancels any captured menu");
            open(EventModifiers.None); sector(6, EventType.MouseUp);
            Check(priorMode == Read<StudioModelEditor.ComponentMode>(editor, "mode"), "Component modes are disabled without a selected mesh");
            Check(cube.GetComponent<MeshFilter>().sharedMesh.vertices[0] == sourceVertices[0], "Menu navigation does not mutate source mesh geometry");
        }
        catch (Exception e) { code = 1; Results.Add("FAIL: " + e); }
        finally
        {
            editor?.Dispose(); if (view) Object.DestroyImmediate(view); if (cube) Object.DestroyImmediate(cube);
            AssetDatabase.DeleteAsset(path); Event.current = original; GUIUtility.hotControl = 0;
            File.WriteAllLines("marking-result.txt", Results);
            EditorApplication.delayCall += () => EditorApplication.Exit(code);
        }
    }
}

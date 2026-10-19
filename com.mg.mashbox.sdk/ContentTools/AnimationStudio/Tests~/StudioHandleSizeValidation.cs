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
    public static class StudioHandleSizeValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> Results = new List<string>();
        static AnimationStudioWindow controller; static MashBoxStudioViewport view; static StudioModelEditor editor;
        static string oldSettings, oldModel, errors = "", documentBefore;
        static bool hadSettings, hadModel; static int step; static double next; static float baseSize;
        static Vector2 pointer, origin;
        static string ModelKey => "MashBox.LastModel." + Application.dataPath;
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        static void Key(KeyCode key, EventModifiers mods = EventModifiers.None) => view.SendEvent(new Event { type = EventType.KeyDown, keyCode = key, modifiers = mods });
        static Vector2 Screen(Vector3 world) { var p = view.camera.WorldToScreenPoint(world); return new Vector2(p.x, view.camera.pixelHeight - p.y) / EditorGUIUtility.pixelsPerPoint; }
        static void Mouse(EventType type, Vector2 p) => view.SendEvent(new Event { type = type, button = 0, mousePosition = p });
        public static void Run()
        {
            hadSettings = EditorPrefs.HasKey(StudioSettings.PreferenceKey); oldSettings = EditorPrefs.GetString(StudioSettings.PreferenceKey);
            hadModel = EditorPrefs.HasKey(ModelKey); oldModel = EditorPrefs.GetString(ModelKey);
            StudioSettings.Current.handleSize = 1; StudioSettings.Save();
            var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Handle Size Test.asset")); doc.AddPrimitive(PrimitiveType.Cube);
            AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Private).Invoke(controller, new object[] { true });
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Private).Invoke(controller, new object[] { doc });
            editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Private).GetValue(controller); editor.SelectObject(0);
            documentBefore = EditorJsonUtility.ToJson(doc);
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First();
            view.position = new Rect(100, 100, 1400, 800); view.pivot = Vector3.zero; view.rotation = Quaternion.identity; view.size = 2; view.orthographic = true; view.Focus();
            Application.logMessageReceived += Log; SceneView.duringSceneGui += Measure; EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
        }
        static void Measure(SceneView v) { if (v == view && Event.current.type == EventType.Repaint) baseSize = HandleUtility.GetHandleSize(editor.Transforms[0].position); }
        static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack; }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .4;
            try
            {
                switch(step++)
                {
                    case 0:
                        Key(KeyCode.Equals); Check(Mathf.Approximately(StudioSettings.Current.handleSize, 1.2f), "Equals/plus key enlarges the handles");
                        Key(KeyCode.Equals, EventModifiers.Shift); Check(Mathf.Approximately(StudioSettings.Current.handleSize, 1.44f), "Shift-plus enlarges handles");
                        Key(KeyCode.KeypadMinus); Key(KeyCode.Minus); Check(Mathf.Approximately(StudioSettings.Current.handleSize, 1), "Main and numpad minus decrease the size");
                        Key(KeyCode.KeypadPlus); Check(Mathf.Approximately(StudioSettings.Current.handleSize, 1.2f), "Numpad plus increases the size");
                        Key(KeyCode.Minus, EventModifiers.Control); Check(Mathf.Approximately(StudioSettings.Current.handleSize, 1.2f), "Modified command shortcuts do not resize handles");
                        EditorGUIUtility.editingTextField = true;
                        Check(!StudioModelEditor.HandleSizeShortcut(new Event { type = EventType.KeyDown, keyCode = KeyCode.Minus }), "Minus remains available when editing numeric text");
                        EditorGUIUtility.editingTextField = false;
                        for(int i = 0; i < 30; i++) Key(KeyCode.Plus);
                        Check(StudioSettings.Current.handleSize == 3, "Handle size has a usable maximum");
                        for(int i = 0; i < 40; i++) Key(KeyCode.Minus);
                        Check(StudioSettings.Current.handleSize == .25f, "Handle size has a visible minimum");
                        Check(EditorJsonUtility.ToJson(editor.Document) == documentBefore, "Resizing handles does not change model transforms or mesh references");
                        StudioSettings.Reload(); Check(StudioSettings.Current.handleSize == .25f, "Handle size persists after preference reload");
                        StudioSettings.Current.handleSize = .5f; StudioSettings.Save(); break;
                    case 1: BeginDrag(); break;
                    case 2: EndDrag("Small"); Undo.PerformUndo(); StudioSettings.Current.handleSize = 2.5f; StudioSettings.Save(); break;
                    case 3: BeginDrag(); break;
                    case 4: EndDrag("Large"); Undo.PerformUndo();
                        editor.ActivateTool(Tool.Rotate); break;
                    case 5: editor.ActivateTool(Tool.Scale); break;
                    case 6: Key(KeyCode.D); break;
                    case 7:
                        Check(editor.Transforms[0].position.sqrMagnitude < .0001f && editor.Transforms[0].localScale == Vector3.one, "Rotate, scale and pivot handles repaint without changing the model");
                        Finish(null); break;
                }
            }
            catch(Exception e) { Finish(e.ToString()); }
        }
        static void BeginDrag()
        {
            origin = Screen(editor.Transforms[0].position);
            pointer = Screen(editor.Transforms[0].position + Vector3.right * baseSize * StudioSettings.Current.handleSize * .8f);
            Mouse(EventType.MouseMove, pointer); view.Repaint();
            Mouse(EventType.MouseDown, pointer);
            Check(!(bool)typeof(StudioModelEditor).GetField("marquee", Private).GetValue(editor), "Resized move axis captures the mouse rather than marquee selection");
            Mouse(EventType.MouseDrag, pointer + Vector2.right * 30);
        }
        static void EndDrag(string label)
        {
            Mouse(EventType.MouseUp, pointer + Vector2.right * 30);
            var delta = Screen(editor.Transforms[0].position) - origin;
            Check(Mathf.Abs(delta.x - 30) < 2 && Mathf.Abs(delta.y) < 1, label + " handle preserves 30-pixel world-space dragging: " + delta);
        }
        static void Finish(string error)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log; SceneView.duringSceneGui -= Measure;
            if (!string.IsNullOrEmpty(errors)) error += errors;
            if (!string.IsNullOrEmpty(error)) Results.Add("FAIL: " + error);
            if (view) view.Close(); if (controller) Object.DestroyImmediate(controller);
            if (hadSettings) EditorPrefs.SetString(StudioSettings.PreferenceKey, oldSettings); else EditorPrefs.DeleteKey(StudioSettings.PreferenceKey); StudioSettings.Reload();
            if (hadModel) EditorPrefs.SetString(ModelKey, oldModel); else EditorPrefs.DeleteKey(ModelKey);
            File.WriteAllLines("handle-size-result.txt", Results); EditorApplication.delayCall += () => EditorApplication.Exit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
}

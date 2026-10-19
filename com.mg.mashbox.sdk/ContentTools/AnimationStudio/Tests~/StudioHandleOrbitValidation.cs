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
    public static class StudioHandleOrbitValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> Results = new List<string>();
        static AnimationStudioWindow controller;
        static MashBoxStudioViewport view;
        static StudioModelEditor editor;
        static string oldSettings, oldModel, errors = "", before;
        static bool hadSettings, hadModel;
        static int step, keyboard, hot;
        static double next;
        static Quaternion rotation;
        static Vector2 pointer;
        static string ModelKey => "MashBox.LastModel." + Application.dataPath;
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        static void Key(KeyCode key) => view.SendEvent(new Event { type = EventType.KeyDown, keyCode = key });
        static void Mouse(EventType type, Vector2 p, Vector2 delta) => view.SendEvent(new Event { type = type, button = 0, mousePosition = p, delta = delta, modifiers = EventModifiers.Alt });
        public static void Run()
        {
            hadSettings = EditorPrefs.HasKey(StudioSettings.PreferenceKey); oldSettings = EditorPrefs.GetString(StudioSettings.PreferenceKey);
            hadModel = EditorPrefs.HasKey(ModelKey); oldModel = EditorPrefs.GetString(ModelKey);
            StudioSettings.Current.handleSize = 1; StudioSettings.Save();
            var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Orbit Shortcut Test.asset")); doc.AddPrimitive(PrimitiveType.Cube);
            AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Private).Invoke(controller, new object[] { true });
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Private).Invoke(controller, new object[] { doc });
            editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Private).GetValue(controller); editor.SelectObject(0);
            before = EditorJsonUtility.ToJson(doc);
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First();
            view.position = new Rect(100,100,1400,800); view.pivot = Vector3.zero; view.rotation = Quaternion.Euler(15,30,0); view.size = 2; view.orthographic = false; view.Focus();
            Application.logMessageReceived += Log; SceneView.beforeSceneGui += Capture; EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
        }
        static void Capture(SceneView v)
        {
            if (v != view) return;
            keyboard = GUIUtility.keyboardControl; hot = GUIUtility.hotControl;
        }
        static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack; }
        static void BeginOrbit()
        {
            rotation = view.rotation; pointer = view.ModelCanvasRect.center + new Vector2(100,100);
            Mouse(EventType.MouseDown, pointer, Vector2.zero);
            Mouse(EventType.MouseDrag, pointer + new Vector2(60,25), new Vector2(60,25));
        }
        static void EndOrbit()
        {
            Mouse(EventType.MouseUp, pointer + new Vector2(60,25), Vector2.zero);
            view.SendEvent(new Event { type = EventType.KeyUp, keyCode = KeyCode.LeftAlt });
            view.SendEvent(new Event { type = EventType.MouseMove, mousePosition = pointer });
        }
        static void Sizes(string label)
        {
            float initial = StudioSettings.Current.handleSize;
            Key(KeyCode.Equals);
            Check(Mathf.Approximately(StudioSettings.Current.handleSize, initial * 1.2f), label + " plus works (keyboard=" + keyboard + ", hot=" + hot + ")");
            Key(KeyCode.Minus);
            Check(Mathf.Approximately(StudioSettings.Current.handleSize, initial), label + " minus works");
            Key(KeyCode.KeypadPlus); Key(KeyCode.KeypadMinus);
            Check(Mathf.Approximately(StudioSettings.Current.handleSize, initial), label + " numpad sizes work");
        }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .4;
            try
            {
                switch(step++)
                {
                    case 0: Sizes("Before orbit"); BeginOrbit(); break;
                    case 1: EndOrbit(); break;
                    case 2:
                        Check(Quaternion.Angle(rotation, view.rotation) > 1, "Native Alt-drag actually orbits the camera");
                        Sizes("After orbit"); Key(KeyCode.D); BeginOrbit(); break;
                    case 3: EndOrbit(); break;
                    case 4:
                        Sizes("Pivot mode after orbit");
                        Check(before == EditorJsonUtility.ToJson(editor.Document), "Navigation and handle sizing leave model data unchanged");
                        // Check the guard inside a valid IMGUI event without assigning keyboard focus away from navigation.
                        SceneView.beforeSceneGui += CheckGuards; view.Repaint(); break;
                    case 5: Finish(null); break;
                }
            }
            catch (Exception e) { Finish(e.ToString()); }
        }
        static void CheckGuards(SceneView v)
        {
            if (v != view || Event.current.type != EventType.Repaint) return;
            SceneView.beforeSceneGui -= CheckGuards;
            int previousHot = GUIUtility.hotControl;
            bool previousText = EditorGUIUtility.editingTextField;
            try
            {
                EditorGUIUtility.editingTextField = true;
                Check(!StudioModelEditor.HandleSizeShortcut(new Event { type = EventType.KeyDown, keyCode = KeyCode.Minus }), "Numeric text retains its minus key");
                EditorGUIUtility.editingTextField = false; GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                Check(!StudioModelEditor.HandleSizeShortcut(new Event { type = EventType.KeyDown, keyCode = KeyCode.Plus }), "Active mouse drag does not resize the gizmo");
            }
            catch (Exception e) { errors += e; }
            finally { GUIUtility.hotControl = previousHot; EditorGUIUtility.editingTextField = previousText; }
        }
        static void Finish(string error)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log; SceneView.beforeSceneGui -= Capture; SceneView.beforeSceneGui -= CheckGuards;
            if (!string.IsNullOrEmpty(errors)) error += errors;
            if (!string.IsNullOrEmpty(error)) Results.Add("FAIL: " + error);
            if (view) view.Close(); if (controller) Object.DestroyImmediate(controller);
            if (hadSettings) EditorPrefs.SetString(StudioSettings.PreferenceKey, oldSettings); else EditorPrefs.DeleteKey(StudioSettings.PreferenceKey); StudioSettings.Reload();
            if (hadModel) EditorPrefs.SetString(ModelKey, oldModel); else EditorPrefs.DeleteKey(ModelKey);
            File.WriteAllLines("handle-orbit-result.txt", Results); EditorApplication.delayCall += () => EditorApplication.Exit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
}

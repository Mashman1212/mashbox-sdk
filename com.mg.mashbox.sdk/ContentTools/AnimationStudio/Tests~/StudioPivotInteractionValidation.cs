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
    public static class StudioPivotInteractionValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static AnimationStudioWindow controller;
        static MashBoxStudioViewport view;
        static StudioModelEditor editor;
        static Vector3 corner, previousPivot;
        static Vector2 mouse;
        static int step;
        static double next;
        static string errors = "", oldModel;
        static bool hadModel;
        static readonly List<string> Results = new List<string>();
        static string ModelKey => "MashBox.LastModel." + Application.dataPath;
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        static object Field(string name) => typeof(StudioModelEditor).GetField(name, Private).GetValue(editor);
        static void Key(EventType type, KeyCode key) => view.SendEvent(new Event { type = type, keyCode = key });
        static void Mouse(EventType type) => view.SendEvent(new Event { type = type, button = 0, mousePosition = mouse });
        public static void Run()
        {
            hadModel = EditorPrefs.HasKey(ModelKey); oldModel = EditorPrefs.GetString(ModelKey);
            var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Pivot Interaction.asset")); doc.AddPrimitive(PrimitiveType.Cube);
            AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Private).Invoke(controller, new object[] { true });
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Private).Invoke(controller, new object[] { doc });
            editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Private).GetValue(controller); editor.SelectObject(0);
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First();
            view.position = new Rect(100, 100, 1400, 800); view.pivot = Vector3.zero; view.rotation = Quaternion.Euler(20, 155, 0); view.size = 2; view.Focus();
            Application.logMessageReceived += Log; EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
        }
        static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack; }
        static void Target(Vector3 point)
        {
            corner = point; var screen = view.camera.WorldToScreenPoint(point); float dpi = EditorGUIUtility.pixelsPerPoint;
            mouse = new Vector2(screen.x / dpi, (view.camera.pixelHeight - screen.y) / dpi);
            Check(view.ModelCanvasRect.Contains(mouse), "Snap target is inside scene canvas");
        }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .4;
            try
            {
                switch (step++)
                {
                    case 0:
                        Key(EventType.KeyDown, KeyCode.D); Key(EventType.KeyUp, KeyCode.D);
                        Check((bool)Field("pivot"), "D enters pivot mode"); break;
                    case 1:
                        Target(new Vector3(.5f, .5f, .5f));
                        Key(EventType.KeyDown, KeyCode.V); Mouse(EventType.MouseDown); Mouse(EventType.MouseDrag);
                        Check((int)Field("pivotSnapControl") != 0, "V drag captures preview pivot snapping"); break;
                    case 2:
                        Mouse(EventType.MouseUp); Key(EventType.KeyUp, KeyCode.V);
                        Check(Vector3.Distance(editor.Transforms[0].position, corner) < .0002f, "V drag snaps pivot to the exact displayed vertex");
                        Check((int)Field("pivotSnapControl") == 0, "Mouse release clears snap capture");
                        Undo.PerformUndo(); break;
                    case 3:
                        Check(editor.Transforms[0].position.sqrMagnitude < 1e-8f, "One Undo restores the pre-snap pivot");
                        Undo.PerformRedo(); break;
                    case 4:
                        Check(Vector3.Distance(editor.Transforms[0].position, corner) < .0002f, "Redo restores the snapped pivot");
                        previousPivot = corner; Target(new Vector3(-.5f, -.5f, .5f));
                        Key(EventType.KeyDown, KeyCode.V); Mouse(EventType.MouseDown); Mouse(EventType.MouseDrag);
                        Key(EventType.KeyDown, KeyCode.Escape); Key(EventType.KeyUp, KeyCode.V); break;
                    case 5:
                        Check(Vector3.Distance(editor.Transforms[0].position, previousPivot) < .0002f, "Escape cancels an in-progress vertex snap");
                        Check((int)Field("pivotSnapControl") == 0, "Escape releases the snap handle");
                        Key(EventType.KeyDown, KeyCode.D); Key(EventType.KeyUp, KeyCode.D);
                        Check(!(bool)Field("pivot"), "D exits pivot mode"); Finish(null); break;
                }
            }
            catch (Exception e) { Finish(e.ToString()); }
        }
        static void Finish(string error)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            if (!string.IsNullOrEmpty(errors)) error += errors;
            if (!string.IsNullOrEmpty(error)) Results.Add("FAIL: " + error);
            if (view) view.Close(); if (controller) Object.DestroyImmediate(controller);
            if (hadModel) EditorPrefs.SetString(ModelKey, oldModel); else EditorPrefs.DeleteKey(ModelKey);
            File.WriteAllLines("pivot-interaction-result.txt", Results); EditorApplication.delayCall += () => EditorApplication.Exit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
}

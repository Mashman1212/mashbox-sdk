using EditorPrefs = MashBoxSDK.AnimationStudio.StudioPreferenceStore;
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
    public static class StudioDockingValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> Results = new List<string>();
        static AnimationStudioWindow controller;
        static MashBoxStudioViewport view;
        static StudioModelDocument doc;
        static string oldModel, errors = "";
        static bool hadModel;
        static int step;
        static double next;
        static string ModelKey => "MashBox.LastModel." + Application.dataPath;
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        static object Parent(EditorWindow window) => typeof(EditorWindow).GetField("m_Parent",Private).GetValue(window);
        static void Open() => typeof(AnimationStudioWindow).GetMethod("OpenViewport",Private).Invoke(controller,null);
        static void F11() => view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.F11 });
        public static void Run()
        {
            hadModel = EditorPrefs.HasKey(ModelKey); oldModel = EditorPrefs.GetString(ModelKey);
            doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Docking Test.asset")); doc.AddPrimitive(PrimitiveType.Cube);
            AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",Private).Invoke(controller,new object[] {true});
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",Private).Invoke(controller,new object[] {doc});
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(); view.Focus();
            Application.logMessageReceived += Log; EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
        }
        static void Log(string text, string stack, LogType type) { if(type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack; }
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .6;
            try
            {
                switch(step++)
                {
                    case 0:
                        Check(view.dockableHost && Parent(view).GetType().Name == "DockArea","MashBox Studio opens inside Unity's dockable tab container");
                        int id = view.GetInstanceID(); Open();
                        Check(view.GetInstanceID() == id && Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Length == 1,"Reopening reuses the existing studio tab");
                        // Exercise migration from an already-open utility host.
                        view.Close();
                        var legacy = ScriptableObject.CreateInstance<MashBoxStudioViewport>(); legacy.Studio = controller;
                        legacy.ShowUtility(); legacy.pivot = new Vector3(2,3,4); legacy.rotation = Quaternion.Euler(20,40,0); legacy.size = 7; legacy.orthographic = true; legacy.modelGridVisible = false;
                        typeof(AnimationStudioWindow).GetField("viewport",Private).SetValue(controller,legacy);
                        Open(); view = (MashBoxStudioViewport)typeof(AnimationStudioWindow).GetField("viewport",Private).GetValue(controller);
                        Check(!legacy && Parent(view).GetType().Name == "DockArea","Existing utility studio migrates into a dockable host");
                        Check(Vector3.Distance(view.pivot,new Vector3(2,3,4)) < .001f && Quaternion.Angle(view.rotation,Quaternion.Euler(20,40,0)) < .001f && Mathf.Abs(view.size-7)<.001f && view.orthographic,"Migration retains camera framing and projection");
                        Check(!view.modelGridVisible && view.Studio == controller,"Migration retains grid visibility and studio ownership");
                        var model = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor",Private).GetValue(controller);
                        Check(model.Document == doc && (UnityEngine.SceneManagement.Scene)typeof(SceneView).GetProperty("customScene",Private).GetValue(view) == model.Scene,"Migration retains the loaded model and preview scene");
                        view.Focus(); break;
                    case 1: F11(); break;
                    case 2: Check(view.IsStudioMaximized,"F11 maximizes the studio view"); F11(); break;
                    case 3:
                        Check(!view.IsStudioMaximized,"F11 restores the previous studio layout");
                        typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",Private).Invoke(controller,new object[] {false});
                        view.Focus(); break;
                    case 4: F11(); break;
                    case 5: Check(view.IsStudioMaximized,"F11 also works in animation mode"); view.ToggleMaximized(); break;
                    case 6: Check(!view.IsStudioMaximized,"Menu maximize action also restores the view"); Finish(null); break;
                }
            }
            catch(Exception e) { Finish(e.ToString()); }
        }
        static void Finish(string error)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            if(!string.IsNullOrEmpty(errors)) error += errors;
            if(!string.IsNullOrEmpty(error)) Results.Add("FAIL: " + error);
            if(view) { if(view.maximized) view.maximized = false; view.Close(); } if(controller) Object.DestroyImmediate(controller);
            if(hadModel) EditorPrefs.SetString(ModelKey,oldModel); else EditorPrefs.DeleteKey(ModelKey);
            File.WriteAllLines("docking-result.txt",Results); EditorApplication.delayCall += () => EditorApplication.Exit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
}

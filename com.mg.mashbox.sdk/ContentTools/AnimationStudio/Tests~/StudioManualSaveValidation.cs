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
    public static class StudioManualSaveValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> Results = new List<string>();
        static AnimationStudioWindow controller;
        static MashBoxStudioViewport view;
        static StudioModelEditor editor;
        static StudioModelDocument doc, other;
        static string oldModel, errors = "", path;
        static bool hadModel;
        static int step;
        static double next;
        static byte[] disk;
        static DateTime modified;
        static string ModelKey => "MashBox.LastModel." + Application.dataPath;
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        static void Unwritten(string label) => Check(File.ReadAllBytes(path).SequenceEqual(disk) && File.GetLastWriteTimeUtc(path) == modified, label);
        static void Snapshot() { disk = File.ReadAllBytes(path); modified = File.GetLastWriteTimeUtc(path); }
        static void Mode(bool modeling) => typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Private).Invoke(controller, new object[] { modeling });
        public static void Run()
        {
            hadModel = EditorPrefs.HasKey(ModelKey); oldModel = EditorPrefs.GetString(ModelKey);
            path = AssetDatabase.GenerateUniqueAssetPath("Assets/Manual Save Test.asset");
            doc = StudioModelDocument.CreateEmpty(path); doc.AddPrimitive(PrimitiveType.Cube); doc.Save();
            other = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Other Save Test.asset"));
            AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First(); Mode(true);
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Private).Invoke(controller, new object[] { doc });
            editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Private).GetValue(controller); editor.SelectObject(0);
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(); view.Focus(); Snapshot();
            Application.logMessageReceived += Log; EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + .5;
        }
        static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack; }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .5;
            try
            {
                switch(step++)
                {
                    case 0:
                        doc.Repivot(0,editor.Transforms,new Vector3(.3f,.2f,.1f)); editor.Changed();
                        var uv = doc.nodes[0].mesh.uv.Select(v => v + new Vector2(.1f,.2f)).ToArray();
                        StudioModelDocument.SetUVs(doc.nodes[0].mesh,0,uv,"Edit test UVs"); editor.Changed();
                        Check(EditorUtility.IsDirty(doc), "Model edits stay marked as unsaved in memory");
                        next = EditorApplication.timeSinceStartup + 2; break;
                    case 1:
                        Unwritten("Idle update ticks do not write pivot or UV edits to disk");
                        StudioUVEditor.Open(editor); break;
                    case 2:
                        Resources.FindObjectsOfTypeAll<StudioUVEditor>().First().Close();
                        Unwritten("Closing the UV editor does not save"); Mode(false); break;
                    case 3:
                        Unwritten("Switching to animation does not save the model"); Mode(true);
                        editor.Load(other); Unwritten("Loading another workspace does not silently save the previous one");
                        editor.Load(doc); editor.SelectObject(0);
                        Check(Vector3.Distance(editor.Transforms[0].position,new Vector3(.3f,.2f,.1f)) < .0001f, "Unsaved edits survive switching away and back");
                        view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(); view.Focus(); break;
                    case 4:
                        view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.S, modifiers = EventModifiers.Control });
                        Check(!File.ReadAllBytes(path).SequenceEqual(disk), "Ctrl+S explicitly writes the edited workspace");
                        Check(!EditorUtility.IsDirty(doc), "Explicit save clears the document dirty state"); Snapshot();
                        doc.nodes[0].name = "Unsaved rename"; editor.Changed();
                        editor.Dispose();
                        typeof(AnimationStudioWindow).GetField("modelEditor",Private).SetValue(controller,null);
                        Unwritten("Disposing the modeling editor does not save");
                        // A reopened editor must save existing dirty asset state, without a private timer flag.
                        using (var reopened = new StudioModelEditor(() => {})) { reopened.Load(doc); reopened.Save(); }
                        Check(!File.ReadAllBytes(path).SequenceEqual(disk), "Explicit Save works after reopening an unsaved workspace");
                        Finish(null); break;
                }
            }
            catch (Exception e) { Finish(e.ToString()); }
        }
        static void Finish(string error)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            if (!string.IsNullOrEmpty(errors)) error += errors;
            if (!string.IsNullOrEmpty(error)) Results.Add("FAIL: " + error);
            foreach(var uv in Resources.FindObjectsOfTypeAll<StudioUVEditor>()) uv.Close();
            if (view) view.Close(); if (controller) Object.DestroyImmediate(controller);
            if (hadModel) EditorPrefs.SetString(ModelKey,oldModel); else EditorPrefs.DeleteKey(ModelKey);
            File.WriteAllLines("manual-save-result.txt",Results); EditorApplication.delayCall += () => EditorApplication.Exit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
}

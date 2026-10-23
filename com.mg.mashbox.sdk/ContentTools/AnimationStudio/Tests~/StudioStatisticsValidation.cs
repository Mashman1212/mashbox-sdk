using EditorPrefs = MashBoxSDK.AnimationStudio.StudioPreferenceStore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioStatisticsValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> Results = new List<string>();
        static AnimationStudioWindow controller;
        static MashBoxStudioViewport view;
        static string oldSettings, oldModel, errors = "";
        static bool hadSettings, hadModel;
        static int step;
        static double next;
        static string ModelKey => "MashBox.LastModel." + Application.dataPath;
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        static IMGUIContainer Element(string field) => (IMGUIContainer)typeof(MashBoxStudioViewport).GetField(field, Private).GetValue(view);
        public static void Run()
        {
            hadSettings = EditorPrefs.HasKey(StudioSettings.PreferenceKey); oldSettings = EditorPrefs.GetString(StudioSettings.PreferenceKey);
            hadModel = EditorPrefs.HasKey(ModelKey); oldModel = EditorPrefs.GetString(ModelKey);
            try
            {
                var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Statistics Test.asset"));
                doc.nodes.Add(new StudioModelDocument.Node { name = "Group" });
                int a = doc.AddPrimitive(PrimitiveType.Cube), b = doc.AddPrimitive(PrimitiveType.Cube);
                doc.nodes[a].parent = doc.nodes[b].parent = 0;
                using (var editor = new StudioModelEditor(() => {}))
                {
                    editor.Load(doc); editor.SelectObject(a);
                    var stats = editor.MeshStatistics();
                    Check(stats.scene.SequenceEqual(new long[] { 16, 24, 12, 24, 48 }), "Two cubes report geometric vertices, polygon edges/faces, render triangles and UV entries");
                    Check(stats.selected.SequenceEqual(new long[] { 8, 12, 6, 12, 24 }), "Selected object counts are separate from workspace totals");
                    var cache = (System.Collections.IDictionary)typeof(StudioModelEditor).GetField("statisticsCache", Private).GetValue(editor);
                    var cached = cache[doc.nodes[a]]; editor.MeshStatistics();
                    Check(ReferenceEquals(cached, cache[doc.nodes[a]]), "Repeated reads reuse cached mesh counts");
                    editor.SelectObject(0); editor.SelectObject(a, true);
                    Check(editor.MeshStatistics().selected.SequenceEqual(stats.scene), "Selecting parent and child does not double-count meshes");
                    editor.Transforms[b].gameObject.SetActive(false);
                    Check(editor.MeshStatistics().scene.SequenceEqual(stats.selected), "Hidden objects are excluded from visible workspace totals");
                    editor.Transforms[b].gameObject.SetActive(true);
                    Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(doc.nodes[a].mesh, "Remove test UVs");
                    doc.nodes[a].mesh.SetUVs(0, new List<Vector2>()); editor.Changed();
                    Check(editor.MeshStatistics().scene[4] == 24, "UV edits invalidate cached counts");
                    Undo.PerformUndo();
                    Check(editor.MeshStatistics().scene[4] == 48, "Undo refreshes statistics");
                    editor.Load(null);
                    Check(editor.MeshStatistics().scene.All(n => n == 0), "Empty workspace reports zero statistics");
                }
                StudioSettings.Current.showMeshStatistics = true; StudioSettings.Save(); StudioSettings.Reload();
                Check(StudioSettings.Current.showMeshStatistics, "Statistics preference persists");
                AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
                typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Private).Invoke(controller, new object[] { true });
                typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Private).Invoke(controller, new object[] { doc });
                view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(); view.position = new Rect(100, 100, 1400, 800);
                Application.logMessageReceived += Log; EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
            }
            catch (Exception e) { Finish(e.ToString()); }
        }
        static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack; }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .4;
            try
            {
                switch(step++)
                {
                    case 0:
                        var stats = Element("meshStatistics"); var options = Element("viewportOptions");
                        Check(stats.resolvedStyle.display == DisplayStyle.Flex && stats.layout.yMin >= options.layout.yMax, "Statistics appear below Local/Grid inside the scene canvas");
                        Check(stats.layout.xMin >= view.ModelCanvasRect.xMin && stats.layout.xMax <= view.ModelCanvasRect.xMax, "Statistics fit within viewport bounds");
                        Check(stats.pickingMode == PickingMode.Ignore && !view.IsOverViewportControls(stats.layout.center), "Statistics remain click-through for modeling");
                        view.ResizePanel(1, 400); StudioSettings.Current.showMeshStatistics = false; StudioSettings.Save(); break;
                    case 1:
                        Check(Element("meshStatistics").resolvedStyle.display == DisplayStyle.None, "Disabling statistics removes the overlay immediately");
                        StudioSettings.Reload(); Check(!StudioSettings.Current.showMeshStatistics, "Disabled preference survives reload");
                        StudioSettings.Current.showMeshStatistics = true; StudioSettings.Save(); break;
                    case 2:
                        Check(Mathf.Abs(Element("meshStatistics").layout.xMin - view.ModelCanvasRect.xMin - 8) < 1, "Statistics track the hierarchy resize");
                        typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Private).Invoke(controller, new object[] { false }); break;
                    case 3:
                        Check(Element("meshStatistics").resolvedStyle.display == DisplayStyle.None, "Model statistics hide in animation mode");
                        typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Private).Invoke(controller, new object[] { true }); break;
                    case 4:
                        Check(Element("meshStatistics").resolvedStyle.display == DisplayStyle.Flex, "Returning to modeling restores statistics"); Finish(null); break;
                }
            }
            catch(Exception e) { Finish(e.ToString()); }
        }
        static void Finish(string error)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            if (!string.IsNullOrEmpty(errors)) error += errors;
            if (!string.IsNullOrEmpty(error)) Results.Add("FAIL: " + error);
            if (view) view.Close(); if (controller) Object.DestroyImmediate(controller);
            if (hadSettings) EditorPrefs.SetString(StudioSettings.PreferenceKey, oldSettings); else EditorPrefs.DeleteKey(StudioSettings.PreferenceKey);
            StudioSettings.Reload();
            if (hadModel) EditorPrefs.SetString(ModelKey, oldModel); else EditorPrefs.DeleteKey(ModelKey);
            File.WriteAllLines("statistics-result.txt", Results); EditorApplication.delayCall += () => EditorApplication.Exit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
}

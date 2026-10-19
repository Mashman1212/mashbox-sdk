using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioDuplicateValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> Results = new List<string>();
        static AnimationStudioWindow controller;
        static MashBoxStudioViewport view;
        static StudioModelEditor editor;
        static string path, oldModel, errors = "";
        static bool hadModel;
        static int step, mainRoots;
        static double next;
        static byte[] disk;
        static Mesh duplicate;
        static string ModelKey => "MashBox.LastModel." + Application.dataPath;
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        public static void Run()
        {
            hadModel = EditorPrefs.HasKey(ModelKey); oldModel = EditorPrefs.GetString(ModelKey);
            try
            {
                path = AssetDatabase.GenerateUniqueAssetPath("Assets/Duplicate Test.asset");
                var doc = StudioModelDocument.CreateEmpty(path);
                doc.AddPrimitive(PrimitiveType.Sphere);
                var source = doc.nodes[0];
                source.position = new Vector3(2,3,4); source.positionOffset = Vector3.right;
                source.rotation = Quaternion.Euler(10,20,30); source.pivotRotation = Quaternion.Euler(5,15,25); source.scale = new Vector3(1,2,3);
                doc.Save(); disk = File.ReadAllBytes(path);
                AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
                typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Private).Invoke(controller, new object[] { true });
                typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Private).Invoke(controller, new object[] { doc });
                editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Private).GetValue(controller);
                editor.SelectObject(0);
                view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(); view.Focus();
                mainRoots = SceneManager.GetActiveScene().rootCount;
                Application.logMessageReceived += Log; EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
            }
            catch (Exception e) { Finish(e.ToString()); }
        }
        static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack; }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .5;
            try
            {
                var doc = editor.Document;
                switch (step++)
                {
                    case 0:
                        view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.D, modifiers = EventModifiers.Control });
                        Check(doc.nodes.Count == 2 && editor.Active == 1 && editor.Transforms.Length == 2, "Native Ctrl+D duplicates once and selects the copy in the viewport/hierarchy");
                        var a = doc.nodes[0]; var b = doc.nodes[1]; duplicate = b.mesh;
                        Check(a.name != b.name && a.mesh != b.mesh && a.mesh.vertices.SequenceEqual(b.mesh.vertices), "Duplicate has a unique name and independent mesh geometry");
                        Check(a.position == b.position && a.positionOffset == b.positionOffset && a.rotation == b.rotation && a.scale == b.scale && a.pivotRotation == b.pivotRotation, "Duplicate preserves transform and custom pivot");
                        Check(a.cutPolygons == null || (!ReferenceEquals(a.cutPolygons,b.cutPolygons) && a.cutPolygons.SequenceEqual(b.cutPolygons)), "Polygon topology metadata is copied independently");
                        Check(!AssetDatabase.Contains(duplicate) && File.ReadAllBytes(path).SequenceEqual(disk), "Duplication does not write meshes or document to disk");
                        Undo.PerformUndo(); break;
                    case 1:
                        Check(doc.nodes.Count == 1 && editor.Transforms.Length == 1, "Undo removes duplicate from hierarchy and viewport");
                        Undo.PerformRedo(); break;
                    case 2:
                        Check(doc.nodes.Count == 2 && doc.nodes[1].mesh == duplicate, "Redo restores the independent mesh");
                        editor.SelectObject(1);
                        view.SendEvent(EditorGUIUtility.CommandEvent("Duplicate"));
                        Check(doc.nodes.Count == 3 && editor.Active == 2, "Unity Duplicate command also works");
                        Check(SceneManager.GetActiveScene().rootCount == mainRoots, "Duplicate creates no objects in the main scene");
                        editor.Save();
                        Check(AssetDatabase.Contains(duplicate) && AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Count() == 3, "Explicit Save persists duplicate meshes as workspace subassets");
                        Check(!File.ReadAllBytes(path).SequenceEqual(disk), "Explicit Save writes the updated workspace");
                        var evt = Event.current;
                        try {
                            EditorGUIUtility.editingTextField = true;
                            Event.current = new Event { type = EventType.KeyDown, keyCode = KeyCode.D, modifiers = EventModifiers.Control };
                            Check(!editor.HandleObjectDuplicate() && doc.nodes.Count == 3, "Ctrl+D does not duplicate while editing text");
                        } finally { Event.current = evt; EditorGUIUtility.editingTextField = false; }
                        ValidateHierarchy(); Finish(null); break;
                }
            }
            catch (Exception e) { Finish(e.ToString()); }
        }
        static void ValidateHierarchy()
        {
            var doc = ScriptableObject.CreateInstance<StudioModelDocument>();
            doc.nodes.Add(new StudioModelDocument.Node { name = "Group", position = Vector3.one });
            doc.nodes.Add(new StudioModelDocument.Node { name = "Bone", parent = 0 });
            doc.nodes.Add(new StudioModelDocument.Node { name = "Skin", parent = 0, skinned = true, rootBone = 1, bones = new[] { 1 } });
            doc.nodes.Add(new StudioModelDocument.Node { name = "Other" });
            Undo.IncrementCurrentGroup();
            var copies = doc.DuplicateObjects(new[] { 0, 1, 3 });
            Check(doc.nodes.Count == 8 && copies.SequenceEqual(new[] { 4, 7 }), "Multiple selection duplicates each subtree once and returns copied roots");
            Check(doc.nodes[5].parent == 4 && doc.nodes[6].parent == 4 && doc.nodes[6].rootBone == 5 && doc.nodes[6].bones[0] == 5, "Duplicated hierarchy remaps parents and internal skin bones");
            Undo.ClearUndo(doc); Object.DestroyImmediate(doc);
        }
        static void Finish(string error)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            if (!string.IsNullOrEmpty(errors)) error += errors;
            if (!string.IsNullOrEmpty(error)) Results.Add("FAIL: " + error);
            if (view) view.Close(); if (controller) Object.DestroyImmediate(controller);
            if (hadModel) EditorPrefs.SetString(ModelKey,oldModel); else EditorPrefs.DeleteKey(ModelKey);
            File.WriteAllLines("duplicate-result.txt",Results);
            EditorApplication.delayCall += () => EditorApplication.Exit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
}

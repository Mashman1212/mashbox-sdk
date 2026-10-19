using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioObjectManagementValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> Results = new List<string>();
        static AnimationStudioWindow controller; static MashBoxStudioViewport view; static StudioModelEditor editor;
        static string oldModel, errors = ""; static bool hadModel; static int step, countBefore, mainRoots;
        static double next; static GameObject prefab; static Mesh meshAsset; static string prefabPath; static Vector2 pointer;
        static string ModelKey => "MashBox.LastModel." + Application.dataPath;
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        public static void Run()
        {
            hadModel = EditorPrefs.HasKey(ModelKey); oldModel = EditorPrefs.GetString(ModelKey);
            try
            {
                var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Object Management.asset"));
                doc.nodes.Add(new StudioModelDocument.Node { name = "Group" });
                int cube = doc.AddPrimitive(PrimitiveType.Cube); doc.nodes[cube].parent = 0;
                int other = doc.AddPrimitive(PrimitiveType.Sphere);
                var retained = doc.nodes[cube].mesh;
                Undo.IncrementCurrentGroup(); doc.DeleteObjects(new[] { 0, cube });
                Check(doc.nodes.Count == 1 && doc.nodes[0].name.StartsWith("Sphere") && doc.nodes[0].parent == -1, "Delete includes a selected subtree once and preserves other roots");
                Check(retained && AssetDatabase.Contains(retained), "Deleted mesh subassets remain available for Undo");
                Undo.PerformUndo(); Check(doc.nodes.Count == 3 && doc.nodes[cube].parent == 0 && doc.nodes[cube].mesh == retained, "Undo restores deleted hierarchy and owned mesh");
                Undo.PerformRedo(); Check(doc.nodes.Count == 1, "Redo deletes the same subtree"); Undo.PerformUndo();
                // Remaining skin references must remap when earlier nodes disappear.
                doc.nodes.Add(new StudioModelDocument.Node { name = "Bone" });
                doc.nodes.Add(new StudioModelDocument.Node { name = "Skin", skinned = true, rootBone = 3, bones = new[] { 3 } });
                Undo.IncrementCurrentGroup(); doc.DeleteObjects(new[] { 0 });
                Check(doc.nodes.Count == 3 && doc.nodes[2].bones[0] == 1 && doc.nodes[2].rootBone == 1, "Deletion remaps remaining skin references");
                string before = EditorJsonUtility.ToJson(doc); bool rejected = false;
                try { doc.DeleteObjects(new[] { 1 }); } catch(InvalidOperationException) { rejected = true; }
                Check(rejected && EditorJsonUtility.ToJson(doc) == before, "Deleting an in-use bone cannot break a surviving skin"); Undo.PerformUndo();
                doc.nodes.RemoveRange(3, 2);
                var root = new GameObject("Dropped Prefab"); var child = GameObject.CreatePrimitive(PrimitiveType.Cube); child.name = "Mesh Child"; child.transform.SetParent(root.transform, false);
                prefabPath = AssetDatabase.GenerateUniqueAssetPath("Assets/Dropped Prefab.prefab"); prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath); Object.DestroyImmediate(root);
                meshAsset = StudioModelDocument.BuildPrimitive(PrimitiveType.Quad); meshAsset.name = "Dropped Mesh";
                AssetDatabase.CreateAsset(meshAsset, AssetDatabase.GenerateUniqueAssetPath("Assets/Dropped Mesh.asset"));
                Check(StudioModelEditor.ModelDropSources(new Object[] { prefab, prefab.transform.GetChild(0).gameObject, prefab }).Length == 1, "Drag payload deduplicates roots and their children");
                AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
                typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Private).Invoke(controller, new object[] { true });
                typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Private).Invoke(controller, new object[] { doc });
                editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Private).GetValue(controller); editor.SelectObject(cube);
                view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(); view.position = new Rect(100, 100, 1400, 800); view.Focus();
                mainRoots = SceneManager.GetActiveScene().rootCount;
                Application.logMessageReceived += Log; EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
            }
            catch(Exception e) { Finish(e.ToString()); }
        }
        static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack; }
        static void Key(KeyCode key) => view.SendEvent(new Event { type = EventType.KeyDown, keyCode = key });
        static void Drop(Object source, Vector2 point, string path = null)
        {
            DragAndDrop.PrepareStartDrag(); DragAndDrop.objectReferences = new[] { source }; DragAndDrop.paths = path == null ? Array.Empty<string>() : new[] { path };
            view.SendEvent(new Event { type = EventType.DragUpdated, mousePosition = point });
            Check(DragAndDrop.visualMode == DragAndDropVisualMode.Copy, "Model drop advertises Copy");
            view.SendEvent(new Event { type = EventType.DragPerform, mousePosition = point });
            DragAndDrop.objectReferences = Array.Empty<Object>(); DragAndDrop.paths = Array.Empty<string>();
        }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .5;
            try
            {
                switch(step++)
                {
                    case 0:
                        Key(KeyCode.Delete); Check(editor.Document.nodes.Count == 2 && editor.Active == -1, "Delete key removes the selected cube and clears selection");
                        Undo.PerformUndo(); break;
                    case 1:
                        Check(editor.Document.nodes.Count == 3 && editor.Transforms.Length == 3, "Undo refreshes hierarchy and viewport after delete");
                        editor.SelectObject(1); Key(KeyCode.Backspace); Check(editor.Document.nodes.Count == 2, "Backspace also deletes objects"); Undo.PerformUndo();
                        pointer = view.ModelCanvasRect.center; countBefore = editor.Document.nodes.Count;
                        Drop(prefab, pointer, prefabPath); break;
                    case 2:
                        Check(editor.Document.nodes.Count == countBefore + 2 && editor.Transforms.Length == editor.Document.nodes.Count, "Viewport prefab drop imports the full hierarchy into the document");
                        Check(editor.Active == countBefore && editor.Document.nodes[countBefore + 1].parent == countBefore, "Imported root is selected and child parent is preserved");
                        Check(editor.Document.nodes[countBefore + 1].mesh != prefab.GetComponentInChildren<MeshFilter>().sharedMesh, "Drop creates an editable owned mesh without editing the prefab");
                        Check(SceneManager.GetActiveScene().rootCount == mainRoots, "Viewport drop creates no stray objects in the main scene");
                        Undo.PerformUndo(); break;
                    case 3:
                        Check(editor.Document.nodes.Count == countBefore, "Imported model supports Undo"); Undo.PerformRedo(); break;
                    case 4:
                        Check(editor.Document.nodes.Count == countBefore + 2, "Imported model supports Redo");
                        countBefore = editor.Document.nodes.Count;
                        var hierarchy = (IMGUIContainer)typeof(MashBoxStudioViewport).GetField("rigControls", Private).GetValue(view);
                        Drop(meshAsset, hierarchy.layout.position + new Vector2(90, 240)); break;
                    case 5:
                        Check(editor.Document.nodes.Count == countBefore + 1 && editor.ActiveMesh && editor.ActiveMesh != meshAsset, "Hierarchy drop imports a standalone mesh into an editable copy");
                        Check(SceneManager.GetActiveScene().rootCount == mainRoots, "Mesh drop leaves the main scene unchanged");
                        EditorGUIUtility.editingTextField = true;
                        var previous = Event.current; Event.current = new Event { type = EventType.KeyDown, keyCode = KeyCode.Backspace };
                        Check(!editor.HandleObjectDelete(), "Backspace remains text editing while a field is active");
                        Event.current = previous; EditorGUIUtility.editingTextField = false;
                        Finish(null); break;
                }
            }
            catch(Exception e) { Finish(e.ToString()); }
        }
        static void Finish(string error)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            if (!string.IsNullOrEmpty(errors)) error += errors;
            if (!string.IsNullOrEmpty(error)) Results.Add("FAIL: " + error);
            DragAndDrop.objectReferences = Array.Empty<Object>(); DragAndDrop.paths = Array.Empty<string>();
            if (view) view.Close(); if (controller) Object.DestroyImmediate(controller);
            if (hadModel) EditorPrefs.SetString(ModelKey, oldModel); else EditorPrefs.DeleteKey(ModelKey);
            File.WriteAllLines("object-management-result.txt", Results); EditorApplication.delayCall += () => EditorApplication.Exit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
}

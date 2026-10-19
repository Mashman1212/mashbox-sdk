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
    public static class StudioMaterialDropValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> Results = new List<string>();
        static AnimationStudioWindow controller;
        static MashBoxStudioViewport view;
        static StudioModelEditor editor;
        static Material original, dropped;
        static string oldModel, errors = "";
        static bool hadModel;
        static int step, slot, roots;
        static double next;
        static string ModelKey => "MashBox.LastModel." + Application.dataPath;
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        static Vector2 Screen(Vector3 world) { var p = view.camera.WorldToScreenPoint(world); return new Vector2(p.x, view.camera.pixelHeight - p.y) / EditorGUIUtility.pixelsPerPoint; }
        public static void Run()
        {
            hadModel = EditorPrefs.HasKey(ModelKey); oldModel = EditorPrefs.GetString(ModelKey);
            var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Material Drop.asset"));
            doc.AddPrimitive(PrimitiveType.Cube); doc.AddPrimitive(PrimitiveType.Cube); doc.nodes[1].position = new Vector3(0,0,2);
            original = new Material(Shader.Find("Unlit/Color")) { color = Color.blue };
            dropped = new Material(original) { color = Color.red };
            AssetDatabase.CreateAsset(original, AssetDatabase.GenerateUniqueAssetPath("Assets/Original.mat"));
            AssetDatabase.CreateAsset(dropped, AssetDatabase.GenerateUniqueAssetPath("Assets/Dropped.mat"));
            var mesh = doc.nodes[0].mesh; var triangles = mesh.triangles;
            mesh.subMeshCount = 2; mesh.SetTriangles(triangles.Take(18).ToArray(), 0); mesh.SetTriangles(triangles.Skip(18).ToArray(), 1);
            doc.nodes[0].materials = new[] { original, original }; doc.nodes[1].materials = new[] { original };
            AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Private).Invoke(controller, new object[] { true });
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Private).Invoke(controller, new object[] { doc });
            editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Private).GetValue(controller); editor.SelectObject(1);
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First();
            view.position = new Rect(100, 100, 1400, 800); view.pivot = Vector3.zero; view.rotation = Quaternion.identity; view.size = 2; view.orthographic = true; view.Focus();
            roots = SceneManager.GetActiveScene().rootCount;
            Application.logMessageReceived += Log; EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
        }
        static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack; }
        static void Drop(Vector2 point, bool accepted)
        {
            DragAndDrop.PrepareStartDrag(); DragAndDrop.objectReferences = new Object[] { dropped }; DragAndDrop.paths = Array.Empty<string>();
            view.SendEvent(new Event { type = EventType.DragUpdated, mousePosition = point });
            Check(DragAndDrop.visualMode == (accepted ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected), accepted ? "Surface accepts material drag" : "Empty space rejects material drag");
            view.SendEvent(new Event { type = EventType.DragPerform, mousePosition = point });
            DragAndDrop.objectReferences = Array.Empty<Object>();
        }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .4;
            try
            {
                switch (step++)
                {
                    case 0:
                        var point = Screen(new Vector3(.2f,.1f,-.5f));
                        Check(editor.TryMaterialTarget(view.camera, point, EditorGUIUtility.pixelsPerPoint, out int node, out slot) && node == 0, "Ray targets the nearest surface, not the selected object behind it");
                        Drop(point, true);
                        Check(editor.Document.nodes[0].materials[slot] == dropped, "Drop stores material in the hit submesh slot");
                        Check(editor.Document.nodes[0].materials[1-slot] == original && editor.Document.nodes[1].materials[0] == original, "Other slots and objects retain their materials");
                        Check(editor.Transforms[0].GetComponent<Renderer>().sharedMaterials[slot] == dropped, "Preview renderer immediately uses the dropped material");
                        Check(original.color == Color.blue && dropped.color == Color.red, "Source materials remain unchanged");
                        Check(editor.Active == 1 && SceneManager.GetActiveScene().rootCount == roots, "Drop preserves selection and main scene objects");
                        Undo.PerformUndo(); break;
                    case 1:
                        Check(editor.Document.nodes[0].materials.All(m => m == original) && editor.Transforms[0].GetComponent<Renderer>().sharedMaterials.All(m => m == original), "Undo restores both saved assignments and preview");
                        Undo.PerformRedo(); break;
                    case 2:
                        Check(editor.Document.nodes[0].materials[slot] == dropped && editor.Transforms[0].GetComponent<Renderer>().sharedMaterials[slot] == dropped, "Redo restores the material");
                        var before = EditorJsonUtility.ToJson(editor.Document);
                        Drop(Screen(new Vector3(0,1.5f,0)), false);
                        Check(before == EditorJsonUtility.ToJson(editor.Document), "Dropping on empty space leaves the document unchanged");
                        editor.Transforms[0].gameObject.SetActive(false);
                        Check(editor.TryMaterialTarget(view.camera, Screen(new Vector3(.2f,.1f,0)), EditorGUIUtility.pixelsPerPoint, out int visible, out _) && visible == 1, "Hidden objects do not intercept material drops");
                        editor.Transforms[0].gameObject.SetActive(true);
                        AssetDatabase.SaveAssets();
                        var path = AssetDatabase.GetAssetPath(editor.Document);
                        Check(File.ReadAllText(path).Contains(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(dropped))), "Workspace asset serializes the dropped material reference");
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
            DragAndDrop.objectReferences = Array.Empty<Object>(); DragAndDrop.paths = Array.Empty<string>();
            if (view) view.Close(); if (controller) Object.DestroyImmediate(controller);
            if (hadModel) EditorPrefs.SetString(ModelKey, oldModel); else EditorPrefs.DeleteKey(ModelKey);
            File.WriteAllLines("material-drop-result.txt", Results); EditorApplication.delayCall += () => EditorApplication.Exit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
}

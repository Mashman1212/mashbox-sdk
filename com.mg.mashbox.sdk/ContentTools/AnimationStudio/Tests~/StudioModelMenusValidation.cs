using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class StudioModelMenusValidation
{
    private static readonly List<string> Results = new List<string>();
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Results.Add("PASS: " + message); }
    public static void Run()
    {
        int code = 0; StudioModelEditor editor = null; SceneView view = null; Scene preview = default;
        const string path = "Assets/PrimitiveValidation.asset", fbxPath = "Assets/PrimitiveValidation.fbx";
        try
        {
            AssetDatabase.DeleteAsset(path); AssetDatabase.DeleteAsset(fbxPath);
            var activeScene = SceneManager.GetActiveScene(); int count = activeScene.rootCount; bool dirty = activeScene.isDirty;
            var doc = StudioModelDocument.CreateEmpty(path);
            editor = new StudioModelEditor(() => { }); editor.Load(doc);
            view = ScriptableObject.CreateInstance<SceneView>(); view.position = new Rect(100, 100, 1300, 850);
            var types = new[] { PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Plane, PrimitiveType.Quad, PrimitiveType.Cylinder };
            foreach (var type in types)
            {
                editor.CreatePrimitive(type, view, editor.Load);
                var mesh = editor.ActiveMesh;
                Check(editor.Active == doc.nodes.Count - 1 && mesh && mesh.vertexCount > 0 && editor.Transforms.Length == doc.nodes.Count, type + " is created, selected and built in preview hierarchy");
                Check(AssetDatabase.GetAssetPath(mesh) == path && mesh.uv.Length == mesh.vertexCount && mesh.normals.Length == mesh.vertexCount, type + " owns persistent mesh data with UV 0 and normals");
                Check(mesh.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)) && mesh.tangents.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), type + " has finite geometry and tangents");
                var positions = mesh.vertices; var normals = mesh.normals; var tris = mesh.triangles;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var a = positions[tris[i]]; var b = positions[tris[i + 1]]; var c = positions[tris[i + 2]];
                    if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f || Vector3.Dot(Vector3.Cross(b - a, c - a), normals[tris[i]]) <= 0)
                        throw new Exception(type + " has degenerate or reversed triangles");
                }
                Check(true, type + " winding agrees with surface normals and has no degenerate triangles");
            }
            Check(activeScene.rootCount == count && activeScene.isDirty == dirty, "Primitive creation leaves gameplay scene objects and dirty state unchanged");
            var firstMesh = doc.nodes[0].mesh;
            editor.CreatePrimitive(PrimitiveType.Cube, view, editor.Load);
            Check(doc.nodes.Last().name != doc.nodes[0].name && doc.nodes.Last().mesh != firstMesh, "Repeated primitives have unique names and independent editable meshes");
            Undo.PerformUndo();
            Check(doc.nodes.Count == 5 && editor.Transforms.Length == 5 && editor.Active == -1, "Undo removes created node and rebuilds selection/hierarchy");
            Undo.PerformRedo();
            Check(doc.nodes.Count == 6 && doc.nodes.Last().mesh && doc.nodes.Last().mesh.vertexCount == 24, "Redo restores the node and its complete mesh");
            editor.Save(); AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            doc = AssetDatabase.LoadAssetAtPath<StudioModelDocument>(path);
            Check(doc.nodes.Count == 6 && doc.nodes.All(n => n.mesh && AssetDatabase.IsSubAsset(n.mesh)), "Saved document reloads all six owned meshes after undo/redo");
            preview = EditorSceneManager.NewPreviewScene(); var transforms = doc.Build(preview);
            var root = StudioModelEditor.ExportRoot(transforms, preview);
            Check(root.GetComponentsInChildren<MeshFilter>().Length == 6, "Export root contains all separately created objects");
            StudioModelEditor.ExportTo(Path.GetFullPath(fbxPath), root);
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceSynchronousImport);
            var imported = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            Check(imported && imported.GetComponentsInChildren<MeshFilter>().Length == 6, "FBX export and reimport preserve all six primitives");
            Check(imported.GetComponentsInChildren<MeshFilter>().All(f => f.sharedMesh.uv.Length == f.sharedMesh.vertexCount), "Exported primitives retain UV 0");
        }
        catch (Exception e) { code = 1; Results.Add("FAIL: " + e); }
        finally
        {
            editor?.Dispose(); if (view) Object.DestroyImmediate(view);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            Undo.ClearAll(); AssetDatabase.DeleteAsset(path); AssetDatabase.DeleteAsset(fbxPath);
            File.WriteAllLines("model-menus-result.txt", Results);
            EditorApplication.Exit(code);
        }
    }
}

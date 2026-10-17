using System;
using System.IO;
using System.Linq;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class StudioMultiModelValidation
{
    private const string Path = "Assets/MultiModelValidation.asset", ExportPath = "Assets/MultiModelValidation.fbx";
    public static void Run()
    {
        StudioModelEditor editor = null; Mesh sourceMesh = null; Scene sourceScene = default, exportScene = default;
        int code = 0; string result;
        try
        {
            AssetDatabase.DeleteAsset(Path); AssetDatabase.DeleteAsset(ExportPath);
            sourceScene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Character"); SceneManager.MoveGameObjectToScene(root, sourceScene);
            var bone = new GameObject("Bone"); SceneManager.MoveGameObjectToScene(bone, sourceScene); bone.transform.SetParent(root.transform, false);
            var body = new GameObject("Body"); SceneManager.MoveGameObjectToScene(body, sourceScene); body.transform.SetParent(root.transform, false);
            var skin = body.AddComponent<SkinnedMeshRenderer>();
            sourceMesh = StudioModelDocument.BuildPrimitive(PrimitiveType.Cube);
            sourceMesh.bindposes = new[] { Matrix4x4.identity };
            sourceMesh.boneWeights = Enumerable.Range(0, sourceMesh.vertexCount).Select(i => new BoneWeight { boneIndex0 = 0, weight0 = 1 }).ToArray();
            skin.sharedMesh = sourceMesh; skin.bones = new[] { bone.transform }; skin.rootBone = bone.transform;
            var doc = StudioModelDocument.Import(root, Path);
            var originalMesh = doc.nodes[2].mesh;
            editor = new StudioModelEditor(() => { }); editor.Load(doc);
            editor.ImportModel(root, editor.Load);
            Check(doc.nodes.Count == 6 && editor.Active == 3 && editor.Document == doc, "Second import replaced existing document or did not select its own root");
            Check(doc.nodes[0].name != doc.nodes[3].name && doc.nodes[3].parent == -1 && doc.nodes[4].parent == 3 && doc.nodes[5].parent == 3, "Independent root names or child parents are wrong");
            Check(doc.nodes[5].bones[0] == 4 && doc.nodes[5].rootBone == 4, "Appended skin indices were not remapped");
            var secondSkin = editor.Transforms[5].GetComponent<SkinnedMeshRenderer>();
            Check(secondSkin.bones[0] == editor.Transforms[4] && secondSkin.rootBone == editor.Transforms[4], "Appended skin binds to the wrong model's bone");
            Check(doc.nodes[5].mesh != sourceMesh && doc.nodes[5].mesh != originalMesh, "Imported models share editable mesh data");
            var modified = doc.nodes[5].mesh.vertices; modified[0] += Vector3.right; doc.nodes[5].mesh.vertices = modified;
            Check(originalMesh.vertices[0] == sourceMesh.vertices[0], "Editing second model changed the first or its source");
            Undo.PerformUndo();
            Check(doc.nodes.Count == 3 && doc.nodes[2].mesh == originalMesh && editor.Transforms.Length == 3, "Import Undo damaged existing model");
            Undo.PerformRedo();
            Check(doc.nodes.Count == 6 && doc.nodes[5].mesh && doc.nodes[5].bones[0] == 4, "Import Redo lost mesh or skin bindings");
            int primitive = doc.AddPrimitive(PrimitiveType.Sphere);
            Check(primitive == 6 && doc.nodes[primitive].parent == -1, "Primitive did not coexist with imported models");
            doc.Save(); AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceSynchronousImport);
            doc = AssetDatabase.LoadAssetAtPath<StudioModelDocument>(Path);
            Check(doc.nodes.Count == 7 && doc.nodes.Count(n => n.parent < 0) == 3 && doc.nodes[5].mesh && doc.nodes[5].bones[0] == 4, "Workspace reload lost multiple roots or skin bindings");
            exportScene = EditorSceneManager.NewPreviewScene(); var transforms = doc.Build(exportScene);
            var exportRoot = StudioModelEditor.ExportRoot(transforms, exportScene);
            Check(exportRoot.transform.childCount == 3, "Export omits model roots");
            StudioModelEditor.ExportTo(System.IO.Path.GetFullPath(ExportPath), exportRoot);
            AssetDatabase.ImportAsset(ExportPath, ImportAssetOptions.ForceSynchronousImport);
            var exported = AssetDatabase.LoadAssetAtPath<GameObject>(ExportPath);
            Check(exported && exported.GetComponentsInChildren<Renderer>().Length == 3, "FBX round trip omits models");
            // Reject incomplete rigs without appending partial nodes or meshes.
            int before = doc.nodes.Count;
            bool rejected = false;
            try { doc.AppendModel(body); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && doc.nodes.Count == before, "Invalid partial skin import was not atomic");
            result = "PASS: Multiple model roots coexist with primitives; unique names, parent indices, skin-bone remapping, independent meshes, source preservation, import Undo/Redo, workspace persistence, complete FBX export and atomic invalid-import rejection.";
        }
        catch (Exception e) { code = 1; result = "FAIL: " + e; }
        finally
        {
            editor?.Dispose(); Undo.ClearAll();
            if (sourceScene.IsValid()) EditorSceneManager.ClosePreviewScene(sourceScene);
            if (exportScene.IsValid()) EditorSceneManager.ClosePreviewScene(exportScene);
            if (sourceMesh) Object.DestroyImmediate(sourceMesh);
            AssetDatabase.DeleteAsset(Path); AssetDatabase.DeleteAsset(ExportPath);
        }
        File.WriteAllText("multi-model-result.txt", result); EditorApplication.Exit(code);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}

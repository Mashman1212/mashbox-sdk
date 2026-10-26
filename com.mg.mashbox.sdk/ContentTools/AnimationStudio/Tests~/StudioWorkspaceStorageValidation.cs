using System;
using System.IO;
using System.Linq;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public static class StudioWorkspaceStorageValidation
{
    static void Check(bool pass, string message) { if (!pass) throw new Exception(message); Debug.Log("PASS " + message); }
    public static void Run()
    {
        string folder = StudioModelDocument.WorkspaceFolder;
        string path = Path.Combine(folder, "StorageValidation.workspace");
        string phase = File.Exists(path) ? "reload" : "create";
        if (phase == "create")
        {
            AssetDatabase.DeleteAsset("Assets/StorageMaterial.mat");
            AssetDatabase.DeleteAsset("Assets/LegacyTest.asset");
            var doc = StudioModelDocument.CreateEmpty(path);
            doc.AddPrimitive(PrimitiveType.Cube);
            doc.AddPrimitive(PrimitiveType.Sphere);
            var material = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "Storage test material" };
            AssetDatabase.CreateAsset(material, "Assets/StorageMaterial.mat");
            doc.nodes[0].materials = new[] { material };
            doc.nodes[0].position = new Vector3(1, 2, 3);
            doc.nodes[0].positionOffset = new Vector3(3, 2, 1);
            var mesh = doc.nodes[0].mesh;
            var delta = Enumerable.Repeat(Vector3.up, mesh.vertexCount).ToArray();
            mesh.AddBlendShapeFrame("Test", 100, delta, new Vector3[delta.Length], new Vector3[delta.Length]);
            doc.Save(); doc.nodes[0].position.x = 42; doc.Save();
            Check(!AssetDatabase.Contains(doc), "workspace remains outside AssetDatabase");
            Check(!AssetDatabase.Contains(mesh), "owned mesh remains outside AssetDatabase");
            var copy = doc.SaveAs(Path.Combine(folder, "Independent.workspace"));
            Check(copy.nodes[0].mesh != mesh, "Save As owns independent meshes");
            copy.nodes[0].position.x = 99; copy.Save();
            Check(doc.nodes[0].position.x == 42, "copy edits preserve original");
            var legacy = StudioModelDocument.CreateEmpty("Assets/LegacyTest.asset");
            legacy.AddPrimitive(PrimitiveType.Cylinder); legacy.Save();
            var converted = StudioModelDocument.OpenFile("Assets/LegacyTest.asset");
            Check(converted && !AssetDatabase.Contains(converted) && File.Exists(converted.WorkspacePath), "legacy workspace converts to root storage");
            Check(AssetDatabase.LoadAssetAtPath<StudioModelDocument>("Assets/LegacyTest.asset"), "legacy source retained");
            bool rejected = false;
            try { doc.SaveAs(Path.Combine(Application.dataPath, "Rejected.workspace")); } catch (ArgumentException) { rejected = true; }
            Check(rejected && !File.Exists(Path.Combine(Application.dataPath,"Rejected.workspace")), "workspace files rejected inside Assets");
            Check(!Directory.GetFiles(Application.dataPath, "*.workspace", SearchOption.AllDirectories).Any(), "no workspace files under Assets");
        }
        else
        {
            var doc = StudioModelDocument.OpenFile(path);
            Check(doc && doc.nodes.Count == 2, "document and node count survive restart");
            Check(doc.nodes[0].position == new Vector3(42,2,3), "latest atomic autosave survives restart");
            Check(doc.nodes[0].positionOffset == new Vector3(3,2,1), "channel data survives restart");
            Check(doc.nodes[0].mesh && doc.nodes[0].mesh.vertexCount == 24, "owned cube mesh survives restart");
            Check(doc.nodes[1].mesh && doc.nodes[1].mesh.vertexCount > 24, "second owned mesh survives restart");
            Check(doc.nodes[0].mesh.blendShapeCount == 1, "mesh blend shape survives restart");
            Check(doc.nodes[0].materials[0] == AssetDatabase.LoadAssetAtPath<Material>("Assets/StorageMaterial.mat"), "project material reference survives restart");
            var copy = StudioModelDocument.OpenFile(Path.Combine(folder,"Independent.workspace"));
            Check(copy.nodes[0].position.x == 99 && copy.nodes[0].mesh != doc.nodes[0].mesh, "independent copy survives restart");
            var source = GameObject.CreatePrimitive(PrimitiveType.Quad);
            try { doc.AppendModel(source); }
            finally { UnityEngine.Object.DestroyImmediate(source); }
            doc.AddPrimitive(PrimitiveType.Cylinder); doc.Save();
            Check(doc.nodes.Count == 4, "reopened document supports import and create");
            var meshes = doc.nodes.Select(n => n.mesh).Where(m => m).Distinct().ToArray();
            UnityEngine.Object.DestroyImmediate(doc);
            foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            doc = StudioModelDocument.OpenFile(path);
            Check(doc.nodes.Count == 4 && doc.nodes.All(n => n.mesh && n.mesh.vertexCount > 0), "saving a reopened workspace retains every mesh on disk");
            var editor = new StudioModelEditor(() => {});
            var view = ScriptableObject.CreateInstance<MashBoxStudioViewport>();
            try
            {
                view.LookAtDirect(new Vector3(4,5,6), Quaternion.Euler(12,25,0), 8);
                var pivot=view.pivot;
                editor.CreatePrimitive(PrimitiveType.Cube,view,d=>editor.Load(d));
                Check(!AssetDatabase.Contains(editor.Document) && editor.Document.WorkspacePath.StartsWith(folder), "first create uses root workspace folder");
                Check(view.pivot == pivot, "first create preserves camera");
                Undo.PerformUndo(); Undo.PerformRedo(); editor.Document.Save();
                Check(editor.Document.nodes.Count == 1 && editor.Document.nodes[0].mesh, "creation undo redo retains owned mesh");
            }
            finally { editor.Dispose(); UnityEngine.Object.DestroyImmediate(view); }
            StudioWorkspaceStatsValidation.Run();
        }
        Debug.Log("STUDIO_WORKSPACE_STORAGE_VALIDATION_SUCCESS " + phase);
    }
}

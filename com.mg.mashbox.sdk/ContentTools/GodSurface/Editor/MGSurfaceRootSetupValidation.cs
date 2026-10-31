using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
namespace MG.GodSurface.Editor
{
    public static class MGSurfaceRootSetupValidation
    {
        public static string Run()
        {
            var log = new StringBuilder();
            void Check(bool value, string text) { if (!value) throw new Exception(text); log.AppendLine("PASS " + text); }
            var originalScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject root = null;
            string sourceFolder = AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/GodPropSetupValidation");
            string bakedFolder = null;
            try
            {
                AssetDatabase.CreateFolder("Assets/Editor", Path.GetFileName(sourceFolder));
                var material = new Material(Shader.Find(MGSurfaceBaker.SourceShader));
                AssetDatabase.CreateAsset(material, sourceFolder + "/Source.mat");
                root = new GameObject("God Prop setup validation"); SceneManager.MoveGameObjectToScene(root, scene);
                var world = root.AddComponent<MGSurfaceWorld>();
                GameObject Cube(string name, Transform parent)
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(cube, scene);
                    cube.name = name; cube.transform.SetParent(parent, false); cube.GetComponent<MeshRenderer>().sharedMaterial = material; return cube;
                }
                var a = Cube("Prop A", root.transform); var b = Cube("Prop B", root.transform);
                b.transform.localPosition = new Vector3(100,0,0);
                var sourceMesh = a.GetComponent<MeshFilter>().sharedMesh;
                var colliderMesh = a.GetComponent<BoxCollider>();
                var outside = Cube("Outside scope", root.transform);
                world.roots = new[] { a.transform, b.transform, a.transform };
                Check(world.EnumerateRenderers().Count() == 2, "Explicit roots exclude unlisted props and deduplicate overlapping entries");
                var check = MGSurfaceRootSetup.Inspect(world);
                Check(check.total == 2 && check.eligible == 2 && check.surfaces == 1, "Preflight reports shared materials once");
                var combiner = a.AddComponent<RuntimeMeshCombiner>(); combiner.enabled = false;
                Check(MGSurfaceRootSetup.Inspect(world).conflicts.Count == 1, "Disabled combiner with Combine On Awake is detected");
                bool refused = false;
                try { MGSurfaceRootSetup.Cook(world, 128, false, false, new MGSurfaceVertexAO.Settings()); }
                catch (InvalidOperationException) { refused = true; }
                Check(refused && !world.surfaceLibrary, "Cook refuses competing renderer ownership before baking");
                Check(MGSurfaceRootSetup.DisableConflicts(world) == 1 && !new SerializedObject(combiner).FindProperty("combineOnAwake").boolValue,
                    "Conflict fix clears Awake as well as component enabled state");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(new SerializedObject(combiner).FindProperty("combineOnAwake").boolValue, "Conflict fix is undoable");
                MGSurfaceRootSetup.DisableConflicts(world);
                var parentCombiner = root.AddComponent<RuntimeMeshCombiner>();
                refused = false; try { MGSurfaceRootSetup.DisableConflicts(world); } catch (InvalidOperationException) { refused = true; }
                Check(refused, "Conflict fix refuses a combiner spanning unlisted props");
                Object.DestroyImmediate(parentCombiner);
                Check(EditorSceneManager.SaveScene(scene, sourceFolder + "/Validation.unity"), "Saved isolated test scene for scene-local cooking");
                string result = MGSurfaceRootSetup.Cook(world, 128, false, true, new MGSurfaceVertexAO.Settings { samples = 16 });
                Check(world.surfaceLibrary && world.previewInEditor && world.renderInPlayMode, "Cook applies a library and enables preview and runtime rendering");
                bakedFolder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(world.surfaceLibrary)).Replace('\\','/');
                Check(bakedFolder.StartsWith(sourceFolder + "/GodSurface/"), "Baked library is beside its scene, outside global Content");
                Check(AssetDatabase.GetAssetPath(a.GetComponent<MeshFilter>().sharedMesh).StartsWith(bakedFolder + "/Meshes/"), "Baked geometry uses the Meshes folder");
                Check(File.Exists(bakedFolder + "/Meshes/Editor/BakeSettings.json"), "AO bake settings stay in editor-only metadata");
                Check(world.surfaceLibrary.geometry && AssetDatabase.GetAssetPath(world.surfaceLibrary.geometry.carrier).StartsWith(bakedFolder + "/Meshes/"), "Cook automatically bakes the shared geometry carrier into Meshes");
                Check(world.InstanceCount == 2 && world.CellCount == 2 && world.PrototypeCount == 1, "Cook registers shared meshes across two spatial cells");
                Check(a.GetComponent<MeshFilter>().sharedMesh != sourceMesh && a.GetComponent<MeshFilter>().sharedMesh == b.GetComponent<MeshFilter>().sharedMesh,
                    "Optional vertex AO creates one shared baked mesh");
                Check(colliderMesh && colliderMesh.enabled && a.GetComponent<BoxCollider>() == colliderMesh, "Cooking preserves original colliders");
                Check(MGSurfaceRootSetup.Inspect(world).surfaces == 1, "Recook resolves source materials from an existing library");
                world.Rebuild(); world.Release();
                Check(!a.GetComponent<MeshRenderer>().forceRenderingOff && !b.GetComponent<MeshRenderer>().forceRenderingOff, "Stopping preview restores normal renderers");
                Check(outside.GetComponent<MeshRenderer>().sharedMaterial == material, "Cook leaves unlisted prop assignments untouched");
                log.AppendLine(result); log.AppendLine("SUCCESS");
                return log.ToString();
            }
            finally
            {
                if (root) Object.DestroyImmediate(root);
                if (originalScene.IsValid()) SceneManager.SetActiveScene(originalScene);
                EditorSceneManager.CloseScene(scene, true);
                if (bakedFolder != null) AssetDatabase.DeleteAsset(bakedFolder);
                AssetDatabase.DeleteAsset(sourceFolder);
            }
        }
    }
}

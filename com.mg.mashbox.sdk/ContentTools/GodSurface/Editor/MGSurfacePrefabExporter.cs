using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MG.GodSurface.Editor
{
    public static class MGSurfacePrefabExporter
    {
        public static GameObject Export(GameObject source, MGSurfaceLibrary library)
        {
            if (!source || !library || EditorUtility.IsPersistent(source)) throw new ArgumentException("Select a placed scene prop and a baked library.");
            if (source.GetComponentInChildren<MGSurfaceWorld>(true)) throw new ArgumentException("Select an individual prop or authored prop assembly, not the world registry.");
            var recipe = MGSurfaceBaker.ReadRecipe(library);
            var replacements = recipe.sources.ToDictionary(s => s.materialGuid, s => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(s.previewGuid)));
            var previewGuids = recipe.sources.Select(s => s.previewGuid).ToHashSet();
            var scene = EditorSceneManager.NewPreviewScene(); GameObject copy = null;
            string destination = null;
            try
            {
                copy = UnityEngine.Object.Instantiate(source); SceneManager.MoveGameObjectToScene(copy, scene);
                if (PrefabUtility.IsPartOfPrefabInstance(copy)) PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                copy.name = source.name + " [God Surface]";
                // Scene placement belongs to each instance. Keep authored scale and the entire child/collider hierarchy.
                copy.transform.position = Vector3.zero; copy.transform.rotation = Quaternion.identity; copy.transform.localScale = source.transform.localScale;
                int converted = 0;
                foreach (var renderer in copy.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var materials = renderer.sharedMaterials; bool managed = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(materials[i]));
                        if (replacements.TryGetValue(guid, out var material)) { materials[i] = material; managed = true; }
                        else if (previewGuids.Contains(guid)) managed = true;
                    }
                    renderer.sharedMaterials = materials;
                    if (managed) { renderer.forceRenderingOff = false; converted++; GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & ~StaticEditorFlags.BatchingStatic); }
                }
                if (converted == 0) throw new InvalidOperationException("This prop has no materials in the selected library.");
                if (!copy.GetComponent<MGSurfaceProp>()) copy.AddComponent<MGSurfaceProp>();
                string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(library)).Replace('\\', '/') + "/Prefabs";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), "Prefabs");
                string name = string.Concat(copy.name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                destination = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".prefab");
                var prefab = PrefabUtility.SaveAsPrefabAsset(copy, destination, out bool success);
                if (!success) throw new InvalidOperationException("Unity could not save the converted prefab.");
                if (prefab.GetComponentsInChildren<Collider>(true).Length != source.GetComponentsInChildren<Collider>(true).Length) throw new InvalidOperationException("Collider count changed during prefab export.");
                return prefab;
            }
            catch { if (destination != null && File.Exists(destination)) AssetDatabase.DeleteAsset(destination); throw; }
            finally { if (copy) UnityEngine.Object.DestroyImmediate(copy); EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}

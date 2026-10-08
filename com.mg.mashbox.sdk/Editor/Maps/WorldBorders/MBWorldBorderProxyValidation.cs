using System;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools.WorldBorders
{
    [InitializeOnLoad]
    public static class MBWorldBorderProxyValidation
    {
        static string Work => Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(MGWorldBorderWindow).Assembly).resolvedPath, "Development~/WorldBorders");
        static MBWorldBorderProxyValidation() { EditorApplication.delayCall += Requested; }
        static void Requested()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Requested; return; }
            var path = Path.Combine(Work, "validate-proxies.request");
            if (!File.Exists(path) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(path); Run();
        }
        static void Check(bool condition, string error) { if (!condition) throw new Exception(error); }
        static void Near(Vector3 actual, Vector3 expected, string error) => Check(Vector3.Distance(actual, expected) < .02f, error);

        [MenuItem("Tools/MashBox/World Borders/Validate Runtime Proxies")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var active = SceneManager.GetActiveScene(); var selection = Selection.objects;
            string folder = "Assets/World Borders/ProxyValidation-" + Guid.NewGuid().ToString("N");
            string generatedFolder = null;
            Scene scene = default;
            var results = new System.Collections.Generic.List<string>();
            try
            {
                MGWorldBorderBuilder.EnsureFolder(folder);
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                var group = MBWorldBorderAuthoring.CreateRectangle(new Vector3(21, 30, 43));
                var walls = group.GetComponentsInChildren<MBWorldBorderWall>();
                Check(walls.Length == 4 && walls.Sum(w => w.TileCount) == 32, "Expected four proxies / 32 tiles; got " + walls.Length + " / " + walls.Sum(w => w.TileCount));
                Check(group.GetComponentsInChildren<Renderer>().Length == 0 && group.GetComponentsInChildren<Collider>().Length == 0, "Authored proxies must have no generated visuals or colliders.");
                EditorSceneManager.SaveScene(scene, folder + "/Proxies.unity");
                var deps = AssetDatabase.GetDependencies(scene.path, true);
                Check(deps.Contains(MGWorldBorderBuilder.DefaultMaterialPath) && deps.Contains(MGWorldBorderBuilder.DefaultAlbedoPath) && deps.Contains(MGWorldBorderBuilder.ShaderPath), "Export scene lost default material/shader/albedo dependencies.");
                Check(!deps.Any(p => p.StartsWith("Assets/Maps/", StringComparison.Ordinal)), "Proxies depend on map-specific assets.");
                results.Add("PASS: four editable proxies / 32 planned tiles, no authored renderers or colliders; saved scene references runtime SDK script and packaged artwork.");
                string materialBefore = EditorJsonUtility.ToJson(MBWorldBorderAuthoring.DefaultMaterial);
                foreach (var wall in walls)
                {
                    wall.BuildGeometry();
                    Check(wall.RuntimeInstance && wall.RuntimeInstance.GetComponentsInChildren<LODGroup>().Length == 8, "Wall generation tile count failed.");
                    var collider = wall.RuntimeInstance.GetComponent<BoxCollider>();
                    Check(collider && collider.enabled && !collider.isTrigger, "Missing independent collision wall.");
                    Near(collider.size, wall.WorldSize, "Collision dimensions differ from proxy.");
                    Near(collider.transform.lossyScale, Vector3.one, "Runtime scale was not normalized.");
                    foreach (var lod in wall.RuntimeInstance.GetComponentsInChildren<LODGroup>())
                    {
                        var level = lod.GetLODs().Single();
                        float cullDistance = lod.size * wall.minLodBias / (2 * level.screenRelativeTransitionHeight * Mathf.Tan(wall.maxVerticalFov * Mathf.Deg2Rad * .5f));
                        float radius = lod.GetComponent<MeshFilter>().sharedMesh.bounds.extents.magnitude;
                        Check(cullDistance + .01f >= wall.visibilityDistance + radius + wall.cullPadding, "LOD can cull a visible tile.");
                        lod.ForceLOD(1);
                    }
                }
                Physics.SyncTransforms();
                foreach (var wall in walls)
                {
                    var c = wall.RuntimeInstance.GetComponent<BoxCollider>();
                    Check(c.Raycast(new Ray(wall.transform.position + wall.transform.forward * 5, -wall.transform.forward), out _, 10), "Collision failed when visuals are culled.");
                    wall.ReleaseRuntime();
                }
                var materialAfter = EditorJsonUtility.ToJson(MBWorldBorderAuthoring.DefaultMaterial);
                if (materialBefore != materialAfter)
                {
                    File.WriteAllText(Path.Combine(Work, "material-before.json"), materialBefore);
                    File.WriteAllText(Path.Combine(Work, "material-after.json"), materialAfter);
                }
                Check(materialBefore == materialAfter, "Generation modified the shared SDK material.");
                results.Add("PASS: runtime geometry, continuous solid collision, conservative LOD distances and unchanged SDK material.");
                var test = walls[0];
                test.transform.localRotation = Quaternion.Euler(12, 37, 8);
                test.transform.localScale = new Vector3(257,129,.75f);
                group.transform.rotation = Quaternion.Euler(0, 23, 0);
                group.transform.localScale = Vector3.one * 2;
                test.BuildGeometry();
                Near(test.RuntimeInstance.transform.lossyScale, Vector3.one, "Scaled parent leaves runtime scale.");
                Near(test.RuntimeInstance.GetComponent<BoxCollider>().size, new Vector3(514,258,1.5f), "Nested scaled dimensions wrong.");
                Check(test.RuntimeInstance.GetComponentsInChildren<LODGroup>().Length == 15, "Non-square tile count incorrect.");
                var filters = test.RuntimeInstance.GetComponentsInChildren<MeshFilter>();
                Near(filters[0].transform.TransformPoint(filters[0].sharedMesh.vertices[0]), test.transform.TransformPoint(new Vector3(-.5f,-.5f,0)), "Generated corner differs from proxy.");
                float expectedArea = 514 * 258, area = filters.Sum(f => f.sharedMesh.bounds.size.x * f.sharedMesh.bounds.size.y);
                Check(Mathf.Abs(area-expectedArea) < .1f, "Tile coverage has gaps or overlaps.");
                Check(Vector2.Distance(filters[0].sharedMesh.uv[1], filters[1].sharedMesh.uv[0]) < .001f, "Pattern UVs are discontinuous across tiles.");
                var oldMesh = filters[0].sharedMesh; var oldMaterial = filters[0].GetComponent<MeshRenderer>().sharedMaterial;
                test.BuildGeometry();
                Check(!oldMesh && !oldMaterial && test.transform.childCount == 1, "Rebuild leaked resources or duplicated runtime geometry.");
                // Normal MonoBehaviours do not receive runtime lifecycle callbacks in edit mode.
                // The isolated Play Mode probe covers automatic OnDisable / OnDestroy delivery.
                test.ReleaseRuntime(); test.enabled = false;
                Check(!test.RuntimeInstance && test.transform.childCount == 0, "Release failed to clean up generated geometry.");
                test.enabled = true;
                Check(!test.RuntimeInstance, "Edit-mode enable generated runtime geometry.");
                test.solidCollision = false; test.BuildGeometry();
                Check(test.RuntimeInstance.GetComponentsInChildren<Collider>().Length == 0, "Visual-only mode generated collision.");
                test.ReleaseRuntime();
                group.transform.localScale = new Vector3(2,1,3);
                Check(test.Problem() != null, "Sheared transform must be rejected.");
                group.transform.localScale = Vector3.one;
                test.tileSize = float.NaN; Check(test.Problem() != null, "NaN tile size accepted.");
                test.tileSize = 1; test.transform.localScale = new Vector3(1000,1000,1);
                Check(test.Problem() != null, "Excessive runtime tiling accepted.");
                Object.DestroyImmediate(group);
                results.Add("PASS: rotated / scaled parents, uneven tiles, coverage, UV seams, rebuild / release cleanup, optional collision and invalid settings.");
                var profile = ScriptableObject.CreateInstance<MGWorldBorderProfile>();
                AssetDatabase.CreateAsset(profile, folder + "/Baked.asset");
                var baked = MGWorldBorderBuilder.Build(profile); generatedFolder = profile.generatedFolder;
                var marker = new GameObject("Unrelated user child"); marker.transform.SetParent(baked.transform);
                MBWorldBorderAuthoring.ConvertBakedBorder(baked);
                Check(baked.GetComponentsInChildren<MBWorldBorderWall>().Length == 4 && baked.GetComponentsInChildren<Renderer>().Length == 0 && baked.GetComponentsInChildren<Collider>().Length == 0, "Baked conversion retained generated geometry or lost proxies.");
                Check(marker && marker.transform.parent == baked.transform, "Conversion removed unrelated child.");
                Undo.PerformUndo();
                Check(baked.GetComponentsInChildren<MBWorldBorderWall>().Length == 0 && baked.GetComponentsInChildren<LODGroup>().Length == 32, "Conversion Undo failed.");
                Undo.PerformRedo();
                Check(baked.GetComponentsInChildren<MBWorldBorderWall>().Length == 4 && baked.GetComponentsInChildren<LODGroup>().Length == 0, "Conversion Redo failed.");
                results.Add("PASS: conversion of existing SDK baked walls preserves root / unrelated children and supports Undo / Redo.");
                results.Add("Validation exercises the runtime generator in an isolated editor scene; it does not enter the user's open map into Play Mode or benchmark GPU frame time.");
                File.WriteAllLines(Path.Combine(Work,"proxy-validation.txt"),results);
                Debug.Log(string.Join("\n",results));
            }
            catch (Exception e) { results.Add("FAIL: " + e); File.WriteAllLines(Path.Combine(Work,"proxy-validation.txt"),results); Debug.LogException(e); }
            finally
            {
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                if (scene.IsValid() && scene.isLoaded)
                {
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var wall in root.GetComponentsInChildren<MBWorldBorderWall>(true)) wall.ReleaseRuntime();
                    EditorSceneManager.CloseScene(scene,true);
                }
                Selection.objects = selection;
                if (!string.IsNullOrEmpty(generatedFolder)) AssetDatabase.DeleteAsset(generatedFolder);
                AssetDatabase.DeleteAsset(folder);
                MGWorldBorderWindow.Open();
            }
        }
    }
}

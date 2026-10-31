using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace MG.GodSurface.Editor
{
    public static class MGSurfaceLibraryValidation
    {
        static void Check(bool pass, string message, StringBuilder log) { if (!pass) throw new Exception(message); log.AppendLine("PASS " + message); }
        static double Difference(Color[] a, Color[] b)
        {
            double difference = 0; for (int i = 0; i < a.Length; i++) difference += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
            return difference / (3 * a.Length);
        }
        static void Save(Color[] pixels, string name)
        {
            var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
            try { texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(Path.Combine(Path.GetTempPath(), name), texture.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        public static string Run(MGSurfaceLibrary library)
        {
            var log = new StringBuilder(); var originalScene = SceneManager.GetActiveScene(); Scene scene = default;
            GameObject root = null, cameraObject = null; RenderTexture target = null; bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                Check(library && library.banks.Length > 0, "Library contains banks", log);
                var recipe = MGSurfaceBaker.ReadRecipe(library);
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                root = new GameObject("God Surface Library Validation"); SceneManager.MoveGameObjectToScene(root, scene);
                var world = root.AddComponent<MGSurfaceWorld>(); world.roots = new[] { root.transform }; world.previewInEditor = true; world.surfaceLibrary = library;
                var one = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(one, scene); one.transform.SetParent(root.transform, false); one.layer = 30;
                var two = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(two, scene); two.transform.SetParent(root.transform, false); two.layer = 30;
                one.transform.localPosition = new Vector3(-.75f, 0, 0); two.transform.localPosition = new Vector3(.75f, 0, 0);
                var first = one.GetComponent<MeshRenderer>(); var second = two.GetComponent<MeshRenderer>();
                cameraObject = new GameObject("God Surface Library Test Camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 1 << 30;
                camera.transform.position = new Vector3(0, 0, -5); camera.transform.rotation = Quaternion.identity; camera.nearClipPlane = .1f; camera.farClipPlane = 20;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGBHalf); target.Create();
                // SubmitRenderRequest can also cull editor/reflection cameras. Observe this test camera only.
                int visibleInstances = -1, cameraID = camera.GetInstanceID();
                Action<int, LODParameters, int, int> observe = (id, view, instances, commands) =>
                { if (id == cameraID) visibleInstances = instances; };
                typeof(MGSurfaceWorld).GetField("cameraDiagnostics", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(world, observe);
                foreach (var source in recipe.sources)
                {
                    var bank = library.banks[source.bank];
                    var other = recipe.sources.FirstOrDefault(s => s.bank == source.bank && s.surfaceID != source.surfaceID) ?? source;
                    world.Release();
                    first.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(source.materialGuid));
                    second.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(other.materialGuid));
                    MGSurfaceValidation.Capture(camera, target); var reference = MGSurfaceValidation.Capture(camera, target);
                    first.sharedMaterial = bank.previewMaterials[source.surfaceID]; second.sharedMaterial = bank.previewMaterials[other.surfaceID];
                    MGSurfaceValidation.Capture(camera, target); var baked = MGSurfaceValidation.Capture(camera, target);
                    double error = Difference(reference, baked);
                    Save(reference, "MGSurface-source.png"); Save(baked, "MGSurface-baked.png");
                    Check(error < .025, $"{source.name}: source vs array material mean RGB error {error:F6}", log);
                    world.Rebuild(); Check(world.LastError == null && world.InstanceCount == 2 && world.DrawGroupCount == 1, "Two surface IDs share one mesh/material draw group: " + world.LastError, log);
                    visibleInstances = -1;
                    MGSurfaceValidation.Capture(camera, target); var instanced = MGSurfaceValidation.Capture(camera, target);
                    double instanceError = Difference(baked, instanced); Save(instanced, "MGSurface-library-instanced.png");
                    Check(instanceError < .001, $"{source.name}: per-instance surface IDs match normal rendering ({instanceError:F6})", log);
                    Check(visibleInstances == 2, "Both library instances survive test camera culling: " + visibleInstances, log);
                    Check(one.GetComponent<Collider>().enabled && two.GetComponent<Collider>().enabled, "Colliders remain enabled", log);
                    if (source.bank == 0 && source.surfaceID == 0)
                    {
                        world.Release();
                        var filter = one.GetComponent<MeshFilter>(); var originalMesh = filter.sharedMesh;
                        var splitMesh = UnityEngine.Object.Instantiate(originalMesh);
                        try
                        {
                            int[] triangles = originalMesh.GetIndices(0); int middle = triangles.Length / 6 * 3;
                            splitMesh.subMeshCount = 2; splitMesh.SetIndices(triangles.Take(middle).ToArray(), MeshTopology.Triangles, 0); splitMesh.SetIndices(triangles.Skip(middle).ToArray(), MeshTopology.Triangles, 1);
                            filter.sharedMesh = splitMesh; first.sharedMaterials = new[] { bank.previewMaterials[source.surfaceID], bank.previewMaterials[other.surfaceID] };
                            two.SetActive(false); camera.transform.position = new Vector3(2, 1, -5); camera.transform.LookAt(one.transform);
                            MGSurfaceValidation.Capture(camera, target); var slotReference = MGSurfaceValidation.Capture(camera, target);
                            world.Rebuild(); MGSurfaceValidation.Capture(camera, target); var slotInstanced = MGSurfaceValidation.Capture(camera, target);
                            Check(world.InstanceCount == 1 && world.DrawGroupCount == 2, "A two-submesh prop registers two material-slot batches", log);
                            Check(Difference(slotReference, slotInstanced) < .001, "Separate submesh surface-ID streams match normal rendering", log);
                        }
                        finally
                        {
                            world.Release(); filter.sharedMesh = originalMesh; first.sharedMaterials = new[] { bank.previewMaterials[source.surfaceID] }; two.SetActive(true);
                            camera.transform.position = new Vector3(0, 0, -5); camera.transform.rotation = Quaternion.identity;
                            UnityEngine.Object.DestroyImmediate(splitMesh);
                        }
                    }
                }
                log.AppendLine("SUCCESS"); return log.ToString();
            }
            catch (Exception ex) { log.AppendLine("FAIL " + ex); return log.ToString(); }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                if (root) UnityEngine.Object.DestroyImmediate(root); if (cameraObject) UnityEngine.Object.DestroyImmediate(cameraObject);
                if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (originalScene.IsValid()) SceneManager.SetActiveScene(originalScene); if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "MGSurfaceLibraryValidation.txt"), log.ToString());
            }
        }
    }
}

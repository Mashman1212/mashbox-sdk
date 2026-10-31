using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MG.GodSurface.Editor
{
    public static class MGSurfaceLodValidation
    {
        [MenuItem("MashBox/Content Tools/Validate God Surface Cell LOD")]
        public static void Run()
        {
            var log = new StringBuilder(); var original = SceneManager.GetActiveScene(); Scene scene = default;
            GameObject root = null, cameraObject = null; Mesh low = null; Material material = null; RenderTexture target = null;
            bool async = ShaderUtil.allowAsyncCompilation; float bias = QualitySettings.lodBias;
            void Check(bool pass, string message) { if (!pass) throw new Exception(message); log.AppendLine("PASS " + message); }
            try
            {
                QualitySettings.lodBias = 1; ShaderUtil.allowAsyncCompilation = false;
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                root = new GameObject("Cell LOD validation"); SceneManager.MoveGameObjectToScene(root, scene);
                var world = root.AddComponent<MGSurfaceWorld>(); world.roots = new[] { root.transform }; world.previewInEditor = true; world.drawCellBounds = true;
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); var high = cube.GetComponent<MeshFilter>().sharedMesh;
                low = UnityEngine.Object.Instantiate(high); low.name = "Validation low mesh"; UnityEngine.Object.DestroyImmediate(cube);
                material = new Material(Shader.Find("HDRP/Lit"));
                GameObject Prop(Vector3 position)
                {
                    var prop = new GameObject("LOD prop"); prop.transform.SetParent(root.transform, false); prop.transform.position = position;
                    var renderers = new MeshRenderer[2];
                    for (int level = 0; level < 2; level++)
                    {
                        var part = new GameObject("LOD" + level); part.layer = 30; part.transform.SetParent(prop.transform, false);
                        part.AddComponent<MeshFilter>().sharedMesh = level == 0 ? high : low;
                        renderers[level] = part.AddComponent<MeshRenderer>(); renderers[level].sharedMaterial = material;
                    }
                    prop.AddComponent<BoxCollider>();
                    var group = prop.AddComponent<LODGroup>();
                    group.SetLODs(new[] { new LOD(.1f, new Renderer[] { renderers[0] }), new LOD(.001f, new Renderer[] { renderers[1] }) });
                    group.size = 2; group.localReferencePoint = Vector3.zero; return prop;
                }
                var near = Prop(new Vector3(4, 0, 4)); var far = Prop(new Vector3(8, 0, 30));
                cameraObject = new GameObject("Cell LOD camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 1 << 30;
                camera.transform.position = new Vector3(6, 0, -8); camera.fieldOfView = 60; camera.nearClipPlane = .1f; camera.farClipPlane = 300;
                target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf); target.Create();
                int visibleInstances = -1, drawCommands = -1;
                int cameraId = camera.GetInstanceID();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                Action<int, LODParameters, int, int> observe = (id, view, instances, commands) =>
                {
                    if (id == cameraId) { visibleInstances = instances; drawCommands = commands; }
                };
                typeof(MGSurfaceWorld).GetField("cameraDiagnostics", flags).SetValue(world, observe);
                void Draw() { world.GetCellDebugStates(cameraId); visibleInstances = drawCommands = -1; MGSurfaceValidation.Capture(camera, target); MGSurfaceValidation.Capture(camera, target); }

                world.lodSelection = MGSurfaceWorld.LodSelection.PerProp; world.Rebuild(); Draw();
                Check(world.LastError == null && visibleInstances == 2 && drawCommands == 2, "Per Prop selects distinct near/far LOD meshes in one cell: " + visibleInstances + "/" + drawCommands);
                world.lodSelection = MGSurfaceWorld.LodSelection.PerCellFamily; world.Rebuild(); Draw();
                Check(world.CellCount == 1 && world.CellLodFamilyCount == 1 && world.LastLodEvaluations == 1, "Matching props share one cell LOD decision");
                Check(visibleInstances == 2 && drawCommands == 1, "Near and far props switch together, with exactly one level each");
                Check(world.GetCellDebugStates(cameraId)[0] == MGSurfaceWorld.CellDebugState.Visible, "Drawing cell reports cyan state for its own camera");
                var remote = Prop(new Vector3(6, 0, 100)); world.Rebuild(); Draw();
                Check(world.CellCount == 2 && world.CellLodFamilyCount == 2 && visibleInstances == 3 && drawCommands == 2, "Other cell selects its LOD independently");
                remote.transform.position = new Vector3(6, 0, 45); world.NotifyTransformChanged(remote.transform);
                // Exercise the same incremental update called by MGSurfaceProp, without waiting for an editor frame.
                typeof(MGSurfaceWorld).GetMethod("UpdateTransforms", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(world, null);
                Draw();
                Check(world.CellCount == 1 && world.CellLodFamilyCount == 1 && visibleInstances == 3 && drawCommands == 1, "Moving a prop across a cell boundary updates its shared LOD family");
                far.transform.Find("LOD1").localPosition = Vector3.right * 80; world.Rebuild();
                Check(world.CellCount == 1 && world.CellLodFamilyCount == 1, "Offset LOD child pivots cannot split a prop across cells");
                far.transform.Find("LOD1").localPosition = Vector3.zero; world.Rebuild();
                camera.orthographic = true; camera.orthographicSize = 2000; Draw();
                Check(visibleInstances == 0 && world.GetCellDebugStates(cameraId)[0] == MGSurfaceWorld.CellDebugState.DistanceCulled,
                    "Cell past its terminal LOD cutoff reports red state");
                var permanent = new GameObject("No LOD cutoff"); permanent.layer = 30; permanent.transform.SetParent(root.transform, false);
                permanent.transform.position = new Vector3(6, 0, 20); permanent.AddComponent<MeshFilter>().sharedMesh = high;
                permanent.AddComponent<MeshRenderer>().sharedMaterial = material; world.Rebuild(); Draw();
                Check(visibleInstances == 1 && world.GetCellDebugStates(cameraId)[0] == MGSurfaceWorld.CellDebugState.PartiallyDistanceCulled,
                    "Mixed cell reports orange while its uncullable prop still draws");
                camera.transform.rotation = Quaternion.Euler(0, 180, 0); Draw();
                Check(visibleInstances == 0 && world.GetCellDebugStates(cameraId)[0] == MGSurfaceWorld.CellDebugState.NotDrawn,
                    "Frustum-rejected cell reports grey instead of false distance rejection");
                world.cellDrawDistance = 500;
                camera.transform.rotation = Quaternion.identity; camera.farClipPlane = 2000;
                var cellBounds = world.CellBounds[0];
                camera.transform.position = new Vector3(cellBounds.center.x, cellBounds.center.y, cellBounds.min.z - 499.9f); Draw();
                Check(visibleInstances == 1, "Cell just inside 500m still draws its prop without an LOD cutoff");
                camera.transform.position = new Vector3(cellBounds.center.x, cellBounds.center.y, cellBounds.min.z - 500); Draw();
                Check(visibleInstances == 0 && drawCommands == 0 && world.GetCellDebugStates(cameraId)[0] == MGSurfaceWorld.CellDebugState.DistanceCulled,
                    "At 500m the entire cell submits no instances or draws and reports red");
                world.lodSelection = MGSurfaceWorld.LodSelection.PerProp; Draw();
                Check(visibleInstances == 0 && drawCommands == 0, "Hard cell distance applies in Per Prop mode too");
                camera.transform.position = new Vector3(cellBounds.center.x, cellBounds.center.y, cellBounds.min.z - 499.9f); Draw();
                Check(visibleInstances == 1, "Returning inside 500m restores the cell automatically");
                Check(near.GetComponent<BoxCollider>().enabled && far.GetComponent<BoxCollider>().enabled, "Colliders remain enabled");
                world.Release();
                Check(!near.GetComponentInChildren<MeshRenderer>().forceRenderingOff, "Original renderer state restored after release");
                log.AppendLine("SUCCESS");
            }
            catch (Exception ex) { log.AppendLine("FAIL " + ex); Debug.LogException(ex); }
            finally
            {
                if (root) UnityEngine.Object.DestroyImmediate(root);
                if (cameraObject) UnityEngine.Object.DestroyImmediate(cameraObject);
                if (low) UnityEngine.Object.DestroyImmediate(low);
                if (material) UnityEngine.Object.DestroyImmediate(material);
                if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (original.IsValid()) SceneManager.SetActiveScene(original);
                if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
                QualitySettings.lodBias = bias; ShaderUtil.allowAsyncCompilation = async;
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "MGSurfaceLodValidation.txt"), log.ToString()); Debug.Log(log.ToString());
            }
        }
    }
}
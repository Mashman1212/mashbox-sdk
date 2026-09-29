#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
using MashBoxSDK.Maps.TerrainSystem;
namespace MashBoxSDK.MapTools
{
    // Explicit, project-scoped requests only. Never changes scene content.
    [InitializeOnLoad]
    internal static class MGTerrainScenePerformanceProbe
    {
        [Serializable] sealed class Request { public string project; public string action; public string label; public bool motion; public bool ground; }
        static readonly string Root = Path.Combine(Path.GetTempPath(), "mg-scene-performance");
        static readonly string[] Markers = { "MGTerrain.InstanceUpdate", "MGTerrain.DetailSelection", "MGTerrain.DetailCandidates", "MGTerrain.DetailCellBuild", "MGTerrain.GpuPrepare", "MGTerrain.BrgCulling", "MGTerrain.SurfaceRendering", "MGTerrain.ResidentVisibility", "MGTerrain.ResidentVisibility.Upload" };
        static Recorder[] recorders;
        static SceneView view;
        static Camera runtimeCamera;
        static Camera[] disabledCameras;
        static RenderTexture runtimeTarget;
        static Vector3 runtimePosition;
        static bool Runtime => request != null && request.action == "runtime";
        static int Warmup => Runtime ? 300 : 30;
        static int Total => Warmup + 180;
        static Vector3 pivot;
        static Quaternion rotation, restoreRotation;
        static float size;
        static bool orthographic, running, moveNext;
        static int frame;
        static double previous, nextPoll;
        static Request request;
        static MGTerrain[] tiles;
        static StringBuilder csv;
        static MGTerrainScenePerformanceProbe()
        {
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }
        static void Tick()
        {
            if (running)
            {
                if (view == null || (!Runtime && EditorApplication.isPlayingOrWillChangePlaymode) || (Runtime && !EditorApplication.isPlaying)) { Stop(); return; }
                if (moveNext)
                {
                    moveNext = false;
                    float t = Mathf.Clamp01((frame - Warmup) / 180f);
                    if (Runtime)
                    {
                        runtimeCamera.transform.SetPositionAndRotation(runtimePosition + rotation * Vector3.forward * (request.motion ? (request.ground ? 3f : 35f) * Mathf.Sin(t * Mathf.PI) : 0), Quaternion.AngleAxis(request.motion ? 40f * Mathf.Sin(t * Mathf.PI * 2) : 0, Vector3.up) * rotation);
                        return;
                    }
                    view.pivot = pivot + rotation * Vector3.forward * (request.motion ? (request.ground ? 3f : 35f) * Mathf.Sin(t * Mathf.PI) : 0);
                    view.rotation = Quaternion.AngleAxis(request.motion ? 40f * Mathf.Sin(t * Mathf.PI * 2) : 0, Vector3.up) * rotation;
                    view.Repaint();
                }
                return;
            }
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + .5;
            string path = Path.Combine(Root, "request.json");
            if (!File.Exists(path)) return;
            try
            {
                request = JsonUtility.FromJson<Request>(File.ReadAllText(path));
                if (request.project.Replace('\\','/') != Application.dataPath.Replace('\\','/')) return;
                if (Runtime && !EditorApplication.isPlaying)
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        var sv = SceneView.lastActiveSceneView;
                        var position = sv.camera.transform.position;
                        var facing = sv.rotation;
                        if(request.ground)
                        {
                            var ray = new Ray(new Vector3(position.x,2000,position.z),Vector3.down);
                            float nearest = float.PositiveInfinity;
                            foreach(var tile in UnityEngine.Object.FindObjectsByType<MGTerrain>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
                                if(tile.RaycastSurface(ray,out var hit,4000) && hit.distance < nearest) { nearest=hit.distance; position=hit.point+Vector3.up*2f; }
                            facing = Quaternion.Euler(3,facing.eulerAngles.y,0);
                        }
                        SessionState.SetString("MGPerfPose", JsonUtility.ToJson(new Pose(position,facing)));
                        EditorApplication.EnterPlaymode();
                    }
                    return;
                }
                File.Delete(path);
                tiles = UnityEngine.Object.FindObjectsByType<MGTerrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                Snapshot();
                if(request.action == "prepare")
                {
                    var changes = new StringBuilder();
                    foreach(var tile in tiles)
                    {
                        var go=tile.gameObject;
                        var flags=GameObjectUtility.GetStaticEditorFlags(go);
                        if((flags & StaticEditorFlags.BatchingStatic)==0) continue;
                        changes.AppendLine(GlobalObjectId.GetGlobalObjectIdSlow(go)+" "+go.name+" flags="+(int)flags);
                        Undo.RecordObject(go,"Preserve MG Terrain GPU source mesh");
                        GameObjectUtility.SetStaticEditorFlags(go,flags & ~StaticEditorFlags.BatchingStatic);
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
                    }
                    File.WriteAllText(Path.Combine(Root,"scene-batching-changes.txt"),changes.ToString());
                    return;
                }
                if(request.action == "validate")
                {
                    MGTerrainStreamingCacheValidation.Run();

                    EditorApplication.ExecuteMenuItem("Tools/MashBox/MG Terrain/Validate Localized Density Painting");
                    return;
                }
                if (request.action != "benchmark" && !Runtime) return;
                view = SceneView.lastActiveSceneView;
                if (view == null || (!Runtime && EditorApplication.isPlayingOrWillChangePlaymode)) throw new Exception("Requires an edit-mode Scene view.");
                pivot = view.pivot; rotation = view.rotation; restoreRotation = rotation; size = view.size; orthographic = view.orthographic;
                if (Runtime)
                {
                    disabledCameras = Camera.allCameras.Where(c => c.enabled && c.cameraType == CameraType.Game).ToArray();
                    foreach(var c in disabledCameras) c.enabled = false;
                    var go = new GameObject("MG Performance Camera") { hideFlags = HideFlags.HideAndDontSave, tag = "MainCamera" };
                    runtimeCamera = go.AddComponent<Camera>();
                    runtimeCamera.CopyFrom(view.camera);
                    foreach(var component in view.camera.GetComponents<Component>())
                        if(component != null && component.GetType().Name == "HDAdditionalCameraData") EditorUtility.CopySerialized(component, go.AddComponent(component.GetType()));
                    runtimeTarget = new RenderTexture(1280,720,24,RenderTextureFormat.ARGBHalf) { name = "MG Performance Target" };
                    runtimeTarget.Create(); runtimeCamera.targetTexture = runtimeTarget;
                    runtimeCamera.cameraType = CameraType.Game; runtimeCamera.enabled = true;
                    runtimeCamera.overrideSceneCullingMask = 0;
                    runtimeCamera.cullingMask = ~0;
                    runtimeCamera.ResetWorldToCameraMatrix(); runtimeCamera.ResetProjectionMatrix();
                    var pose = JsonUtility.FromJson<Pose>(SessionState.GetString("MGPerfPose",""));
                    runtimePosition = pose.position;
                    rotation = pose.rotation;
                    runtimeCamera.transform.SetPositionAndRotation(runtimePosition,rotation);
                }
                recorders = Markers.Select(Recorder.Get).ToArray();
                foreach(var r in recorders) r.enabled = true;
                csv = new StringBuilder("frame,interval_ms,instances,draws,regenerated,gpu_tiles,candidates," + string.Join(",", Markers) + "\n");
                frame = 0; previous = 0; running = true; moveNext = true;
                RenderPipelineManager.endCameraRendering += End;
                File.WriteAllText(Path.Combine(Root,"status.txt"),"running " + request.label);
            }
            catch(Exception e) { Stop(); File.WriteAllText(Path.Combine(Root,"error.txt"),e.ToString()); Debug.LogException(e); }
        }
        static void Snapshot()
        {
            Directory.CreateDirectory(Root);
            var sb = new StringBuilder("GPU=" + SystemInfo.graphicsDeviceName + "\nCamera=" + runtimePosition + " rotation=" + rotation.eulerAngles + "\n");
            foreach(var w in UnityEngine.Object.FindObjectsByType<MGTerrainWorld>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) sb.AppendLine(w.name + " " + EditorJsonUtility.ToJson(w));
            foreach(var t in tiles) sb.AppendLine(t.name + " gpuFailed=" + typeof(MGTerrain).GetField("m_DetailGpuGenerationUnavailable",System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(t) + " brgFailed=" + typeof(MGTerrain).GetField("m_DetailBrgUnavailable",System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(t) + " world=" + (t.World ? t.World.name : "none") + " instances=" + t.LastSubmittedDensityDetailInstances + " draws=" + t.LastDensityDetailDrawCalls + " GPU=" + t.IsGpuProceduralDensityDetailActive);
            foreach(var tile in tiles)
            {
                var mesh = tile.GetComponent<MeshFilter>()?.sharedMesh;
                if(mesh == null) continue;
                sb.AppendLine(tile.name + " mesh="+mesh.name+" grid="+tile.SurfaceGridWidth+"x"+tile.SurfaceGridHeight+" vertices="+mesh.vertexCount+" bounds="+mesh.bounds);
                if(mesh.isReadable)
                {
                    var vs=mesh.vertices;
                    sb.AppendLine("samples="+string.Join(";",vs.Take(8)));
                    sb.AppendLine("unique X="+vs.Select(v=>v.x).Distinct().Count()+" Z="+vs.Select(v=>v.z).Distinct().Count());
                }
            }
            File.WriteAllText(Path.Combine(Root,request.label + "-snapshot.txt"),sb.ToString());
            try
            {
                var assembly = typeof(Editor).Assembly;
                var logs = assembly.GetType("UnityEditor.LogEntries");
                var entryType = assembly.GetType("UnityEditor.LogEntry");
                var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                int count = (int)logs.GetMethod("StartGettingEntries",flags).Invoke(null,null);
                var output = new StringBuilder();
                try
                {
                    var entry = Activator.CreateInstance(entryType);
                    for(int i = Math.Max(0,count-100); i < count; i++)
                    {
                        logs.GetMethod("GetEntryInternal",flags).Invoke(null,new object[]{i,entry});
                        output.AppendLine(entryType.GetField("message").GetValue(entry)?.ToString());
                    }
                }
                finally { logs.GetMethod("EndGettingEntries",flags).Invoke(null,null); }
                File.WriteAllText(Path.Combine(Root,request.label + "-console.txt"),output.ToString());
            }
            catch(Exception e) { File.WriteAllText(Path.Combine(Root,"console-probe-error.txt"),e.ToString()); }
        }
        static void End(ScriptableRenderContext context, Camera camera)
        {
            if (!running || view == null || camera != (Runtime ? runtimeCamera : view.camera)) return;
            double now = EditorApplication.timeSinceStartup;
            if(frame >= Warmup && previous > 0)
            {
                csv.Append(frame).Append(',').Append(((now - previous) * 1000).ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                csv.Append(',').Append(tiles.Sum(t=>(long)t.LastSubmittedDensityDetailInstances));
                csv.Append(',').Append(tiles.Sum(t=>t.LastDensityDetailDrawCalls));
                csv.Append(',').Append(tiles.Sum(t=>(long)t.LastRegeneratedDetailInstances));
                csv.Append(',').Append(tiles.Count(t=>t.IsGpuProceduralDensityDetailActive));
                csv.Append(',').Append(tiles.Sum(t=>t.LastDetailCandidateBoundsBuilt));
                foreach(var r in recorders) csv.Append(',').Append((r.elapsedNanoseconds / 1e6).ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                csv.AppendLine();
            }
            previous = now; frame++; moveNext = true;
            if(frame % 30 == 0) File.WriteAllText(Path.Combine(Root,"status.txt"),request.label + " frame " + frame + "/" + Total);
            if(frame >= Total)
            {
                File.WriteAllText(Path.Combine(Root,request.label + ".csv"),csv.ToString());
                Snapshot();
                RenderPipelineManager.endCameraRendering -= End;
                EditorApplication.delayCall += () => { Stop(); File.WriteAllText(Path.Combine(Root,"status.txt"),"complete " + request.label); };
            }
        }
        static void Stop()
        {
            if(!running) return;
            running = false;
            RenderPipelineManager.endCameraRendering -= End;
            if(recorders != null) foreach(var r in recorders) r.enabled = false;
            if(runtimeCamera != null)
            {
                var previousTarget = RenderTexture.active;
                RenderTexture.active = runtimeTarget;
                var texture = new Texture2D(runtimeTarget.width,runtimeTarget.height,TextureFormat.RGB24,false);
                texture.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0); texture.Apply();
                File.WriteAllBytes(Path.Combine(Root,request.label + ".png"),texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture); RenderTexture.active = previousTarget;
                UnityEngine.Object.DestroyImmediate(runtimeCamera.gameObject); runtimeCamera = null;
                runtimeTarget.Release(); UnityEngine.Object.DestroyImmediate(runtimeTarget);
                foreach(var c in disabledCameras) if(c != null) c.enabled = true;
                EditorApplication.ExitPlaymode();
            }
            if(view != null) { view.pivot = pivot; view.rotation = restoreRotation; view.size = size; view.orthographic = orthographic; view.Repaint(); }
        }
    }
}
#endif

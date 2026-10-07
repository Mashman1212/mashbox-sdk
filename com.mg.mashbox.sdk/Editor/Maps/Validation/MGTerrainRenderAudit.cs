#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.Maps.TerrainSystem;
namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainRenderAudit
    {
        static readonly string Root = Path.Combine(Path.GetTempPath(), "mg-terrain-render-optimization");
        static MGTerrainRenderAudit()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopBenchmark;
            EditorApplication.quitting += StopBenchmark;
        }
        static SceneView benchView;
        static Vector3 savedPivot;
        static Quaternion savedRotation;
        static float savedSize;
        static bool savedOrthographic;
        static readonly Dictionary<MeshRenderer, Material[]> savedMaterials = new Dictionary<MeshRenderer, Material[]>();
        static readonly Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
        static readonly float[] qualities = { 50, 4, 50, 4 };
        static int phase, frame;
        static double previousFrame;
        static readonly FrameTiming[] timings = new FrameTiming[1];
        static StringBuilder samples;
        [MenuItem("Tools/MashBox/MG Terrain/Rendering/Compare Tessellation 50 vs 4")]
        static void StartBenchmark()
        {
            try { BeginBenchmark(); }
            catch (Exception e) { StopBenchmark(); Debug.LogException(e); }
        }
        static void BeginBenchmark()
        {
            Directory.CreateDirectory(Root);
            if (benchView != null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var view = SceneView.lastActiveSceneView;
            if (view == null) throw new InvalidOperationException("Open a scene view first.");
            var terrains = UnityEngine.Object.FindObjectsByType<MGTerrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            // HDRP may reset the SceneView camera transform between renders.
            // Reconstruct its position from the persistent editor orbit state.
            var distanceProperty = typeof(SceneView).GetProperty("cameraDistance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            float orbitDistance = distanceProperty != null ? (float)distanceProperty.GetValue(view) : view.size / Mathf.Sin(30 * Mathf.Deg2Rad);
            var origin = view.pivot - view.rotation * Vector3.forward * orbitDistance;
            origin.y = 2000;
            float closest = float.PositiveInfinity;
            Vector3 ground = default;
            foreach (var terrain in terrains)
                if (terrain.RaycastSurface(new Ray(origin, Vector3.down), out var hit, 4000) && hit.distance < closest)
                { closest = hit.distance; ground = hit.point; }
            if (float.IsPositiveInfinity(closest)) throw new InvalidOperationException("Scene camera must be over an MG terrain surface.");
            benchView = view; savedPivot = view.pivot; savedRotation = view.rotation; savedSize = view.size; savedOrthographic = view.orthographic;
            foreach (var terrain in terrains)
            {
                var renderer = terrain.MeshRenderer;
                if (renderer == null) continue;
                var originals = renderer.sharedMaterials;
                var replacement = (Material[])originals.Clone();
                for(int i=0;i<replacement.Length;i++)
                {
                    var original = originals[i];
                    if(original == null || !original.HasProperty("_TessellationQuality")) continue;
                    if(!copies.TryGetValue(original, out var copy))
                    { copy = new Material(original) { hideFlags = HideFlags.HideAndDontSave }; copies.Add(original,copy); }
                    replacement[i] = copy;
                }
                savedMaterials.Add(renderer, originals); renderer.sharedMaterials = replacement;
            }
            var rotation = Quaternion.Euler(8, savedRotation.eulerAngles.y, 0);
            view.pivot = ground + Vector3.up * 2 + rotation * Vector3.forward * 10; view.rotation = rotation; view.size = 5; view.orthographic = false;
            File.WriteAllText(Path.Combine(Root,"benchmark-info.txt"), "materials="+copies.Count+" ground="+ground+" originalOrbitCameraXZ="+origin+" pivot="+view.pivot+" size="+view.size);
            samples = new StringBuilder("phase,quality,frame,wall_ms,gpu_ms,cpu_ms,triangles,batches\n");
            phase=0; frame=0; previousFrame=0;
            foreach(var copy in copies.Values) copy.SetFloat("_TessellationQuality", qualities[0]);
            RenderPipelineManager.endCameraRendering += OnCameraEnd;
            EditorApplication.update += RepaintBenchmark;
            view.Repaint();
        }
        static void RepaintBenchmark() { if(benchView != null) benchView.Repaint(); }
        static void OnCameraEnd(ScriptableRenderContext context, Camera camera)
        {
            try { RecordBenchmarkFrame(context, camera); }
            catch (Exception e) { StopBenchmark(); Debug.LogException(e); }
        }
        static void RecordBenchmarkFrame(ScriptableRenderContext context, Camera camera)
        {
            if(benchView == null || camera != benchView.camera) return;
            double now = EditorApplication.timeSinceStartup;
            double elapsed = previousFrame > 0 ? (now-previousFrame)*1000 : 0;
            previousFrame=now;
            FrameTimingManager.CaptureFrameTimings();
            uint available = FrameTimingManager.GetLatestTimings(1,timings);
            if(frame >= 60)
                samples.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0},{1},{2},{3:F4},{4:F4},{5:F4},{6},{7}",phase,qualities[phase],frame,elapsed,available>0?timings[0].gpuFrameTime:0,available>0?timings[0].cpuFrameTime:0,UnityEditor.UnityStats.triangles,UnityEditor.UnityStats.drawCalls));
            if(++frame < 140) return;
            if(camera.targetTexture != null && phase < 2)
            {
                var previous = RenderTexture.active;
                Texture2D capture = null;
                try
                {
                    RenderTexture.active=camera.targetTexture;
                    capture = new Texture2D(camera.targetTexture.width,camera.targetTexture.height,TextureFormat.RGB24,false);
                    capture.ReadPixels(new Rect(0,0,capture.width,capture.height),0,0); capture.Apply();
                    File.WriteAllBytes(Path.Combine(Root,"quality-"+qualities[phase]+".png"),capture.EncodeToPNG());
                }
                finally { RenderTexture.active=previous; if(capture!=null) UnityEngine.Object.DestroyImmediate(capture); }
            }
            if(++phase == qualities.Length)
            {
                File.WriteAllText(Path.Combine(Root,"benchmark.csv"),samples.ToString());
                StopBenchmark();
                Debug.Log("MG Terrain tessellation comparison finished; original materials and scene camera restored.");
                return;
            }
            frame=0;
            foreach(var copy in copies.Values) copy.SetFloat("_TessellationQuality", qualities[phase]);
        }
        [MenuItem("Tools/MashBox/MG Terrain/Rendering/Stop Tessellation Comparison")]
        static void StopBenchmark()
        {
            RenderPipelineManager.endCameraRendering -= OnCameraEnd;
            RenderPipelineManager.endCameraRendering -= CaptureCullComparison;
            EditorApplication.update -= RepaintBenchmark;
            foreach(var pair in savedMaterials) if(pair.Key != null) pair.Key.sharedMaterials=pair.Value;
            savedMaterials.Clear();
            foreach(var copy in copies.Values) if(copy != null) UnityEngine.Object.DestroyImmediate(copy);
            copies.Clear();
            if(benchView != null) { benchView.pivot = savedPivot; benchView.rotation = savedRotation; benchView.size = savedSize; benchView.orthographic = savedOrthographic; benchView.Repaint(); }
            benchView=null;
        }

        static void CaptureCullComparison(ScriptableRenderContext context, Camera camera)
        {
            if(camera.cameraType != CameraType.SceneView) return;
            RenderPipelineManager.endCameraRendering -= CaptureCullComparison;
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            int oldVisible=0, newVisible=0, tested=0;
            long oldTriangles=0, newTriangles=0;
            var expand = typeof(MGTerrain).GetMethod("ExpandDistantMorphBounds",BindingFlags.Instance|BindingFlags.NonPublic);
            foreach(var terrain in UnityEngine.Object.FindObjectsByType<MGTerrain>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
            {
                float oldPadding=0;
                foreach(var material in terrain.MeshRenderer.sharedMaterials)
                    if(material != null && material.HasProperty("_TessellationMaxDisplacement")) oldPadding=Mathf.Max(oldPadding,Mathf.Abs(material.GetFloat("_TessellationMaxDisplacement")));
                foreach(var renderer in terrain.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var filter=renderer.GetComponent<MeshFilter>();
                    if(filter==null || !terrain.IsSurfaceRenderTile(filter) || !renderer.enabled || renderer.forceRenderingOff) continue;
                    tested++;
                    var scale=filter.transform.lossyScale;
                    var local=filter.sharedMesh.bounds;
                    local.Expand(new Vector3(oldPadding/Mathf.Max(.0001f,Mathf.Abs(scale.x)),oldPadding/Mathf.Max(.0001f,Mathf.Abs(scale.y)),oldPadding/Mathf.Max(.0001f,Mathf.Abs(scale.z)))*2);
                    local=(Bounds)expand.Invoke(terrain,new object[]{local});
                    var matrix=filter.transform.localToWorldMatrix;
                    Vector3 x=matrix.MultiplyVector(new Vector3(local.extents.x,0,0)), y=matrix.MultiplyVector(new Vector3(0,local.extents.y,0)), z=matrix.MultiplyVector(new Vector3(0,0,local.extents.z));
                    var extent=new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z));
                    var oldBounds=new Bounds(matrix.MultiplyPoint3x4(local.center),extent*2);
                    long triangles=0;
                    for(int i=0;i<filter.sharedMesh.subMeshCount;i++) triangles+=(long)filter.sharedMesh.GetIndexCount(i)/3;
                    if(GeometryUtility.TestPlanesAABB(planes,oldBounds)) { oldVisible++; oldTriangles+=triangles; }
                    if(GeometryUtility.TestPlanesAABB(planes,renderer.bounds)) { newVisible++; newTriangles+=triangles; }
                }
            }
            File.WriteAllText(Path.Combine(Root,"culling.txt"),"Tested active chunk renderers="+tested+"; frustum candidates before="+oldVisible+", after="+newVisible+"; source triangles before="+oldTriangles+", after="+newTriangles+". This is a conservative frustum test, not measured draw calls or FPS.");
        }

        static void ValidateBounds()
        {
            var shader = Shader.Find("Shader Graphs/MG_Lit_Trail");
            var method = typeof(MGTerrain).GetMethod("GetSurfaceDisplacementPadding", BindingFlags.Static | BindingFlags.NonPublic);
            if(shader == null || method == null) throw new Exception("Optimized terrain code/shader not loaded.");
            var material = new Material(shader);
            try
            {
                if(!material.HasProperty("_TessellationQuality") || material.GetFloat("_TessellationQuality") != 4) throw new Exception("Tessellation default is not 4.");
                material.SetFloat("_TessellationMaxDisplacement",200);
                material.SetFloat("_TessellationMode",0);
                material.SetFloat("_TesselationAmplitudeMaster",1);
                for(int i=0;i<8;i++) { material.SetFloat("_TesselationRemapMin"+i.ToString("00"),0); material.SetFloat("_TesselationRemapMax"+i.ToString("00"),1); }
                float normal = (float)method.Invoke(null,new object[]{material,false});
                float external = (float)method.Invoke(null,new object[]{material,true});
                if(normal != 1 || external != 200) throw new Exception("Normal or property-block bounds regression.");
                material.SetFloat("_TesselationRemapMin03",-40); material.SetFloat("_TesselationAmplitudeMaster",-3);
                float extreme = (float)method.Invoke(null,new object[]{material,false});
                if(extreme < 24 || extreme > 25) throw new Exception("Extreme displacement was not conservatively bounded.");
                material.SetFloat("_TessellationMode",1);
                if((float)method.Invoke(null,new object[]{material,false}) != 200) throw new Exception("Phong fallback regression.");
                File.WriteAllText(Path.Combine(Root,"validation.txt"),"PASS: default tessellation=4; terrain bounds=1m; external overrides retain 200m; negative/large displacement covered; Phong preserves configured bounds.");
            }
            finally { UnityEngine.Object.DestroyImmediate(material); }
        }

        [MenuItem("Tools/MashBox/MG Terrain/Rendering/Capture Render Audit")]
        static void Audit()
        {
            Directory.CreateDirectory(Root);
            ValidateBounds();
            var text = new StringBuilder();
            foreach (var terrain in UnityEngine.Object.FindObjectsByType<MGTerrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var renderer = terrain.MeshRenderer;
                if (renderer == null) continue;
                text.AppendLine(terrain.name + " | chunks=" + terrain.SurfaceTileCount + " | active=" + terrain.ActiveSurfaceRendererCount + " | bounds=" + renderer.bounds + " | propertyBlock=" + renderer.HasPropertyBlock());
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    text.AppendLine("  " + AssetDatabase.GetAssetPath(material) + " | " + material.shader.name);
                    foreach (var key in new[]{"_TessellationQuality", "_POMAmplitudeMaster", "_TesselationAmplitudeMaster", "_TessellationMaxDisplacement", "_DistantSurfaceStrength"})
                        if(material.HasProperty(key)) text.AppendLine("  " + key + "=" + material.GetFloat(key));
                    foreach(var message in ShaderUtil.GetShaderMessages(material.shader)) text.AppendLine(message.severity + ": " + message.message);
                }
            }
            File.WriteAllText(Path.Combine(Root, "audit.txt"), text.ToString());
            var importer = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.ShaderGraph.ShaderGraphImporter")).FirstOrDefault(t => t != null);
            var getText = importer.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(m => m.Name == "GetShaderText" && m.GetParameters().Length == 4);
            object[] args = { "Packages/com.mg.mashbox.sdk/Shaders/HDRP/Lit/Surfaces/MG_Lit_Trail.shadergraph", null, null, null };
            File.WriteAllText(Path.Combine(Root, "MG_Lit_Trail.generated.shader"), (string)getText.Invoke(null, args));
            RenderPipelineManager.endCameraRendering -= CaptureCullComparison;
            RenderPipelineManager.endCameraRendering += CaptureCullComparison;
            SceneView.RepaintAll();
            Debug.Log("MG Terrain render audit written to " + Root);
        }
    }
}
#endif


using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[InitializeOnLoad]
static class MGReflectionCaptureDiagnostics
{
    const string Root = "D:/MappyX/Assets/MGTerrainImplementation~/ReflectionFix/";
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static double until;
    static int faces;
    static bool requested;
    static MGReflectionCaptureDiagnostics()
    {
        EditorApplication.delayCall += () => {
            if (SessionState.GetBool("MGReflectionDiagnostic3", false)) return;
            SessionState.SetBool("MGReflectionDiagnostic3", true);
            Run();
        };
    }
    static object Field(object o, string name) => o.GetType().GetField(name, Flags)?.GetValue(o);
    static int Count(object o) => o is ICollection c ? c.Count : -1;
    [MenuItem("Tools/MapiX/Diagnose Reflection Grass")]
    static void Run()
    {
        faces = 0;
        File.WriteAllText(Root + "capture-diagnostics.txt", "Playing=" + Application.isPlaying + "\n");
        RenderPipelineManager.beginCameraRendering -= CameraBegin;
        RenderPipelineManager.beginCameraRendering += CameraBegin;
        requested = false;
        until = EditorApplication.timeSinceStartup + 20;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }
    static void CameraBegin(ScriptableRenderContext context, Camera camera)
    {
        if (!requested || camera.cameraType != CameraType.Reflection || faces++ >= 6) return;
        var text = new StringBuilder($"\nCAMERA {camera.name} {camera.transform.position} {camera.transform.forward}\n");
        var planes = GeometryUtility.CalculateFrustumPlanes(camera);
        foreach (var terrain in UnityEngine.Object.FindObjectsByType<MGTerrain>(FindObjectsSortMode.None))
        {
            var cache = Field(terrain, "m_DensityDetailCache") as IDictionary;
            int cpu = 0, gpu = 0, visible = 0, draws = 0;
            if (cache != null) foreach (var chunk in cache.Values)
            {
                if ((bool)Field(chunk,"gpuProcedural")) gpu++; else cpu++;
                if (GeometryUtility.TestPlanesAABB(planes,(Bounds)Field(chunk,"worldBounds"))) {
                    visible++; draws += Count(Field(chunk,"combinedDraws")) + Count(Field(chunk,"batches"));
                }
            }
            if (cpu+gpu == 0) continue;
            text.AppendLine($"{terrain.name} dirty={Field(terrain,"m_RenderCacheDirty")}/{Field(terrain,"m_DetailRenderCacheDirty")} cache cpu={cpu} gpu={gpu} face cells={visible} draws={draws} resident={Count(Field(terrain,"m_ResidentGpuCells"))}");
        }
        File.AppendAllText(Root+"capture-diagnostics.txt",text.ToString());
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < until) { EditorApplication.QueuePlayerLoopUpdate(); return; }
        if (!requested)
        {
            requested = true;
            foreach (var probe in UnityEngine.Object.FindObjectsByType<HDAdditionalReflectionData>(FindObjectsSortMode.None))
                if (probe.name == "WORLD REFLECTION")
                    typeof(HDProbe).GetProperty("wasRenderedAfterOnEnable", Flags).SetValue(probe, false);
            until = EditorApplication.timeSinceStartup + 8;
            return;
        }
        EditorApplication.update -= Tick;
        RenderPipelineManager.beginCameraRendering -= CameraBegin;
        File.AppendAllText(Root+"capture-diagnostics.txt", "\nFaces observed="+faces);
        foreach (var probe in UnityEngine.Object.FindObjectsByType<HDAdditionalReflectionData>(FindObjectsSortMode.None))
            if (probe.name == "WORLD REFLECTION" && probe.realtimeTexture is RenderTexture rt)
            {
                var previous = RenderTexture.active;
                var read = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
                var png = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                try
                {
                    for (int f=0; f<6; f++)
                    {
                        Graphics.SetRenderTarget(rt,0,(CubemapFace)f);
                        read.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); read.Apply();
                        var pixels=read.GetPixels();
                        for(int i=0;i<pixels.Length;i++) {
                            var c=pixels[i]*0.0001f;
                            pixels[i]=new Color(Mathf.LinearToGammaSpace(c.r/(1+c.r)),Mathf.LinearToGammaSpace(c.g/(1+c.g)),Mathf.LinearToGammaSpace(c.b/(1+c.b)),1);
                        }
                        png.SetPixels(pixels); png.Apply();
                        File.WriteAllBytes(Root+"after-capture-"+f+".png",png.EncodeToPNG());
                    }
                }
                finally { RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(read); UnityEngine.Object.DestroyImmediate(png); }
            }
    }
}

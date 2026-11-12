#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using MashBoxSDK.Maps.TerrainSystem;

namespace MashBoxSDK.MapTools
{
    // Explicit Play Mode comparison. Does not repaint, start Play Mode, bake or save assets.
    public static class MGTerrainPlayModeComparison
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        sealed class Saved
        {
            internal MGTerrain tile;
            internal bool enabled, details;
            internal ShadowCastingMode shadows;
            internal Material[] materials;
        }
        static readonly string[] Phases={"Baseline","Surface off","Surface shadows off","No tessellation","HDRP Lit surface","Details off","Unlimited details","500k details","50k details","Baseline restored"};
        static readonly List<Saved> saved=new();
        static readonly Dictionary<MGTerrainWorld,float> distances=new();
        static readonly Dictionary<MGTerrainWorld,bool> draws=new();
        static readonly Dictionary<MGTerrainWorld,int> budgets=new();
        static readonly FieldInfo Quality=typeof(MGTerrainWorld).GetField("m_Quality",Flags);
        static readonly FieldInfo Budget=typeof(MGTerrainWorld).GetField("m_MaxVisibleDetailInstances",Flags);
        static readonly List<Material> temporary=new();
        static readonly List<double> frames=new(), worldTimes=new();
        static ProfilerRecorder worldRecorder;
        static int phase, frame, lastFrame;
        static string report;
        static bool running;
        static double deadline;
        static readonly FieldInfo Draw=typeof(MGTerrain).GetField("m_DrawInstances",Flags);
        static readonly FieldInfo Distance=typeof(MGTerrainWorld).GetField("m_SurfaceShaderDistance",Flags);

        [MenuItem("Tools/MashBox/MG Terrain/Validation/Compare Play Mode Rendering %#F10")]
        public static void Begin()
        {
            if(running || !EditorApplication.isPlaying) { Debug.Log("Enter Play Mode before comparing terrain rendering."); return; }
            try
            {
                foreach(var tile in UnityEngine.Object.FindObjectsByType<MGTerrain>(FindObjectsSortMode.None))
                {
                    if(!tile.isActiveAndEnabled || tile.MeshRenderer==null) continue;
                    saved.Add(new Saved{tile=tile,enabled=tile.MeshRenderer.enabled,shadows=tile.MeshRenderer.shadowCastingMode,
                        materials=tile.MeshRenderer.sharedMaterials,details=(bool)Draw.GetValue(tile)});
                    if(tile.World!=null && !distances.ContainsKey(tile.World)) distances.Add(tile.World,(float)Distance.GetValue(tile.World));
                }
                foreach(var w in distances.Keys)
                {
                    w.ApplySharedQuality();
                    draws[w]=((MGTerrainWorldQuality)Quality.GetValue(w)).m_DrawInstances;
                    budgets[w]=(int)Budget.GetValue(w);
                }
                string folder=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"LocalDiagnostics","TerrainPerformance","PlayModeComparison");
                Directory.CreateDirectory(folder); report=Path.Combine(folder,DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt");
                File.WriteAllText(report,$"Tiles={saved.Count}; VSync={QualitySettings.vSyncCount}; targetFPS={Application.targetFrameRate}; resolution={Screen.width}x{Screen.height}\n");
                worldRecorder=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"MGTerrain.WorldRender",1);
                phase=0;frame=0;lastFrame=-1;frames.Clear();worldTimes.Clear();running=true;deadline=EditorApplication.timeSinceStartup+180;
                EditorApplication.update+=Step;
                AssemblyReloadEvents.beforeAssemblyReload+=Stop;
                EditorApplication.playModeStateChanged+=PlayChanged;
                Debug.Log("Terrain fixed-camera comparison started. Keep the Game view visible and camera still.");
            }
            catch(Exception e){Debug.LogException(e);Stop();}
        }
        static void PlayChanged(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingPlayMode)Stop();}
        static void Restore()
        {
            foreach(var s in saved) if(s.tile!=null)
            {
                s.tile.MeshRenderer.enabled=s.enabled;s.tile.MeshRenderer.shadowCastingMode=s.shadows;
                s.tile.MeshRenderer.sharedMaterials=s.materials;Draw.SetValue(s.tile,s.details);
            }
            foreach(var pair in distances)if(pair.Key!=null)Distance.SetValue(pair.Key,pair.Value);
            foreach(var pair in draws)if(pair.Key!=null)((MGTerrainWorldQuality)Quality.GetValue(pair.Key)).m_DrawInstances=pair.Value;
            foreach(var pair in budgets)if(pair.Key!=null)Budget.SetValue(pair.Key,pair.Value);
            foreach(var m in temporary)if(m!=null)UnityEngine.Object.DestroyImmediate(m);
            temporary.Clear();
        }
        static void Apply()
        {
            Restore();
            foreach(var s in saved)
            {
                if(s.tile==null)continue;
                if(phase==1)s.tile.MeshRenderer.enabled=false;
                if(phase==2)s.tile.MeshRenderer.shadowCastingMode=ShadowCastingMode.Off;
                if(phase==4)
                {
                    var shader=Shader.Find("HDRP/Lit"); if(shader==null)throw new Exception("HDRP/Lit is unavailable.");
                    var mats=s.materials.Select(original=>{
                        var m=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
                        if(original!=null && original.HasProperty("_BaseColor"))m.SetColor("_BaseColor",original.GetColor("_BaseColor"));
                        temporary.Add(m);return m;
                    }).ToArray();
                    s.tile.MeshRenderer.sharedMaterials=mats;
                }
                if(phase==5)Draw.SetValue(s.tile,false);
            }
            if(phase==5)foreach(var w in draws.Keys)((MGTerrainWorldQuality)Quality.GetValue(w)).m_DrawInstances=false;
            if(phase>=6 && phase<=8)foreach(var w in budgets.Keys)Budget.SetValue(w,phase==6?0:phase==7?500000:50000);
            if(phase==3)foreach(var world in distances.Keys)if(world!=null)Distance.SetValue(world,0f);
        }
        static void Step()
        {
            try
            {
                if(!EditorApplication.isPlaying || EditorApplication.timeSinceStartup>deadline){Stop();return;}
                if(lastFrame==Time.frameCount)return;
                lastFrame=Time.frameCount;
                if(++frame<=180)return;
                frames.Add(Time.unscaledDeltaTime*1000.0);
                if(worldRecorder.Valid && worldRecorder.Count>0)worldTimes.Add(worldRecorder.LastValue/1000000.0);
                if(frame<360)return;
                frames.Sort();worldTimes.Sort();
                double median=frames[frames.Count/2], p95=frames[(int)(frames.Count*.95)];
                double world=worldTimes.Count>0?worldTimes[worldTimes.Count/2]:-1;
                var line=$"{Phases[phase]}: median={median:F3}ms ({1000/median:F1} FPS), p95={p95:F3}ms, worldCPU={world:F3}ms, draws={UnityStats.drawCalls}, triangles={UnityStats.triangles}, requested={distances.Keys.Sum(w=>w.LastRequestedDetailInstances)}, selected={distances.Keys.Sum(w=>w.LastSubmittedDetailInstances)}, detailsEnabled={saved.Count(s=>(bool)Draw.GetValue(s.tile))}";
                File.AppendAllText(report,line+"\n");Debug.Log(line);
                if(++phase==Phases.Length){File.AppendAllText(report,"COMPLETE\n");Stop();return;}
                frames.Clear();worldTimes.Clear();frame=0;Apply();
            }
            catch(Exception e){Debug.LogException(e);Stop();}
        }
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Stop Play Mode Comparison")]
        public static void Stop()
        {
            EditorApplication.update-=Step;AssemblyReloadEvents.beforeAssemblyReload-=Stop;EditorApplication.playModeStateChanged-=PlayChanged;
            Restore();saved.Clear();distances.Clear();draws.Clear();budgets.Clear();
            if(worldRecorder.Valid)worldRecorder.Dispose();
            if(running)Debug.Log("Terrain comparison stopped; original rendering settings restored. "+report);
            running=false;
        }
    }
}
#endif
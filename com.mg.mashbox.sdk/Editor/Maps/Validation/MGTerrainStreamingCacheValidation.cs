#if UNITY_EDITOR && UNITY_6000_0_OR_NEWER
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MashBoxSDK.Maps.TerrainSystem;
namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainStreamingCacheValidation
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static object Get(object o,string n) => o.GetType().GetField(n,Flags).GetValue(o);
        static void Set(object o,string n,object v) => o.GetType().GetField(n,Flags).SetValue(o,v);
        static object New(string n,params object[] args) => Activator.CreateInstance(typeof(MGTerrain).GetNestedType(n,BindingFlags.NonPublic),Flags,null,args,null);
        static object Call(object o,string n,params object[] args) => o.GetType().GetMethod(n,Flags).Invoke(o,args);
        static void Check(bool value,string message) { if(!value) throw new Exception(message); }
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Streaming Cache Performance")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = EditorSceneManager.NewPreviewScene();
            var go = new GameObject("Cache validation");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            go.SetActive(false);
            string path = Path.Combine(Path.GetTempPath(),"mg-scene-performance","cache-validation.txt");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var tile = go.AddComponent<MGTerrain>();
                Set(tile,"m_UseBatchRendererGroup",false);
                Set(tile,"m_MaxCachedDetailChunks",32);
                Set(tile,"m_DetailRenderTick",100);
                var cells = (IDictionary)Get(tile,"m_DensityDetailCache");
                var caches = (IDictionary)Get(tile,"m_FixedCandidateCaches");
                var cache = New("FixedCandidateCache"); caches.Add(0,cache);
                var bounds = (IDictionary)Get(cache,"geometryBounds");
                var occupancy = new bool[]{true,false,true}; Set(cache,"occupiedCells",occupancy);
                var candidates = (IList)Get(cache,"candidates");
                var all = new object[45];
                for(int i=0;i<45;i++)
                {
                    var key = New("DetailChunkKey",0,i*4,0,4,0);
                    var chunk = New("DensityDetailChunk"); all[i]=chunk;
                    Set(chunk,"lastUsedTick",100);
                    cells.Add(key,chunk); bounds.Add(key,new Bounds(Vector3.zero,Vector3.one));
                    candidates.Add(New("DetailCandidateChunk",i*4,0,4,0,new Bounds(Vector3.zero,Vector3.one),1f,true,chunk));
                }
                Call(tile,"PruneDetailChunkCache");
                Check(cells.Count==45,"Current working set must remain resident above the soft limit.");
                for(int i=0;i<40;i++) Set(all[i],"lastUsedTick",i);
                Set(tile,"m_DetailStreamingSettled",true);
                Call(tile,"PruneDetailChunkCache");
                Check(cells.Count==32,"Eviction must reach the soft limit.");
                Check(ReferenceEquals(caches[0],cache) && bounds.Count==45 && ReferenceEquals(Get(cache,"occupiedCells"),occupancy),"Eviction must preserve cached geometry and occupancy.");
                for(int i=0;i<45;i++)
                {
                    Check(cells.Contains(New("DetailChunkKey",0,i*4,0,4,0))==(i>=13),"Eviction must remove the oldest unused cells first.");
                    Check((Get(candidates[i],"cachedChunk")==null)==(i<13),"Candidate references must not retain disposed cells.");
                }
                Check(!(bool)Get(tile,"m_DetailStreamingSettled"),"Eviction must resume streaming.");
                var camera=go.AddComponent<Camera>(); camera.enabled=false; camera.cameraType=CameraType.SceneView;
                Check((bool)Call(tile,"IsDensityDetailStreamingCamera",camera),"Scene camera must be eligible in edit mode.");
                camera.cullingMask=0;
                Check(!(bool)Call(tile,"IsDensityDetailStreamingCamera",camera),"Scene camera must respect terrain layer masks.");
                var originalFlags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI;
                GameObjectUtility.SetStaticEditorFlags(go, originalFlags);
                var other = new GameObject("Unrelated static object");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(other,scene);
                GameObjectUtility.SetStaticEditorFlags(other,originalFlags);
                MGTerrainBuildProcessor.PreserveTerrainSourceMeshes(scene);
                Check(GameObjectUtility.GetStaticEditorFlags(go)==(originalFlags & ~StaticEditorFlags.BatchingStatic),"Only terrain static batching must be excluded.");
                Check(GameObjectUtility.GetStaticEditorFlags(other)==originalFlags,"Unrelated static geometry must remain batched.");
                MGTerrainBuildProcessor.PreserveTerrainSourceMeshes(scene);
                Check(GameObjectUtility.GetStaticEditorFlags(go)==(originalFlags & ~StaticEditorFlags.BatchingStatic),"Scene preprocessing must be idempotent.");
                UnityEngine.Object.DestroyImmediate(other);
                File.WriteAllText(path,"PASS: build-time terrain batching exclusion, other static flags preserved, unrelated renderers unchanged, idempotent processing; soft-limit working set, oldest-first eviction, retained bounds and occupancy, disposed-cell reference removal, streaming restart, Scene camera eligibility and layer mask.");
                Debug.Log("MG Terrain streaming cache validation passed.");
            }
            catch(Exception e) { File.WriteAllText(path,"FAIL: " + e); Debug.LogException(e); }
            finally { UnityEngine.Object.DestroyImmediate(go); EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
#endif


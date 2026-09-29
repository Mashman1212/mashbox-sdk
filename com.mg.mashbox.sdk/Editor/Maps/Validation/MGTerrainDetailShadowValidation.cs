#if UNITY_EDITOR && UNITY_6000_0_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainDetailShadowValidation
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly Type Terrain = typeof(MashBoxSDK.Maps.TerrainSystem.MGTerrain);
        static object Get(object o,string n) => o.GetType().GetField(n,F).GetValue(o);
        static void Set(object o,string n,object value) => o.GetType().GetField(n,F).SetValue(o,value);
        static object Call(object o,string n,params object[] args) => o.GetType().GetMethod(n,F).Invoke(o,args);
        static void Check(bool ok,string why) { if(!ok) throw new Exception(why); }
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Detail Shadows")]
        public static void Run()
        {
            var notes=new List<string>();
            string root=Path.Combine(Path.GetTempPath(),"mg-grass-runtime");Directory.CreateDirectory(root);
            var preview=EditorSceneManager.NewPreviewScene();GameObject owner=null;
            try
            {
                var select=Terrain.GetMethod("SelectShadowPopulation",BindingFlags.NonPublic|BindingFlags.Static);
                int Population(int n,bool tree,float distance,float limit)=>(int)select.Invoke(null,new object[]{n,tree,distance,limit});
                Check(Population(1000,false,30,60)==1000,"Nearby shadows must remain full.");
                Check(Population(1000,false,60,60)==0,"Far grass shadows must be omitted.");
                Check(Population(1000,true,10000,60)==1000,"Tree shadows must remain unchanged.");
                Check(Population(1000,false,10000,0)==1000,"Zero limit must preserve legacy shadows.");
                int previous=1000;
                for(int d=0;d<=1000;d++){int count=Population(1000,false,d*.1f,60);Check(count>=0&&count<=previous,"Fade must be monotonic and bounded.");previous=count;}
                notes.Add("PASS: nearby fidelity, monotonic fade, far removal, unlimited mode and tree preservation.");
                owner=new GameObject("Shadow prefix validation");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(owner,preview);owner.SetActive(false);
                var tile=owner.AddComponent<MashBoxSDK.Maps.TerrainSystem.MGTerrain>();
                object chunk=Activator.CreateInstance(Terrain.GetNestedType("DensityDetailChunk",BindingFlags.NonPublic),true);
                object cell=Activator.CreateInstance(Terrain.GetNestedType("ResidentGpuCell",BindingFlags.NonPublic),true);
                Set(cell,"chunk",chunk);Set(cell,"start",37);
                var spawns=(IList)Get(chunk,"proceduralSpawns");
                var spawnType=Terrain.GetNestedType("DensityDetailSpawn",BindingFlags.NonPublic);
                var constructor=spawnType.GetConstructors(F).Single();
                int[] counts={1,7,2,11,3,19};int total=counts.Sum();Set(chunk,"instanceCount",total);
                for(int i=0;i<counts.Length;i++)spawns.Add(constructor.Invoke(new object[]{i,0,counts[i],Vector4.one,0}));
                var visibleCounts=(IDictionary)Get(tile,"m_ResidentVisibleCounts");
                var scratch=(List<int>)Get(tile,"m_ShadowIndexScratch");
                for(int resident=1;resident<=total;resident++)
                {
                    Set(cell,"population",resident);
                    for(int visible=1;visible<=resident;visible++)
                    {
                        visibleCounts[chunk]=visible;
                        scratch.Clear();Call(tile,"AppendShadowIndices",cell,visible);var cameraSet=new HashSet<int>(scratch);
                        Check(cameraSet.Count==visible,"Full prefix has wrong population or duplicates.");
                        for(int selected=0;selected<=visible;selected++)
                        {
                            scratch.Clear();Call(tile,"AppendShadowIndices",cell,selected);
                            Check(scratch.Count==selected&&scratch.Distinct().Count()==selected,"Shadow prefix count or duplicates.");
                            Check(scratch.All(index=>index>=37&&index<37+resident&&cameraSet.Contains(index)),"Shadow must be a subset of visible resident instances.");
                        }
                    }
                }
                notes.Add("PASS: exhaustive uneven-texel resident/camera/shadow budgets preserve exact counts, bounds, uniqueness and subset identity.");
                Set(tile,"m_DetailShadowSelectionReady",true);
                Call(tile,"ClearDensityDetailBrgVisibility");
                Check(!(bool)Get(tile,"m_DetailShadowSelectionReady"),"Clearing camera visibility must clear shadow readiness.");
                notes.Add("PASS: visibility clearing invalidates shadow selection.");
                File.WriteAllLines(Path.Combine(root,"shadow-validation.txt"),notes);
                Debug.Log("MG Terrain detail shadow validation passed.");
            }
            catch(Exception e){notes.Add("FAIL: "+e);File.WriteAllLines(Path.Combine(root,"shadow-validation.txt"),notes);Debug.LogException(e);}
            finally{if(owner!=null)UnityEngine.Object.DestroyImmediate(owner);EditorSceneManager.ClosePreviewScene(preview);}
        }
        public static void ValidateLive()
        {
            int checkedTiles=0,indices=0;
            foreach(var tile in UnityEngine.Object.FindObjectsByType<MashBoxSDK.Maps.TerrainSystem.MGTerrain>(FindObjectsSortMode.None))
            {
                if(!(bool)Get(tile,"m_DetailShadowSelectionReady"))continue;
                var groups=(IList)Get(tile,"m_DetailShadowGroups");int count=(int)Get(tile,"m_DetailShadowCount");
                var shadow=((IEnumerable)Get(tile,"m_DetailShadowIndices")).Cast<int>().Take(count).ToArray();
                if(!tile.IsIndirectDensityDetailActive){var visible=((IEnumerable)Get(tile,"m_DetailBrgSequentialVisibleIndices")).Cast<int>().Take((int)Get(tile,"m_DetailBrgVisibleCount")).ToHashSet();Check(shadow.All(visible.Contains),"Live shadow index outside camera visibility.");}
                uint end=0;foreach(var group in groups){Check((uint)Get(group,"visibleOffset")==end,"Shadow groups must have contiguous offsets.");end+=(uint)Get(group,"visibleCount");}
                Check(end==count,"Shadow group counts must match native index length.");checkedTiles++;indices+=count;
            }
            if(checkedTiles==0)throw new Exception("No live shadow selection to validate.");
            File.AppendAllText(Path.Combine(Path.GetTempPath(),"mg-grass-runtime","live-shadow-validation.txt"),"PASS: "+checkedTiles+" live tiles, "+indices+" shadow indices, valid group offsets and camera subset.\n");
        }
    }
}
#endif

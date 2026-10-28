using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace MashBoxSDK.Culling
{
    // Host games provide equipped garments and target renderers after binding bones.
    // Only triangle indices are changed on owned copies; skin weights/materials stay intact.
    public sealed class ClothingCullingController : MonoBehaviour
    {
        public struct Target { public SkinnedMeshRenderer renderer; public CullingRegion region; }
        public struct Garment { public GameObject owner; public ClothingCullingProfile profile; }
        sealed class State
        {
            public Target target;
            public Mesh original,copy;
            public string[] boneNames;
            public int[][] submeshes;
            public bool[] hidden,applied;
            public readonly List<int> kept=new List<int>();
            public readonly Dictionary<ClothingCullingSurface,ClothingCullingMap> maps=new Dictionary<ClothingCullingSurface,ClothingCullingMap>();
        }
        static readonly ProfilerMarker ConfigureMarker=new ProfilerMarker("SDK.Culling.Configure");
        static readonly ProfilerMarker RefreshMarker=new ProfilerMarker("SDK.Culling.Refresh");
        static readonly ProfilerMarker MapMarker=new ProfilerMarker("SDK.Culling.BuildMap");
        static readonly ProfilerMarker ApplyMarker=new ProfilerMarker("SDK.Culling.ApplyTriangles");
        readonly List<State> states=new List<State>();
        Garment[] garments=Array.Empty<Garment>();Target[] savedTargets=Array.Empty<Target>();int activeHash;

        public void Configure(Target[] targets,Garment[] clothing)
        {
            using(ConfigureMarker.Auto())
            {
                savedTargets=targets ?? Array.Empty<Target>();garments=clothing ?? Array.Empty<Garment>();
                // Changing a shirt must not discard the body's expensive bind-space maps.
                for(int i=states.Count-1;i>=0;i--)
                {
                    var state=states[i];bool retain=false;
                    foreach(var target in savedTargets)
                        if(target.renderer && target.renderer==state.target.renderer && target.region==state.target.region &&
                            (target.renderer.sharedMesh==state.copy || target.renderer.sharedMesh==state.original))
                        {retain=true;break;}
                    if(!retain){Release(state);states.RemoveAt(i);}
                }
                foreach(var target in savedTargets)
                {
                    if(!target.renderer || !target.renderer.sharedMesh)continue;
                    State state=null;
                    foreach(var candidate in states)if(candidate.target.renderer==target.renderer){state=candidate;break;}
                    if(state!=null){target.renderer.sharedMesh=state.copy;continue;}
                    var original=target.renderer.sharedMesh;
                    if(!original.isReadable){Debug.LogWarning("Body culling needs Read/Write enabled: "+original.name,target.renderer);continue;}
                    var bones=target.renderer.bones;var names=new string[bones.Length];
                    for(int b=0;b<bones.Length;b++)names[b]=bones[b]?bones[b].name:"";
                    var submeshes=new int[original.subMeshCount][];int count=0,maxIndices=0;
                    for(int s=0;s<submeshes.Length;s++){submeshes[s]=original.GetTriangles(s);count+=submeshes[s].Length/3;maxIndices=Math.Max(maxIndices,submeshes[s].Length);}
                    var copy=Instantiate(original);copy.name=original.name+" (SDK culled)";
                    state=new State{target=target,original=original,copy=copy,boneNames=names,submeshes=submeshes,hidden=new bool[count],applied=new bool[count]};
                    state.kept.Capacity=maxIndices;
                    states.Add(state);target.renderer.sharedMesh=copy;
                }
                Refresh();
            }
        }
        int ActiveHash(){unchecked{int hash=17;foreach(var g in garments)hash=hash*31+(g.owner && g.owner.activeInHierarchy && g.profile ? g.profile.GetInstanceID():0);return hash;}}
        void LateUpdate(){if(activeHash!=ActiveHash())Refresh();}
        public void Refresh()
        {
            using(RefreshMarker.Auto())
            {
                activeHash=ActiveHash();
                foreach(var state in states)
                {
                    if(!state.target.renderer || state.target.renderer.sharedMesh!=state.copy)continue;
                    Array.Clear(state.hidden,0,state.hidden.Length);
                    foreach(var garment in garments)
                    {
                        if(!garment.owner || !garment.owner.activeInHierarchy || !garment.profile)continue;
                        foreach(var mask in garment.profile.masks)
                        {
                            if(mask==null || !mask.surface || mask.surface.region!=state.target.region)continue;
                            // Presets generally contain an empty Bust mask. Do not build a map for it.
                            bool any=false;
                            if(mask.hidden!=null)for(int i=0;i<mask.hidden.Length;i++)if(mask.hidden[i]!=0){any=true;break;}
                            if(!any)continue;
                            try
                            {
                                if(!state.maps.TryGetValue(mask.surface,out var map))
                                {
                                    using(MapMarker.Auto())map=new ClothingCullingMap(mask.surface,state.original,state.boneNames);
                                    state.maps[mask.surface]=map;
                                }
                                map.UnionInto(mask,state.hidden);
                            }
                            catch(Exception e){Debug.LogWarning("Skipped incompatible culling mask: "+e.Message,state.target.renderer);}
                        }
                    }
                    bool changed=false;
                    for(int i=0;i<state.hidden.Length;i++)if(state.hidden[i]!=state.applied[i]){changed=true;break;}
                    if(!changed)continue;
                    using(ApplyMarker.Auto())
                    {
                        int offset=0;
                        for(int s=0;s<state.submeshes.Length;s++)
                        {
                            var source=state.submeshes[s];state.kept.Clear();
                            for(int i=0;i<source.Length;i+=3,offset++)
                                if(!state.hidden[offset]){state.kept.Add(source[i]);state.kept.Add(source[i+1]);state.kept.Add(source[i+2]);}
                            state.copy.SetTriangles(state.kept,s,false);
                        }
                        state.copy.bounds=state.original.bounds;
                        Array.Copy(state.hidden,state.applied,state.hidden.Length);
                    }
                }
            }
        }
        void Release(State state)
        {
            if(state.target.renderer && state.target.renderer.sharedMesh==state.copy)state.target.renderer.sharedMesh=state.original;
            if(state.copy){if(Application.isPlaying)Destroy(state.copy);else DestroyImmediate(state.copy);}
        }
        public void Restore()
        {
            foreach(var state in states)Release(state);
            states.Clear();
        }
        void OnEnable(){if(savedTargets.Length>0)Configure(savedTargets,garments);}
        void OnDisable()=>Restore();
        void OnDestroy()=>Restore();
    }
}

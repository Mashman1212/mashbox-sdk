using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Weight-only pelvis correction. Never moves points or changes bind poses.
    internal static class StudioCrotchWeights
    {
        internal static int Correct(StudioSkinWeights skin,Vector3[] points,int[][] neighbours,
            Vector3 center,Vector3 up,Vector3 across,float width,float length,bool[] legs,int hips,bool[] locked,bool strong)
        {
            if(locked[hips] || width<=0 || length<=0) return 0;
            var original=skin.Values.Select(v=>v.ToArray()).ToArray();
            var masks=new float[points.Length];
            var caps=new float[points.Length];
            for(int v=0;v<points.Length;v++)
            {
                var delta=points[v]-center;
                float side=Mathf.Abs(Vector3.Dot(delta,across))/width;
                float below=-Vector3.Dot(delta,up)/length;
                // The narrow central saddle belongs to the pelvis. Fade out
                // before the outer thighs and down each leg to avoid saggy legs.
                masks[v]=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.2f,strong ? 1.1f : .95f,side))) *
                    Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.45f,-.08f,below)) *
                    (1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,strong ? .65f : .5f,below)));
                caps[v]=Mathf.Lerp(strong ? .08f : .3f,1,1-masks[v]);
                Cap(skin.Values[v],caps[v],legs,hips,locked);
            }
            var corrected=skin.Values.Select(v=>v.ToArray()).ToArray();
            // Smooth only the unlocked leg/hips budget along garment edges,
            // using snapshots so results do not depend on vertex order. The
            // caller supplies seam-aware adjacency (UV splits stay together).
            for(int pass=0;pass<3;pass++)
            {
                var before=skin.Values.Select(v=>v.ToArray()).ToArray();
                for(int v=0;v<points.Length;v++)
                {
                    if(masks[v]<.001f || neighbours[v].Length==0) continue;
                    bool Editable(int b) => !locked[b] && (b==hips || legs[b]);
                    float budget=before[v].Where(w=>Editable(w.boneIndex)).Sum(w=>w.weight);
                    if(budget<1e-8f) continue;
                    var average=new Dictionary<int,float>();
                    foreach(int n in neighbours[v]) foreach(var w in before[n]) if(Editable(w.boneIndex))
                        average[w.boneIndex]=(average.TryGetValue(w.boneIndex,out float x)?x:0)+w.weight;
                    float sum=average.Values.Sum(); if(sum<1e-8f) continue;
                    var result=before[v].Where(w=>!Editable(w.boneIndex)).ToList();
                    var keys=before[v].Where(w=>Editable(w.boneIndex)).Select(w=>w.boneIndex).Union(average.Keys);
                    var values=new Dictionary<int,float>();
                    foreach(int bone in keys)
                    {
                        float old=before[v].Where(w=>w.boneIndex==bone).Sum(w=>w.weight);
                        float start=corrected[v].Where(w=>w.boneIndex==bone).Sum(w=>w.weight);
                        float target=average.TryGetValue(bone,out float x)?x/sum*budget:0;
                        float limit=(strong ? .1f : .05f)*masks[v];
                        values[bone]=Mathf.Clamp(Mathf.Lerp(old,target,.3f*masks[v]),Mathf.Max(0,start-limit),start+limit);
                    }
                    float total=values.Values.Sum(); if(total<1e-8f) continue;
                    foreach(var pair in values) if(pair.Value>0) result.Add(new BoneWeight1{boneIndex=pair.Key,weight=pair.Value/total*budget});
                    Cap(result,caps[v],legs,hips,locked);
                    skin.Values[v].Clear();skin.Values[v].AddRange(result);
                }
            }
            int changed=0;
            for(int v=0;v<points.Length;v++)
            {
                float delta=original[v].Sum(w=>Mathf.Abs(w.weight-skin.Weight(v,w.boneIndex)))+
                    skin.Values[v].Where(w=>!original[v].Any(o=>o.boneIndex==w.boneIndex)).Sum(w=>w.weight);
                if(delta>1e-6f) changed++;
            }
            return changed;
        }

        private static void Cap(List<BoneWeight1> list,float cap,bool[] legs,int hips,bool[] locked)
        {
            float total=0,available=0;
            foreach(var w in list) if(legs[w.boneIndex]) {total+=w.weight;if(!locked[w.boneIndex])available+=w.weight;}
            float remove=Mathf.Min(available,Mathf.Max(0,total-cap));
            if(remove<1e-7f || available<1e-7f) return;
            for(int i=0;i<list.Count;i++) if(legs[list[i].boneIndex]&&!locked[list[i].boneIndex])
            {var w=list[i];w.weight*=1-remove/available;list[i]=w;}
            int h=list.FindIndex(w=>w.boneIndex==hips);
            if(h>=0) {var w=list[h];w.weight+=remove;list[h]=w;}
            else list.Add(new BoneWeight1{boneIndex=hips,weight=remove});
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private void SmoothArmpitTransition(StudioSkinWeights skin,Vector3[] posed,float[] region,Vector3 center,Vector3 up,Vector3 across,float width,float height,bool[] arm,int chest,bool automatic = false)
        {
            var original=skin.Values.Select(v=>v.ToArray()).ToArray();
            var changed=new HashSet<int>();
            var masks=new float[posed.Length];
            for(int v=0;v<posed.Length;v++)
            {
                var offset=posed[v]-center;
                float below=-Vector3.Dot(offset,up)/height,side=Mathf.Abs(Vector3.Dot(offset,across))/width;
                masks[v]=4*region[v]*(1-region[v]) * Mathf.SmoothStep(0,1,Mathf.InverseLerp(.015f,.12f,below)) *
                    (1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.5f,.75f,below))) *
                    Mathf.SmoothStep(0,1,Mathf.InverseLerp(.45f,.7f,side)) *
                    (1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.3f,1.6f,side)));
            }
            // Jacobi passes use immutable snapshots, avoiding vertex-order bias.
            // Only arm/chest exchange changes; other and locked weights stay exact.
            for(int pass=0;pass<3;pass++)
            {
                var before=skin.Values.Select(v=>v.ToArray()).ToArray();
                for(int v=0;v<posed.Length;v++)
                {
                    float mask=masks[v]; if(mask<.001f || neighbours[v].Length==0) continue;
                    var average=new Dictionary<int,float>(); float count=0;
                    foreach(int n in neighbours[v])
                    {
                        count++;
                        foreach(var w in before[n]) if(arm[w.boneIndex] && !locked[w.boneIndex])
                            average[w.boneIndex]=(average.TryGetValue(w.boneIndex,out float x)?x:0)+w.weight;
                    }
                    var current=before[v];
                    float initialArm=original[v].Where(w=>arm[w.boneIndex] && !locked[w.boneIndex]).Sum(w=>w.weight);
                    float oldArm=current.Where(w=>arm[w.boneIndex] && !locked[w.boneIndex]).Sum(w=>w.weight);
                    float oldChest=current.Where(w=>w.boneIndex==chest).Sum(w=>w.weight);
                    var next=new Dictionary<int,float>(); float alpha=.35f*mask;
                    foreach(var w in current) if(arm[w.boneIndex] && !locked[w.boneIndex]) next[w.boneIndex]=w.weight*(1-alpha);
                    foreach(var pair in average) next[pair.Key]=(next.TryGetValue(pair.Key,out float x)?x:0)+pair.Value/count*alpha;
                    float sum=next.Values.Sum();
                    // Bound displacement of the established transition: at most six
                    // percentage points of arm weight per action, across all passes.
                    float target=Mathf.Clamp(sum,Mathf.Max(0,initialArm-.06f*mask),initialArm+.06f*mask);
                    target=Mathf.Clamp(target,0,oldArm+oldChest);
                    if(sum<1e-8f) continue;
                    var result=current.Where(w=>w.boneIndex!=chest && !(arm[w.boneIndex]&&!locked[w.boneIndex])).ToList();
                    foreach(var pair in next) if(pair.Value>1e-8f) result.Add(new BoneWeight1 { boneIndex=pair.Key,weight=pair.Value/sum*target });
                    float remaining=oldArm+oldChest-target;
                    if(remaining>0) result.Add(new BoneWeight1 { boneIndex=chest,weight=remaining });
                    float difference=result.Sum(w=>Mathf.Abs(w.weight-current.Where(c=>c.boneIndex==w.boneIndex).Sum(c=>c.weight)))+
                        current.Where(w=>!result.Any(r=>r.boneIndex==w.boneIndex)).Sum(w=>w.weight);
                    if(difference<1e-6f) continue;
                    skin.Values[v].Clear(); skin.Values[v].AddRange(result); changed.Add(v);
                }
            }
            if(changed.Count>0)
            {
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[]{Document,Part.mesh},"Smooth armpit weights");
                skin.Apply(Part.mesh); Rebuild(); Changed();
            }
            error=null;
            if(!automatic)RecordSkinTutorialAction("armpitSmooth");
            if(!automatic) lastView?.ShowNotification(new GUIContent(changed.Count>0 ? $"Smoothed armpit weights on {changed.Count:N0} vertices" : "No armpit weight transition needed smoothing"));
        }
    }
}

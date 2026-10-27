using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        internal bool CanRepairArmpits => HasEditableMesh && Part.bound && Part.role == StudioSkinningDocument.MeshRole.Clothing;
        internal void ReduceArmpitTenting() => ReduceArmpitTenting(false);
        internal void ReduceArmpitTentingStrong() => ReduceArmpitTenting(true);
        internal void SmoothArmpitWeights() => ReduceArmpitTenting(false,true);
        private void ReduceArmpitTenting(bool strong, bool smoothOnly = false, bool automatic = false)
        {
            if (!CanRepairArmpits) return;
            EndStroke(); PausePreviewAnimation();
            int Find(string name) => Array.FindIndex(bones,b=>StudioSkinningDocument.BoneName(b.name)==name);
            if(automatic && (!previewClip || previewClip.frameRate<=0 || previewClip.length<2f/previewClip.frameRate)) return;
            int left=Find("LeftArm"),right=Find("RightArm"),hips=Find("Hips");
            if(automatic && (left<0 || right<0 || hips<0)) return;
            if(left<0 || right<0 || hips<0 || !previewClip || previewClip.frameRate<=0 || previewClip.length<2f/previewClip.frameRate)
                throw new InvalidOperationException("Armpit correction requires the SDK human arm/hips joints and A-pose test frame.");
            var chest=bones[left].parent;
            while(chest && !bones[right].IsChildOf(chest)) chest=chest.parent;
            int chestIndex=Array.IndexOf(bones,chest);
            if(automatic && (chestIndex<0 || chest==bones[hips] || locked[chestIndex])) return;
            if(chestIndex<0 || chest==bones[hips]) throw new InvalidOperationException("Could not identify a shared chest joint.");
            if(locked[chestIndex]) throw new InvalidOperationException("Unlock the chest influence before reducing armpit tenting.");
            var positions=bones.Select(b=>b.localPosition).ToArray();
            var rotations=bones.Select(b=>b.localRotation).ToArray();
            var scales=bones.Select(b=>b.localScale).ToArray();
            float time=previewTime;
            try
            {
                JumpToPreviewFrame(2);
                var skin=new StudioSkinWeights(Part.mesh);
                var binds=Part.mesh.bindposes;
                if(binds.Length!=bones.Length) throw new InvalidOperationException("Skin bone mapping does not match the SDK skeleton.");
                var matrices=bones.Select((b,i)=>b.localToWorldMatrix*binds[i]).ToArray();
                var verts=Part.mesh.vertices;
                var shoulderCenter=(bones[left].position+bones[right].position)*.5f;
                var up=(shoulderCenter-bones[hips].position).normalized;
                var across=(bones[right].position-bones[left].position).normalized;
                float halfWidth=Vector3.Distance(bones[left].position,bones[right].position)*.5f;
                float torsoHeight=Vector3.Distance(shoulderCenter,bones[hips].position);
                if(halfWidth<.001f || torsoHeight<.001f) throw new InvalidOperationException("Human reference proportions are invalid.");
                // Include clavicles, upper arms and their descendants. Chest/neck stay untouched.
                var arm=new bool[bones.Length];
                for(int i=0;i<bones.Length;i++) arm[i]=bones[i]==bones[left].parent || bones[i]==bones[right].parent ||
                    bones[i]==bones[left] || bones[i]==bones[right] || bones[i].IsChildOf(bones[left]) || bones[i].IsChildOf(bones[right]);
                arm[chestIndex]=false;
                var posed=new Vector3[verts.Length];
                for(int i=0;i<posed.Length;i++) posed[i]=skin.PreviewSkinMatrix(i,matrices).MultiplyPoint3x4(verts[i]);
                var torsoRegion=BuildArmpitTorsoRegion(posed,verts,shoulderCenter,up,across,halfWidth,torsoHeight,automatic);
                if(smoothOnly)
                {
                    SmoothArmpitTransition(skin,posed,torsoRegion,shoulderCenter,up,across,halfWidth,torsoHeight,arm,chestIndex,automatic);
                    return;
                }
                int changed=0;
                for(int v=0;v<verts.Length;v++)
                {
                    var point=posed[v];
                    var offset=point-shoulderCenter;
                    float below=-Vector3.Dot(offset,up)/torsoHeight;
                    float lateral=Mathf.Abs(Vector3.Dot(offset,across))/halfWidth;
                    // Feather below the armpit and toward the sleeve. No change to the
                    // shoulder cap, outer sleeve, neck, or lower torso.
                    float mask=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.02f,strong ? .13f : .19f,below)) *
                        (1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(strong ? 1f : .85f,strong ? 1.5f : 1.35f,lateral))) *
                        (1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.65f,.95f,below)));
                    if(mask<=0) continue;
                    mask*=torsoRegion[v];
                    if(mask<=0) continue;
                    var list=skin.Values[v]; float totalArm=0,unlockedArm=0;
                    foreach(var w in list) if(arm[w.boneIndex]) { totalArm+=w.weight; if(!locked[w.boneIndex]) unlockedArm+=w.weight; }
                    // A cap rather than repeated smoothing makes repeated clicks stable.
                    // The old linear cap left too much influence in the feathered
                    // side panel. Tighten that transition, retaining a smooth edge.
                    float cap=Mathf.Lerp(strong ? .015f : .05f,1,Mathf.Pow(1-mask,strong ? 3f : 2f));
                    float remove=Mathf.Min(unlockedArm,Mathf.Max(0,totalArm-cap));
                    if(remove<1e-6f || unlockedArm<1e-6f) continue;
                    for(int i=list.Count-1;i>=0;i--)
                    {
                        var w=list[i];
                        if(arm[w.boneIndex] && !locked[w.boneIndex]) { w.weight*=1-remove/unlockedArm; list[i]=w; }
                    }
                    int existing=list.FindIndex(w=>w.boneIndex==chestIndex);
                    if(existing>=0) { var w=list[existing]; w.weight+=remove; list[existing]=w; }
                    else list.Add(new BoneWeight1 { boneIndex=chestIndex,weight=remove });
                    changed++;
                }
                if(changed>0)
                {
                    Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[]{Document,Part.mesh},"Reduce armpit tenting");
                    skin.Apply(Part.mesh); Rebuild(); Changed();
                }
                error=null;
                if(!automatic)RecordSkinTutorialAction("armpit");
                if(!automatic) lastView?.ShowNotification(new GUIContent(changed>0 ? $"Reduced armpit pull on {changed:N0} vertices" : "No excess arm weights found in the torso region"));
            }
            finally
            {
                SamplePreviewAnimation(time,false);
                for(int i=0;i<bones.Length;i++) { bones[i].localPosition=positions[i]; bones[i].localRotation=rotations[i]; bones[i].localScale=scales[i]; }
                RefreshSurface(); repaint();
            }
        }
    }
}

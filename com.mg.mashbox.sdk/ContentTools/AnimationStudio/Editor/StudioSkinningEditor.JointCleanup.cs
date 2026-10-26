using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private void BindWithJointCleanup()
        {
            if (!CanSkinSelected) return;
            EndStroke();
            Undo.IncrementCurrentGroup();
            int group=Undo.GetCurrentGroup();
            try
            {
                TransferBodySkin();
                if (CanRepairArmpits)
                {
                    // Analyze in the test pose, but keep the user's actual bind
                    // matrices and restore their displayed pose after each pass.
                    ReduceArmpitTenting(true,false,true);
                    ReduceArmpitTenting(false,true,true);
                    RepairCrotch(true,true);
                }
                Undo.SetCurrentGroupName("Skin and smooth joint weights");
                RecordSkinTutorialAction("bind");
                lastView?.ShowNotification(new GUIContent("Skinned from body and bust with joint cleanup"));
            }
            catch
            {
                // Binding and automatic cleanup are one transaction.
                Undo.RevertAllDownToGroup(group);
                Rebuild();
                throw;
            }
            finally { Undo.CollapseUndoOperations(group); }
        }

        internal void FixCrotchWeights() => RepairCrotch(false);
        internal void FixCrotchWeightsStrong() => RepairCrotch(true);

        private void RepairCrotch(bool strong,bool automatic=false)
        {
            if (!CanRepairArmpits) return;
            EndStroke(); PausePreviewAnimation();
            int Find(string name) => Array.FindIndex(bones,b=>StudioSkinningDocument.BoneName(b.name)==name);
            int hips=Find("Hips"),left=Find("LeftUpLeg"),right=Find("RightUpLeg"),lk=Find("LeftLeg"),rk=Find("RightLeg");
            if(hips<0 || left<0 || right<0 || lk<0 || rk<0)
            {
                if(automatic) return;
                throw new InvalidOperationException("Crotch correction needs the SDK hips, upper legs and knees.");
            }
            if(locked[hips])
            {
                if(automatic) return;
                throw new InvalidOperationException("Unlock the hips influence before correcting crotch weights.");
            }
            var positions=bones.Select(b=>b.localPosition).ToArray();
            var rotations=bones.Select(b=>b.localRotation).ToArray();
            var scales=bones.Select(b=>b.localScale).ToArray();
            float time=previewTime;
            try
            {
                // Use a neutral pose for consistent anatomical bounds. Without
                // the test clip use the current pose; binding still works offline.
                if(previewClip && previewClip.frameRate>0 && previewClip.length>=2f/previewClip.frameRate) JumpToPreviewFrame(2);
                var skin=new StudioSkinWeights(Part.mesh);
                var binds=Part.mesh.bindposes;
                if(binds.Length!=bones.Length) throw new InvalidOperationException("Skin bone mapping does not match the SDK skeleton.");
                var matrices=bones.Select((b,i)=>b.localToWorldMatrix*binds[i]).ToArray();
                var points=Part.mesh.vertices;
                for(int i=0;i<points.Length;i++) points[i]=skin.SkinMatrix(i,matrices).MultiplyPoint3x4(points[i]);
                var center=(bones[left].position+bones[right].position)*.5f;
                var across=(bones[right].position-bones[left].position).normalized;
                var up=(center-(bones[lk].position+bones[rk].position)*.5f).normalized;
                up=Vector3.ProjectOnPlane(up,across).normalized;
                float width=Vector3.Distance(bones[left].position,bones[right].position)*.5f;
                float length=(Vector3.Distance(bones[left].position,bones[lk].position)+Vector3.Distance(bones[right].position,bones[rk].position))*.5f;
                if(width<.001f || length<.001f || up.sqrMagnitude<.5f) return;
                var legs=new bool[bones.Length];
                for(int i=0;i<bones.Length;i++) legs[i]=i==left || i==right || bones[i].IsChildOf(bones[left]) || bones[i].IsChildOf(bones[right]);
                int changed=StudioCrotchWeights.Correct(skin,points,neighbours,center,up,across,width,length,legs,hips,locked,strong);
                if(changed>0)
                {
                    Undo.IncrementCurrentGroup();
                    Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[]{Document,Part.mesh},"Fix crotch weights");
                    skin.Apply(Part.mesh); Rebuild(); Changed();
                }
                error=null;
                if(!automatic)RecordSkinTutorialAction(strong?"crotchStrong":"crotchSmall");
                if(!automatic) lastView?.ShowNotification(new GUIContent(changed>0 ? $"Corrected and smoothed crotch weights on {changed:N0} vertices" : "No excess leg pull found in the crotch region"));
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

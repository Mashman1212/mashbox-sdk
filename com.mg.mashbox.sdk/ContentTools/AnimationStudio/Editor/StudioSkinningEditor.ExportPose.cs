using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private Matrix4x4[] CapturePartExportPose(StudioSkinningDocument.Part part)
        {
            // Re-baking blended seam vertices into another bind pose changes
            // their motion relative to the original, separately equipped body.
            if (part.preserveStitchedBindPose)
            {
                var binds = part.mesh.bindposes;
                if (binds.Length != bones.Length)
                    throw new InvalidOperationException("Stitched mesh no longer matches this skeleton. Stitch it again before export.");
                return binds.Select(b => b.inverse).ToArray();
            }
            return CaptureExportTPose();
        }
        private Matrix4x4[] CaptureExportTPose()
        {
            if(!previewClip) throw new InvalidOperationException("T-pose export requires the SDK Skinning Test animation (frame 0).");
            EndStroke();
            var positions=bones.Select(b=>b.localPosition).ToArray();
            var rotations=bones.Select(b=>b.localRotation).ToArray();
            var scales=bones.Select(b=>b.localScale).ToArray();
            float time=previewTime; bool playing=previewPlaying;
            bool animatorEnabled=!previewAnimator || previewAnimator.enabled;
            try
            {
                previewPlaying=false; SamplePreviewAnimation(0,false);
                return bones.Select(b=>b.localToWorldMatrix).ToArray();
            }
            finally
            {
                SamplePreviewAnimation(time,false);
                for(int i=0;i<bones.Length;i++) { bones[i].localPosition=positions[i]; bones[i].localRotation=rotations[i]; bones[i].localScale=scales[i]; }
                if(previewAnimator) previewAnimator.enabled=animatorEnabled;
                previewPlaying=playing; previewLastTick=UnityEditor.EditorApplication.timeSinceStartup;
                RefreshSurface(); repaint();
            }
        }
        private void PoseExportSkeleton(GameObject root,Matrix4x4[] pose)
        {
            foreach(var animator in root.GetComponentsInChildren<Animator>(true)) animator.enabled=false;
            var transforms=root.GetComponentsInChildren<Transform>(true);
            // Hierarchy order ensures parents are posed before children.
            foreach(var target in transforms)
            {
                if(target==root.transform) continue;
                var matching=Enumerable.Range(0,bones.Length).Where(i=>deformBones[i] && StudioSkinningDocument.BoneName(bones[i].name)==StudioSkinningDocument.BoneName(target.name)).ToArray();
                if(matching.Length!=1) continue;
                var world=root.transform.localToWorldMatrix*pose[matching[0]];
                var local=target.parent.worldToLocalMatrix*world;
                target.localPosition=local.GetColumn(3); target.localRotation=local.rotation; target.localScale=local.lossyScale;
            }
        }
        private static void BakeExportGeometry(Mesh mesh,Matrix4x4[] boneMatrices)
        {
            var weights=new StudioSkinWeights(mesh);
            var transforms=new Matrix4x4[mesh.vertexCount];
            for(int v=0;v<transforms.Length;v++) transforms[v]=weights.SkinMatrix(v,boneMatrices);
            TransformExportVertices(mesh, transforms);
        }
        private static void TransformExportVertices(Mesh mesh, Matrix4x4[] transforms)
        {
            var vertices=mesh.vertices; var normals=mesh.normals; var tangents=mesh.tangents;
            var normalMatrices=transforms.Select(m=>m.inverse.transpose).ToArray();
            for(int v=0;v<vertices.Length;v++)
            {
                vertices[v]=transforms[v].MultiplyPoint3x4(vertices[v]);
                if(normals.Length==vertices.Length) normals[v]=normalMatrices[v].MultiplyVector(normals[v]).normalized;
                if(tangents.Length==vertices.Length) { var t=transforms[v].MultiplyVector(tangents[v]).normalized; tangents[v]=new Vector4(t.x,t.y,t.z,tangents[v].w); }
            }
            var shapes=new List<(string name,float weight,Vector3[] v,Vector3[] n,Vector3[] t)>();
            for(int s=0;s<mesh.blendShapeCount;s++) for(int f=0;f<mesh.GetBlendShapeFrameCount(s);f++)
            {
                var v=new Vector3[vertices.Length]; var n=new Vector3[v.Length]; var t=new Vector3[v.Length];
                mesh.GetBlendShapeFrameVertices(s,f,v,n,t);
                for(int i=0;i<v.Length;i++) { v[i]=transforms[i].MultiplyVector(v[i]); n[i]=normalMatrices[i].MultiplyVector(n[i]); t[i]=transforms[i].MultiplyVector(t[i]); }
                shapes.Add((mesh.GetBlendShapeName(s),mesh.GetBlendShapeFrameWeight(s,f),v,n,t));
            }
            mesh.vertices=vertices; mesh.normals=normals; mesh.tangents=tangents;
            mesh.ClearBlendShapes(); foreach(var shape in shapes) mesh.AddBlendShapeFrame(shape.name,shape.weight,shape.v,shape.n,shape.t);
            mesh.RecalculateBounds();
        }
        private void BakePartForTPose(Mesh copy,Matrix4x4[] pose)
        {
            var weights = new StudioSkinWeights(copy);
            weights.LimitToFour(); weights.Apply(copy);
            var bind=copy.bindposes;
            if(bind.Length!=bones.Length) throw new InvalidOperationException("T-pose export requires a mesh bound to the SDK skeleton.");
            BakeExportGeometry(copy,pose.Select((m,i)=>m*bind[i]).ToArray());
            copy.bindposes=pose.Select(m=>m.inverse).ToArray();
        }
        private static void ValidateExportRestPose(Mesh mesh,SkinnedMeshRenderer renderer)
        {
            var binds=mesh.bindposes;
            if(binds.Length!=renderer.bones.Length) throw new InvalidOperationException("Exported skeleton and bind poses do not match.");
            var matrices=renderer.bones.Select((b,i)=>renderer.transform.worldToLocalMatrix*b.localToWorldMatrix*binds[i]).ToArray();
            var weights=new StudioSkinWeights(mesh); var vertices=mesh.vertices;
            float tolerance=Mathf.Max(.0001f,mesh.bounds.size.magnitude*.0001f);
            for(int v=0;v<vertices.Length;v++)
                if((weights.SkinMatrix(v,matrices).MultiplyPoint3x4(vertices[v])-vertices[v]).sqrMagnitude>tolerance*tolerance)
                    throw new InvalidOperationException("The FBX round trip did not retain the exported T-pose bind. No replacement will be kept.");
        }
    }
}

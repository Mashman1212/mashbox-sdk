using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        internal void ImportSkinning(StudioSkinningDocument.Part source,string[] names,Matrix4x4[] rest)
        {
            if(source==null || !source.mesh)return;
            var reference=StudioSkinningDocument.CreateEditableCopy(source.mesh);
            bool added=false;
            try
            {
                reference.name=source.name+" Reference";
                var part=new StudioRetopologyDocument.Part {name=source.name,reference=reference,materials=source.materials==null?null:(Material[])source.materials.Clone()};
                if(source.bound)
                {
                    var binds=reference.bindposes;
                    if(binds.Length!=rest.Length || names.Length!=rest.Length)
                        throw new InvalidOperationException("The skin's bind matrices do not match its skeleton. Nothing was sent.");
                    part.skinBoneNames=(string[])names.Clone();
                    part.skinBindCorrections=Enumerable.Range(0,rest.Length).Select(i=>rest[i]*binds[i]).ToArray();
                }
                else {reference.boneWeights=Array.Empty<BoneWeight>();reference.bindposes=Array.Empty<Matrix4x4>();}
                EndStripStroke();EndRelaxBrush();CancelResolution();CancelVertexSelection();ClearComponents();ClearPatchCorners();
                Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,"Send skinning mesh to Retopology");
                Document.parts.Add(part);Document.selected=Document.parts.Count-1;added=true;
                Document.dirty=true;EditorUtility.SetDirty(Document);Rebuild();Frame();
                status="Mesh copied from Skinning. Draw new topology, or use Copy reference to edit.";
            }
            finally {if(!added)UnityEngine.Object.DestroyImmediate(reference);}
        }

        private static void CaptureReferenceSkin(StudioRetopologyDocument.Part part, SkinnedMeshRenderer skin, Transform root, Matrix4x4 meshToWorkspace)
        {
            var sourceBones=skin.bones; var binds=part.reference.bindposes;
            part.skinBoneNames=sourceBones.Select(b=>b?b.name:null).ToArray();
            part.skinBindCorrections=new Matrix4x4[sourceBones.Length];
            for(int i=0;i<sourceBones.Length;i++)
                if(sourceBones[i] && i<binds.Length)
                    part.skinBindCorrections[i]=root.worldToLocalMatrix*sourceBones[i].localToWorldMatrix*binds[i]*meshToWorkspace.inverse;
        }

        internal static StudioRetopologyDocument.Part ReferenceSkin(StudioRetopologyDocument.Part part)
        {
            if(part.skinBoneNames!=null && part.skinBindCorrections!=null) return part;
            // Older workspaces predate the bone snapshot. Recover only an unambiguous
            // renderer whose complete transformed reference geometry still matches.
            var matches=new List<StudioRetopologyDocument.Part>();
            foreach(string guid in AssetDatabase.FindAssets("t:Model"))
            {
                var root=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if(!root)continue;
                foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if(skin.name!=part.name || !skin.sharedMesh || skin.sharedMesh.vertexCount!=part.reference.vertexCount)continue;
                    var copy=StudioSkinningDocument.CreateEditableCopy(skin.sharedMesh);
                    try
                    {
                        var matrix=root.transform.worldToLocalMatrix*skin.transform.localToWorldMatrix;
                        var points=copy.vertices;var expected=part.reference.vertices;
                        if(points.Where((v,i)=>(matrix.MultiplyPoint3x4(v)-expected[i]).sqrMagnitude>1e-10f).Any())continue;
                        var candidate=new StudioRetopologyDocument.Part {reference=copy};
                        CaptureReferenceSkin(candidate,skin,root.transform,matrix);matches.Add(candidate);
                    }
                    finally {UnityEngine.Object.DestroyImmediate(copy);}
                }
            }
            if(matches.Count!=1)throw new InvalidOperationException("This older reference has skin weights but no unique source skeleton. Reimport its reference to capture the skin, or send an unskinned reference instead. Your topology is unchanged.");
            return matches[0];
        }
    }

    internal sealed partial class StudioSkinningEditor
    {
        internal Action<StudioSkinningDocument.Part,string[],Matrix4x4[]> SendToRetopology;
        internal void SendSelectedToRetopology()
        {
            if(!HasEditableMesh || SendToRetopology==null)return;
            EndStroke();FinishMeshRename(false);CancelBrushInteraction();
            SendToRetopology(Part,bones.Select(b=>b.name).ToArray(),rest);
        }

        internal void ImportRetopology(StudioRetopologyDocument.Part source)
        {
            if(source==null || !source.reference || source.faces.Count==0)return;
            EndStroke();
            var mesh=StudioRetopologyDocument.BuildMesh(source);
            bool added=false;
            try
            {
                var weights=new StudioSkinWeights(source.reference);
                bool bound=weights.HasWeights;
                if(bound)
                {
                    var skin=StudioRetopologyEditor.ReferenceSkin(source);
                    var map=skin.skinBoneNames.Select(name=>
                    {
                        var matching=Enumerable.Range(0,bones.Length).Where(i=>!string.IsNullOrEmpty(name) && StudioSkinningDocument.BoneName(bones[i].name)==StudioSkinningDocument.BoneName(name)).ToArray();
                        return matching.Length==1?matching[0]:-1;
                    }).ToArray();
                    foreach(var list in weights.Values)foreach(var w in list)
                        if(w.boneIndex<0 || w.boneIndex>=map.Length || map[w.boneIndex]<0 || w.boneIndex>=skin.skinBindCorrections.Length)
                            throw new InvalidOperationException("The reference skin contains a bone that cannot be uniquely matched to the SDK Skeleton. Nothing was transferred.");
                    weights.Remap(map);
                    TransferRetopologyWeights(source.reference,weights,mesh);
                    var binds=rest.Select(r=>r.inverse).ToArray();
                    for(int i=0;i<map.Length;i++)if(map[i]>=0)binds[map[i]]=rest[map[i]].inverse*skin.skinBindCorrections[i];
                    mesh.bindposes=binds;
                }
                var part=new StudioSkinningDocument.Part { name=source.name+" Low",mesh=mesh,materials=source.materials, bound=bound };
                Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,"Send retopology to Skinner");
                if(EditorUtility.IsPersistent(Document)){mesh.hideFlags=HideFlags.None;AssetDatabase.AddObjectToAsset(mesh,Document);}
                Document.parts.Add(part);Document.selected=Document.parts.Count-1;added=true;
                Rebuild();ChooseImportedMeshTool();Changed();
            }
            finally {if(!added)UnityEngine.Object.DestroyImmediate(mesh);}
        }

        private static void TransferRetopologyWeights(Mesh reference,StudioSkinWeights weights,Mesh target)
        {
            var points=reference.vertices;var indices=reference.triangles;
            var triangles=new List<FitBodyTriangle>();
            for(int i=0;i<indices.Length;i+=3)
            {
                int a=indices[i],b=indices[i+1],c=indices[i+2];
                if(weights.Values[a].Count==0 || weights.Values[b].Count==0 || weights.Values[c].Count==0)continue;
                var t=new FitBodyTriangle {a=points[a],b=points[b],c=points[c],wa=weights.Values[a],wb=weights.Values[b],wc=weights.Values[c]};
                if(Vector3.Cross(t.b-t.a,t.c-t.a).sqrMagnitude<1e-16f)continue;
                t.bounds=new Bounds(t.a,Vector3.zero);t.bounds.Encapsulate(t.b);t.bounds.Encapsulate(t.c);triangles.Add(t);
            }
            if(triangles.Count==0)throw new InvalidOperationException("The reference has no usable skinned triangles to transfer from.");
            var surface=new FitBodySurface(triangles.ToArray());var result=new StudioSkinWeights(target);
            var cache=new Dictionary<Vector3,BoneWeight1[]>();var vertices=target.vertices;
            for(int i=0;i<vertices.Length;i++)
            {
                if(!cache.TryGetValue(vertices[i],out var values)){values=surface.InterpolatedWeights(vertices[i]);cache.Add(vertices[i],values);}
                result.Values[i].AddRange(values);
            }
            result.Apply(target);
        }
    }
}

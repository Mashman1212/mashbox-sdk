using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private FitBodySurface CreateBodyWeightSurface()
        {
            var triangles = new List<FitBodyTriangle>();
            for (int i=0;i<Document.parts.Count;i++)
            {
                var source=Document.parts[i];
                if (source==Part || !source.bound || !source.mesh ||
                    (source.role!=StudioSkinningDocument.MeshRole.Body && source.role!=StudioSkinningDocument.MeshRole.Bust)) continue;
                var renderer=objects[i].GetComponent<SkinnedMeshRenderer>();
                if (!renderer) continue;
                var sourceWeights=new StudioSkinWeights(source.mesh);
                var baked=new Mesh();
                try
                {
                    renderer.BakeMesh(baked);
                    var points=baked.vertices; var indices=baked.triangles;
                    var matrix=renderer.transform.localToWorldMatrix;
                    for(int k=0;k<indices.Length;k+=3)
                    {
                        int a=indices[k],b=indices[k+1],c=indices[k+2];
                        bool Valid(int v) => sourceWeights.Values[v].Count>0 && sourceWeights.Values[v].Sum(w=>w.weight)>1e-8f && sourceWeights.Values[v].All(w=>w.boneIndex>=0 && w.boneIndex<bones.Length && w.weight>=0 && !float.IsNaN(w.weight) && !float.IsInfinity(w.weight));
                        if(!Valid(a)||!Valid(b)||!Valid(c)) continue;
                        var t=new FitBodyTriangle { a=matrix.MultiplyPoint3x4(points[a]), b=matrix.MultiplyPoint3x4(points[b]), c=matrix.MultiplyPoint3x4(points[c]),
                            wa=sourceWeights.Values[a],wb=sourceWeights.Values[b],wc=sourceWeights.Values[c] };
                        if(Vector3.Cross(t.b-t.a,t.c-t.a).sqrMagnitude<1e-16f) continue;
                        t.bounds=new Bounds(t.a,Vector3.zero); t.bounds.Encapsulate(t.b); t.bounds.Encapsulate(t.c); triangles.Add(t);
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(baked); }
            }
            if(triangles.Count==0)
                throw new InvalidOperationException("Skin needs a skinned Body or Bust reference. Configure the default body/bust or mark a skinned mesh as Body or Bust.");
            return new FitBodySurface(triangles.ToArray());
        }
        private void TransferBodySkin()
        {
            EndStroke();
            if (Part == null || IsReferencePart || Part.role == StudioSkinningDocument.MeshRole.Headgear) return;
            // Freeze the displayed pose without resampling or changing the timeline.
            // Body transfer and the new bind matrices must use the same pose.
            PausePreviewAnimation();
            var bindPoses=bones.Select(b=>b.worldToLocalMatrix).ToArray();
            var surface=CreateBodyWeightSurface();
            var transferred=new StudioSkinWeights(Part.mesh);
            var positions=Part.mesh.vertices;
            // Identical positions at UV/normal seams receive exactly identical weights.
            var cache=new Dictionary<Vector3,BoneWeight1[]>();
            for(int v=0;v<positions.Length;v++)
            {
                if(!cache.TryGetValue(positions[v],out var values))
                {
                    values=surface.InterpolatedWeights(positions[v]);
                    cache.Add(positions[v],values);
                }
                transferred.Values[v].Clear(); transferred.Values[v].AddRange(values);
            }
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[]{Document,Part.mesh},"Skin from body and bust");
            transferred.Apply(Part.mesh);
            Part.mesh.bindposes=bindPoses;
            Part.bound=true; error=null; Rebuild(); Changed();
        }
    }
}

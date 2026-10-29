using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioRetopologyWeldCleanup
    {
        internal static int Apply(Part part,Dictionary<int,float> weights,float fraction)
        {
            var edges=StudioRetopologyOperations.Edges(part);
            var borders=new HashSet<int>(edges.Where(e=>e.Value.Count==1).SelectMany(e=>new[]{e.Key.Item1,e.Key.Item2}));
            var lengths=new Dictionary<int,float>();
            foreach(var edge in edges.Keys)
            {
                float length=Vector3.Distance(part.vertices[edge.Item1],part.vertices[edge.Item2]);
                if(length<=1e-8f)continue;
                foreach(int id in new[]{edge.Item1,edge.Item2})if(!lengths.ContainsKey(id)||length<lengths[id])lengths[id]=length;
            }
            var ids=borders.Where(i=>weights.TryGetValue(i,out float weight)&&weight>.05f&&lengths.ContainsKey(i)).OrderBy(i=>i).ToArray();
            var used=new HashSet<int>();int changes=0;
            foreach(int a in ids)
            {
                if(used.Contains(a))continue;
                int best=-1;float distance=float.PositiveInfinity;
                foreach(int b in ids)
                {
                    if(b<=a||used.Contains(b))continue;
                    float limit=Mathf.Min(lengths[a],lengths[b])*Mathf.Clamp(fraction,.001f,.5f);
                    float d=(part.vertices[a]-part.vertices[b]).sqrMagnitude;
                    if(d<=limit*limit&&d<distance&&!part.faces.Any(f=>f.vertices.Contains(a)&&f.vertices.Contains(b))) {best=b;distance=d;}
                }
                if(best<0)continue;
                var faces=new List<Face>();var signatures=new HashSet<string>();bool valid=true;
                foreach(var f in part.faces)
                {
                    var mapped=f.vertices.Select(i=>i==best?a:i).ToArray();
                    if(mapped.Distinct().Count()!=mapped.Length||!signatures.Add(string.Join(",",mapped.OrderBy(i=>i)))){valid=false;break;}
                    // Do not flip or collapse a neighboring face while sealing a seam.
                    Vector3 Normal(int[] v){var n=Vector3.zero;for(int i=1;i+1<v.Length;i++)n+=Vector3.Cross(part.vertices[v[i]]-part.vertices[v[0]],part.vertices[v[i+1]]-part.vertices[v[0]]);return n;}
                    var before=Normal(f.vertices);var after=Normal(mapped);
                    if(mapped.Contains(a)&&f.vertices.Contains(best)&&(after.sqrMagnitude<1e-16f||Vector3.Dot(before.normalized,after.normalized)<.5f)){valid=false;break;}
                    faces.Add(new Face(mapped,f.material){cornerUV=f.cornerUV==null?null:(Vector2[])f.cornerUV.Clone()});
                }
                if(!valid||StudioRetopologyOperations.Edges(new Part{faces=faces}).Any(e=>e.Value.Count>2))continue;
                // Face UVs remain independent of the welded geometric vertex.
                for(int f=0;f<faces.Count;f++)if(faces[f].cornerUV==null)
                    faces[f].cornerUV=Enumerable.Range(0,part.faces[f].vertices.Length).Select(c=>StudioRetopologyDocument.FaceUV(part,part.faces[f],c)).ToArray();
                part.faces=faces;used.Add(a);used.Add(best);changes++;
            }
            if(changes>0)StudioRetopologyOperations.Compact(part);
            return changes;
        }
    }
}

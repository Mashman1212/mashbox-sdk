using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part = MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face = MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioRetopologyQuadCleanup
    {
        internal static int Apply(Part part, HashSet<int> region, Dictionary<int,float> weights)
        {
            // Work on whole faces, never split the perimeter or touch authored UV seams.
            var inside = region.Where(i => i >= 0 && i < part.faces.Count &&
                (part.faces[i].cornerUV == null || part.faces[i].cornerUV.Length == 0) &&
                part.faces[i].vertices.All(v => weights.ContainsKey(v))).ToHashSet();
            if (inside.Count == 0) { weights.Clear(); return 0; }
            var local = new Part { vertices = part.vertices, faces = inside.OrderBy(i=>i).Select(i=>part.faces[i]).ToList() };
            var outsideVertices = part.faces.Where((f,i)=>!inside.Contains(i)).SelectMany(f=>f.vertices).ToHashSet();
            var boundary = StudioRetopologyOperations.Edges(local).Where(e=>e.Value.Count!=2).SelectMany(e=>new[]{e.Key.Item1,e.Key.Item2}).ToHashSet();
            foreach(int vertex in weights.Keys.ToArray())
                if(outsideVertices.Contains(vertex) || boundary.Contains(vertex) || !local.faces.Any(f=>f.vertices.Contains(vertex))) weights.Remove(vertex);
            var globalEdges = StudioRetopologyOperations.Edges(part);
            int changes = StudioRetopologyOperations.PairQuads(local);
            // A small bounded number of flips per dab avoids wholesale remeshing.
            for(int pass=0;pass<4;pass++)
            {
                bool flipped=false;
                var edges=StudioRetopologyOperations.Edges(local);
                foreach(var edge in edges)
                {
                    if(edge.Value.Count!=2)continue;
                    int ia=edge.Value[0],ib=edge.Value[1];var a=local.faces[ia];var b=local.faces[ib];
                    if(a.vertices.Length!=4 || b.vertices.Length!=4 || a.material!=b.material)continue;
                    var next=new Dictionary<int,int>();bool invalid=false;
                    foreach(var face in new[]{a,b})for(int j=0;j<4;j++)
                    {
                        int x=face.vertices[j],y=face.vertices[(j+1)%4];
                        if(StudioRetopologyOperations.Edge(x,y)==edge.Key)continue;
                        if(next.ContainsKey(x)){invalid=true;break;}next[x]=y;
                    }
                    if(invalid||next.Count!=6)continue;
                    var loop=new int[6];loop[0]=next.Keys.Min();
                    for(int j=1;j<6;j++){if(!next.TryGetValue(loop[j-1],out loop[j])){invalid=true;break;}}
                    if(invalid||loop.Distinct().Count()!=6||!next.TryGetValue(loop[5],out int end)||end!=loop[0])continue;
                    var normal=Vector3.Cross(part.vertices[a.vertices[1]]-part.vertices[a.vertices[0]],part.vertices[a.vertices[2]]-part.vertices[a.vertices[0]]).normalized;
                    var other=Vector3.Cross(part.vertices[b.vertices[1]]-part.vertices[b.vertices[0]],part.vertices[b.vertices[2]]-part.vertices[b.vertices[0]]).normalized;
                    if(Vector3.Dot(normal,other)<.94f)continue;
                    // Only flip inside a convex, similarly oriented six-sided patch.
                    for(int j=0;j<6;j++)if(Vector3.Dot(Vector3.Cross(part.vertices[loop[(j+1)%6]]-part.vertices[loop[j]],part.vertices[loop[(j+5)%6]]-part.vertices[loop[j]]),normal)<-1e-8f)invalid=true;
                    if(invalid)continue;
                    float best=Quality(part,a.vertices,normal)+Quality(part,b.vertices,normal);int[] qa=null,qb=null;
                    for(int j=0;j<3;j++)
                    {
                        var diagonal=StudioRetopologyOperations.Edge(loop[j],loop[j+3]);
                        if(edges.ContainsKey(diagonal) || globalEdges.ContainsKey(diagonal))continue;
                        var x=Enumerable.Range(0,4).Select(k=>loop[(j+k)%6]).ToArray();
                        var y=Enumerable.Range(0,4).Select(k=>loop[(j+3+k)%6]).ToArray();
                        float score=Quality(part,x,normal)+Quality(part,y,normal);
                        if(score+.05f<best){best=score;qa=x;qb=y;}
                    }
                    if(qa==null)continue;
                    local.faces[ia]=new Face(qa,a.material);local.faces[ib]=new Face(qb,a.material);changes++;flipped=true;break;
                }
                if(!flipped)break;
            }
            if(changes>0)
            {
                part.faces=part.faces.Where((f,i)=>!inside.Contains(i)).Concat(local.faces).ToList();
                part.needsUV=true;
            }
            return changes;
        }
        static float Quality(Part p,int[] q,Vector3 normal)
        {
            float min=float.MaxValue,max=0,score=0;
            for(int i=0;i<4;i++)
            {
                var x=p.vertices[q[(i+1)%4]]-p.vertices[q[i]];var y=p.vertices[q[(i+3)%4]]-p.vertices[q[i]];
                if(Vector3.Dot(Vector3.Cross(x,y),normal)<=1e-10f)return float.PositiveInfinity;
                float length=x.magnitude;min=Mathf.Min(min,length);max=Mathf.Max(max,length);
                score+=Mathf.Abs(Vector3.Dot(x.normalized,y.normalized));
            }
            return min<1e-6f?float.PositiveInfinity:score+max/min*.25f;
        }
    }
}

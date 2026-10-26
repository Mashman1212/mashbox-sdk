using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioRetopologyCrotch
    {
        internal static string Validate(Part part,HashSet<int> vertices,HashSet<(int,int)> edges,out int[][] loops)
        {
            loops=null;
            const string hint="Select three complete borders: the hip bottom and both leg tops. In Edges mode, double-click the first border, then Shift-double-click the other two.";
            if(part==null||vertices==null)return hint;
            loops=StudioRetopologyLoops.Boundaries(part).Where(r=>r.All(vertices.Contains)&&
                (edges==null||Enumerable.Range(0,r.Length).All(i=>edges.Contains(StudioRetopologyOperations.Edge(r[i],r[(i+1)%r.Length]))))).ToArray();
            if(loops.Length!=3||loops.SelectMany(r=>r).Distinct().Count()!=vertices.Count)return hint;
            if(loops.SelectMany(r=>r).Count()!=vertices.Count)return "The three borders must not share vertices.";
            if(loops.Any(r=>r.Length<4))return "Each pants border needs at least four vertices.";
            Vector3 Center(int[] r)=>r.Aggregate(Vector3.zero,(v,i)=>v+part.vertices[i])/r.Length;
            loops=loops.OrderByDescending(r=>Center(r).y).ToArray();
            var hip=Center(loops[0]);var left=Center(loops[1]);var right=Center(loops[2]);
            float scale=Mathf.Max((hip-left).magnitude,(hip-right).magnitude);
            if(hip.y<=Mathf.Max(left.y,right.y)+scale*.01f)return "Pants Bridge expects upright pants with the hip border above both leg tops (local Y up).";
            if((left.x-hip.x)*(right.x-hip.x)>=0)return "Choose one leg border on each side of the hip center (local X).";
            return null;
        }

        internal static string Build(Part part,HashSet<int> vertices,HashSet<(int,int)> edges,int rows,StudioRetopologySurface surface,float offset)
        {
            var issue=Validate(part,vertices,edges,out var loops);
            if(issue!=null)throw new InvalidOperationException(issue);
            var hip=loops[0];var left=loops[1];var right=loops[2];
            int li=0,ri=0;float best=float.MaxValue;
            // Join the nearest facing inner edges. Opposite directed edges are required
            // so the new crotch face consumes both boundaries with consistent winding.
            for(int i=0;i<left.Length;i++)for(int j=0;j<right.Length;j++)
            {
                var a=part.vertices[left[i]];var b=part.vertices[left[(i+1)%left.Length]];
                var c=part.vertices[right[j]];var d=part.vertices[right[(j+1)%right.Length]];
                if(Vector3.Dot((b-a).normalized,(d-c).normalized)>-.1f)continue;
                float cost=(a-d).sqrMagnitude+(b-c).sqrMagnitude;
                if(cost<best){best=cost;li=i;ri=j;}
            }
            if(float.IsInfinity(best)||best==float.MaxValue)throw new InvalidOperationException("No facing crotch edges found. Check the leg borders and face winding.");
            int material=part.faces.First(f=>f.vertices.Contains(hip[0])).material;
            int initial=part.faces.Count;
            part.faces.Add(new Face(new[]{left[(li+1)%left.Length],left[li],right[(ri+1)%right.Length],right[ri]},material));
            var legIds=new HashSet<int>(left.Concat(right));
            var joined=StudioRetopologyLoops.Boundaries(part).FirstOrDefault(r=>r.Length==legIds.Count&&r.All(legIds.Contains));
            if(joined==null)throw new InvalidOperationException("Could not form a simple crotch border. Undoable operation cancelled.");
            var bottom=joined.Reverse().ToArray();
            // Search cyclic alignment without reversing winding. This prevents a twisted belt.
            int shift=0;best=float.MaxValue;
            for(int k=0;k<bottom.Length;k++)
            {
                float cost=0;
                for(int i=0;i<hip.Length;i++)
                {
                    float f=(float)i*bottom.Length/hip.Length;int j=Mathf.FloorToInt(f);
                    var target=Vector3.Lerp(part.vertices[bottom[(j+k)%bottom.Length]],part.vertices[bottom[(j+k+1)%bottom.Length]],f-j);
                    cost+=(part.vertices[hip[i]]-target).sqrMagnitude;
                }
                if(cost<best){best=cost;shift=k;}
            }
            bottom=Enumerable.Range(0,bottom.Length).Select(i=>bottom[(i+shift)%bottom.Length]).ToArray();
            rows=Mathf.Clamp(rows,1,16);var previous=hip;
            for(int row=1;row<rows;row++)
            {
                var next=new int[hip.Length];float t=(float)row/rows;
                for(int i=0;i<hip.Length;i++)
                {
                    float f=(float)i*bottom.Length/hip.Length;int j=Mathf.FloorToInt(f);
                    var target=Vector3.Lerp(part.vertices[bottom[j]],part.vertices[bottom[(j+1)%bottom.Length]],f-j);
                    var point=Vector3.Lerp(part.vertices[hip[i]],target,t);
                    next[i]=part.vertices.Count;part.vertices.Add(surface==null?point:surface.Project(point,offset));
                    part.uv.Add(Vector2.Lerp(part.uv[hip[i]],Vector2.Lerp(part.uv[bottom[j]],part.uv[bottom[(j+1)%bottom.Length]],f-j),t));
                }
                Connect(previous,next);previous=next;
            }
            Connect(previous,bottom);
            var allEdges=StudioRetopologyOperations.Edges(part);
            if(allEdges.Any(e=>e.Value.Count>2))throw new InvalidOperationException("The bridge would create non-manifold edges. Operation cancelled.");
            foreach(var loop in loops)for(int i=0;i<loop.Length;i++)
                if(allEdges[StudioRetopologyOperations.Edge(loop[i],loop[(i+1)%loop.Length])].Count!=2)
                    throw new InvalidOperationException("A selected border was not fully stitched. Operation cancelled.");
            int triangles=part.faces.Skip(initial).Count(f=>f.vertices.Length==3);
            part.needsUV=true;
            return $"Pants bridge: {part.faces.Count-initial} faces, {triangles} transition triangles. All three borders stitched. Review the crotch and unwrap new UVs.";

            void Connect(int[] a,int[] b)
            {
                int count=Mathf.Max(a.Length,b.Length);
                for(int i=0;i<count;i++)
                {
                    int ai=Mathf.FloorToInt((float)i*a.Length/count),an=Mathf.FloorToInt((float)(i+1)*a.Length/count);
                    int bi=Mathf.FloorToInt((float)i*b.Length/count),bn=Mathf.FloorToInt((float)(i+1)*b.Length/count);
                    int[] face=ai==an?new[]{a[ai%a.Length],b[bi%b.Length],b[bn%b.Length]}:
                        bi==bn?new[]{a[an%a.Length],a[ai%a.Length],b[bi%b.Length]}:
                        new[]{a[an%a.Length],a[ai%a.Length],b[bi%b.Length],b[bn%b.Length]};
                    part.faces.Add(new Face(face,material));
                }
            }
        }
    }
}

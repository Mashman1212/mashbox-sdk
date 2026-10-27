using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioRetopologyTube
    {
        private static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        internal static Part Generate(Mesh reference,Vector3 start,Vector3 end,int sides,int divisions,float offset)
        {
            if(!reference)throw new InvalidOperationException("Choose a reference mesh first.");
            float size=reference.bounds.size.magnitude;
            if((end-start).magnitude<Mathf.Max(size*.001f,1e-5f))throw new InvalidOperationException("Place the second tube point farther along the same arm or leg.");
            sides=Mathf.Clamp(sides,4,64);divisions=Mathf.Clamp(divisions,1,64);
            var axis=(end-start).normalized;
            var u=Vector3.Cross(axis,Mathf.Abs(axis.y)<.9f?Vector3.up:Vector3.right).normalized;
            var v=Vector3.Cross(axis,u);
            var vertices=reference.vertices;var triangles=reference.triangles;
            var result=new Part{name="Tube"};float tolerance=Mathf.Max(size*1e-6f,1e-7f);
            List<Vector3> SampleRing(float along)
            {
                var origin=Vector3.Lerp(start,end,along);
                var ring=new List<Vector3>(sides);
                var points=new List<Vector2>();var edges=new HashSet<(int,int)>();
                int Weld(Vector3 p)
                {
                    var xy=new Vector2(Vector3.Dot(p-origin,u),Vector3.Dot(p-origin,v));
                    for(int i=0;i<points.Count;i++)if((points[i]-xy).sqrMagnitude<tolerance*tolerance)return i;
                    points.Add(xy);return points.Count-1;
                }
                for(int t=0;t<triangles.Length;t+=3)
                {
                    var a=vertices[triangles[t]];var b=vertices[triangles[t+1]];var c=vertices[triangles[t+2]];
                    Vector3 first=default,second=default;int count=0;
                    void Cut(Vector3 p,Vector3 q)
                    {
                        float dp=Vector3.Dot(p-origin,axis),dq=Vector3.Dot(q-origin,axis);
                        if((dp<=0&&dq>0)||(dq<=0&&dp>0))
                        {var hit=Vector3.Lerp(p,q,dp/(dp-dq));if(count++==0)first=hit;else second=hit;}
                    }
                    Cut(a,b);Cut(b,c);Cut(c,a);
                    if(count!=2||(first-second).sqrMagnitude<tolerance*tolerance)continue;
                    int x=Weld(first),y=Weld(second);if(x!=y)edges.Add(StudioRetopologyOperations.Edge(x,y));
                }
                var adjacent=new Dictionary<int,List<int>>();
                foreach(var edge in edges)
                {
                    if(!adjacent.ContainsKey(edge.Item1))adjacent[edge.Item1]=new List<int>();
                    if(!adjacent.ContainsKey(edge.Item2))adjacent[edge.Item2]=new List<int>();
                    adjacent[edge.Item1].Add(edge.Item2);adjacent[edge.Item2].Add(edge.Item1);
                }
                var remaining=new HashSet<int>(adjacent.Keys);HashSet<int> chosen=null;float best=float.PositiveInfinity;
                while(remaining.Count>0)
                {
                    var component=new HashSet<int>();var queue=new Queue<int>();queue.Enqueue(remaining.First());
                    while(queue.Count>0){int i=queue.Dequeue();if(!remaining.Remove(i))continue;component.Add(i);foreach(int next in adjacent[i])queue.Enqueue(next);}
                    // Choose the local closed section nearest the user's guide, not another limb.
                    float distance=component.Min(i=>points[i].sqrMagnitude);
                    if(distance<best){best=distance;chosen=component;}
                }
                if(chosen==null||chosen.Count<3||chosen.Any(i=>adjacent[i].Count!=2))
                    return null;
                var min=new Vector2(float.MaxValue,float.MaxValue);var max=-min;
                foreach(int i in chosen){min=Vector2.Min(min,points[i]);max=Vector2.Max(max,points[i]);}
                var center=(min+max)*.5f;
                for(int i=0;i<sides;i++)
                {
                    float angle=2*Mathf.PI*i/sides;var direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));float distance=float.PositiveInfinity;
                    foreach(var edge in edges)
                    {
                        if(!chosen.Contains(edge.Item1))continue;
                        var a=points[edge.Item1]-center;var span=points[edge.Item2]-points[edge.Item1];float den=Cross(direction,span);
                        if(Mathf.Abs(den)<1e-10f)continue;
                        float d=Cross(a,span)/den,f=Cross(a,direction)/den;
                        if(d>0&&f>=-1e-5f&&f<=1.00001f)distance=Mathf.Min(distance,d);
                    }
                    if(float.IsInfinity(distance))return null;
                    var xy=center+direction*(distance+offset);
                    ring.Add(origin+u*xy.x+v*xy.y);
                }
                return ring;
            }
            // Surface clicks near an open cuff can put an oblique slice across the rim.
            // Search only toward the other click, bounded to a quarter of the guide at
            // each end. Never bridge an invalid interior section or choose another limb.
            float FitEnd(bool first)
            {
                float edge=first?0:1,sign=first?1:-1;
                if(SampleRing(edge)!=null)return edge;
                float failed=0;
                for(int step=1;step<=32;step++)
                {
                    float inset=.25f*step/32;
                    if(SampleRing(edge+sign*inset)==null){failed=inset;continue;}
                    float valid=inset;
                    for(int refine=0;refine<8;refine++)
                    {
                        float mid=(failed+valid)*.5f;
                        if(SampleRing(edge+sign*mid)==null)failed=mid;else valid=mid;
                    }
                    return edge+sign*valid;
                }
                throw new InvalidOperationException("Could not fit a complete ring near this tube end. Choose two points along the same unbranched sleeve or limb, or use a shorter section.");
            }
            float firstRow=FitEnd(true),lastRow=FitEnd(false);
            for(int row=0;row<=divisions;row++)
            {
                var ring=SampleRing(Mathf.Lerp(firstRow,lastRow,(float)row/divisions));
                if(ring==null)throw new InvalidOperationException("The tube crosses an opening or a sharp bend between its ends. Use a shorter section, then bridge the ends.");
                result.vertices.AddRange(ring);
                for(int i=0;i<sides;i++)result.uv.Add(new Vector2((float)i/sides,(float)row/divisions));
            }
            for(int row=0;row<divisions;row++)for(int i=0;i<sides;i++)
            {int j=(i+1)%sides;result.faces.Add(new Face(new[]{row*sides+i,row*sides+j,(row+1)*sides+j,(row+1)*sides+i}));}
            result.needsUV=true;return result;
        }
    }
}

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
            for(int row=0;row<=divisions;row++)
            {
                var origin=Vector3.Lerp(start,end,(float)row/divisions);
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
                    throw new InvalidOperationException("Tube needs a closed section around the limb. Place both points slightly inside the cuff/opening, away from the armpit or crotch split.");
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
                    if(float.IsInfinity(distance))throw new InvalidOperationException("This section bends or folds too sharply for a tube. Use a shorter section, then bridge the ends.");
                    var xy=center+direction*(distance+offset);
                    result.vertices.Add(origin+u*xy.x+v*xy.y);result.uv.Add(new Vector2((float)i/sides,(float)row/divisions));
                }
            }
            for(int row=0;row<divisions;row++)for(int i=0;i<sides;i++)
            {int j=(i+1)%sides;result.faces.Add(new Face(new[]{row*sides+i,row*sides+j,(row+1)*sides+j,(row+1)*sides+i}));}
            result.needsUV=true;return result;
        }
    }
}

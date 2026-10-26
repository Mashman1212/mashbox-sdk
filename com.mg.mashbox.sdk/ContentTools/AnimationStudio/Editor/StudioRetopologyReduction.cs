using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part = MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face = MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;

namespace MashBoxSDK.AnimationStudio
{
    // Conservative quadric edge collapse. Border, seam, material and crease
    // constraints may stop above the budget; never force invalid collapses.
    internal static class StudioRetopologyReduction
    {
        private struct Quadric
        {
            public double a,b,c,d,e,f,g,h,i,j;
            public static Quadric Plane(Vector3 n,float w) => new Quadric { a=n.x*n.x,b=n.x*n.y,c=n.x*n.z,d=n.x*w,e=n.y*n.y,f=n.y*n.z,g=n.y*w,h=n.z*n.z,i=n.z*w,j=w*w };
            public static Quadric operator +(Quadric x,Quadric y)=>new Quadric {a=x.a+y.a,b=x.b+y.b,c=x.c+y.c,d=x.d+y.d,e=x.e+y.e,f=x.f+y.f,g=x.g+y.g,h=x.h+y.h,i=x.i+y.i,j=x.j+y.j};
            public double Error(Vector3 p)=>Math.Max(0,a*p.x*p.x+2*b*p.x*p.y+2*c*p.x*p.z+2*d*p.x+e*p.y*p.y+2*f*p.y*p.z+2*g*p.y+h*p.z*p.z+2*i*p.z+j);
        }
        private sealed class Vertex { public Vector3 p; public Vector2 uv; public Quadric q; public HashSet<int> faces=new HashSet<int>(); public bool pinned,dead; public int version; }
        private sealed class Tri { public int a,b,c,material; public bool dead; public int[] IDs=>new[]{a,b,c}; }
        private sealed class Candidate { public int a,b,va,vb; public Vector3 point; public double cost; public long id; }
        internal static int Reduce(Part part,int target,bool borders,float crease,Func<float,bool> cancelled=null)
        {
            var v=part.vertices.Select((p,i)=>new Vertex { p=p,uv=part.uv[i] }).ToArray();
            var tris=new List<Tri>();
            foreach(var f in part.faces)for(int j=1;j<f.vertices.Length-1;j++)tris.Add(new Tri { a=f.vertices[0],b=f.vertices[j],c=f.vertices[j+1],material=f.material });
            int initial=tris.Count,count=initial; target=Mathf.Clamp(target,4,Mathf.Max(4,count));
            var edges=new Dictionary<(int,int),List<int>>();
            Vector3 Normal(Tri t)=>Vector3.Cross(v[t.b].p-v[t.a].p,v[t.c].p-v[t.a].p).normalized;
            for(int i=0;i<tris.Count;i++)
            {
                var t=tris[i];var n=Normal(t);var q=Quadric.Plane(n,-Vector3.Dot(n,v[t.a].p));
                foreach(int id in t.IDs){v[id].faces.Add(i);v[id].q+=q;}
                foreach(var edge in new[]{StudioRetopologyOperations.Edge(t.a,t.b),StudioRetopologyOperations.Edge(t.b,t.c),StudioRetopologyOperations.Edge(t.c,t.a)})
                { if(!edges.TryGetValue(edge,out var list))edges.Add(edge,list=new List<int>());list.Add(i); }
            }
            foreach(var pair in edges)
            {
                bool pin=pair.Value.Count>2 || (borders&&pair.Value.Count==1);
                if(pair.Value.Count==2)pin|=tris[pair.Value[0]].material!=tris[pair.Value[1]].material || Vector3.Angle(Normal(tris[pair.Value[0]]),Normal(tris[pair.Value[1]]))>crease;
                if(pin){v[pair.Key.Item1].pinned=true;v[pair.Key.Item2].pinned=true;}
            }
            // Split vertices at UV/hard-normal seams must not drift apart.
            foreach(var group in Enumerable.Range(0,v.Length).GroupBy(i=>v[i].p).Where(g=>g.Count()>1))foreach(int i in group)v[i].pinned=true;
            var queue=new SortedSet<Candidate>(Comparer<Candidate>.Create((a,b)=>{int c=a.cost.CompareTo(b.cost);return c!=0?c:a.id.CompareTo(b.id);}));long serial=0;
            HashSet<int> Neighbors(int id)
            {
                var result=new HashSet<int>();foreach(int f in v[id].faces)if(!tris[f].dead)foreach(int i in tris[f].IDs)if(i!=id)result.Add(i);return result;
            }
            void Offer(int a,int b)
            {
                if(a==b||v[a].dead||v[b].dead||v[a].pinned||v[b].pinned)return;
                if(a>b){int temp=a;a=b;b=temp;}
                var q=v[a].q+v[b].q;var point=(v[a].p+v[b].p)*.5f;double cost=q.Error(point);
                foreach(var test in new[]{v[a].p,v[b].p})if(q.Error(test)<cost){point=test;cost=q.Error(test);}
                queue.Add(new Candidate { a=a,b=b,va=v[a].version,vb=v[b].version,point=point,cost=cost,id=serial++ });
            }
            foreach(var edge in edges.Keys)Offer(edge.Item1,edge.Item2);
            int attempts=0;
            while(count>target&&queue.Count>0)
            {
                if((attempts++&2047)==0 && cancelled!=null && cancelled(1f-(float)(count-target)/Mathf.Max(1,initial-target)))throw new OperationCanceledException("Reduction cancelled. The working mesh was not changed.");
                var c=queue.Min;queue.Remove(c);int a=c.a,b=c.b;
                if(v[a].dead||v[b].dead||v[a].version!=c.va||v[b].version!=c.vb)continue;
                var shared=v[a].faces.Intersect(v[b].faces).Where(f=>!tris[f].dead).ToArray();
                if(shared.Length!=2 && (borders||shared.Length!=1))continue;
                var common=Neighbors(a);common.IntersectWith(Neighbors(b));
                var opposite=new HashSet<int>(shared.SelectMany(f=>tris[f].IDs).Where(i=>i!=a&&i!=b));
                if(!common.SetEquals(opposite))continue; // Link condition preserves manifoldness.
                var affected=v[a].faces.Union(v[b].faces).Where(f=>!tris[f].dead).ToArray();bool valid=true;
                foreach(int f in affected)
                {
                    if(shared.Contains(f))continue;var t=tris[f];var ids=t.IDs;
                    Vector3 P(int i)=>i==a||i==b?c.point:v[i].p;
                    var cross=Vector3.Cross(P(ids[1])-P(ids[0]),P(ids[2])-P(ids[0]));
                    if(cross.sqrMagnitude<1e-16f||Vector3.Dot(cross.normalized,Normal(t))<.25f){valid=false;break;}
                }
                if(!valid)continue;
                var neighbors=Neighbors(a);neighbors.UnionWith(Neighbors(b));
                float blend=(v[b].p-v[a].p).sqrMagnitude>1e-16f?Mathf.Clamp01(Vector3.Dot(c.point-v[a].p,v[b].p-v[a].p)/(v[b].p-v[a].p).sqrMagnitude):.5f;
                v[a].uv=Vector2.Lerp(v[a].uv,v[b].uv,blend);v[a].p=c.point;v[a].q+=v[b].q;
                foreach(int f in affected)
                {
                    var t=tris[f];foreach(int i in t.IDs)v[i].faces.Remove(f);
                    if(shared.Contains(f)){t.dead=true;count--;continue;}
                    if(t.a==b)t.a=a;if(t.b==b)t.b=a;if(t.c==b)t.c=a;
                    foreach(int i in t.IDs)v[i].faces.Add(f);
                }
                v[b].dead=true;v[b].faces.Clear();v[a].version++;v[b].version++;
                foreach(int i in neighbors)if(!v[i].dead){v[i].version++;foreach(int n in Neighbors(i))Offer(i,n);}
                foreach(int n in Neighbors(a))Offer(a,n);
            }
            part.vertices=v.Select(x=>x.p).ToList();part.uv=v.Select(x=>x.uv).ToList();
            part.faces=tris.Where(t=>!t.dead).Select(t=>new Face(t.IDs,t.material)).ToList();
            StudioRetopologyOperations.Compact(part);return count;
        }
    }
}

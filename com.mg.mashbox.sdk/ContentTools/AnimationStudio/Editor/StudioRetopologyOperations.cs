using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Face = MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
using Part = MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioRetopologyOperations
    {
        internal static (int,int) Edge(int a,int b) => a<b?(a,b):(b,a);
        internal static Dictionary<(int,int),List<int>> Edges(Part p)
        {
            var result=new Dictionary<(int,int),List<int>>();
            for(int i=0;i<p.faces.Count;i++)
            {
                var ids=p.faces[i].vertices;
                for(int j=0;j<ids.Length;j++)
                {
                    var key=Edge(ids[j],ids[(j+1)%ids.Length]);
                    if(!result.TryGetValue(key,out var faces)) result.Add(key,faces=new List<int>());
                    faces.Add(i);
                }
            }
            return result;
        }
        internal static string Audit(Part p)
        {
            var edges=Edges(p);
            int degenerate=p.faces.Count(f=>f.vertices.Distinct().Count()!=f.vertices.Length ||
                Vector3.Cross(p.vertices[f.vertices[1]]-p.vertices[f.vertices[0]],p.vertices[f.vertices[2]]-p.vertices[f.vertices[0]]).sqrMagnitude<1e-16f);
            int nonmanifold=edges.Count(e=>e.Value.Count>2), boundary=edges.Count(e=>e.Value.Count==1);
            int unused=p.vertices.Count-p.faces.SelectMany(f=>f.vertices).Distinct().Count();
            int quads=p.faces.Count(f=>f.vertices.Length==4), triangles=p.faces.Sum(f=>f.vertices.Length-2);
            return $"{p.vertices.Count:N0} vertices   {triangles:N0} triangles\n{quads:N0} quads   {boundary:N0} boundary edges\n{nonmanifold} non-manifold edges   {degenerate} degenerate faces   {unused} unused vertices";
        }
        internal static void Compact(Part p)
        {
            var used=p.faces.SelectMany(f=>f.vertices).Distinct().OrderBy(i=>i).ToArray();
            var map=new Dictionary<int,int>(); for(int i=0;i<used.Length;i++)map[used[i]]=i;
            p.vertices=used.Select(i=>p.vertices[i]).ToList();
            p.uv=used.Select(i=>i<p.uv.Count?p.uv[i]:Vector2.zero).ToList();
            foreach(var f in p.faces) f.vertices=f.vertices.Select(i=>map[i]).ToArray();
        }
        internal static void Clean(Part p)
        {
            // Preserve UV seams; weld only identical positions AND texture coordinates.
            var unique=new Dictionary<(Vector3,Vector2),int>(); var remap=new int[p.vertices.Count];
            for(int i=0;i<remap.Length;i++)
            {
                var key=(p.vertices[i],p.uv[i]);
                if(!unique.TryGetValue(key,out int index)) unique.Add(key,index=i);
                remap[i]=index;
            }
            var seen=new HashSet<string>(); var faces=new List<Face>();
            foreach(var f in p.faces)
            {
                var ids=f.vertices.Select(i=>remap[i]).ToArray();
                if(ids.Distinct().Count()!=ids.Length)continue;
                if(Vector3.Cross(p.vertices[ids[1]]-p.vertices[ids[0]],p.vertices[ids[2]]-p.vertices[ids[0]]).sqrMagnitude<1e-16f)continue;
                string key=f.material+":"+string.Join(",",ids.OrderBy(i=>i));
                if(seen.Add(key))faces.Add(new Face(ids,f.material));
            }
            p.faces=faces; Compact(p);
        }
        internal static int PairQuads(Part p,float angle=25)
        {
            var edges=Edges(p); var candidates=new List<(float score,int a,int b,int[] quad)>();
            foreach(var entry in edges)
            {
                if(entry.Value.Count!=2)continue;
                int a=entry.Value[0], b=entry.Value[1]; var fa=p.faces[a]; var fb=p.faces[b];
                if(fa.vertices.Length!=3||fb.vertices.Length!=3||fa.material!=fb.material)continue;
                var boundary=new Dictionary<int,int>(); bool invalid=false;
                foreach(var face in new[]{fa,fb})for(int i=0;i<3;i++)
                {
                    int x=face.vertices[i],y=face.vertices[(i+1)%3]; if(Edge(x,y)==entry.Key)continue;
                    if(boundary.ContainsKey(x)){invalid=true;break;} boundary[x]=y;
                }
                if(invalid||boundary.Count!=4)continue;
                var q=new int[4]; q[0]=boundary.Keys.First();
                for(int i=1;i<4;i++)q[i]=boundary[q[i-1]];
                if(boundary[q[3]]!=q[0]||q.Distinct().Count()!=4)continue;
                Vector3 Normal(Face f)=>Vector3.Cross(p.vertices[f.vertices[1]]-p.vertices[f.vertices[0]],p.vertices[f.vertices[2]]-p.vertices[f.vertices[0]]).normalized;
                var normal=Normal(fa); if(Vector3.Angle(normal,Normal(fb))>angle)continue;
                float min=float.MaxValue,max=0,score=0;
                for(int i=0;i<4;i++)
                {
                    Vector3 x=p.vertices[q[(i+1)%4]]-p.vertices[q[i]],y=p.vertices[q[(i+3)%4]]-p.vertices[q[i]];
                    if(Vector3.Dot(Vector3.Cross(x,y),normal)<=1e-10f){invalid=true;break;}
                    min=Mathf.Min(min,x.magnitude);max=Mathf.Max(max,x.magnitude);score+=Mathf.Abs(Vector3.Dot(x.normalized,y.normalized));
                }
                if(invalid||min<1e-6f||max/min>5)continue;
                candidates.Add((score+max/min*.1f,a,b,q));
            }
            var consumed=new HashSet<int>(); var result=new List<Face>(); int paired=0;
            foreach(var c in candidates.OrderBy(c=>c.score))
                if(!consumed.Contains(c.a)&&!consumed.Contains(c.b))
                { consumed.Add(c.a);consumed.Add(c.b);result.Add(new Face(c.quad,p.faces[c.a].material));paired++; }
            for(int i=0;i<p.faces.Count;i++)if(!consumed.Contains(i))result.Add(p.faces[i]);
            p.faces=result; return paired;
        }
        internal static void Relax(Part p,StudioRetopologySurface surface,bool borders,float offset,HashSet<int> selection=null)
        {
            var edges=Edges(p); var pinned=new HashSet<int>(); var neighbors=new HashSet<int>[p.vertices.Count];
            for(int i=0;i<neighbors.Length;i++)neighbors[i]=new HashSet<int>();
            foreach(var e in edges)
            {
                neighbors[e.Key.Item1].Add(e.Key.Item2);neighbors[e.Key.Item2].Add(e.Key.Item1);
                if(borders&&e.Value.Count!=2){pinned.Add(e.Key.Item1);pinned.Add(e.Key.Item2);}
            }
            for(int pass=0;pass<3;pass++)
            {
                var next=p.vertices.ToArray();
                for(int i=0;i<next.Length;i++)
                {
                    if(pinned.Contains(i)||neighbors[i].Count==0||(selection!=null&&selection.Count>0&&!selection.Contains(i)))continue;
                    Vector3 average=Vector3.zero;foreach(int j in neighbors[i])average+=p.vertices[j];
                    var point=Vector3.Lerp(p.vertices[i],average/neighbors[i].Count,.35f);
                    next[i]=surface==null?point:surface.Project(point,offset);
                }
                p.vertices=next.ToList();
            }
        }
        internal static void Subdivide(Part p,StudioRetopologySurface surface,float offset)
        {
            if(p.faces.Count>100000)throw new InvalidOperationException("Subdivision is limited to 100,000 input faces. Reduce the mesh first.");
            var mids=new Dictionary<(int,int),int>();var result=new List<Face>();
            int Add(Vector3 point,Vector2 uv){int id=p.vertices.Count;p.vertices.Add(surface==null?point:surface.Project(point,offset));p.uv.Add(uv);return id;}
            foreach(var f in p.faces)
            {
                var ids=f.vertices;Vector3 center=Vector3.zero;Vector2 uv=Vector2.zero;
                foreach(int i in ids){center+=p.vertices[i];uv+=p.uv[i];}
                int c=Add(center/ids.Length,uv/ids.Length);var edge=new int[ids.Length];
                for(int i=0;i<ids.Length;i++)
                {
                    int a=ids[i],b=ids[(i+1)%ids.Length];var key=Edge(a,b);
                    if(!mids.TryGetValue(key,out int mid)){mid=Add((p.vertices[a]+p.vertices[b])*.5f,(p.uv[a]+p.uv[b])*.5f);mids.Add(key,mid);} edge[i]=mid;
                }
                for(int i=0;i<ids.Length;i++)result.Add(new Face(new[]{ids[i],edge[i],c,edge[(i+ids.Length-1)%ids.Length]},f.material));
            }
            p.faces=result;
        }
        internal static void Mirror(Part p,bool leftToRight)
        {
            float sign=leftToRight?1:-1;
            // Clip crossing polygons at the center plane, sharing each edge cut.
            // Removing whole crossing faces would open a slit through the mesh.
            var source=new List<Face>();var cuts=new Dictionary<(int,int),int>();
            const float epsilon=1e-5f;
            int Cut(int a,int b)
            {
                if(Mathf.Abs(p.vertices[a].x)<epsilon)return a;
                if(Mathf.Abs(p.vertices[b].x)<epsilon)return b;
                var key=Edge(a,b);if(cuts.TryGetValue(key,out int found))return found;
                float t=p.vertices[a].x/(p.vertices[a].x-p.vertices[b].x);
                var point=Vector3.Lerp(p.vertices[a],p.vertices[b],t);point.x=0;
                int id=p.vertices.Count;p.vertices.Add(point);p.uv.Add(Vector2.Lerp(p.uv[a],p.uv[b],t));cuts[key]=id;return id;
            }
            foreach(var face in p.faces)
            {
                var polygon=new List<int>();
                for(int j=0;j<face.vertices.Length;j++)
                {
                    int a=face.vertices[j],b=face.vertices[(j+1)%face.vertices.Length];
                    bool insideA=p.vertices[a].x*sign>=-epsilon,insideB=p.vertices[b].x*sign>=-epsilon;
                    if(insideA)polygon.Add(a);
                    if(insideA!=insideB)polygon.Add(Cut(a,b));
                }
                var ids=polygon.Distinct().ToArray();
                if(ids.Length>=3)source.Add(new Face(ids,face.material));
            }
            if(source.Count==0)throw new InvalidOperationException("No faces lie on that side of local X=0.");
            var map=new Dictionary<int,int>();
            foreach(int i in source.SelectMany(f=>f.vertices).Distinct())
            {
                var point=p.vertices[i];if(Mathf.Abs(point.x)<1e-5f){point.x=0;p.vertices[i]=point;map[i]=i;continue;}
                point.x=-point.x;map[i]=p.vertices.Count;p.vertices.Add(point);p.uv.Add(p.uv[i]);
            }
            p.faces=source.Concat(source.Where(f=>f.vertices.Any(i=>Mathf.Abs(p.vertices[i].x)>1e-5f)).Select(f=>new Face(f.vertices.Reverse().Select(i=>map[i]).ToArray(),f.material))).ToList();
            Compact(p);p.needsUV=true;
        }
    }
}

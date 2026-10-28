using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // Shared by picking/highlights and geometry tools. A triangle's three vertices
        // can belong to both sides of an n-gon cut; membership alone is not ownership.
        internal static List<int>[] MapSourceFaceTriangles(Mesh mesh,IList<Face> faces,Vector3[] positions=null)
        {
            var result=Enumerable.Range(0,faces.Count).Select(_=>new List<int>()).ToArray();
            var vertices=faces.Select(f=>new HashSet<int>(f.Vertices)).ToArray();
            var memberships=new Dictionary<int,List<int>>();
            var boundary=new HashSet<(int,int,int)>();
            var directed=new Dictionary<(int,int,int),List<int>>();
            (int,int,int) Edge(int a,int b,int sub)=>a<b?(a,b,sub):(b,a,sub);
            for(int f=0;f<faces.Count;f++)
            {
                var face=faces[f];
                foreach(int v in vertices[f])
                {if(!memberships.TryGetValue(v,out var list))memberships[v]=list=new List<int>();list.Add(f);}
            }
            var triangles=new List<(int a,int b,int c,int sub)>();
            var uses=new Dictionary<(int,int,int),List<int>>();
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var indices=mesh.GetTriangles(sub);
                for(int i=0;i<indices.Length;i+=3)
                {
                    triangles.Add((indices[i],indices[i+1],indices[i+2],sub));
                }
            }
            var owners=Enumerable.Repeat(-1,triangles.Count).ToArray();var ambiguous=new List<int>();var assigned=new int[faces.Count];
            for(int i=0;i<triangles.Count;i++)
            {
                var t=triangles[i];int owner=-1,count=0;
                if(memberships.TryGetValue(t.a,out var list))foreach(int f in list)
                    if(faces[f].Submesh==t.sub&&vertices[f].Contains(t.b)&&vertices[f].Contains(t.c)){owner=f;count++;}
                if(count==0)throw new InvalidOperationException("Authored face boundaries do not cover their render triangles.");
                if(count==1){owners[i]=owner;assigned[owner]++;}else ambiguous.Add(i);
            }
            // Most meshes need no adjacency graph: only ambiguous triangles and
            // their immediate render neighbours need boundary-aware resolution.
            if(ambiguous.Count==0)
            {
                for(int i=0;i<triangles.Count;i++){var t=triangles[i];result[owners[i]].AddRange(new[]{t.a,t.b,t.c});}
                return result;
            }
            for(int f=0;f<faces.Count;f++)for(int e=0;e<faces[f].Vertices.Count;e++)
            {
                var face=faces[f];int a=face.Vertices[e],b=face.Vertices[(e+1)%face.Vertices.Count];boundary.Add(Edge(a,b,face.Submesh));
                var key=(a,b,face.Submesh);if(!directed.TryGetValue(key,out var list))directed[key]=list=new List<int>();if(!list.Contains(f))list.Add(f);
            }
            var relevant=new HashSet<(int,int,int)>();
            foreach(int id in ambiguous)
            {var t=triangles[id];foreach(var edge in new[]{Edge(t.a,t.b,t.sub),Edge(t.b,t.c,t.sub),Edge(t.c,t.a,t.sub)})if(!boundary.Contains(edge))relevant.Add(edge);}
            for(int id=0;id<triangles.Count;id++)
            {
                var t=triangles[id];foreach(var edge in new[]{Edge(t.a,t.b,t.sub),Edge(t.b,t.c,t.sub),Edge(t.c,t.a,t.sub)})
                    if(relevant.Contains(edge)){if(!uses.TryGetValue(edge,out var list))uses[edge]=list=new List<int>();list.Add(id);}
            }
            var adjacency=new List<int>[triangles.Count];
            foreach(var pair in uses)
            {
                // Never flood over an authored perimeter or an unresolved non-manifold edge.
                if(pair.Value.Count!=2||boundary.Contains(pair.Key))continue;
                int a=pair.Value[0],b=pair.Value[1];
                if(adjacency[a]==null)adjacency[a]=new List<int>();if(adjacency[b]==null)adjacency[b]=new List<int>();adjacency[a].Add(b);adjacency[b].Add(a);
            }
            var seen=new bool[triangles.Count];var charts=new CutChart[faces.Count];var contours=new Vector2[faces.Count][];
            bool Contains(int f,Vector3 point)
            {
                if(charts[f]==null)
                {
                    var polygon=faces[f].Vertices;var origin=positions[polygon[0]];var normal=Vector3.zero;
                    for(int i=1;i+1<polygon.Count;i++)normal+=Vector3.Cross(positions[polygon[i]]-origin,positions[polygon[i+1]]-origin);
                    charts[f]=new CutChart(origin,normal.normalized);contours[f]=polygon.Select(v=>charts[f].Project(positions[v])).ToArray();
                }
                return CutInside(charts[f].Project(point),contours[f]);
            }
            for(int start=0;start<triangles.Count;start++)
            {
                if(seen[start]||owners[start]>=0)continue;
                var component=new List<int>();var queue=new Queue<int>();queue.Enqueue(start);seen[start]=true;
                while(queue.Count>0){int id=queue.Dequeue();component.Add(id);if(adjacency[id]==null)continue;foreach(int next in adjacency[id])if(!seen[next]){seen[next]=true;queue.Enqueue(next);}}
                var first=triangles[start];
                var candidates=memberships.TryGetValue(first.a,out var membership)?membership.Where(f=>faces[f].Submesh==first.sub).ToList():new List<int>();
                candidates.RemoveAll(f=>component.Any(id=>!vertices[f].Contains(triangles[id].a)||!vertices[f].Contains(triangles[id].b)||!vertices[f].Contains(triangles[id].c)));
                candidates.RemoveAll(f=>component.Any(id=>owners[id]>=0&&owners[id]!=f));
                if(candidates.Count==0)throw new InvalidOperationException("Authored face boundaries do not cover their render triangles.");
                var seeds=new HashSet<int>();
                foreach(int id in component)
                {
                    var t=triangles[id];foreach(var edge in new[]{(t.a,t.b,t.sub),(t.b,t.c,t.sub),(t.c,t.a,t.sub)})
                        if(directed.TryGetValue(edge,out var list))foreach(int f in list)if(candidates.Contains(f))seeds.Add(f);
                }
                if(seeds.Count>0)candidates.RemoveAll(f=>!seeds.Contains(f));
                if(candidates.Count>1)
                {
                    // Conservative fallback for duplicate/coincident boundaries or a
                    // legacy render buffer with no directly matching perimeter edge.
                    if(positions==null)positions=mesh.vertices;
                    var point=(positions[first.a]+positions[first.b]+positions[first.c])/3;
                    var inside=candidates.Where(f=>Contains(f,point)).ToList();if(inside.Count>0)candidates=inside;
                    int needed=component.Count(id=>owners[id]<0);var available=candidates.Where(f=>assigned[f]+needed<=(faces[f].Vertices.Count-2)).ToList();if(available.Count>0)candidates=available;
                }
                int owner=candidates[0];assigned[owner]+=component.Count(id=>owners[id]<0);foreach(int id in component)owners[id]=owner;
            }
            // Keep the exact native triangle order/diagonals within each authored face.
            for(int i=0;i<triangles.Count;i++)
            {var t=triangles[i];result[owners[i]].Add(t.a);result[owners[i]].Add(t.b);result[owners[i]].Add(t.c);}
            return result;
        }
    }
}

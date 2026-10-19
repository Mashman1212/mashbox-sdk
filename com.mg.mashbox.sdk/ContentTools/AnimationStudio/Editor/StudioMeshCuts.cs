using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    // Polygon boundaries are retained separately from Unity's render triangles.
    internal sealed partial class StudioMeshCuts
    {
        internal sealed class Face { internal int Submesh; internal List<int> Vertices; }
        internal struct Point
        {
            internal int A, B; internal float T;
            internal Point(int a, int b, float t) { A = a; B = b; T = t; }
        }
        internal struct Cut { internal int Face; internal Point A, B; }
        internal readonly List<Face> Faces = new List<Face>();
        internal readonly Vector3[] Positions;
        private readonly int[] welded;
        private readonly Dictionary<(int, int), List<(int face, int edge)>> neighbours = new Dictionary<(int, int), List<(int, int)>>();
        internal StudioMeshCuts(StudioModelDocument.Node node)
        {
            var mesh = node.mesh; Positions = mesh.vertices;
            welded = new int[Positions.Length]; var ids = new Dictionary<Vector3, int>();
            for (int i = 0; i < Positions.Length; i++)
            { if (!ids.TryGetValue(Positions[i], out int id)) { id = ids.Count; ids.Add(Positions[i], id); } welded[i] = id; }
            if (node.cutPolygons != null && node.cutTriangles != null && node.cutTriangles.SequenceEqual(mesh.triangles))
            {
                var data = node.cutPolygons;
                for (int i = 0; i < data.Length;)
                { int sub = data[i++], count = data[i++]; Faces.Add(new Face { Submesh = sub, Vertices = data.Skip(i).Take(count).ToList() }); i += count; }
            }
            else ReadTriangles(mesh);
            for (int f = 0; f < Faces.Count; f++) for (int e = 0; e < Faces[f].Vertices.Count; e++)
            {
                var v = Faces[f].Vertices; var key = Key(v[e], v[(e + 1) % v.Count]);
                if (!neighbours.TryGetValue(key, out var uses)) neighbours.Add(key, uses = new List<(int, int)>());
                uses.Add((f, e));
            }
        }
        private (int, int) Key(int a, int b) => welded[a] < welded[b] ? (welded[a], welded[b]) : (welded[b], welded[a]);
        internal Vector3 Position(Point p) => Vector3.Lerp(Positions[p.A], Positions[p.B], p.T);
        private void ReadTriangles(Mesh mesh)
        {
            var triangles = new List<Face>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) throw new InvalidOperationException("Cut tools require triangle mesh surfaces.");
                var indices = mesh.GetTriangles(s);
                for (int i = 0; i < indices.Length; i += 3) triangles.Add(new Face { Submesh = s, Vertices = indices.Skip(i).Take(3).ToList() });
            }
            var edges = new Dictionary<(int, int), List<int>>();
            for (int f = 0; f < triangles.Count; f++) for (int e = 0; e < 3; e++)
            {
                var v = triangles[f].Vertices; int a = v[e], b = v[(e + 1) % 3]; var key = (Math.Min(a,b), Math.Max(a,b));
                if (!edges.TryGetValue(key, out var list)) edges.Add(key, list = new List<int>()); list.Add(f);
            }
            var candidates = new List<(float score, int a, int b, List<int> polygon)>();
            foreach (var pair in edges.Values.Where(v => v.Count == 2))
            {
                int a = pair[0], b = pair[1]; if (triangles[a].Submesh != triangles[b].Submesh) continue;
                var av = triangles[a].Vertices; var bv = triangles[b].Vertices;
                var normal = Vector3.Cross(Positions[av[1]]-Positions[av[0]], Positions[av[2]]-Positions[av[0]]).normalized;
                var other = Vector3.Cross(Positions[bv[1]]-Positions[bv[0]], Positions[bv[2]]-Positions[bv[0]]).normalized;
                if (Vector3.Dot(normal, other) < .9999f) continue;
                var boundary = new Dictionary<int, int>();
                foreach (var v in new[] { av, bv }) for (int e = 0; e < 3; e++)
                { int x=v[e], y=v[(e+1)%3]; if (boundary.TryGetValue(y, out int z) && z==x) boundary.Remove(y); else boundary[x]=y; }
                // Build boundary explicitly to avoid overwriting a shared edge's start.
                boundary.Clear();
                foreach (var v in new[] { av, bv }) for (int e = 0; e < 3; e++)
                { int x=v[e], y=v[(e+1)%3]; if (av.Contains(x) && av.Contains(y) && bv.Contains(x) && bv.Contains(y)) continue; boundary[x]=y; }
                if (boundary.Count != 4) continue;
                var polygon = new List<int>(); int current=boundary.Keys.First();
                for(int i=0;i<4;i++) { polygon.Add(current); if(!boundary.TryGetValue(current,out current)) break; }
                if(polygon.Distinct().Count()!=4 || current!=polygon[0]) continue;
                float score=0; bool convex=true;
                for(int i=0;i<4;i++)
                { var x=Positions[polygon[(i+1)%4]]-Positions[polygon[i]]; var y=Positions[polygon[(i+2)%4]]-Positions[polygon[(i+1)%4]];
                  if(Vector3.Dot(Vector3.Cross(x,y),normal)<=1e-10f) convex=false; score+=Mathf.Abs(Vector3.Dot(x.normalized,y.normalized)); }
                if(convex) candidates.Add((score,a,b,polygon));
            }
            var used = new HashSet<int>();
            foreach(var c in candidates.OrderBy(c=>c.score))
                if(!used.Contains(c.a)&&!used.Contains(c.b)) { used.Add(c.a); used.Add(c.b); Faces.Add(new Face { Submesh=triangles[c.a].Submesh, Vertices=c.polygon }); }
            for(int i=0;i<triangles.Count;i++) if(!used.Contains(i)) Faces.Add(triangles[i]);
        }
        internal List<Cut> Loop(Point seed)
        {
            var cuts=new List<Cut>(); var pending=new Queue<Point>(); var visited=new HashSet<int>(); pending.Enqueue(seed);
            while(pending.Count>0)
            {
                var point=pending.Dequeue();
                if(!neighbours.TryGetValue(Key(point.A,point.B),out var uses) || uses.Count>2) continue;
                foreach(var use in uses)
                {
                    var v=Faces[use.face].Vertices; if(v.Count!=4 || !visited.Add(use.face)) continue;
                    int e=use.edge; float t=welded[v[e]]==welded[point.A]?point.T:1-point.T;
                    var entry=new Point(v[e],v[(e+1)%4],t);
                    var exit=new Point(v[(e+2)%4],v[(e+3)%4],1-t);
                    cuts.Add(new Cut { Face=use.face,A=entry,B=exit }); pending.Enqueue(exit);
                }
            }
            return cuts;
        }
        internal bool TryCut(Point a, Point b, out Cut cut)
        {
            cut=default;
            if((Position(a)-Position(b)).sqrMagnitude<1e-10f || Key(a.A,a.B)==Key(b.A,b.B)) return false;
            for(int f=0;f<Faces.Count;f++)
            {
                var v=Faces[f].Vertices; bool hasA=false,hasB=false;
                for(int e=0;e<v.Count;e++) { var key=Key(v[e],v[(e+1)%v.Count]); hasA|=key==Key(a.A,a.B); hasB|=key==Key(b.A,b.B); }
                if(hasA&&hasB) { cut=new Cut { Face=f,A=a,B=b }; return true; }
            }
            return false;
        }
        internal void Apply(StudioModelDocument.Node node, IList<Cut> cuts)
        {
            if(cuts.Count==0) throw new InvalidOperationException("No quad strip or face to cut here.");
            var mesh=node.mesh;
            if(mesh.blendShapeCount>0 || mesh.boneWeights.Length>0) throw new InvalidOperationException("Cut tools currently support static meshes without skin weights or blend shapes.");
            var positions=Positions.ToList(); var normals=mesh.normals.ToList(); var tangents=mesh.tangents.ToList(); var colors=mesh.colors.ToList();
            var uvs=new List<Vector4>[8]; var dimensions=new int[8];
            for(int c=0;c<8;c++) { uvs[c]=new List<Vector4>(); mesh.GetUVs(c,uvs[c]); dimensions[c]=mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0+c); }
            var splits=new Dictionary<(int,int),List<Point>>();
            foreach(var cut in cuts) foreach(var p in new[]{cut.A,cut.B})
            { var key=Key(p.A,p.B); if(!splits.TryGetValue(key,out var list)) splits[key]=list=new List<Point>();
              if(!list.Any(q=>(Position(q)-Position(p)).sqrMagnitude<1e-10f)) list.Add(p); }
            var created=new Dictionary<(int,int,float),int>();
            int Insert(int a,int b,float t,Vector3 position)
            {
                if(t<.00001f)return a; if(t>.99999f)return b;
                var key=a<b?(a,b,t):(b,a,1-t); if(created.TryGetValue(key,out int existing))return existing;
                int id=positions.Count; created[key]=id; positions.Add(position);
                if(normals.Count>0)normals.Add(Vector3.Lerp(normals[a],normals[b],t).normalized);
                if(tangents.Count>0) { var x=Vector4.Lerp(tangents[a],tangents[b],t); var n=new Vector3(x.x,x.y,x.z).normalized; tangents.Add(new Vector4(n.x,n.y,n.z,t<.5f?tangents[a].w:tangents[b].w)); }
                if(colors.Count>0)colors.Add(Color.Lerp(colors[a],colors[b],t));
                for(int c=0;c<8;c++)if(uvs[c].Count>0)uvs[c].Add(Vector4.Lerp(uvs[c][a],uvs[c][b],t)); return id;
            }
            var result=new List<Face>();
            for(int f=0;f<Faces.Count;f++)
            {
                var face=Faces[f];var expanded=new List<int>();var v=face.Vertices;
                for(int e=0;e<v.Count;e++)
                {
                    int a=v[e],b=v[(e+1)%v.Count];expanded.Add(a);
                    if(!splits.TryGetValue(Key(a,b),out var list))continue;
                    foreach(var p in list.OrderBy(p=>welded[a]==welded[p.A]?p.T:1-p.T))
                    { float t=welded[a]==welded[p.A]?p.T:1-p.T;int id=Insert(a,b,t,Position(p));if(id!=a&&id!=b)expanded.Add(id); }
                }
                var pieces=new List<List<int>>{expanded};
                foreach(var cut in cuts.Where(c=>c.Face==f))
                {
                    var pa=Position(cut.A);var pb=Position(cut.B);bool split=false;
                    for(int p=0;p<pieces.Count;p++)
                    {
                        var polygon=pieces[p];int a=polygon.FindIndex(i=>(positions[i]-pa).sqrMagnitude<1e-10f),b=polygon.FindIndex(i=>(positions[i]-pb).sqrMagnitude<1e-10f);
                        if(a<0||b<0)continue; if(a>b){int swap=a;a=b;b=swap;}
                        if(b-a<2||b-a>polygon.Count-2)continue;
                        pieces[p]=polygon.GetRange(a,b-a+1);var second=polygon.Skip(b).Concat(polygon.Take(a+1)).ToList();pieces.Add(second);split=true;break;
                    }
                    if(!split)throw new InvalidOperationException("This cut would cross an existing cut or create a degenerate face.");
                }
                foreach(var polygon in pieces)result.Add(new Face { Submesh=face.Submesh,Vertices=polygon });
            }
            var indices=Enumerable.Range(0,mesh.subMeshCount).Select(_=>new List<int>()).ToArray();
            foreach(var face in result)Triangulate(face.Vertices,positions,indices[face.Submesh]);
            mesh.Clear();mesh.indexFormat=positions.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.SetVertices(positions);if(normals.Count>0)mesh.SetNormals(normals);if(tangents.Count>0)mesh.SetTangents(tangents);if(colors.Count>0)mesh.SetColors(colors);
            for(int c=0;c<8;c++)if(uvs[c].Count>0)
            { if(dimensions[c]==2)mesh.SetUVs(c,uvs[c].Select(v=>new Vector2(v.x,v.y)).ToList());else if(dimensions[c]==3)mesh.SetUVs(c,uvs[c].Select(v=>new Vector3(v.x,v.y,v.z)).ToList());else mesh.SetUVs(c,uvs[c]); }
            mesh.subMeshCount=indices.Length;for(int s=0;s<indices.Length;s++)mesh.SetTriangles(indices[s],s);
            mesh.RecalculateBounds();mesh.UploadMeshData(false);
            node.cutPolygons=result.SelectMany(f=>new[]{f.Submesh,f.Vertices.Count}.Concat(f.Vertices)).ToArray();node.cutTriangles=mesh.triangles;
        }
        private static void Triangulate(List<int> polygon,List<Vector3> positions,List<int> indices)
        {
            var remaining=new List<int>(polygon);Vector3 normal=Vector3.zero;
            for(int i=0;i<remaining.Count;i++)normal+=Vector3.Cross(positions[remaining[i]],positions[remaining[(i+1)%remaining.Count]]);
            normal.Normalize();
            while(remaining.Count>3)
            {
                bool found=false;
                for(int i=0;i<remaining.Count;i++)
                {
                    int a=remaining[(i+remaining.Count-1)%remaining.Count],b=remaining[i],c=remaining[(i+1)%remaining.Count];
                    var pa=positions[a];var pb=positions[b];var pc=positions[c];
                    if(Vector3.Dot(Vector3.Cross(pb-pa,pc-pb),normal)<=1e-10f)continue;
                    bool occupied=remaining.Any(x=>x!=a&&x!=b&&x!=c &&
                        Vector3.Dot(Vector3.Cross(pb-pa,positions[x]-pa),normal)>=-1e-9f &&
                        Vector3.Dot(Vector3.Cross(pc-pb,positions[x]-pb),normal)>=-1e-9f &&
                        Vector3.Dot(Vector3.Cross(pa-pc,positions[x]-pc),normal)>=-1e-9f);
                    if(occupied)continue;
                    indices.AddRange(new[]{a,b,c});remaining.RemoveAt(i);found=true;break;
                }
                if(!found)throw new InvalidOperationException("Cannot triangulate this face safely; cut was not applied.");
            }
            if(remaining.Count==3)indices.AddRange(remaining);
        }
    }
}

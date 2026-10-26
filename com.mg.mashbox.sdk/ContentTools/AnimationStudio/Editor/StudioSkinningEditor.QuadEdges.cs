using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private static HashSet<ulong> QuadWireDiagonals(Mesh mesh) => StudioQuadWire.Diagonals(mesh);
    }

    internal static class StudioQuadWire
    {
        // Recover conservative quad pairs for DISPLAY only. Never modify mesh topology.
        internal static HashSet<ulong> Diagonals(Mesh mesh)
        {
            var hidden=new HashSet<ulong>();
            var points=mesh.vertices;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                if(mesh.GetTopology(sub)!=MeshTopology.Triangles) continue;
                var triangles=mesh.GetTriangles(sub);
                var edges=new Dictionary<ulong,List<(int triangle,int a,int b,int opposite)>>();
                void Add(int t,int a,int b,int c)
                {
                    ulong key=((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
                    if(!edges.TryGetValue(key,out var list)) { list=new List<(int,int,int,int)>();edges.Add(key,list); }
                    list.Add((t,a,b,c));
                }
                for(int i=0;i<triangles.Length;i+=3)
                { Add(i/3,triangles[i],triangles[i+1],triangles[i+2]);Add(i/3,triangles[i+1],triangles[i+2],triangles[i]);Add(i/3,triangles[i+2],triangles[i],triangles[i+1]); }
                var candidates=new List<(float score,ulong edge,int first,int second)>();
                foreach(var entry in edges)
                {
                    if(entry.Value.Count!=2) continue;
                    var x=entry.Value[0];var y=entry.Value[1];
                    if(x.a!=y.b || x.b!=y.a || x.opposite==y.opposite) continue;
                    Vector3 a=points[x.a],b=points[x.b],c=points[x.opposite],d=points[y.opposite];
                    var n1=Vector3.Cross(b-a,c-a);var n2=Vector3.Cross(a-b,d-b);
                    if(n1.sqrMagnitude<1e-14f || n2.sqrMagnitude<1e-14f || Vector3.Dot(n1.normalized,n2.normalized)<.98f) continue;
                    var normal=(n1.normalized+n2.normalized).normalized;
                    var corners=new[]{a,d,b,c}; float score=0;bool convex=true;
                    for(int k=0;k<4;k++)
                    {
                        var incoming=corners[k]-corners[(k+3)%4];var outgoing=corners[(k+1)%4]-corners[k];
                        if(incoming.sqrMagnitude<1e-14f || outgoing.sqrMagnitude<1e-14f || Vector3.Dot(Vector3.Cross(incoming,outgoing),normal)<=0) { convex=false;break; }
                        float cosine=Mathf.Abs(Vector3.Dot(incoming.normalized,outgoing.normalized));
                        if(cosine>.85f) { convex=false;break; }
                        score+=cosine; // Prefer the most rectangular pairing.
                    }
                    if(convex) candidates.Add((score,entry.Key,x.triangle,y.triangle));
                }
                candidates.Sort((a,b)=> { int cmp=a.score.CompareTo(b.score);return cmp!=0?cmp:a.edge.CompareTo(b.edge); });
                var paired=new HashSet<int>();
                foreach(var c in candidates)
                {
                    if(paired.Contains(c.first)||paired.Contains(c.second)) continue;
                    paired.Add(c.first);paired.Add(c.second);hidden.Add(c.edge);
                }
            }
            return hidden;
        }

        internal static Vector3[] Lines(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var hidden = Diagonals(mesh);
            var used = new HashSet<ulong>();
            var lines = new List<Vector3>();
            void Edge(int a, int b, bool inferQuads)
            {
                ulong key = ((ulong)(uint)Math.Min(a,b) << 32) | (uint)Math.Max(a,b);
                if (a == b || (inferQuads && hidden.Contains(key)) || !used.Add(key)) return;
                lines.Add(vertices[a]); lines.Add(vertices[b]);
            }
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var topology = mesh.GetTopology(sub);
                int size = topology == MeshTopology.Quads ? 4 : topology == MeshTopology.Triangles ? 3 : 0;
                if (size == 0) continue;
                var indices = mesh.GetIndices(sub);
                for (int i = 0; i < indices.Length; i += size)
                    for (int edge = 0; edge < size; edge++)
                        Edge(indices[i + edge], indices[i + (edge + 1) % size], size == 3);
            }
            return lines.ToArray();
        }
    }
}

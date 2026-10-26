using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;

namespace MashBoxSDK.AnimationStudio
{
    // UV charts are sampled separately so a seam never averages two UV islands.
    internal sealed class StudioRetopologyUVTransfer
    {
        private struct Tri { internal Vector3 a,b,c; internal Vector2 u,v,w; internal Bounds bounds; internal int chart; }
        private sealed class Node { internal Bounds bounds; internal int start,count; internal Node left,right; internal HashSet<int> charts; }
        private readonly Tri[] triangles;
        private readonly Node root;
        internal StudioRetopologyUVTransfer(Mesh mesh)
        {
            if(!mesh)throw new InvalidOperationException("The reference mesh is missing.");
            var p=mesh.vertices;var uv=mesh.uv;var indices=mesh.triangles;
            if(uv.Length!=p.Length||indices.Length==0)throw new InvalidOperationException("The reference needs UV0 coordinates and triangle faces before transferring UVs.");
            if(uv.Any(v=>float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsInfinity(v.x)||float.IsInfinity(v.y)))throw new InvalidOperationException("The reference contains invalid UV coordinates.");
            var parents=Enumerable.Range(0,indices.Length/3).ToArray();
            int Find(int i){while(parents[i]!=i){parents[i]=parents[parents[i]];i=parents[i];}return i;}
            var corners=new Dictionary<(Vector3,Vector2),int>();
            var ids=new int[p.Length];
            for(int i=0;i<p.Length;i++){var key=(p[i],uv[i]);if(!corners.TryGetValue(key,out int id)){id=corners.Count;corners.Add(key,id);}ids[i]=id;}
            var edges=new Dictionary<(int,int),int>();
            var list=new List<Tri>();
            for(int t=0;t<parents.Length;t++)
            {
                for(int j=0;j<3;j++)
                {
                    int a=ids[indices[t*3+j]],b=ids[indices[t*3+(j+1)%3]];var key=(Math.Min(a,b),Math.Max(a,b));
                    if(edges.TryGetValue(key,out int other))parents[Find(t)]=Find(other);else edges.Add(key,t);
                }
            }
            for(int t=0;t<parents.Length;t++)
            {
                int a=indices[t*3],b=indices[t*3+1],c=indices[t*3+2];
                if(Vector3.Cross(p[b]-p[a],p[c]-p[a]).sqrMagnitude<1e-20f)continue;
                var tri=new Tri{a=p[a],b=p[b],c=p[c],u=uv[a],v=uv[b],w=uv[c],chart=Find(t),bounds=new Bounds(p[a],Vector3.zero)};
                tri.bounds.Encapsulate(p[b]);tri.bounds.Encapsulate(p[c]);list.Add(tri);
            }
            triangles=list.ToArray();
            if(triangles.Length==0)throw new InvalidOperationException("The reference has no usable surface triangles.");
            root=Build(0,triangles.Length);
        }
        private Node Build(int start,int count)
        {
            var n=new Node{start=start,count=count,bounds=triangles[start].bounds,charts=new HashSet<int>()};
            for(int i=start;i<start+count;i++){n.bounds.Encapsulate(triangles[i].bounds);n.charts.Add(triangles[i].chart);}
            if(count<=12)return n;
            var s=n.bounds.size;int axis=s.x>s.y?(s.x>s.z?0:2):(s.y>s.z?1:2);
            Array.Sort(triangles,start,count,Comparer<Tri>.Create((a,b)=>a.bounds.center[axis].CompareTo(b.bounds.center[axis])));
            int half=count/2;n.left=Build(start,half);n.right=Build(start+half,count-half);return n;
        }
        private void Search(Node n,Vector3 p,int chart,ref float best,ref int hit,ref Vector3 point)
        {
            if(n.bounds.SqrDistance(p)>best||(chart>=0&&!n.charts.Contains(chart)))return;
            if(n.left!=null)
            {
                var a=n.left;var b=n.right;if(a.bounds.SqrDistance(p)>b.bounds.SqrDistance(p)){a=n.right;b=n.left;}
                Search(a,p,chart,ref best,ref hit,ref point);Search(b,p,chart,ref best,ref hit,ref point);return;
            }
            for(int i=n.start;i<n.start+n.count;i++)
            {
                var t=triangles[i];if(chart>=0&&chart!=t.chart)continue;
                var q=StudioRetopologySurface.Closest(p,t.a,t.b,t.c);float d=(q-p).sqrMagnitude;
                if(d>=best)continue;best=d;hit=i;point=q;
            }
        }
        private Vector2 Sample(Vector3 p,int chart,out int foundChart)
        {
            float best=float.PositiveInfinity;int hit=-1;var q=p;Search(root,p,chart,ref best,ref hit,ref q);
            if(hit<0)throw new InvalidOperationException("Could not sample the reference UVs.");
            var t=triangles[hit];foundChart=t.chart;
            var a=t.b-t.a;var b=t.c-t.a;var v=q-t.a;
            float aa=Vector3.Dot(a,a),ab=Vector3.Dot(a,b),bb=Vector3.Dot(b,b),va=Vector3.Dot(v,a),vb=Vector3.Dot(v,b);
            float denominator=aa*bb-ab*ab;
            float y=(bb*va-ab*vb)/denominator,z=(aa*vb-ab*va)/denominator;
            var uv=t.u*(1-y-z)+t.v*y+t.w*z;
            if(float.IsNaN(uv.x)||float.IsNaN(uv.y)||float.IsInfinity(uv.x)||float.IsInfinity(uv.y))throw new InvalidOperationException("UV sampling produced an invalid coordinate.");
            return uv;
        }
        internal void Transfer(Part part)
        {
            if(part.faces.Count==0)throw new InvalidOperationException("Create a working mesh before transferring UVs.");
            var vertexUV=part.vertices.Select(p=>Sample(p,-1,out _)).ToList();
            var faceUV=new List<Vector2[]>();
            foreach(var face in part.faces)
            {
                var center=Vector3.zero;foreach(int id in face.vertices)center+=part.vertices[id];center/=face.vertices.Length;
                Sample(center,-1,out int chart);
                faceUV.Add(face.vertices.Select(id=>Sample(part.vertices[id],chart,out _)).ToArray());
            }
            part.uv=vertexUV;
            for(int i=0;i<part.faces.Count;i++)part.faces[i].cornerUV=faceUV[i];
            part.needsUV=false;
        }
    }
}

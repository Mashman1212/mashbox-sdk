using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
namespace MashBoxSDK.AnimationStudio
{
    internal static partial class StudioMeshFracture
    {
        // Partition a constrained section, never contract its crack perimeter.
        // Every fan lies in a convex patch (or a verified visibility kernel).
        private static List<int[]> SimplifyInterior(SurfaceWork work,List<int> outer,List<List<int>> holes,V normal,CancellationToken cancel)
        {
            double Side(int a,int b,V p)=>V.Dot(V.Cross(work.Points[b]-work.Points[a],p-work.Points[a]),normal);
            V Mean(List<int> ring){var p=new V();foreach(int v in ring)p+=work.Points[v];return p/ring.Count;}
            bool FanFits(List<int> ring,V center)=>Enumerable.Range(0,ring.Count).All(i=>Side(ring[i],ring[(i+1)%ring.Count],center)>1e-20);
            bool Convex(List<int> ring)=>ring.Distinct().Count()==ring.Count&&Enumerable.Range(0,ring.Count).All(i=>Side(ring[i],ring[(i+1)%ring.Count],work.Points[ring[(i+2)%ring.Count]])>=-1e-22);
            var patches=new List<List<int>>();
            if(holes.Count==0&&FanFits(outer,Mean(outer)))patches.Add(outer);
            else
            {
                var triangles=TriangulateDomain(work,outer,holes,normal);
                for(int i=0;i<triangles.Length;i+=3)patches.Add(new List<int>{triangles[i],triangles[i+1],triangles[i+2]});
                bool changed=true;
                while(changed)
                {
                    cancel.ThrowIfCancellationRequested();changed=false;
                    var edges=new Dictionary<(int,int),(int patch,int edge)>();
                    for(int a=0;a<patches.Count&&!changed;a++)for(int e=0;!changed&&e<patches[a].Count;e++)
                    {
                        var first=patches[a];int u=first[e],v=first[(e+1)%first.Count];
                        if(edges.TryGetValue((v,u),out var other)&&other.patch!=a)
                        {
                            var second=patches[other.patch];var joined=new List<int>();
                            for(int k=1;k<=first.Count;k++)joined.Add(first[(e+k)%first.Count]);
                            for(int k=2;k<second.Count;k++)joined.Add(second[(other.edge+k)%second.Count]);
                            if(Convex(joined)&&FanFits(joined,Mean(joined)))
                            {patches[other.patch]=joined;patches.RemoveAt(a);changed=true;}
                        }
                        else edges[(u,v)]=(a,e);
                    }
                }
            }
            var faces=new List<int[]>();
            foreach(var ring in patches)
            {
                cancel.ThrowIfCancellationRequested();
                if(ring.Count==3){faces.Add(ring.ToArray());continue;}
                var center=Mean(ring);
                if(!FanFits(ring,center))throw new InvalidOperationException("A simplified fracture fan would overlap its boundary.");
                int id=work.Vertex(center);
                for(int i=0;i<ring.Count;i+=2)
                    if(i+1<ring.Count)faces.Add(new[]{id,ring[i],ring[(i+1)%ring.Count],ring[(i+2)%ring.Count]});
                    else faces.Add(new[]{id,ring[i],ring[0]});
            }
            return faces;
        }
    }
}

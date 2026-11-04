using System;
using System.Collections.Generic;
using System.Linq;

namespace MashBoxSDK.AnimationStudio
{
    internal static partial class StudioMeshFracture
    {
        private static double RingArea(SurfaceWork work,List<int> ring,V normal)
        {var a=work.Points[ring[0]];double area=0;for(int i=1;i+1<ring.Count;i++)area+=V.Dot(V.Cross(work.Points[ring[i]]-a,work.Points[ring[i+1]]-a),normal);return area;}

        // Bridge cavity rings without inserting geometric samples. The duplicated
        // bridge endpoints are topology traversal only, not duplicated vertices.
        private static List<(List<int> outer,List<List<int>> holes)> SectionDomains(SurfaceWork work,List<List<int>> loops,V normal)
        {
            var domains=loops.Where(l=>RingArea(work,l,normal)>0).Select(l=>(outer:l,holes:new List<List<int>>())).ToList();
            foreach(var hole in loops.Where(l=>RingArea(work,l,normal)<=0))
            {
                var owners=domains.Where(d=>InsideRing(work,hole[0],d.outer,normal)).OrderBy(d=>RingArea(work,d.outer,normal)).ToList();
                if(owners.Count==0)throw new InvalidOperationException("A fracture section has an inward shell without an enclosing solid.");
                owners[0].holes.Add(hole);
            }
            return domains;
        }
        private static bool InsideRing(SurfaceWork work,int vertex,List<int> ring,V normal)
        {return InsideRingPoint(work,work.Points[vertex],ring,normal);}
        private static bool InsideRingPoint(SurfaceWork work,V point,List<int> ring,V normal)
        {
            var axis=Math.Abs(normal.x)<.8?new V(1,0,0):new V(0,1,0);var x=V.Cross(normal,axis);var y=V.Cross(normal,x);bool inside=false;
            double px=V.Dot(point,x),py=V.Dot(point,y);
            for(int i=0,j=ring.Count-1;i<ring.Count;j=i++)
            {var a=work.Points[ring[i]];var b=work.Points[ring[j]];double ax=V.Dot(a,x),ay=V.Dot(a,y),bx=V.Dot(b,x),by=V.Dot(b,y);if((ay>py)!=(by>py)&&px<(bx-ax)*(py-ay)/(by-ay)+ax)inside=!inside;}
            return inside;
        }
        private static int[] TriangulateDomain(SurfaceWork work,List<int> outer,List<List<int>> holes,V normal)
        {
            var originals=new[]{outer}.Concat(holes).ToList();var simplified=new List<List<int>>();
            foreach(var original in originals)
            {
                var ring=original.ToList();bool changed=true;
                while(changed&&ring.Count>3)
                {changed=false;for(int i=0;i<ring.Count&&ring.Count>3;i++){int a=ring[(i+ring.Count-1)%ring.Count],b=ring[i],c=ring[(i+1)%ring.Count];if(SegmentDistance(work.Points[b],work.Points[a],work.Points[c])>2e-8)continue;ring.RemoveAt(i);changed=true;i--;}}
                simplified.Add(ring);
            }
            var triangles=TriangulateDomainCore(work,simplified[0],simplified.Skip(1).ToList(),normal).ToList();
            for(int r=0;r<originals.Count;r++)for(int e=0;e<simplified[r].Count;e++)
            {
                int a=simplified[r][e],b=simplified[r][(e+1)%simplified[r].Count],at=originals[r].IndexOf(a);var chain=new List<int>{a};
                do{at=(at+1)%originals[r].Count;chain.Add(originals[r][at]);}while(originals[r][at]!=b);
                if(chain.Count==2)continue;bool found=false;
                for(int t=0;t<triangles.Count&&!found;t+=3)for(int k=0;k<3;k++)
                {
                    if(triangles[t+k]!=a||triangles[t+(k+1)%3]!=b)continue;int c=triangles[t+(k+2)%3];triangles.RemoveRange(t,3);
                    for(int j=0;j+1<chain.Count;j++)triangles.AddRange(new[]{chain[j],chain[j+1],c});found=true;break;
                }
                if(!found)throw new InvalidOperationException("A fracture boundary sample could not be restored.");
            }
            return triangles.ToArray();
        }
        private static int[] TriangulateDomainCore(SurfaceWork work,List<int> outer,List<List<int>> holes,V normal)
        {
            double Cross(int a,int b,int c)=>V.Dot(V.Cross(work.Points[b]-work.Points[a],work.Points[c]-work.Points[a]),normal);
            var rings=new[]{outer}.Concat(holes).ToList();var polygon=outer.ToList();
            foreach(var hole in holes.OrderByDescending(h=>h.Max(v=>work.Points[v].x)))
            {
                int bestA=-1,bestB=-1;double best=double.PositiveInfinity;
                for(int a=0;a<polygon.Count;a++)for(int b=0;b<hole.Count;b++)
                {
                    int va=polygon[a],vb=hole[b];double length=(work.Points[va]-work.Points[vb]).Length;if(length>=best)continue;
                    bool blocked=false;
                    foreach(var ring in rings.Concat(new[]{polygon}))for(int e=0;e<ring.Count;e++)
                    {
                        int c=ring[e],d=ring[(e+1)%ring.Count];if(c==va||c==vb||d==va||d==vb)continue;
                        double abC=Cross(va,vb,c),abD=Cross(va,vb,d),cdA=Cross(c,d,va),cdB=Cross(c,d,vb);
                        if(abC*abD<=0&&cdA*cdB<=0){blocked=true;break;}
                    }
                    var midpoint=(work.Points[va]+work.Points[vb])*.5;
                    if(blocked||!InsideRingPoint(work,midpoint,outer,normal)||holes.Any(h=>InsideRingPoint(work,midpoint,h,normal)))continue;
                    best=length;bestA=a;bestB=b;
                }
                if(bestA<0)throw new InvalidOperationException("A cavity boundary could not be connected without crossing the solid section.");
                var traversal=Enumerable.Range(0,hole.Count).Select(i=>hole[(bestB+i)%hole.Count]).ToList();traversal.Add(hole[bestB]);traversal.Add(polygon[bestA]);polygon.InsertRange(bestA+1,traversal);
            }
            var triangles=new List<int>();var remaining=polygon.ToList();
            while(remaining.Count>3)
            {
                int best=-1;double quality=-1;
                for(int i=0;i<remaining.Count;i++)
                {
                    int a=remaining[(i+remaining.Count-1)%remaining.Count],b=remaining[i],c=remaining[(i+1)%remaining.Count];double area=Cross(a,b,c);if(area<=1e-20)continue;
                    if(remaining.Any(v=>v!=a&&v!=b&&v!=c&&Cross(a,b,v)>=-1e-22&&Cross(b,c,v)>=-1e-22&&Cross(c,a,v)>=-1e-22))continue;
                    var ab=work.Points[b]-work.Points[a];var bc=work.Points[c]-work.Points[b];var ca=work.Points[a]-work.Points[c];double q=area/(V.Dot(ab,ab)+V.Dot(bc,bc)+V.Dot(ca,ca));
                    if(q>quality){quality=q;best=i;}
                }
                if(best<0)throw new InvalidOperationException("A fracture section is self-intersecting or numerically collapsed.");
                triangles.AddRange(new[]{remaining[(best+remaining.Count-1)%remaining.Count],remaining[best],remaining[(best+1)%remaining.Count]});remaining.RemoveAt(best);
            }
            if(remaining.Count==3){if(Cross(remaining[0],remaining[1],remaining[2])<=1e-20)throw new InvalidOperationException("A fracture section contains a collapsed triangle.");triangles.AddRange(remaining);}
            return triangles.ToArray();
        }
    }
}

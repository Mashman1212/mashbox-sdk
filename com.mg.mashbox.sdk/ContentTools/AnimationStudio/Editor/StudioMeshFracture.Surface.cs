using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal static partial class StudioMeshFracture
    {
        // Fracture cells are closed surface solids. Only their shared interfaces
        // are remeshed: no volume-grid topology leaks into the exterior mesh.
        private sealed class SurfaceFace
        {
            internal List<int> V;
            internal int Source=-1, Interface=-1;
        }
        private sealed class Region
        {
            internal int Cell, Source=-1, Other=-1;
            internal List<int> Loop;
            internal List<int> Triangles;
            internal Dictionary<int,int> CornerSources;
            internal bool Planar;
        }
        private readonly struct CutPlane
        {
            internal readonly V Normal;
            internal readonly double Distance;
            internal CutPlane(V normal,double distance){double length=normal.Length;Normal=normal/length;Distance=distance/length;}
            internal double At(V p)=>V.Dot(Normal,p)-Distance;
        }
        private sealed class SurfaceWork
        {
            internal readonly List<V> Points=new List<V>();
            internal readonly List<Vector3> FloatPoints=new List<Vector3>();
            private readonly Dictionary<(long,long,long),List<int>> buckets=new Dictionary<(long,long,long),List<int>>();
            internal int Vertex(V p)
            {
                const double tolerance=2e-8;
                long x=(long)Math.Floor(p.x/tolerance),y=(long)Math.Floor(p.y/tolerance),z=(long)Math.Floor(p.z/tolerance);
                for(long dx=-1;dx<=1;dx++)for(long dy=-1;dy<=1;dy++)for(long dz=-1;dz<=1;dz++)
                    if(buckets.TryGetValue((x+dx,y+dy,z+dz),out var nearby))foreach(int v in nearby)if((Points[v]-p).Length<=tolerance)return v;
                int id=Points.Count;Points.Add(p);FloatPoints.Add(p.Float);
                if(!buckets.TryGetValue((x,y,z),out var list))buckets.Add((x,y,z),list=new List<int>());list.Add(id);return id;
            }
        }

        internal static Result Compute(Input original,Settings settings,CancellationToken cancel,Action<Progress> report)
        {
            void Progress(string stage,float value){cancel.ThrowIfCancellationRequested();report?.Invoke(new Progress(stage,value));}
            if(settings.Count<2||settings.Count>64||!Finite(settings.Detail)||settings.Detail<=0||!Finite(settings.Roughness)||settings.Roughness<0||
                !Finite(settings.WeldTolerance)||settings.WeldTolerance<0||!Finite(settings.Gap)||settings.Gap<0||!Finite(settings.UVScale)||settings.UVScale<=0||
                !Finite(settings.Bias)||settings.Bias<0||settings.Bias>1||!Finite(settings.Variation)||settings.Variation<0||settings.Variation>1||!Finite(settings.Direction)||settings.Direction.sqrMagnitude<1e-12f)
                throw new InvalidOperationException("Fracture requires 2–64 pieces, finite nonnegative roughness/weld/gap, positive detail/UV scales and a nonzero bias direction.");
            if(original.Positions.Length==0||original.Positions.Any(p=>!Finite(p)))throw new InvalidOperationException("Fracture requires finite source geometry.");
            if(original.Normals.Any(v=>!Finite(v))||original.Tangents.Any(v=>!Finite(v.x)||!Finite(v.y)||!Finite(v.z)||!Finite(v.w))||
                original.Colors.Any(v=>!Finite(v.r)||!Finite(v.g)||!Finite(v.b)||!Finite(v.a))||original.UV.Any(c=>c!=null&&c.Any(v=>!Finite(v.x)||!Finite(v.y)||!Finite(v.z)||!Finite(v.w))))
                throw new InvalidOperationException("Fracture requires finite source attributes.");
            var source=new Input{Positions=original.Positions.ToArray(),Normals=original.Normals,Tangents=original.Tangents,Colors=original.Colors,UV=original.UV,
                Dimensions=original.Dimensions,Geometry=original.Geometry.ToArray(),Triangles=original.Triangles,Materials=original.Materials,AuthoredFaces=original.AuthoredFaces,Submeshes=original.Submeshes};
            Progress("Welding source boundaries",0);
            WeldSource(source,settings.WeldTolerance,cancel);
            var low=source.Positions.Aggregate(Vector3.Min);var high=source.Positions.Aggregate(Vector3.Max);double scale=(high-low).magnitude;
            if(scale<1e-10||double.IsInfinity(scale))throw new InvalidOperationException("Fracture requires a finite nonzero solid.");
            var origin=new V((low+high)*.5f);var work=new SurfaceWork();var raw=new int[source.Positions.Length];
            var geo=new Dictionary<int,int>();
            for(int i=0;i<raw.Length;i++)
            {if(!geo.TryGetValue(source.Geometry[i],out int id)){id=work.Vertex((new V(source.Positions[i])-origin)/scale);geo.Add(source.Geometry[i],id);}raw[i]=id;}
            var surface=new List<SurfaceFace>();
            for(int i=0;i<source.Triangles.Length;i++)
            {var vertices=source.Triangles[i].Select(v=>raw[v]).ToList();if(vertices.Distinct().Count()<3)continue;surface.Add(new SurfaceFace{V=vertices,Source=i});}
            ValidateClosed(surface.Select(f=>f.V),"The source must be a closed manifold solid after boundary welding");
            double volume=surface.Sum(f=>V.Dot(work.Points[f.V[0]],V.Cross(work.Points[f.V[1]],work.Points[f.V[2]])))/6;
            if(volume<=1e-12)throw new InvalidOperationException("Fracture requires consistently outward-wound solid surfaces.");
            int originalPointCount=work.Points.Count;
            var query=new SurfaceQuery(work,surface);
            Progress("Generating interior seeds",.05f);
            var random=new System.Random(settings.Seed);var sites=new List<V>();var min=(new V(low)-origin)/scale;var max=(new V(high)-origin)/scale;
            for(int i=0;i<settings.Count;i++)
            {
                bool found=false;
                for(int attempt=0;attempt<100000;attempt++)
                {
                    if((attempt&127)==0)cancel.ThrowIfCancellationRequested();
                    var p=new V(min.x+(max.x-min.x)*random.NextDouble(),min.y+(max.y-min.y)*random.NextDouble(),min.z+(max.z-min.z)*random.NextDouble());
                    if(i>0&&random.NextDouble()<settings.Variation){var anchor=sites[random.Next(sites.Count)];p=anchor+(p-anchor)*(.2+.6*(1-settings.Variation));}
                    if(sites.Any(s=>(s-p).Length<1e-5)||!query.Inside(p))continue;sites.Add(p);found=true;break;
                }
                if(!found)throw new InvalidOperationException("Could not place all seeds inside the solid. Check its closure or reduce Piece count.");
            }
            var axis=new V(settings.Direction.normalized);V Metric(V p)=>p-axis*(V.Dot(axis,p)*(1-1/(1+settings.Bias*5)));
            var planes=new CutPlane[sites.Count,sites.Count];
            for(int a=0;a<sites.Count;a++)for(int b=a+1;b<sites.Count;b++)
            {var x=Metric(sites[a]);var y=Metric(sites[b]);var n=Metric(y-x);double d=(V.Dot(y,y)-V.Dot(x,x))*.5;planes[a,b]=new CutPlane(n,d);planes[b,a]=new CutPlane(n*-1,-d);}
            var regions=new List<Region>();
            for(int cell=0;cell<sites.Count;cell++)
            {
                var faces=surface.Select(f=>new SurfaceFace{V=new List<int>(f.V),Source=f.Source}).ToList();
                // Nearest neighbors usually reject most distant bisectors outright.
                var order=Enumerable.Range(0,sites.Count).Where(i=>i!=cell).OrderBy(i=>(sites[i]-sites[cell]).Length).ToArray();
                for(int c=0;c<order.Length;c++)
                {Progress("Cutting solid cells",.1f+.4f*(cell+(float)c/order.Length)/sites.Count);faces=ClipSolid(work,faces,planes[cell,order[c]],order[c],cancel);if(faces.Count==0)break;}
                if(faces.Count==0)throw new InvalidOperationException("A requested cell produced no solid.");
                regions.AddRange(MergeSurfaceRegions(work,faces,source,cell));
            }
            Progress("Welding shared patch boundaries",.51f);
            CleanRegionBoundaries(work,regions,originalPointCount,Math.Max(2e-8,settings.WeldTolerance/scale),cancel);
            ConformRegionEdges(work,regions,cancel);
            // Retain only one interface definition. Reversed copies are generated
            // from this SAME quad patch, never independently retopologized.
            var interfaces=regions.Where(r=>r.Other>=0&&r.Cell<r.Other).ToList();
            foreach(var region in interfaces)
            {
                var matches=regions.Where(r=>r.Cell==region.Other&&r.Other==region.Cell).ToArray();
                if(!matches.Any(r=>SameRing(region.Loop,r.Loop)))throw new InvalidOperationException("Neighboring fracture boundaries disagree; no partial fragments were published.");
            }
            RefinePatchBoundaries(work,regions,interfaces,settings.Detail/scale,cancel);
            MakeEvenPatchBoundaries(work,regions,interfaces);
            var result=new Result{Source=source,Parameters=settings.Copy()};
            void SyncPoints(){while(result.Positions.Count<work.Points.Count){var p=(origin+work.Points[result.Positions.Count]*scale).Float;if(!Finite(p))throw new InvalidOperationException("Fracture generated non-finite geometry.");result.Positions.Add(p);result.Original.Add(p);result.SurfacePositions.Add(p);}}
            SyncPoints();var pieces=Enumerable.Range(0,sites.Count).Select(_=>new Piece()).ToArray();
            var sourceFaces=Enumerable.Range(0,source.Triangles.Length).GroupBy(t=>source.AuthoredFaces[t]).ToDictionary(g=>g.Key,g=>g.ToArray());
            int CornerTriangle(Region region,int vertex)
            {
                var p=new V(result.SurfacePositions[vertex]);int best=region.Source;double score=double.PositiveInfinity;
                foreach(int index in sourceFaces[source.AuthoredFaces[region.Source]])
                {
                    var tri=source.Triangles[index];var a=new V(source.Positions[tri[0]]);var b=new V(source.Positions[tri[1]])-a;var c=new V(source.Positions[tri[2]])-a;var q=p-a;
                    double bb=V.Dot(b,b),bc=V.Dot(b,c),cc=V.Dot(c,c),den=bb*cc-bc*bc;if(den<1e-30)continue;
                    double y=(cc*V.Dot(q,b)-bc*V.Dot(q,c))/den,z=(bb*V.Dot(q,c)-bc*V.Dot(q,b))/den;
                    double distance=(q-b*y-c*z).Length/scale+Math.Max(0,-y)+Math.Max(0,-z)+Math.Max(0,y+z-1);
                    if(distance<score){score=distance;best=index;}
                }
                if(score>1e-4)throw new InvalidOperationException("A source attribute sample left its authored surface.");return best;
            }
            foreach(var region in regions.Where(r=>r.Source>=0))
            {
                var exteriorNormal=new V();for(int k=1;k+1<region.Loop.Count;k++)exteriorNormal+=V.Cross(work.Points[region.Loop[k]]-work.Points[region.Loop[0]],work.Points[region.Loop[k+1]]-work.Points[region.Loop[0]]);
                var render=region.Planar?TriangulateDomain(work,region.Loop,new List<List<int>>(),exteriorNormal/exteriorNormal.Length):region.Triangles.ToArray();
                if(render.Any(v=>!region.Loop.Contains(v)))throw new InvalidOperationException("Exterior tessellation lost a required source sample.");
                pieces[region.Cell].Faces.Add(new Face{Exterior=region.Source,Vertices=region.Loop.ToArray(),CornerTriangles=region.Loop.Select(v=>CornerTriangle(region,v)).ToArray(),RenderTriangles=render});
            }
            for(int i=0;i<interfaces.Count;i++)
            {
                Progress("Building shared quad patches",.55f+.3f*i/Math.Max(1,interfaces.Count));var region=interfaces[i];var normal=planes[region.Cell,region.Other].Normal;
                if(RingArea(work,region.Loop,normal)<=0)continue;
                var related=interfaces.Where(r=>r.Cell==region.Cell&&r.Other==region.Other).Select(r=>r.Loop).ToList();
                var domain=SectionDomains(work,related,normal).Single(d=>ReferenceEquals(d.outer,region.Loop));
                var boundary=domain.outer.Concat(domain.holes.SelectMany(h=>h)).ToList();
                int[] patchTriangles=TriangulateDomain(work,domain.outer,domain.holes,normal).Select(v=>boundary.IndexOf(v)).ToArray();
                var patch=StudioMeshCuts.FractureQuadPatch(boundary.Select(v=>work.Points[v].Float).ToArray(),normal.Float,patchTriangles,domain.holes.Count==0);
                var ids=new int[patch.points.Length];for(int v=0;v<ids.Length;v++)ids[v]=v<boundary.Count?boundary[v]:work.Vertex(new V(patch.points[v]));
                SyncPoints();
                // Graph-of-height interfaces have identical geometry on both sides.
                // The envelope vanishes at boundaries and near unrelated exterior
                // surfaces, protecting thin wings and multi-interface junctions.
                for(int v=boundary.Count;v<ids.Length;v++)
                {
                    cancel.ThrowIfCancellationRequested();var p=work.Points[ids[v]];double distance=query.Distance(p);
                    for(int e=0;e<region.Loop.Count;e++)distance=Math.Min(distance,SegmentDistance(p,work.Points[region.Loop[e]],work.Points[region.Loop[(e+1)%region.Loop.Count]]));
                    double amplitude=settings.Roughness/scale;double envelope=amplitude>0?Math.Min(1,distance/(5*amplitude)):0;
                    double phase=settings.Seed*.731+region.Cell*1.37+region.Other*2.11,frequency=2*Math.PI*scale/settings.Detail;
                    double noise=Math.Sin(p.x*frequency+phase)*Math.Sin(p.y*frequency*.83+phase*.61)*Math.Cos(p.z*frequency*.91+phase*.37);
                    var displaced=p+normal*(amplitude*envelope*noise);result.Positions[ids[v]]=(origin+displaced*scale).Float;
                }
                foreach(var quad in patch.quads)
                {
                    var face=quad.Select(v=>ids[v]).ToArray();pieces[region.Cell].Faces.Add(new Face{Vertices=face,Exterior=-1,CutNormal=normal.Float});
                    pieces[region.Other].Faces.Add(new Face{Vertices=face.Reverse().ToArray(),Exterior=-1,CutNormal=-normal.Float});
                }
            }
            Progress("Validating fragments",.87f);
            foreach(var piece in pieces)
            {
                cancel.ThrowIfCancellationRequested();ValidateClosed(piece.Faces.Select(f=>f.Vertices.ToList()),"A generated fragment has a non-manifold boundary");
                foreach(var component in Components(piece))result.Pieces.Add(component);
            }
            if(settings.Gap>0){Progress("Applying piece clearance",.92f);ApplyPieceGap(result,settings.Gap,cancel);}
            ValidateResult(result,cancel);Progress("Ready to publish",1);return result;
        }

        private static List<SurfaceFace> ClipSolid(SurfaceWork work,List<SurfaceFace> source,CutPlane plane,int neighbor,CancellationToken cancel)
        {
            const double eps=1e-10;bool outside=false,inside=false;
            foreach(int v in source.SelectMany(f=>f.V).Distinct()){double d=plane.At(work.Points[v]);outside|=d>eps;inside|=d<-eps;}
            if(!outside)return source;if(!inside)return new List<SurfaceFace>();
            var output=new List<SurfaceFace>();var cutEdges=new List<(int,int)>();
            foreach(var face in source)
            {
                cancel.ThrowIfCancellationRequested();var loop=new List<int>();
                for(int i=0;i<face.V.Count;i++)
                {
                    int a=face.V[i],b=face.V[(i+1)%face.V.Count];double da=plane.At(work.Points[a]),db=plane.At(work.Points[b]);bool ia=da<=eps,ib=db<=eps;
                    if(ia&&(loop.Count==0||loop[loop.Count-1]!=a))loop.Add(a);
                    if(ia!=ib){int v=Math.Abs(da)<eps?a:Math.Abs(db)<eps?b:work.Vertex(work.Points[a]+(work.Points[b]-work.Points[a])*(da/(da-db)));if(loop.Count==0||loop[loop.Count-1]!=v)loop.Add(v);}
                }
                if(loop.Count>1&&loop[0]==loop[loop.Count-1])loop.RemoveAt(loop.Count-1);if(loop.Count<3)continue;
                output.Add(new SurfaceFace{V=loop,Source=face.Source,Interface=face.Interface});
                for(int i=0;i<loop.Count;i++){int a=loop[i],b=loop[(i+1)%loop.Count];if(Math.Abs(plane.At(work.Points[a]))<1e-7&&Math.Abs(plane.At(work.Points[b]))<1e-7)cutEdges.Add((b,a));}
            }
            var loops=BoundaryLoops(cutEdges);
            foreach(var domain in SectionDomains(work,loops,plane.Normal))
            {
                var triangles=TriangulateDomain(work,domain.outer,domain.holes,plane.Normal);
                for(int i=0;i<triangles.Length;i+=3)output.Add(new SurfaceFace{V=triangles.Skip(i).Take(3).ToList(),Interface=neighbor});
            }
            return output;
        }
private static List<List<int>> BoundaryLoops(IEnumerable<(int a,int b)> input)
        {
            var edges=new Dictionary<(int,int),List<(int a,int b)>>();
            foreach(var e in input){if(e.a==e.b)continue;var key=(Math.Min(e.a,e.b),Math.Max(e.a,e.b));if(!edges.TryGetValue(key,out var list))edges.Add(key,list=new List<(int,int)>());list.Add(e);}
            var boundary=new List<(int a,int b)>();foreach(var list in edges.Values){if(list.Count==1)boundary.Add(list[0]);else if(list.Count!=2||list[0].a!=list[1].b)throw new InvalidOperationException("Fracture found a branching or inconsistently wound section.");}
            var outgoing=new Dictionary<int,int>();foreach(var e in boundary){if(outgoing.ContainsKey(e.a))throw new InvalidOperationException("Fracture section touches or branches at a vertex.");outgoing.Add(e.a,e.b);}
            var used=new HashSet<int>();var loops=new List<List<int>>();foreach(var edge in boundary)
            {
                if(used.Contains(edge.a))continue;var loop=new List<int>();int at=edge.a;
                do{if(!used.Add(at)||!outgoing.TryGetValue(at,out int next))throw new InvalidOperationException("Fracture encountered an open section; increase Weld tolerance only if this is a small source crack.");loop.Add(at);at=next;}while(at!=edge.a);
                if(loop.Count>=3)loops.Add(loop);
            }
            return loops;
        }
        private static IEnumerable<Region> MergeSurfaceRegions(SurfaceWork work,List<SurfaceFace> faces,Input source,int cell)
        {
            foreach(var group in faces.GroupBy(f=>f.Source>=0?(true,source.AuthoredFaces[f.Source]):(false,f.Interface)))
            {
                var edgeList=group.SelectMany(f=>Enumerable.Range(0,f.V.Count).Select(i=>(f.V[i],f.V[(i+1)%f.V.Count])));
                var loops=BoundaryLoops(edgeList);var triangles=new List<int>();var corner=new Dictionary<int,int>();
                foreach(var face in group)
                {
                    for(int k=1;k+1<face.V.Count;k++)triangles.AddRange(new[]{face.V[0],face.V[k],face.V[k+1]});
                    if(face.Source>=0)foreach(int v in face.V)if(!corner.ContainsKey(v))corner.Add(v,face.Source);
                }
                foreach(var loop in loops)
                {
                    var set=new HashSet<int>(loop);var render=new List<int>();for(int t=0;t<triangles.Count;t+=3)if(set.Contains(triangles[t])&&set.Contains(triangles[t+1])&&set.Contains(triangles[t+2]))render.AddRange(triangles.Skip(t).Take(3));
                    var a=work.Points[loop[0]];V n=new V();for(int k=1;k+1<loop.Count;k++)n+=V.Cross(work.Points[loop[k]]-a,work.Points[loop[k+1]]-a);n/=Math.Max(n.Length,1e-20);
                    bool planar=loop.All(v=>Math.Abs(V.Dot(work.Points[v]-a,n))<1e-7);
                    yield return new Region{Cell=cell,Source=group.Key.Item1?group.First().Source:-1,Other=group.Key.Item1?-1:group.Key.Item2,Loop=loop,Triangles=render,CornerSources=corner,Planar=planar};
                }
            }
        }
        private static bool SameRing(List<int> a,List<int> b)=>a.Count==b.Count&&new HashSet<int>(a).SetEquals(b);
        private static void CleanRegionBoundaries(SurfaceWork work,List<Region> regions,int originalCount,double tolerance,CancellationToken cancel)
        {
            bool changed=true;while(changed)
            {
                changed=false;var adjacency=new Dictionary<int,HashSet<int>>();var protectedVertices=new HashSet<int>(regions.Where(r=>r.Source>=0&&!r.Planar).SelectMany(r=>r.Loop));
                foreach(var r in regions)for(int i=0;i<r.Loop.Count;i++){int v=r.Loop[i];if(!adjacency.TryGetValue(v,out var near))adjacency.Add(v,near=new HashSet<int>());near.Add(r.Loop[(i+r.Loop.Count-1)%r.Loop.Count]);near.Add(r.Loop[(i+1)%r.Loop.Count]);}
                foreach(var entry in adjacency)
                {
                    cancel.ThrowIfCancellationRequested();int v=entry.Key;if(v<originalCount||protectedVertices.Contains(v)||entry.Value.Count!=2)continue;var ends=entry.Value.ToArray();
                    if(SegmentDistance(work.Points[v],work.Points[ends[0]],work.Points[ends[1]])>tolerance)continue;
                    if(regions.Any(r=>r.Loop.Contains(v)&&r.Loop.Count<=3))continue;
                    foreach(var r in regions.Where(r=>r.Loop.Contains(v)))
                    {int at=r.Loop.IndexOf(v),previous=r.Loop[(at+r.Loop.Count-1)%r.Loop.Count];r.Loop.RemoveAt(at);for(int i=0;i<r.Triangles.Count;i++)if(r.Triangles[i]==v)r.Triangles[i]=previous;RemoveDegenerateTriangles(r.Triangles);}
                    changed=true;
                }
            }
        }
        private static void RemoveDegenerateTriangles(List<int> triangles)
        {for(int i=triangles.Count-3;i>=0;i-=3)if(triangles[i]==triangles[i+1]||triangles[i+1]==triangles[i+2]||triangles[i+2]==triangles[i])triangles.RemoveRange(i,3);}
        private static double SegmentDistance(V p,V a,V b){var d=b-a;double t=Math.Max(0,Math.Min(1,V.Dot(p-a,d)/Math.Max(1e-30,V.Dot(d,d))));return(p-a-d*t).Length;}
        private static void SplitRegionEdge(Region r,int a,int b,List<int> inserted)
        {
            if(inserted.Count==0)return;
            for(int i=0;i<r.Loop.Count;i++)if(r.Loop[i]==a&&r.Loop[(i+1)%r.Loop.Count]==b)
            {
                r.Loop.InsertRange(i+1,inserted);int previous=a;
                foreach(int v in inserted)
                {
                    if(r.Source>=0)
                    {
                        r.CornerSources[v]=r.CornerSources.TryGetValue(a,out int f)?f:r.Source;
                        for(int t=0;t<r.Triangles.Count;t+=3)for(int k=0;k<3;k++)
                        {
                            int x=r.Triangles[t+k],y=r.Triangles[t+(k+1)%3],z=r.Triangles[t+(k+2)%3];
                            if(!((x==previous&&y==b)||(x==b&&y==previous)))continue;
                            r.Triangles[t]=x;r.Triangles[t+1]=v;r.Triangles[t+2]=z;r.Triangles.AddRange(new[]{v,y,z});goto SplitDone;
                        }
                    }
                    SplitDone:previous=v;
                }
                return;
            }
        }
        private static void ConformRegionEdges(SurfaceWork work,List<Region> regions,CancellationToken cancel)
        {
            var lines=new Dictionary<string,HashSet<int>>();var edges=new List<(Region r,int a,int b,string key)>();
            foreach(var r in regions)for(int i=0;i<r.Loop.Count;i++)
            {
                int a=r.Loop[i],b=r.Loop[(i+1)%r.Loop.Count];var direction=work.Points[b]-work.Points[a];direction/=direction.Length;
                if(direction.x< -1e-8||(Math.Abs(direction.x)<1e-8&&direction.y< -1e-8)||(Math.Abs(direction.x)<1e-8&&Math.Abs(direction.y)<1e-8&&direction.z<0))direction*= -1;
                var moment=V.Cross(work.Points[a],direction);string key=string.Join(",",new[]{direction.x,direction.y,direction.z,moment.x,moment.y,moment.z}.Select(x=>Math.Round(x*100000)));
                if(!lines.TryGetValue(key,out var set))lines.Add(key,set=new HashSet<int>());set.Add(a);set.Add(b);edges.Add((r,a,b,key));
            }
            foreach(var e in edges)
            {
                cancel.ThrowIfCancellationRequested();var d=work.Points[e.b]-work.Points[e.a];double length=V.Dot(d,d);
                var added=lines[e.key].Where(v=>v!=e.a&&v!=e.b).Select(v=>(v,t:V.Dot(work.Points[v]-work.Points[e.a],d)/length))
                    .Where(p=>p.t>1e-7&&p.t<1-1e-7&&SegmentDistance(work.Points[p.v],work.Points[e.a],work.Points[e.b])<1e-7).OrderBy(p=>p.t).Select(p=>p.v).ToList();
                SplitRegionEdge(e.r,e.a,e.b,added);
            }
        }
        private static void ApplyEdgeSplits(List<Region> regions,Dictionary<(int,int),List<int>> splits)
        {
            foreach(var r in regions)
            {var loop=r.Loop.ToArray();for(int i=0;i<loop.Length;i++){int a=loop[i],b=loop[(i+1)%loop.Length];if(splits.TryGetValue((Math.Min(a,b),Math.Max(a,b)),out var ids))SplitRegionEdge(r,a,b,a<b?ids:ids.AsEnumerable().Reverse().ToList());}}
        }
        private static void RefinePatchBoundaries(SurfaceWork work,List<Region> regions,List<Region> interfaces,double detail,CancellationToken cancel)
        {
            var splits=new Dictionary<(int,int),List<int>>();
            foreach(var r in interfaces)for(int i=0;i<r.Loop.Count;i++)
            {
                cancel.ThrowIfCancellationRequested();int a=r.Loop[i],b=r.Loop[(i+1)%r.Loop.Count];var key=(Math.Min(a,b),Math.Max(a,b));if(splits.ContainsKey(key))continue;
                int count=(int)Math.Ceiling((work.Points[a]-work.Points[b]).Length/detail);if(count>128)throw new InvalidOperationException("Requested patch detail is too fine for this mesh; increase Detail scale.");
                if(count<2)continue;var points=new List<int>();for(int k=1;k<count;k++)points.Add(work.Vertex(work.Points[key.Item1]+(work.Points[key.Item2]-work.Points[key.Item1])*((double)k/count)));splits.Add(key,points);
            }
            ApplyEdgeSplits(regions,splits);
        }
        private static void MakeEvenPatchBoundaries(SurfaceWork work,List<Region> regions,List<Region> interfaces)
        {
            // Sparse parity solve: split only the edges needed for even quad disks,
            // propagating each split to every incident interface and exterior face.
            var edges=new List<(int,int)>();var ids=new Dictionary<(int,int),int>();var pivots=new SortedDictionary<int,(HashSet<int> row,bool odd)>();
            foreach(var region in interfaces)
            {
                var row=new HashSet<int>();for(int i=0;i<region.Loop.Count;i++){int a=region.Loop[i],b=region.Loop[(i+1)%region.Loop.Count];var key=(Math.Min(a,b),Math.Max(a,b));if(!ids.TryGetValue(key,out int id)){id=edges.Count;ids.Add(key,id);edges.Add(key);}row.Add(id);}
                bool odd=(region.Loop.Count&1)!=0;
                while(row.Count>0){int pivot=row.Min();if(!pivots.TryGetValue(pivot,out var prior)){pivots.Add(pivot,(row,odd));break;}row.SymmetricExceptWith(prior.row);odd^=prior.odd;}
                if(row.Count==0&&odd)throw new InvalidOperationException("Shared patch boundaries cannot be quadrangulated consistently.");
            }
            var selected=new HashSet<int>();foreach(var item in pivots.Reverse()){bool value=item.Value.odd;foreach(int id in item.Value.row)if(id!=item.Key&&selected.Contains(id))value=!value;if(value)selected.Add(item.Key);}
            var splits=new Dictionary<(int,int),List<int>>();foreach(int id in selected){var edge=edges[id];splits.Add(edge,new List<int>{work.Vertex((work.Points[edge.Item1]+work.Points[edge.Item2])*.5)});}ApplyEdgeSplits(regions,splits);
        }

        private static void WeldSource(Input source,float tolerance,CancellationToken cancel)
        {
            if(tolerance<=0)return;
            var groups=Enumerable.Range(0,source.Geometry.Length).GroupBy(i=>source.Geometry[i]).ToDictionary(g=>g.Key,g=>g.ToArray());
            var counts=new Dictionary<(int,int),int>();foreach(var tri in source.Triangles)for(int k=0;k<3;k++){int a=source.Geometry[tri[k]],b=source.Geometry[tri[(k+1)%3]];var key=(Math.Min(a,b),Math.Max(a,b));counts.TryGetValue(key,out int count);counts[key]=count+1;}
            var boundary=new HashSet<int>(counts.Where(e=>e.Value==1).SelectMany(e=>new[]{e.Key.Item1,e.Key.Item2}));
            var buckets=new Dictionary<(long,long,long),List<int>>();var parent=groups.Keys.ToDictionary(i=>i,i=>i);int Root(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
            foreach(int id in boundary.OrderBy(i=>i))
            {
                cancel.ThrowIfCancellationRequested();var p=source.Positions[groups[id][0]];long x=(long)Math.Floor(p.x/tolerance),y=(long)Math.Floor(p.y/tolerance),z=(long)Math.Floor(p.z/tolerance);
                for(long dx=-1;dx<=1;dx++)for(long dy=-1;dy<=1;dy++)for(long dz=-1;dz<=1;dz++)if(buckets.TryGetValue((x+dx,y+dy,z+dz),out var near))foreach(int other in near)
                    if((source.Positions[groups[other][0]]-p).sqrMagnitude<=tolerance*tolerance){int a=Root(id),b=Root(other);parent[Math.Max(a,b)]=Math.Min(a,b);}
                if(!buckets.TryGetValue((x,y,z),out var list))buckets.Add((x,y,z),list=new List<int>());list.Add(id);
            }
            foreach(var cluster in groups.Keys.GroupBy(Root))
            {var ids=cluster.ToArray();if(ids.Length<2)continue;var center=ids.Aggregate(Vector3.zero,(p,i)=>p+source.Positions[groups[i][0]])/ids.Length;foreach(int id in ids)foreach(int raw in groups[id]){source.Geometry[raw]=cluster.Key;source.Positions[raw]=center;}}
        }
        private static void ValidateClosed(IEnumerable<List<int>> loops,string message)
        {
            var edges=new Dictionary<(int,int),(int count,int balance)>();foreach(var loop in loops)for(int i=0;i<loop.Count;i++){int a=loop[i],b=loop[(i+1)%loop.Count];if(a==b)throw new InvalidOperationException(message+" (collapsed edge).");var key=(Math.Min(a,b),Math.Max(a,b));edges.TryGetValue(key,out var e);edges[key]=(e.count+1,e.balance+(a<b?1:-1));}
            if(edges.Count==0||edges.Any(e=>e.Value.count!=2||e.Value.balance!=0))throw new InvalidOperationException(message+". No partial output was published.");
        }
        private static IEnumerable<Piece> Components(Piece piece)
        {
            var owners=new Dictionary<(int,int),int>();var adjacency=piece.Faces.Select(_=>new List<int>()).ToArray();
            for(int i=0;i<piece.Faces.Count;i++){var loop=piece.Faces[i].Vertices;for(int k=0;k<loop.Length;k++){int a=loop[k],b=loop[(k+1)%loop.Length];var key=(Math.Min(a,b),Math.Max(a,b));if(owners.TryGetValue(key,out int other)){adjacency[i].Add(other);adjacency[other].Add(i);}else owners.Add(key,i);}}
            var visited=new HashSet<int>();for(int i=0;i<piece.Faces.Count;i++){if(!visited.Add(i))continue;var result=new Piece();var queue=new Queue<int>();queue.Enqueue(i);while(queue.Count>0){int f=queue.Dequeue();result.Faces.Add(piece.Faces[f]);foreach(int next in adjacency[f])if(visited.Add(next))queue.Enqueue(next);}yield return result;}
        }
        private static void ApplyPieceGap(Result result,float gap,CancellationToken cancel)
        {
            foreach(var piece in result.Pieces)
            {
                cancel.ThrowIfCancellationRequested();var vertices=piece.Faces.SelectMany(f=>f.Vertices).Distinct().ToArray();
                var center=vertices.Aggregate(Vector3.zero,(p,v)=>p+result.Positions[v])/vertices.Length;
                float clearance=float.PositiveInfinity;
                foreach(var face in piece.Faces.Where(f=>f.Exterior<0))foreach(int v in face.Vertices)
                    clearance=Mathf.Min(clearance,Vector3.Dot(result.Positions[v]-center,face.CutNormal));
                if(float.IsPositiveInfinity(clearance))continue;
                float shrink=gap/(2*clearance);
                if(clearance<=0||shrink>=1)throw new InvalidOperationException("Piece Gap would consume a thin fragment. Reduce the gap.");
                // A uniform contraction preserves every face, seam and thin feature;
                // per-vertex normal offsets can invert tiny source triangles. Each
                // interface retreats at least half the requested gap along its cut
                // plane normal. The slight exterior contraction is intentional.
                var remap=new Dictionary<int,int>();foreach(int vertex in vertices)
                {
                    int id=result.Positions.Count;remap.Add(vertex,id);result.Positions.Add(center+(result.Positions[vertex]-center)*(1-shrink));result.Original.Add(result.Original[vertex]);result.SurfacePositions.Add(result.SurfacePositions[vertex]);
                }
                foreach(var face in piece.Faces){face.Vertices=face.Vertices.Select(v=>remap[v]).ToArray();if(face.RenderTriangles!=null)face.RenderTriangles=face.RenderTriangles.Select(v=>remap[v]).ToArray();}
            }
        }
        private static void ValidateResult(Result result,CancellationToken cancel)
        {
            double scale=(result.Source.Positions.Aggregate(Vector3.Max)-result.Source.Positions.Aggregate(Vector3.Min)).magnitude;
            foreach(var piece in result.Pieces)
            {
                double volume=0;foreach(var face in piece.Faces)
                {
                    cancel.ThrowIfCancellationRequested();var vs=face.Vertices;var triangles=face.RenderTriangles??Enumerable.Range(1,vs.Length-2).SelectMany(k=>new[]{vs[0],vs[k],vs[k+1]}).ToArray();
                    for(int t=0;t<triangles.Length;t+=3)
                    {
                        var a=new V(result.Positions[triangles[t]]);var b=new V(result.Positions[triangles[t+1]]);var c=new V(result.Positions[triangles[t+2]]);
                        if(!Finite(a.Float)||V.Cross(b-a,c-a).Length<scale*scale*1e-15)throw new InvalidOperationException("Fracture produced a degenerate face. Reduce Weld tolerance, roughness or Piece Gap.");
                        var oldA=new V(result.SurfacePositions[triangles[t]]);var oldB=new V(result.SurfacePositions[triangles[t+1]]);var oldC=new V(result.SurfacePositions[triangles[t+2]]);
                        if(V.Dot(V.Cross(b-a,c-a),V.Cross(oldB-oldA,oldC-oldA))<=0)throw new InvalidOperationException("Piece Gap or roughness inverted a face; reduce its value.");
                        volume+=V.Dot(a,V.Cross(b,c))/6;
                    }
                }
                if(volume<=scale*scale*scale*1e-13)throw new InvalidOperationException("Fracture produced an inverted or negligible-volume component.");
            }
        }

        // A compact immutable triangle BVH serves both seed containment and the
        // roughness clearance envelope. It is shared by all cells in a compute.
        private sealed class SurfaceQuery
        {
            private sealed class Branch{internal V Low,High;internal Branch Left,Right;internal int[] Triangles;}
            private readonly SurfaceWork work;private readonly SurfaceFace[] faces;private readonly Branch root;
            internal SurfaceQuery(SurfaceWork work,List<SurfaceFace> faces){this.work=work;this.faces=faces.ToArray();root=Build(Enumerable.Range(0,faces.Count).ToArray());}
            private Branch Build(int[] ids)
            {
                var points=ids.SelectMany(i=>faces[i].V).Select(i=>work.Points[i]).ToArray();var low=new V(points.Min(p=>p.x),points.Min(p=>p.y),points.Min(p=>p.z));var high=new V(points.Max(p=>p.x),points.Max(p=>p.y),points.Max(p=>p.z));
                var branch=new Branch{Low=low,High=high};if(ids.Length<=12){branch.Triangles=ids;return branch;}
                var size=high-low;int axis=size.x>=size.y&&size.x>=size.z?0:size.y>=size.z?1:2;double Axis(V v)=>axis==0?v.x:axis==1?v.y:v.z;
                var sorted=ids.OrderBy(i=>faces[i].V.Sum(v=>Axis(work.Points[v]))).ToArray();branch.Left=Build(sorted.Take(ids.Length/2).ToArray());branch.Right=Build(sorted.Skip(ids.Length/2).ToArray());return branch;
            }
            internal bool Inside(V p)
            {
                // Irrational-looking fixed direction avoids systematic axis/edge hits.
                var d=new V(1,.371390676354,.21761872173);int hits=0;var stack=new Stack<Branch>();stack.Push(root);
                while(stack.Count>0){var b=stack.Pop();if(!RayBox(p,d,b.Low,b.High))continue;if(b.Triangles==null){stack.Push(b.Left);stack.Push(b.Right);continue;}foreach(int id in b.Triangles){var t=faces[id].V;var a=work.Points[t[0]];var e1=work.Points[t[1]]-a;var e2=work.Points[t[2]]-a;var h=V.Cross(d,e2);double det=V.Dot(e1,h);if(Math.Abs(det)<1e-15)continue;double u=V.Dot(p-a,h)/det;if(u<0||u>1)continue;var q=V.Cross(p-a,e1);double v=V.Dot(d,q)/det;if(v<0||u+v>1)continue;if(V.Dot(e2,q)/det>1e-10)hits++;}}
                return(hits&1)!=0;
            }
            private static bool RayBox(V p,V d,V low,V high)
            {double near=0,far=double.PositiveInfinity;foreach(var axis in new[]{(p.x,d.x,low.x,high.x),(p.y,d.y,low.y,high.y),(p.z,d.z,low.z,high.z)}){double a=(axis.Item3-axis.Item1)/axis.Item2,b=(axis.Item4-axis.Item1)/axis.Item2;near=Math.Max(near,Math.Min(a,b));far=Math.Min(far,Math.Max(a,b));if(near>far)return false;}return true;}
            internal double Distance(V p)
            {
                double best=double.PositiveInfinity;var stack=new Stack<Branch>();stack.Push(root);
                while(stack.Count>0)
                {
                    var b=stack.Pop();double x=Math.Max(b.Low.x-p.x,Math.Max(0,p.x-b.High.x)),y=Math.Max(b.Low.y-p.y,Math.Max(0,p.y-b.High.y)),z=Math.Max(b.Low.z-p.z,Math.Max(0,p.z-b.High.z));if(x*x+y*y+z*z>=best*best)continue;
                    if(b.Triangles==null){stack.Push(b.Left);stack.Push(b.Right);continue;}
                    foreach(int id in b.Triangles){var t=faces[id].V;var a=work.Points[t[0]];var b1=work.Points[t[1]];var c=work.Points[t[2]];var n=V.Cross(b1-a,c-a);double length=n.Length;if(length<1e-20)continue;n/=length;double height=V.Dot(p-a,n);var q=p-n*height;
                        if(V.Dot(V.Cross(b1-a,q-a),n)>=0&&V.Dot(V.Cross(c-b1,q-b1),n)>=0&&V.Dot(V.Cross(a-c,q-c),n)>=0)best=Math.Min(best,Math.Abs(height));
                        else best=Math.Min(best,Math.Min(SegmentDistance(p,a,b1),Math.Min(SegmentDistance(p,b1,c),SegmentDistance(p,c,a))));}
                }
                return best;
            }
        }
    }
}









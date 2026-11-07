using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
 internal static partial class StudioMeshFracture
 {
  internal static Result Punch(Input source,Vector3 center,Vector3 normal,Vector3 axis,float radius,int sides,float depth,bool through,float rotation,bool clean,out int[] affected,out int[] geometry,bool limitThroughDepth=false)
  {
   var low=source.Positions.Aggregate(Vector3.Min);var high=source.Positions.Aggregate(Vector3.Max);double scale=(high-low).magnitude;var origin=new V((low+high)*.5f);var work=new SurfaceWork();
   if(scale<1e-10)throw new InvalidOperationException("The mesh has no usable size.");
   var raw=source.Positions.Select(p=>work.Vertex((new V(p)-origin)/scale)).ToArray();var surface=source.Triangles.Select((t,i)=>new SurfaceFace{Source=i,V=t.Select(v=>raw[v]).ToList()}).ToList();
   ValidateClosed(surface.Select(f=>f.V),"Hole Puncher requires a closed, consistently wound surface");
   var c=(new V(center)-origin)/scale;var n=new V(normal.normalized);var x=new V(axis.normalized);var y=V.Cross(n,x);double r=radius/scale;
   var planes=new List<CutPlane>();
   // A polygonal prism intersects the real surface; neither entry nor exit is assumed flat.
   for(int i=0;i<sides;i++){double angle=(i+.5)*Math.PI*2/sides+rotation*Math.PI/180;var outward=x*Math.Cos(angle)+y*Math.Sin(angle);planes.Add(new CutPlane(outward,V.Dot(outward,c)+r*Math.Cos(Math.PI/sides)));}
   bool boundedThrough=through&&limitThroughDepth;
   if(!through||boundedThrough)planes.Add(new CutPlane(n*-1,-V.Dot(n,c)+depth/scale));
   if(boundedThrough)planes.Add(new CutPlane(n,V.Dot(n,c)+r));
   int originalPointCount=work.Points.Count;var outside=new List<SurfaceFace>();var inside=surface;
   foreach(var plane in planes)
   {
    foreach(var face in inside.Where(f=>f.Source>=0))
    {
     var loop=new List<int>();for(int i=0;i<face.V.Count;i++){int a=face.V[i],b=face.V[(i+1)%face.V.Count];double da=plane.At(work.Points[a]),db=plane.At(work.Points[b]);bool ia=da>=-1e-10,ib=db>=-1e-10;if(ia)loop.Add(a);if(ia!=ib)loop.Add(Math.Abs(da)<1e-10?a:Math.Abs(db)<1e-10?b:work.Vertex(work.Points[a]+(work.Points[b]-work.Points[a])*(da/(da-db))));}loop=loop.Where((v,i)=>i==0||v!=loop[i-1]).ToList();if(loop.Count>1&&loop[0]==loop[loop.Count-1])loop.RemoveAt(loop.Count-1);if(loop.Distinct().Count()>=3)outside.Add(new SurfaceFace{Source=face.Source,V=loop});
    }
    inside=ClipSolid(work,inside,plane,planes.IndexOf(plane),CancellationToken.None);if(inside.Count==0)throw new InvalidOperationException("The stamp does not intersect a solid region.");
   }
   affected=inside.Where(f=>f.Source>=0).Select(f=>source.AuthoredFaces[f.Source]).Distinct().ToArray();
   if(affected.Length==0)throw new InvalidOperationException("The stamp did not cross the mesh surface.");
   if(clean)
   {
    var cleanResult=new Result{Source=source,Parameters=new Settings{UVScale=1}};
    foreach(var point in work.Points){var p=(origin+point*scale).Float;cleanResult.Positions.Add(p);cleanResult.Original.Add(p);cleanResult.SurfacePositions.Add(p);}
    var cleanPiece=CleanPunchPatch(cleanResult,work,raw,ref affected,c,n,x,y,r,sides,depth/scale,through&&!boundedThrough,rotation,origin,scale);
    if(boundedThrough&&cleanPiece.Faces.Where(f=>f.Exterior>=0&&f.Vertices.Any(v=>v>=originalPointCount)).SelectMany(f=>f.Vertices).Any(v=>{var p=work.Points[v]-c;double z=V.Dot(p,n);return (p-n*z).Length<=r+1e-8&&(z>r+1e-8||z< -depth/scale-1e-8);}))throw new InvalidOperationException("A cut-volume end intersects a sloping rim. Adjust Depth or reposition the stamp, or disable Clean Patch.");
    geometry=Enumerable.Range(source.Geometry.Max()+1,work.Points.Count).ToArray();for(int i=0;i<raw.Length;i++)geometry[raw[i]]=source.Geometry[i];
    cleanResult.Pieces.Add(cleanPiece);return cleanResult;
   }
   outside.AddRange(inside.Where(f=>f.Source<0).Select(f=>new SurfaceFace{Source=-1,Interface=f.Interface,V=f.V.AsEnumerable().Reverse().ToList()}));
   var regions=outside.Select(f=>new Region{Source=f.Source,Other=f.Interface,Loop=f.V,Triangles=new List<int>(),CornerSources=new Dictionary<int,int>()}).ToList();
   void Conform()
   {
    ConformRegionEdges(work,regions,CancellationToken.None);
    // Near-parallel clipped edges can straddle the shared line hash's rounding bins.
    // Repair only unmatched edge chains against their own boundary endpoints.
    for(int pass=0;pass<3;pass++){
     var edges=regions.SelectMany(r=>Enumerable.Range(0,r.Loop.Count).Select(i=>(r,a:r.Loop[i],b:r.Loop[(i+1)%r.Loop.Count]))).ToArray();var unmatched=edges.GroupBy(e=>(Math.Min(e.a,e.b),Math.Max(e.a,e.b))).Where(g=>g.Count()==1).SelectMany(g=>g).ToArray();if(unmatched.Length==0)return;var vertices=unmatched.SelectMany(e=>new[]{e.a,e.b}).Distinct().ToArray();bool changed=false;
     foreach(var e in unmatched){var d=work.Points[e.b]-work.Points[e.a];double length=V.Dot(d,d);var added=vertices.Where(v=>v!=e.a&&v!=e.b).Select(v=>(v,t:V.Dot(work.Points[v]-work.Points[e.a],d)/length)).Where(p=>p.t>1e-8&&p.t<1-1e-8&&SegmentDistance(work.Points[p.v],work.Points[e.a],work.Points[e.b])<5e-8).OrderBy(p=>p.t).Select(p=>p.v).ToList();if(added.Count>0){SplitRegionEdge(e.r,e.a,e.b,added);changed=true;}}
     if(!changed)return;
    }
   }
   Conform();
   var walls=new List<Region>();
   foreach(var group in regions.Where(r=>r.Source<0).GroupBy(r=>r.Other))
   {
    var edges=group.SelectMany(r=>Enumerable.Range(0,r.Loop.Count).Select(i=>(a:r.Loop[i],b:r.Loop[(i+1)%r.Loop.Count]))).GroupBy(e=>(Math.Min(e.a,e.b),Math.Max(e.a,e.b)));
    var boundary=new List<(int,int)>();foreach(var e in edges){int net=e.Sum(p=>p.a<p.b?1:-1);if(Math.Abs(net)>1)throw new InvalidOperationException("Overlapping stamp wall boundary.");if(net!=0)boundary.Add(net>0?e.Key:(e.Key.Item2,e.Key.Item1));}
    var wn=planes[group.Key].Normal*-1;
    foreach(var domain in SectionDomains(work,BoundaryLoops(boundary),wn)){if(domain.holes.Count==0){walls.Add(new Region{Source=-1,Other=group.Key,Loop=domain.outer,Triangles=new List<int>(),CornerSources=new Dictionary<int,int>()});continue;}var triangles=TriangulateDomain(work,domain.outer,domain.holes,wn);for(int i=0;i<triangles.Length;i+=3)walls.Add(new Region{Source=-1,Other=group.Key,Loop=triangles.Skip(i).Take(3).ToList(),Triangles=new List<int>(),CornerSources=new Dictionary<int,int>()});}
   }
   regions.RemoveAll(r=>r.Source<0);regions.AddRange(walls);Conform();
   ValidateClosed(regions.Select(f=>f.Loop),"The stamp produced an unmatched surface boundary");
   var result=new Result{Source=source,Parameters=new Settings{UVScale=1}};foreach(var point in work.Points){var p=(origin+point*scale).Float;result.Positions.Add(p);result.Original.Add(p);result.SurfacePositions.Add(p);}
   // Reconstitute untouched authored faces, keeping their original render tessellation.
   var affectedSet=new HashSet<int>(affected);var originals=Enumerable.Range(0,source.Triangles.Length).GroupBy(i=>source.AuthoredFaces[i]).ToDictionary(g=>g.Key,g=>g.ToArray());
   var restored=new List<Region>();
   foreach(var group in regions.Where(r=>r.Source>=0&&!affectedSet.Contains(source.AuthoredFaces[r.Source])).GroupBy(r=>source.AuthoredFaces[r.Source]))
   {
    var ids=originals[group.Key];var edges=ids.SelectMany(t=>Enumerable.Range(0,3).Select(k=>(raw[source.Triangles[t][k]],raw[source.Triangles[t][(k+1)%3]])));
    var corners=new Dictionary<int,int>();foreach(int t in ids)foreach(int v in source.Triangles[t])corners[raw[v]]=t;
    foreach(var loop in BoundaryLoops(edges))restored.Add(new Region{Source=ids[0],Loop=loop,Triangles=ids.SelectMany(t=>source.Triangles[t].Select(v=>raw[v])).ToList(),CornerSources=corners});
   }
   regions.RemoveAll(r=>r.Source>=0&&!affectedSet.Contains(source.AuthoredFaces[r.Source]));regions.AddRange(restored);Conform();
   // Discard intermediate prism-plane partitions. Only final surface boundaries
   // belong in the authored patch. Coplanar source triangles may share a patch;
   // warped faces retain their actual piecewise-planar surface.
   var rebuilt=new List<Region>();
   foreach(var group in regions.Where(r=>r.Source>=0&&affectedSet.Contains(source.AuthoredFaces[r.Source])).GroupBy(r=>source.AuthoredFaces[r.Source]))
   {
    var first=group.First();var ids=originals[group.Key];var tri=source.Triangles[ids[0]];var pa=work.Points[raw[tri[0]]];var pn=V.Cross(work.Points[raw[tri[1]]]-pa,work.Points[raw[tri[2]]]-pa);pn/=pn.Length;
    bool planar=ids.SelectMany(i=>source.Triangles[i]).All(v=>Math.Abs(V.Dot(work.Points[raw[v]]-pa,pn))<1e-8);
    var patches=planar?new[]{group.ToArray()}:group.GroupBy(r=>r.Source).Select(g=>g.ToArray()).ToArray();
    foreach(var patch in patches)
    {
     int sourceTriangle=patch[0].Source;var st=source.Triangles[sourceTriangle];var a=work.Points[raw[st[0]]];var normalPatch=V.Cross(work.Points[raw[st[1]]]-a,work.Points[raw[st[2]]]-a);normalPatch/=normalPatch.Length;
     var corners=new Dictionary<int,int>();foreach(var fragment in patch)foreach(int v in fragment.Loop)if(!corners.ContainsKey(v))corners[v]=fragment.CornerSources.TryGetValue(v,out int t)?t:fragment.Source;
     var boundaries=BoundaryLoops(patch.SelectMany(r=>Enumerable.Range(0,r.Loop.Count).Select(i=>(r.Loop[i],r.Loop[(i+1)%r.Loop.Count]))));
     foreach(var domain in SectionDomains(work,boundaries,normalPatch))
     {
      foreach(var ring in new[]{domain.outer}.Concat(domain.holes))rebuilt.Add(new Region{Source=sourceTriangle,Loop=ring,Triangles=new List<int>(),CornerSources=corners,Planar=true});
     }
    }
   }
   regions.RemoveAll(r=>r.Source>=0&&affectedSet.Contains(source.AuthoredFaces[r.Source]));regions.AddRange(rebuilt);
   foreach(var region in regions)region.Planar=true;
   CleanRegionBoundaries(work,regions,originalPointCount,2e-8,CancellationToken.None);
   var tessellated=new List<Region>();
   foreach(var group in rebuilt.GroupBy(r=>r.Source)){
    var st=source.Triangles[group.Key];var normalPatch=V.Cross(work.Points[raw[st[1]]]-work.Points[raw[st[0]]],work.Points[raw[st[2]]]-work.Points[raw[st[0]]]);normalPatch/=normalPatch.Length;
    foreach(var domain in SectionDomains(work,group.Select(r=>r.Loop).ToList(),normalPatch)){var triangles=TriangulateDomain(work,domain.outer,domain.holes,normalPatch);var corners=group.First().CornerSources;for(int i=0;i<triangles.Length;i+=3)tessellated.Add(new Region{Source=group.Key,Loop=triangles.Skip(i).Take(3).ToList(),Triangles=new List<int>(),CornerSources=corners});}
   }
   regions.RemoveAll(r=>rebuilt.Contains(r));regions.AddRange(tessellated);
   var piece=new Piece();
   foreach(var region in regions)
   {
    var a=work.Points[region.Loop[0]];var normalSum=new V();for(int k=1;k+1<region.Loop.Count;k++)normalSum+=V.Cross(work.Points[region.Loop[k]]-a,work.Points[region.Loop[k+1]]-a);if(normalSum.Length<1e-15)throw new InvalidOperationException("The stamp meets a degenerate surface fragment.");
    var tris=region.Triangles.Count>0?region.Triangles.ToArray():TriangulateDomain(work,region.Loop,new List<List<int>>(),normalSum/normalSum.Length);
    if(region.Source<0){for(int i=0;i<tris.Length;i+=3)piece.Faces.Add(new Face{Vertices=tris.Skip(i).Take(3).ToArray(),Exterior=-1,RenderTriangles=tris.Skip(i).Take(3).ToArray(),CutNormal=normalSum.Float.normalized});}
    else piece.Faces.Add(new Face{Vertices=region.Loop.ToArray(),Exterior=region.Source,CornerTriangles=region.Loop.Select(v=>region.CornerSources.TryGetValue(v,out int t)?t:region.Source).ToArray(),RenderTriangles=tris,CutNormal=normalSum.Float.normalized});
   }

   ValidateClosed(piece.Faces.Select(f=>f.Vertices.ToList()),"The final stamp patch has an unmatched boundary");
   if(piece.Faces.Count==0)throw new InvalidOperationException("The stamp would remove the entire mesh. Reduce Radius or Depth.");geometry=Enumerable.Range(source.Geometry.Max()+1,work.Points.Count).ToArray();for(int i=0;i<raw.Length;i++)geometry[raw[i]]=source.Geometry[i];result.Pieces.Add(piece);return result;
  }
  // Clean mode replaces a wider surface disk, rather than preserving the thin
  // wedges left by clipping individual source polygons. Each sheet is sampled
  // along the cutter axis; its rim and support ring have the requested count.
  private static Piece CleanPunchPatch(Result result, SurfaceWork work, int[] raw,
   ref int[] affected, V center, V normal, V axis, V up, double radius, int sides,
   double depth, bool through, double rotation, V origin, double scale)
  {
   var source=result.Source;
   var authored=Enumerable.Range(0,source.Triangles.Length).GroupBy(i=>source.AuthoredFaces[i]).ToDictionary(g=>g.Key,g=>g.ToArray());
   V Point(int t,int k)=>work.Points[raw[source.Triangles[t][k]]];
   V Normal(int t){var v=V.Cross(Point(t,1)-Point(t,0),Point(t,2)-Point(t,0));return v/v.Length;}
   double Cross(V a,V b)=>V.Dot(V.Cross(a,b),normal);
   V Project(V p){var d=p-center;return d-normal*V.Dot(d,normal);}
   double Distance(int t){var a=Project(Point(t,0));var b=Project(Point(t,1));var c=Project(Point(t,2));double s=Cross(b-a,c-a);if(Math.Abs(s)>1e-20){double u=Cross(b*-1,c*-1)/s,v=Cross(c*-1,a*-1)/s,w=1-u-v;if(u>=0&&v>=0&&w>=0)return 0;}return Math.Min(SegmentDistance(new V(),a,b),Math.Min(SegmentDistance(new V(),b,c),SegmentDistance(new V(),c,a)));}
   var adjacency=authored.Keys.ToDictionary(id=>id,id=>new HashSet<int>());
   var edges=Enumerable.Range(0,source.Triangles.Length).SelectMany(t=>Enumerable.Range(0,3).Select(k=>(a:raw[source.Triangles[t][k]],b:raw[source.Triangles[t][(k+1)%3]],f:source.AuthoredFaces[t]))).GroupBy(e=>(Math.Min(e.a,e.b),Math.Max(e.a,e.b)));
   foreach(var edge in edges){var fs=edge.Select(e=>e.f).Distinct().ToArray();if(fs.Length==2){adjacency[fs[0]].Add(fs[1]);adjacency[fs[1]].Add(fs[0]);}}
   var signs=authored.ToDictionary(g=>g.Key,g=>Math.Sign(g.Value.Sum(t=>V.Dot(Normal(t),normal))));
   var chosen=new HashSet<int>(affected);var queue=new Queue<int>(affected);
   double supportRadius=radius*1.45,patchRadius=radius*1.85;
   while(queue.Count>0){int f=queue.Dequeue();foreach(int next in adjacency[f])if(!chosen.Contains(next)&&signs[next]==signs[f]&&authored[next].All(t=>Math.Abs(V.Dot(Normal(t),normal))>.12)&&authored[next].Any(t=>Distance(t)<patchRadius)){chosen.Add(next);queue.Enqueue(next);}}
   var remaining=new HashSet<int>(chosen);var patches=new List<HashSet<int>>();
   while(remaining.Count>0){var patch=new HashSet<int>();queue.Enqueue(remaining.First());while(queue.Count>0){int f=queue.Dequeue();if(!remaining.Remove(f))continue;patch.Add(f);foreach(int next in adjacency[f])if(remaining.Contains(next)&&signs[next]==signs[f])queue.Enqueue(next);}patches.Add(patch);}
   var piece=new Piece();var rings=new List<(int[] vertices,double height,int sign)>();
   var cornerSource=new Dictionary<int,int>();
   int AddPoint(V p,int triangle=-1){int v=work.Vertex(p);if(triangle>=0)cornerSource[v]=triangle;return v;}
   void Face(int[] vertices,int triangle,V facing)
   {
    double area=0;for(int i=1;i+1<vertices.Length;i++)area+=V.Dot(V.Cross(work.Points[vertices[i]]-work.Points[vertices[0]],work.Points[vertices[i+1]]-work.Points[vertices[0]]),facing);
    if(area<0)Array.Reverse(vertices);
    var triangles=TriangulateDomain(work,vertices.ToList(),new List<List<int>>(),facing);
    piece.Faces.Add(new Face{Vertices=vertices,RenderTriangles=triangles,Exterior=triangle,CutNormal=facing.Float,CornerTriangles=triangle<0?null:vertices.Select(v=>cornerSource[v]).ToArray()});
   }
   foreach(var patch in patches)
   {
    int firstPatchFace=piece.Faces.Count;
    var ts=patch.SelectMany(f=>authored[f]).ToArray();int sign=signs[patch.First()];var facing=normal*sign;
    if(sign==0||ts.Any(t=>V.Dot(Normal(t),facing)<.12))throw new InvalidOperationException("Clean Patch needs a surface sheet facing the stamp; reduce Radius or disable Clean Patch for this fold.");
    if(ts.Select(t=>source.Materials[t]).Distinct().Count()!=1)throw new InvalidOperationException("Clean Patch would cross a material boundary. Reduce Radius or disable Clean Patch.");
    var loops=BoundaryLoops(ts.SelectMany(t=>Enumerable.Range(0,3).Select(k=>(raw[source.Triangles[t][k]],raw[source.Triangles[t][(k+1)%3]]))));
    if(loops.Count!=1||!InsideRingPoint(work,center,loops[0],facing))throw new InvalidOperationException("Clean Patch needs one enclosing surface patch. Reduce Radius or disable Clean Patch.");
    var outer=loops[0];foreach(int t in ts)foreach(int v in source.Triangles[t])cornerSource[raw[v]]=t;
    (V p,int t) Sample(V p)
    {
     int found=-1;V point=new V();
     foreach(int t in ts){var a=Point(t,0);var b=Point(t,1)-a;var d=Point(t,2)-a;double den=Cross(b,d);if(Math.Abs(den)<1e-20)continue;double u=Cross(p-a,d)/den,v=Cross(b,p-a)/den;if(u< -1e-8||v< -1e-8||u+v>1+1e-8)continue;var hit=a+b*u+d*v;if(found>=0&&(hit-point).Length>2e-7)throw new InvalidOperationException("Clean Patch crosses overlapping surface layers. Reduce Radius or disable Clean Patch.");found=t;point=hit;}
     if(found<0)throw new InvalidOperationException("The clean support ring extends beyond this surface patch. Reduce Radius or disable Clean Patch.");return(point,found);
    }
    int SurfacePoint(V p){var sample=Sample(p);return AddPoint(sample.p,sample.t);}
    var rim=new int[sides];var support=new int[sides];
    for(int i=0;i<sides;i++){double angle=i*Math.PI*2/sides+rotation*Math.PI/180;var direction=axis*Math.Cos(angle)+up*Math.Sin(angle);rim[i]=SurfacePoint(center+direction*radius);support[i]=SurfacePoint(center+direction*supportRadius);}
    rings.Add((rim,rim.Average(v=>V.Dot(work.Points[v]-center,normal)),sign));
    for(int i=0;i<sides;i++){int j=(i+1)%sides;Face(new[]{rim[i],support[i],support[j],rim[j]},ts[0],facing);}
    var hole=support.ToList();if(RingArea(work,hole,facing)>0)hole.Reverse();
    var triangles=TriangulateDomain(work,outer,new List<List<int>>{hole},facing);var transition=new List<int[]>();for(int i=0;i<triangles.Length;i+=3)transition.Add(triangles.Skip(i).Take(3).ToArray());
    // Improve the transition triangulation before selecting quad pairs. Boundary
    // edges, including the regular support ring, are never flipped.
    double Quality(int[] t){var a=work.Points[t[1]]-work.Points[t[0]];var b=work.Points[t[2]]-work.Points[t[1]];var c=work.Points[t[0]]-work.Points[t[2]];return V.Dot(V.Cross(a,b),facing)/(V.Dot(a,a)+V.Dot(b,b)+V.Dot(c,c));}
    for(int pass=0;pass<40;pass++)
    {
     bool changed=false;var links=transition.SelectMany((t,i)=>Enumerable.Range(0,3).Select(k=>(i,a:t[k],b:t[(k+1)%3],c:t[(k+2)%3]))).GroupBy(e=>(Math.Min(e.a,e.b),Math.Max(e.a,e.b))).Where(g=>g.Count()==2).ToArray();var used=new HashSet<int>();
     foreach(var link in links){var pair=link.ToArray();var a=pair[0];var b=pair[1];if(used.Contains(a.i)||used.Contains(b.i)||a.a!=b.b)continue;var t0=new[]{a.c,b.c,a.b};var t1=new[]{b.c,a.c,a.a};double before=Math.Min(Quality(transition[a.i]),Quality(transition[b.i])),after=Math.Min(Quality(t0),Quality(t1));if(after<=before+1e-8)continue;transition[a.i]=t0;transition[b.i]=t1;used.Add(a.i);used.Add(b.i);changed=true;}
     if(!changed)break;
    }
    // Pair by shape quality, not source order or shared original face identity.
    var pairs=new List<(double score,int a,int b,int[] q)>();
    for(int i=0;i<transition.Count;i++)for(int j=i+1;j<transition.Count;j++)if(transition[i].Intersect(transition[j]).Count()==2){var boundary=BoundaryLoops(new[]{transition[i],transition[j]}.SelectMany(t=>Enumerable.Range(0,3).Select(k=>(t[k],t[(k+1)%3]))));if(boundary.Count!=1||boundary[0].Count!=4)continue;var q=boundary[0].ToArray();double quality=1;for(int k=0;k<4;k++){var a=work.Points[q[(k+1)%4]]-work.Points[q[k]];var b=work.Points[q[(k+2)%4]]-work.Points[q[(k+1)%4]];quality=Math.Min(quality,V.Dot(V.Cross(a,b),facing)/(a.Length*b.Length));}if(quality>.15)pairs.Add((quality,i,j,q));}
    var paired=new HashSet<int>();foreach(var pair in pairs.OrderByDescending(p=>p.score)){if(paired.Contains(pair.a)||paired.Contains(pair.b))continue;Face(pair.q,ts[0],facing);paired.Add(pair.a);paired.Add(pair.b);}for(int i=0;i<transition.Count;i++)if(!paired.Contains(i))Face(transition[i],ts[0],facing);
    foreach(var face in piece.Faces.Skip(firstPatchFace))for(int i=0;i<face.RenderTriangles.Length;i+=3){var p=(work.Points[face.RenderTriangles[i]]+work.Points[face.RenderTriangles[i+1]]+work.Points[face.RenderTriangles[i+2]])/3;if((Sample(p).p-p).Length>radius*.075)throw new InvalidOperationException("Clean Patch would change this surface too much. Reduce Radius or disable Clean Patch to retain the original surface.");}
   }
   rings=rings.OrderByDescending(r=>r.height).ToList();
   for(int k=0;k<rings.Count;k++)
   {
    var entry=rings[k];if(entry.sign!=1)throw new InvalidOperationException("Clean Patch encountered an unmatched exit surface.");
    int[] bottom;bool floor=false;
    if(k+1<rings.Count&&rings[k+1].sign==-1){bottom=rings[++k].vertices;}
    else{if(through)throw new InvalidOperationException("Clean Patch could not match an entry and exit ring.");floor=true;bottom=entry.vertices.Select(v=>AddPoint(work.Points[v]-normal*(V.Dot(work.Points[v]-center,normal)+depth))).ToArray();}
    for(int i=0;i<sides;i++){int j=(i+1)%sides;if(V.Dot(work.Points[entry.vertices[i]]-work.Points[bottom[i]],normal)<=1e-8)throw new InvalidOperationException("Clean Patch depth crosses the rim. Increase Depth or disable Clean Patch for a breakout.");var inward=V.Cross(work.Points[entry.vertices[j]]-work.Points[entry.vertices[i]],normal*-1);inward/=inward.Length;Face(new[]{entry.vertices[i],entry.vertices[j],bottom[j],bottom[i]},-1,inward);}
    if(floor)
    {
     // A four-sided transfinite grid fills even circular boundaries without a
     // long diagonal fan or a central high-valence pole.
     if(sides>=4&&sides%2==0){int u=sides/4,v=sides/2-u;var grid=new int[u+1,v+1];for(int i=0;i<=u;i++){grid[i,0]=bottom[i];grid[i,v]=bottom[2*u+v-i];}for(int j=0;j<=v;j++){grid[u,j]=bottom[u+j];grid[0,j]=bottom[(sides-j)%sides];}for(int j=1;j<v;j++)for(int i=1;i<u;i++){double s=(double)i/u,t=(double)j/v;var p=work.Points[grid[i,0]]*(1-t)+work.Points[grid[i,v]]*t+work.Points[grid[0,j]]*(1-s)+work.Points[grid[u,j]]*s-(work.Points[grid[0,0]]*((1-s)*(1-t))+work.Points[grid[u,0]]*(s*(1-t))+work.Points[grid[0,v]]*((1-s)*t)+work.Points[grid[u,v]]*(s*t));grid[i,j]=AddPoint(p);}for(int j=0;j<v;j++)for(int i=0;i<u;i++)Face(new[]{grid[i,j],grid[i+1,j],grid[i+1,j+1],grid[i,j+1]},-1,normal);}
     else{int middle=AddPoint(center-normal*depth);for(int i=0;i<sides;i++)Face(new[]{bottom[i],bottom[(i+1)%sides],middle},-1,normal);}
    }
   }
   // Restore every untouched authored polygon with its exact render triangles.
   foreach(var group in authored.Where(g=>!chosen.Contains(g.Key))){var ts=group.Value;var loops=BoundaryLoops(ts.SelectMany(t=>Enumerable.Range(0,3).Select(k=>(raw[source.Triangles[t][k]],raw[source.Triangles[t][(k+1)%3]]))));var corners=new Dictionary<int,int>();foreach(int t in ts)foreach(int v in source.Triangles[t])corners[raw[v]]=t;foreach(var loop in loops)piece.Faces.Add(new Face{Vertices=loop.ToArray(),Exterior=ts[0],CornerTriangles=loop.Select(v=>corners[v]).ToArray(),RenderTriangles=ts.SelectMany(t=>source.Triangles[t].Select(v=>raw[v])).ToArray()});}
   for(int v=result.Positions.Count;v<work.Points.Count;v++){var p=(origin+work.Points[v]*scale).Float;result.Positions.Add(p);result.Original.Add(p);result.SurfacePositions.Add(p);}
   ValidateClosed(piece.Faces.Select(f=>f.Vertices.ToList()),"The clean ring patch has an unmatched boundary");
   affected=chosen.OrderBy(f=>f).ToArray();
   return piece;
  }
 }
}






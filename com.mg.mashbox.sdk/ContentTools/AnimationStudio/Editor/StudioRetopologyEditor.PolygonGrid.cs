using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        internal static Part BuildPolygonGrid(Vector3[] points,float target,int divisions,bool[] fixedEdges=null,int[] layoutCorners=null)
        {
            var triangles=TriangulatePolygon(points);
            var result=new Part { vertices=points.ToList(),uv=points.Select(p=>Vector2.zero).ToList() };
            Vector3 normal=Vector3.zero;
            for(int i=0;i<points.Length;i++)normal+=Vector3.Cross(points[i]-points[0],points[(i+1)%points.Length]-points[0]);
            normal.Normalize();
            bool convex=points.Length==4&&Enumerable.Range(0,4).All(i=>Vector3.Dot(Vector3.Cross(points[(i+1)%4]-points[i],points[(i+2)%4]-points[(i+1)%4]),normal)>0);
            if(convex&&fixedEdges==null)
            {
                // Four-corner outlines retain the same regular row/column flow
                // as Draw Patch rather than inheriting a triangulation diagonal.
                int columns=Mathf.Clamp(target>0?Mathf.CeilToInt((Vector3.Distance(points[0],points[1])+Vector3.Distance(points[3],points[2]))/(2*target)):divisions,1,96);
                int rows=Mathf.Clamp(target>0?Mathf.CeilToInt((Vector3.Distance(points[0],points[3])+Vector3.Distance(points[1],points[2]))/(2*target)):divisions,1,96);
                var grid=new int[columns+1,rows+1];
                for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++)
                {
                    int id=x==0&&y==0?0:x==columns&&y==0?1:x==columns&&y==rows?2:x==0&&y==rows?3:-1;
                    if(id<0)
                    {
                        id=result.vertices.Count;float u=(float)x/columns,v=(float)y/rows;
                        result.vertices.Add(Vector3.Lerp(Vector3.Lerp(points[0],points[1],u),Vector3.Lerp(points[3],points[2],u),v));result.uv.Add(new Vector2(u,v));
                    }
                    grid[x,y]=id;
                }
                for(int y=0;y<rows;y++)for(int x=0;x<columns;x++)result.faces.Add(new Face(new[]{grid[x,y],grid[x+1,y],grid[x+1,y+1],grid[x,y+1]}));
                return result;
            }
            if(TryBuildPolygonLayout(points,target,divisions,normal,out var layout,fixedEdges,layoutCorners))return layout;
            if(fixedEdges!=null&&fixedEdges.Any(edge=>edge))
                return BuildPolygonTransition(points,triangles,target,divisions);
            // Seed non-rectangular outlines with convex patches, then subdivide
            // conformingly: shared edge midpoints are created only once, and a
            // remaining triangle becomes three quads around its center.
            for(int i=0;i<triangles.Length;i+=3)result.faces.Add(new Face(new[]{triangles[i],triangles[i+1],triangles[i+2]}));
            StudioRetopologyOperations.PairQuads(result);
            // Irregular concave regions need extraordinary vertices, but their
            // density must follow area, not the longest triangulation diagonal.
            float area=0;
            for(int i=0;i<triangles.Length;i+=3)area+=Vector3.Cross(points[triangles[i+1]]-points[triangles[i]],points[triangles[i+2]]-points[triangles[i]]).magnitude*.5f;
            float desired=target>0?area/(target*target):divisions*divisions;
            int seedQuads=result.faces.Sum(f=>f.vertices.Length);
            int levels=Mathf.Clamp(1+Mathf.RoundToInt(Mathf.Log(Mathf.Max(1,desired/seedQuads),4)),1,5);
            for(int level=0;level<levels;level++)
            {
                if(result.faces.Sum(f=>f.vertices.Length)>24000)throw new InvalidOperationException("This outline would create too many quads. Lower Density or Divisions before filling.");
                StudioRetopologyOperations.Subdivide(result,null,0);
            }
            // Keep the complete outline fixed while improving interior spacing.
            var before=result.vertices.ToList();
            StudioRetopologyOperations.Relax(result,null,true,0);
            if(result.faces.Any(f=>Enumerable.Range(0,4).Any(i=>Vector3.Dot(Vector3.Cross(result.vertices[f.vertices[(i+1)%4]]-result.vertices[f.vertices[i]],result.vertices[f.vertices[(i+2)%4]]-result.vertices[f.vertices[(i+1)%4]]),normal)<=0)))result.vertices=before;
            return result;
        }

        private static Part BuildPolygonTransition(Vector3[] points,int[] triangles,float target,int divisions)
        {
            var result=new Part {vertices=points.ToList(),uv=points.Select(p=>Vector2.zero).ToList()};
            float area=0;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var ids=new[]{triangles[i],triangles[i+1],triangles[i+2]};
                result.faces.Add(new Face(ids));
                area+=Vector3.Cross(points[ids[1]]-points[ids[0]],points[ids[2]]-points[ids[0]]).magnitude*.5f;
            }
            float spacing=target>0?target:Mathf.Sqrt(area)/Mathf.Max(1,divisions);
            // Split shared interior edges conformingly. The authored boundary is
            // immutable: its vertices and edge counts must match both neighbors.
            for(int round=0;round<16&&result.faces.Count<12000;round++)
            {
                var edges=StudioRetopologyOperations.Edges(result);
                var occupied=new HashSet<int>();var splits=new Dictionary<int,(int a,int b,int mid)>();
                foreach(var edge in edges.Where(e=>e.Value.Count==2).OrderByDescending(e=>(result.vertices[e.Key.Item1]-result.vertices[e.Key.Item2]).sqrMagnitude))
                {
                    int a=edge.Key.Item1,b=edge.Key.Item2;
                    if((result.vertices[a]-result.vertices[b]).sqrMagnitude<=spacing*spacing*2.25f)break;
                    if(edge.Value.Any(occupied.Contains))continue;
                    float length=(result.vertices[a]-result.vertices[b]).sqrMagnitude;
                    // A locked long boundary can prevent further refinement.
                    // Never bisect a shorter edge repeatedly into a sliver.
                    if(edge.Value.Any(f=>Enumerable.Range(0,3).Any(i=>
                        (result.vertices[result.faces[f].vertices[i]]-result.vertices[result.faces[f].vertices[(i+1)%3]]).sqrMagnitude>length*1.0001f)))continue;
                    int mid=result.vertices.Count;
                    result.vertices.Add((result.vertices[a]+result.vertices[b])*.5f);result.uv.Add(Vector2.zero);
                    foreach(int face in edge.Value){occupied.Add(face);splits.Add(face,(a,b,mid));}
                }
                if(splits.Count==0)break;
                var faces=new List<Face>();
                for(int f=0;f<result.faces.Count;f++)
                {
                    var ids=result.faces[f].vertices;
                    if(!splits.TryGetValue(f,out var split)){faces.Add(result.faces[f]);continue;}
                    for(int i=0;i<3;i++)
                    {
                        int a=ids[i],b=ids[(i+1)%3],c=ids[(i+2)%3];
                        if(!(a==split.a&&b==split.b||a==split.b&&b==split.a))continue;
                        faces.Add(new Face(new[]{a,split.mid,c}));faces.Add(new Face(new[]{split.mid,b,c}));break;
                    }
                }
                result.faces=faces;
            }
            StudioRetopologyOperations.PairQuads(result);
            return result;
        }
        internal static void ProtectPolygonSeams(Part part,Part grid,int[] map,int originalVertexCount,Dictionary<(int,int),List<int>> edges)
        {
            // Surface projection can pull a thin transition triangle back across
            // its welded seam. Limit only new vertices, and only when the original
            // unprojected face was on the valid side. Actual overlap still fails.
            for(int pass=0;pass<8;pass++)
            {
                bool changed=false;
                foreach(var face in grid.faces)
                for(int i=0;i<face.vertices.Length;i++)
                {
                    int a=map[face.vertices[i]],b=map[face.vertices[(i+1)%face.vertices.Length]];
                    if(!edges.TryGetValue(StudioRetopologyOperations.Edge(a,b),out var adjacent)||adjacent.Count!=1)continue;
                    var other=part.faces[adjacent[0]].vertices;
                    var occupied=other.Aggregate(Vector3.zero,(sum,id)=>sum+part.vertices[id])/other.Length;
                    var normal=Vector3.zero;
                    for(int j=0;j<other.Length;j++)normal+=Vector3.Cross(part.vertices[other[j]]-occupied,part.vertices[other[(j+1)%other.Length]]-occupied);
                    if(normal.sqrMagnitude<1e-20f)continue;
                    normal.Normalize();var edge=part.vertices[b]-part.vertices[a];
                    float Side(Vector3 point)=>Vector3.Dot(Vector3.Cross(edge,point-part.vertices[a]),normal);
                    float occupiedSide=Side(occupied);
                    var current=face.vertices.Aggregate(Vector3.zero,(sum,id)=>sum+part.vertices[map[id]])/face.vertices.Length;
                    var original=face.vertices.Aggregate(Vector3.zero,(sum,id)=>sum+(map[id]<originalVertexCount?part.vertices[map[id]]:grid.vertices[id]))/face.vertices.Length;
                    float from=Side(current)*Mathf.Sign(occupiedSide),to=Side(original)*Mathf.Sign(occupiedSide);
                    if(from<=0||to>=0)continue;
                    float blend=Mathf.Clamp01((from-to*.05f)/(from-to));
                    foreach(int id in face.vertices)
                        if(map[id]>=originalVertexCount)part.vertices[map[id]]=Vector3.Lerp(part.vertices[map[id]],grid.vertices[id],blend);
                    changed=true;
                }
                if(!changed)break;
            }
        }
        private void AppendPolygonGrid(Part grid,Vector3 normal,int fixedPoints,bool mirrored=false)
        {
            float tolerance=Mathf.Max(.000001f,Active.reference.bounds.size.magnitude*.00001f);
            var oldEdges=StudioRetopologyOperations.Edges(Active);
            var boundary=new HashSet<int>(StudioRetopologyOperations.Edges(grid).Where(e=>e.Value.Count==1).SelectMany(e=>new[]{e.Key.Item1,e.Key.Item2}));
            bool OnEdge(Vector3 p,Vector3 a,Vector3 b)
            {
                float length=(b-a).sqrMagnitude;if(length<tolerance*tolerance)return false;
                float t=Vector3.Dot(p-a,b-a)/length;
                return t>=0&&t<=1&&(p-Vector3.Lerp(a,b,t)).sqrMagnitude<=tolerance*tolerance;
            }
            var buckets=new Dictionary<(int,int,int),List<int>>();
            (int,int,int) Key(Vector3 p)=>(Mathf.FloorToInt(p.x/tolerance),Mathf.FloorToInt(p.y/tolerance),Mathf.FloorToInt(p.z/tolerance));
            void Bucket(int id)
            {var key=Key(Active.vertices[id]);if(!buckets.TryGetValue(key,out var list))buckets[key]=list=new List<int>();list.Add(id);}
            for(int i=0;i<Active.vertices.Count;i++)Bucket(i);
            int Weld(Vector3 point)
            {
                var key=Key(point);
                for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)
                    if(buckets.TryGetValue((key.Item1+x,key.Item2+y,key.Item3+z),out var list))
                        foreach(int id in list)if((Active.vertices[id]-point).sqrMagnitude<=tolerance*tolerance)return id;
                int added=Active.vertices.Count;Active.vertices.Add(point);Active.uv.Add(Vector2.zero);Bucket(added);return added;
            }
            int originalVertexCount=Active.vertices.Count;
            var map=new int[grid.vertices.Count];
            float span=StudioRetopologyOperations.Edges(grid).Keys.Average(e=>Vector3.Distance(grid.vertices[e.Item1],grid.vertices[e.Item2]));
            for(int i=0;i<map.Length;i++)
            {
                var point=grid.vertices[i];
                bool seam=boundary.Contains(i)&&oldEdges.Any(e=>OnEdge(point,Active.vertices[e.Key.Item1],Active.vertices[e.Key.Item2]));
                if(Document.project&&i>=fixedPoints&&!seam)
                {
                    // Half-model references have no surface on the reflected
                    // side. Project against their authored half, then reflect;
                    // nearest-surface fallback would collapse the mirrored grid.
                    if(mirrored&&!Active.reference.bounds.Contains(point))
                    {
                        var source=new Vector3(-point.x,point.y,point.z);
                        var projected=Surface.ProjectAlong(source,new Vector3(-normal.x,normal.y,normal.z),Document.offset,span);
                        point=new Vector3(-projected.x,projected.y,projected.z);
                    }
                    else point=Surface.ProjectAlong(point,normal,Document.offset,span);
                }
                map[i]=Weld(point);
            }
            // A drawn side can subdivide an existing open edge. Insert the new
            // boundary vertices into its neighbor too, preventing T-junctions.
            foreach(var edge in oldEdges)
            {
                int a=edge.Key.Item1,b=edge.Key.Item2;var start=Active.vertices[a];var delta=Active.vertices[b]-start;
                var inserted=boundary.Select(i=>map[i]).Distinct().Where(i=>i!=a&&i!=b&&OnEdge(Active.vertices[i],start,start+delta)).OrderBy(i=>Vector3.Dot(Active.vertices[i]-start,delta)).ToArray();
                if(inserted.Length==0)continue;
                if(edge.Value.Count!=1)throw new InvalidOperationException("The polygon meets an occupied edge. Draw from an open border instead.");
                var face=Active.faces[edge.Value[0]];var ids=face.vertices;var replacement=new List<int>();
                for(int i=0;i<ids.Length;i++)
                {
                    replacement.Add(ids[i]);int next=ids[(i+1)%ids.Length];
                    if(ids[i]==a&&next==b)replacement.AddRange(inserted);
                    else if(ids[i]==b&&next==a)replacement.AddRange(inserted.Reverse());
                }
                face.vertices=replacement.ToArray();face.cornerUV=null;
            }
            var edges=StudioRetopologyOperations.Edges(Active);bool? reverse=null;
            ProtectPolygonSeams(Active,grid,map,originalVertexCount,edges);
            foreach(var face in grid.faces)
            {
                var ids=face.vertices.Select(i=>map[i]).ToArray();
                if(ids.Distinct().Count()!=ids.Length||ids.Length<3)throw new InvalidOperationException("The grid collapses near the outline. Lower Density or adjust the points.");
                for(int i=0;i<ids.Length;i++)
                {
                    int a=ids[i],b=ids[(i+1)%ids.Length];
                    if(!edges.TryGetValue(StudioRetopologyOperations.Edge(a,b),out var neighbors))continue;
                    if(neighbors.Count!=1)throw new InvalidOperationException("This patch meets an occupied edge. Draw from open borders.");
                    var other=Active.faces[neighbors[0]].vertices;
                    var edgeVector=Active.vertices[b]-Active.vertices[a];
                    var occupied=other.Aggregate(Vector3.zero,(sum,id)=>sum+Active.vertices[id])/other.Length;
                    var incoming=ids.Aggregate(Vector3.zero,(sum,id)=>sum+Active.vertices[id])/ids.Length;
                    // Test in the neighboring face's tangent plane. A single
                    // outline normal can reverse the side test on a curved limb.
                    var localNormal=Vector3.zero;
                    for(int j=0;j<other.Length;j++)
                        localNormal+=Vector3.Cross(Active.vertices[other[j]]-occupied,Active.vertices[other[(j+1)%other.Length]]-occupied);
                    localNormal=localNormal.sqrMagnitude>1e-20f?localNormal.normalized:normal;
                    float occupiedSide=Vector3.Dot(Vector3.Cross(edgeVector,occupied-Active.vertices[a]),localNormal);
                    float incomingSide=Vector3.Dot(Vector3.Cross(edgeVector,incoming-Active.vertices[a]),localNormal);
                    if(occupiedSide*incomingSide>tolerance*tolerance*tolerance*tolerance)
                        throw new InvalidOperationException("The polygon overlaps an existing face. Draw on the empty side of its border.");
                    bool forward=Enumerable.Range(0,other.Length).Any(j=>other[j]==a&&other[(j+1)%other.Length]==b);
                    if(reverse.HasValue&&reverse.Value!=forward)throw new InvalidOperationException("The outline would create inconsistent winding along an existing border.");
                    reverse=forward;
                }
            }
            var first=grid.faces[0].vertices;
            bool flip=reverse??Vector3.Dot(Vector3.Cross(grid.vertices[first[1]]-grid.vertices[first[0]],grid.vertices[first[first.Length-1]]-grid.vertices[first[0]]),normal)<0;
            foreach(var face in grid.faces)
            {
                var ids=face.vertices.Select(i=>map[i]).ToArray();if(flip)Array.Reverse(ids);
                Active.faces.Add(new Face(ids));
            }
        }
    }
}

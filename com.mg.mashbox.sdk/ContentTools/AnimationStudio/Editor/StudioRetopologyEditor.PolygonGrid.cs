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
        internal static Part BuildPolygonGrid(Vector3[] points,float target,int divisions)
        {
            var triangles=TriangulatePolygon(points);
            var result=new Part { vertices=points.ToList(),uv=points.Select(p=>Vector2.zero).ToList() };
            Vector3 normal=Vector3.zero;
            for(int i=0;i<points.Length;i++)normal+=Vector3.Cross(points[i]-points[0],points[(i+1)%points.Length]-points[0]);
            normal.Normalize();
            bool convex=points.Length==4&&Enumerable.Range(0,4).All(i=>Vector3.Dot(Vector3.Cross(points[(i+1)%4]-points[i],points[(i+2)%4]-points[(i+1)%4]),normal)>0);
            if(convex)
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
            // Seed non-rectangular outlines with convex patches, then subdivide
            // conformingly: shared edge midpoints are created only once, and a
            // remaining triangle becomes three quads around its center.
            for(int i=0;i<triangles.Length;i+=3)result.faces.Add(new Face(new[]{triangles[i],triangles[i+1],triangles[i+2]}));
            StudioRetopologyOperations.PairQuads(result);
            float longest=StudioRetopologyOperations.Edges(result).Keys.Max(e=>Vector3.Distance(result.vertices[e.Item1],result.vertices[e.Item2]));
            int levels=Mathf.Clamp(Mathf.CeilToInt(Mathf.Log(Mathf.Max(1,target>0?longest/target:divisions),2)),1,6);
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

        private void AppendPolygonGrid(Part grid,Vector3 normal,int fixedPoints)
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
            var map=new int[grid.vertices.Count];
            float span=StudioRetopologyOperations.Edges(grid).Keys.Max(e=>Vector3.Distance(grid.vertices[e.Item1],grid.vertices[e.Item2]));
            for(int i=0;i<map.Length;i++)
            {
                var point=grid.vertices[i];
                bool seam=boundary.Contains(i)&&oldEdges.Any(e=>OnEdge(point,Active.vertices[e.Key.Item1],Active.vertices[e.Key.Item2]));
                if(Document.project&&i>=fixedPoints&&!seam)point=Surface.ProjectAlong(point,normal,Document.offset,span);
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
            foreach(var face in grid.faces)
            {
                var ids=face.vertices.Select(i=>map[i]).ToArray();
                if(ids.Distinct().Count()!=4)throw new InvalidOperationException("The grid collapses near the outline. Lower Density or adjust the points.");
                for(int i=0;i<4;i++)
                {
                    int a=ids[i],b=ids[(i+1)%4];
                    if(!edges.TryGetValue(StudioRetopologyOperations.Edge(a,b),out var neighbors))continue;
                    if(neighbors.Count!=1)throw new InvalidOperationException("This patch meets an occupied edge. Draw from open borders.");
                    var other=Active.faces[neighbors[0]].vertices;
                    var edgeVector=Active.vertices[b]-Active.vertices[a];
                    var occupied=other.Aggregate(Vector3.zero,(sum,id)=>sum+Active.vertices[id])/other.Length;
                    var incoming=ids.Aggregate(Vector3.zero,(sum,id)=>sum+Active.vertices[id])/ids.Length;
                    float occupiedSide=Vector3.Dot(Vector3.Cross(edgeVector,occupied-Active.vertices[a]),normal);
                    float incomingSide=Vector3.Dot(Vector3.Cross(edgeVector,incoming-Active.vertices[a]),normal);
                    if(occupiedSide*incomingSide>tolerance*tolerance*tolerance*tolerance)
                        throw new InvalidOperationException("The polygon overlaps an existing face. Draw on the empty side of its border.");
                    bool forward=Enumerable.Range(0,other.Length).Any(j=>other[j]==a&&other[(j+1)%other.Length]==b);
                    if(reverse.HasValue&&reverse.Value!=forward)throw new InvalidOperationException("The outline would create inconsistent winding along an existing border.");
                    reverse=forward;
                }
            }
            var first=grid.faces[0].vertices;
            bool flip=reverse??Vector3.Dot(Vector3.Cross(grid.vertices[first[1]]-grid.vertices[first[0]],grid.vertices[first[3]]-grid.vertices[first[0]]),normal)<0;
            foreach(var face in grid.faces)
            {
                var ids=face.vertices.Select(i=>map[i]).ToArray();if(flip)Array.Reverse(ids);
                Active.faces.Add(new Face(ids));
            }
        }
    }
}

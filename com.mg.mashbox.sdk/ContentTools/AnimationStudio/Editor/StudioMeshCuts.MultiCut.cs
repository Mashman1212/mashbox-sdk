using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        internal sealed class FaceStroke
        {
            internal int Face;
            internal readonly List<Vector3> Points = new List<Vector3>();
            internal FaceStroke(int face, params Vector3[] points) { Face = face; Points.AddRange(points); }
        }
        internal Vector3 CutFaceNormal(int face)
        {
            var v=Faces[face].Vertices;var n=Vector3.zero;var origin=Positions[v[0]];
            for(int i=1;i+1<v.Count;i++)n+=Vector3.Cross(Positions[v[i]]-origin,Positions[v[i+1]]-origin);
            return n.normalized;
        }
        internal bool OnCutBoundary(int face,Vector3 p,out Point point)
        {
            var v=Faces[face].Vertices;float epsilon=CutTolerance;
            for(int i=0;i<v.Count;i++)
            {
                int a=v[i],b=v[(i+1)%v.Count];var delta=Positions[b]-Positions[a];
                float t=delta.sqrMagnitude>0?Vector3.Dot(p-Positions[a],delta)/delta.sqrMagnitude:0;
                if(t>=-epsilon&&t<=1+epsilon&&(Vector3.Lerp(Positions[a],Positions[b],Mathf.Clamp01(t))-p).sqrMagnitude<=epsilon*epsilon)
                { point=new Point(a,b,Mathf.Clamp01(t));return true; }
            }
            point=default;return false;
        }
        private float cachedCutTolerance;
        private float CutTolerance
        {
            get { if(cachedCutTolerance>0)return cachedCutTolerance;if(Positions.Length==0)return 1e-6f;var bounds=new Bounds(Positions[0],Vector3.zero);foreach(var p in Positions)bounds.Encapsulate(p);return cachedCutTolerance=Mathf.Max(1e-7f,bounds.size.magnitude*1e-6f); }
        }
        private sealed class CutChart
        {
            internal readonly Vector3 Origin,X,Y,Normal;
            internal CutChart(Vector3 origin,Vector3 normal)
            { Origin=origin;Normal=normal;X=Vector3.Cross(Mathf.Abs(normal.y)<.9f?Vector3.up:Vector3.right,normal).normalized;Y=Vector3.Cross(normal,X); }
            internal Vector2 Project(Vector3 p) { p-=Origin;return new Vector2(Vector3.Dot(p,X),Vector3.Dot(p,Y)); }
        }
        private static double CutCross(Vector2 a,Vector2 b)=>(double)a.x*b.y-(double)a.y*b.x;
        private static bool CutInside(Vector2 p,IList<Vector2> polygon)
        {
            bool inside=false;
            for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
            { var a=polygon[i];var b=polygon[j];if((a.y>p.y)!=(b.y>p.y)&&p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x)inside=!inside; }
            return inside;
        }
        internal List<FaceStroke> SliceCut(Plane plane)
        {
            float epsilon=CutTolerance;var result=new List<FaceStroke>();
            for(int f=0;f<Faces.Count;f++)
            {
                var vertices=Faces[f].Vertices;var points=new List<Vector3>();bool positive=false,negative=false;
                foreach(int v in vertices){float d=plane.GetDistanceToPoint(Positions[v]);positive|=d>epsilon;negative|=d<-epsilon;}
                if(!positive||!negative)continue; // Coplanar/tangent surfaces are already boundaries.
                for(int i=0;i<vertices.Count;i++)
                {
                    var a=Positions[vertices[i]];var b=Positions[vertices[(i+1)%vertices.Count]];
                    float da=plane.GetDistanceToPoint(a),db=plane.GetDistanceToPoint(b);
                    if(Mathf.Abs(da)<=epsilon)points.Add(a);
                    else if((da>0)!=(db>0))points.Add(Vector3.LerpUnclamped(a,b,da/(da-db)));
                }
                var normal=CutFaceNormal(f);var direction=Vector3.Cross(normal,plane.normal).normalized;
                points=points.OrderBy(p=>Vector3.Dot(p,direction)).ToList();
                var unique=new List<Vector3>();foreach(var p in points)if(unique.Count==0||(p-unique[unique.Count-1]).sqrMagnitude>epsilon*epsilon)unique.Add(p);
                var chart=new CutChart(Positions[vertices[0]],normal);var polygon=vertices.Select(v=>chart.Project(Positions[v])).ToArray();
                for(int i=0;i+1<unique.Count;i++)
                    if(CutInside(chart.Project((unique[i]+unique[i+1])*.5f),polygon))result.Add(new FaceStroke(f,unique[i],unique[i+1]));
            }
            return result;
        }

        // Build bounded cells from the authored boundary and placed straight segments.
        // Intersections are genuine cut vertices; no quad conversion, bevel or repair pass.
        internal void ApplyMultiCut(StudioModelDocument.Node node,IList<FaceStroke> strokes,Action beforeWrite=null)
        {
            if(strokes.Count==0)throw new InvalidOperationException("No complete cut to apply.");
            var mesh=node.mesh;int sourceSubmeshes=mesh.subMeshCount;
            if(node.skinned||node.blendShape!=null||mesh.blendShapeCount>0||mesh.boneWeights.Length>0)throw new InvalidOperationException("Bake skinning and blend shapes before cutting.");
            float epsilon=CutTolerance,epsilon2=epsilon*epsilon;
            var sourceTriangles=BevelSourceTriangles(mesh);
            var positions=Positions.ToList();var normals=mesh.normals.ToList();var tangents=mesh.tangents.ToList();var colors=mesh.colors.ToList();
            var groups=node.normalGroups!=null&&node.normalGroups.Length==Positions.Length?node.normalGroups.ToList():null;
            var uv=new List<Vector4>[8];var dimensions=new int[8];
            for(int c=0;c<8;c++){uv[c]=new List<Vector4>();mesh.GetUVs(c,uv[c]);dimensions[c]=mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0+c);}
            var edgePoints=new Dictionary<(int,int),List<Vector3>>();
            foreach(var stroke in strokes)
            {
                if(stroke.Face<0||stroke.Face>=Faces.Count||stroke.Points.Count<2)throw new InvalidOperationException("Incomplete face path.");
                // Endpoints may join other pending strokes; validate the complete face graph below.
                foreach(var p in stroke.Points)
                    if(OnCutBoundary(stroke.Face,p,out var q))
                    { var key=Key(q.A,q.B);if(!edgePoints.TryGetValue(key,out var list))edgePoints[key]=list=new List<Vector3>();if(!list.Any(x=>(x-p).sqrMagnitude<=epsilon2))list.Add(p); }
            }
            var vertexCache=new Dictionary<(int,int,float),int>();
            int Add(int a,int b,int c,Vector3 weight,Vector3 p)
            {
                int id=positions.Count;positions.Add(p);
                if(normals.Count>0)normals.Add((normals[a]*weight.x+normals[b]*weight.y+normals[c]*weight.z).normalized);
                if(tangents.Count>0){var t=tangents[a]*weight.x+tangents[b]*weight.y+tangents[c]*weight.z;var d=new Vector3(t.x,t.y,t.z).normalized;tangents.Add(new Vector4(d.x,d.y,d.z,tangents[a].w));}
                if(colors.Count>0)colors.Add(colors[a]*weight.x+colors[b]*weight.y+colors[c]*weight.z);
                if(groups!=null)groups.Add(groups[a]);
                for(int channel=0;channel<8;channel++)if(uv[channel].Count>0)uv[channel].Add(uv[channel][a]*weight.x+uv[channel][b]*weight.y+uv[channel][c]*weight.z);
                return id;
            }
            int BoundaryVertex(Point q,Vector3 p)
            {
                if((Positions[q.A]-p).sqrMagnitude<=epsilon2)return q.A;if((Positions[q.B]-p).sqrMagnitude<=epsilon2)return q.B;
                var key=q.A<q.B?(q.A,q.B,q.T):(q.B,q.A,1-q.T);
                if(vertexCache.TryGetValue(key,out int id))return id;
                return vertexCache[key]=Add(q.A,q.B,q.B,new Vector3(1-q.T,q.T,0),p);
            }
            int InteriorVertex(int f,Vector3 p)
            {
                var triangles=sourceTriangles[f];
                for(int i=0;i+2<triangles.Count;i+=3)
                {
                    int a=triangles[i],b=triangles[i+1],c=triangles[i+2];var x=Positions[b]-Positions[a];var y=Positions[c]-Positions[a];var d=p-Positions[a];
                    double xx=Vector3.Dot(x,x),xy=Vector3.Dot(x,y),yy=Vector3.Dot(y,y),dx=Vector3.Dot(d,x),dy=Vector3.Dot(d,y),den=xx*yy-xy*xy;
                    if(Math.Abs(den)<1e-24)continue;float wb=(float)((dx*yy-dy*xy)/den),wc=(float)((dy*xx-dx*xy)/den),wa=1-wb-wc;
                    if(wa>=-1e-5f&&wb>=-1e-5f&&wc>=-1e-5f)return Add(a,b,c,new Vector3(wa,wb,wc),p);
                }
                throw new InvalidOperationException("A placed point is outside its source face; cut was not applied.");
            }
            var result=new List<Face>();var resultIndices=new List<List<int>>();
            for(int f=0;f<Faces.Count;f++)
            {
                var face=Faces[f];var boundary=new List<int>();
                for(int e=0;e<face.Vertices.Count;e++)
                {
                    int a=face.Vertices[e],b=face.Vertices[(e+1)%face.Vertices.Count];boundary.Add(a);
                    if(!edgePoints.TryGetValue(Key(a,b),out var points))continue;
                    var delta=Positions[b]-Positions[a];
                    foreach(var p in points.OrderBy(p=>Vector3.Dot(p-Positions[a],delta)))
                    { float t=Vector3.Dot(p-Positions[a],delta)/delta.sqrMagnitude;int id=BoundaryVertex(new Point(a,b,t),p);if(id!=a&&id!=b&&(boundary.Count==0||(positions[boundary[boundary.Count-1]]-p).sqrMagnitude>epsilon2))boundary.Add(id); }
                }
                var paths=strokes.Where(s=>s.Face==f).ToArray();
                if(paths.Length==0)
                {
                    result.Add(new Face{Submesh=face.Submesh,Vertices=boundary});var indices=new List<int>();
                    if(boundary.Count==face.Vertices.Count)indices.AddRange(sourceTriangles[f]);else Triangulate(boundary,positions,indices);
                    resultIndices.Add(indices);continue;
                }
                var chart=new CutChart(Positions[face.Vertices[0]],CutFaceNormal(f));
                var polygon=boundary.Select(v=>chart.Project(positions[v])).ToArray();
                var localIds=new List<int>(boundary);
                int Vertex(Vector3 p)
                {
                    foreach(int v in localIds)if((positions[v]-p).sqrMagnitude<=epsilon2)return v;
                    int id=OnCutBoundary(f,p,out var q)?BoundaryVertex(q,p):InteriorVertex(f,p);localIds.Add(id);return id;
                }
                var segments=new List<(int a,int b)>();
                for(int i=0;i<boundary.Count;i++)segments.Add((boundary[i],boundary[(i+1)%boundary.Count]));
                foreach(var path in paths)for(int i=0;i+1<path.Points.Count;i++)
                {
                    var a=path.Points[i];var b=path.Points[i+1];if((b-a).sqrMagnitude<=epsilon2)throw new InvalidOperationException("Cut contains a zero-length segment.");
                    if(Mathf.Abs(Vector3.Dot(a-chart.Origin,chart.Normal))>epsilon*4||Mathf.Abs(Vector3.Dot(b-chart.Origin,chart.Normal))>epsilon*4)
                        throw new InvalidOperationException("This face is not planar enough for an interior cut; split it along its existing surface first.");
                    segments.Add((Vertex(a),Vertex(b)));
                }
                // Split all true segment crossings, including crossings of authored perimeters.
                var divisions=segments.Select(s=>new List<(float t,int v)>{(0,s.a),(1,s.b)}).ToArray();
                for(int i=0;i<segments.Count;i++)for(int j=i+1;j<segments.Count;j++)
                {
                    var a=chart.Project(positions[segments[i].a]);var b=chart.Project(positions[segments[i].b]);var c=chart.Project(positions[segments[j].a]);var d=chart.Project(positions[segments[j].b]);
                    var r=b-a;var s=d-c;double den=CutCross(r,s);
                    if(Math.Abs(den)<1e-16)continue;
                    float t=(float)(CutCross(c-a,s)/den),u=(float)(CutCross(c-a,r)/den);
                    if(t<0||t>1||u<0||u>1)continue;
                    int v=Vertex(Vector3.Lerp(positions[segments[i].a],positions[segments[i].b],t));divisions[i].Add((t,v));divisions[j].Add((u,v));
                }
                var adjacency=new Dictionary<int,List<int>>();
                void Edge(int a,int b)
                {
                    if(a==b)return;
                    if(!adjacency.TryGetValue(a,out var list))adjacency[a]=list=new List<int>();if(!list.Contains(b))list.Add(b);
                    if(!adjacency.TryGetValue(b,out list))adjacency[b]=list=new List<int>();if(!list.Contains(a))list.Add(a);
                }
                foreach(var division in divisions)
                {
                    var ordered=division.OrderBy(p=>p.t).Select(p=>p.v).Distinct().ToArray();
                    for(int i=0;i+1<ordered.Length;i++)
                    {
                        int a=ordered[i],b=ordered[i+1];var midpoint=chart.Project((positions[a]+positions[b])*.5f);
                        if(!OnCutBoundary(f,(positions[a]+positions[b])*.5f,out _)&&!CutInside(midpoint,polygon))throw new InvalidOperationException("The straight path leaves its face; place a point on the perimeter before continuing.");
                        Edge(a,b);
                    }
                }
                foreach(var pair in adjacency)
                {
                    if(pair.Value.Count<2)throw new InvalidOperationException("Cut has an unfinished interior endpoint; finish on the perimeter.");
                    var p=chart.Project(positions[pair.Key]);pair.Value.Sort((a,b)=>Math.Atan2(chart.Project(positions[a]).y-p.y,chart.Project(positions[a]).x-p.x).CompareTo(Math.Atan2(chart.Project(positions[b]).y-p.y,chart.Project(positions[b]).x-p.x)));
                }
                var visited=new HashSet<(int,int)>();double areaSum=0;
                foreach(var pair in adjacency)foreach(int neighbour in pair.Value)
                {
                    if(visited.Contains((pair.Key,neighbour)))continue;
                    int start=pair.Key,next=neighbour,a=start,b=next;var cycle=new List<int>();
                    for(int guard=0;guard<=segments.Count*segments.Count*4+16;guard++)
                    {
                        if(!visited.Add((a,b)))break;cycle.Add(a);var neighbours=adjacency[b];int at=neighbours.IndexOf(a);int c=neighbours[(at+neighbours.Count-1)%neighbours.Count];a=b;b=c;if(a==start&&b==next)break;
                    }
                    double area=0;for(int i=0;i<cycle.Count;i++)area+=CutCross(chart.Project(positions[cycle[i]]),chart.Project(positions[cycle[(i+1)%cycle.Count]]));
                    if(area<=epsilon2)continue;
                    // A path returning to its exact boundary anchor may yield a weakly simple boundary; keep it if render triangulation is valid.
                    var indices=new List<int>();Triangulate(cycle,positions,indices);double renderArea=0;for(int t=0;t<indices.Count;t+=3){var ta=chart.Project(positions[indices[t]]);renderArea+=CutCross(chart.Project(positions[indices[t+1]])-ta,chart.Project(positions[indices[t+2]])-ta);}if(Math.Abs(renderArea-area)>Math.Max(epsilon2*16,area*1e-4))throw new InvalidOperationException("The cut boundary cannot be rendered safely; cut was not applied.");result.Add(new Face{Submesh=face.Submesh,Vertices=cycle});resultIndices.Add(indices);areaSum+=area;
                }
                double sourceArea=0;for(int i=0;i<polygon.Length;i++)sourceArea+=CutCross(polygon[i],polygon[(i+1)%polygon.Length]);
                if(Math.Abs(areaSum-sourceArea)>Math.Max(epsilon2*16,Math.Abs(sourceArea)*1e-4))throw new InvalidOperationException("Cut cells do not cover the source face; cut was not applied.");
            }
            // All validation/triangulation happens before touching the live mesh.
            beforeWrite?.Invoke();
            mesh.Clear();mesh.indexFormat=positions.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;mesh.SetVertices(positions);
            if(normals.Count>0)mesh.SetNormals(normals);if(tangents.Count>0)mesh.SetTangents(tangents);if(colors.Count>0)mesh.SetColors(colors);
            for(int c=0;c<8;c++)if(uv[c].Count>0){if(dimensions[c]==2)mesh.SetUVs(c,uv[c].Select(v=>new Vector2(v.x,v.y)).ToList());else if(dimensions[c]==3)mesh.SetUVs(c,uv[c].Select(v=>new Vector3(v.x,v.y,v.z)).ToList());else mesh.SetUVs(c,uv[c]);}
            int submeshes=sourceSubmeshes;mesh.subMeshCount=submeshes;
            for(int sub=0;sub<submeshes;sub++){var indices=new List<int>();for(int f=0;f<result.Count;f++)if(result[f].Submesh==sub)indices.AddRange(resultIndices[f]);mesh.SetTriangles(indices,sub);}
            mesh.RecalculateBounds();mesh.UploadMeshData(false);node.cutPolygons=result.SelectMany(f=>new[]{f.Submesh,f.Vertices.Count}.Concat(f.Vertices)).ToArray();node.cutTriangles=mesh.triangles;node.normalGroups=groups?.ToArray();
        }
    }
}

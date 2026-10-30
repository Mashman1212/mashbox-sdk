using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // A through-plane cut follows the existing rendered surface of warped
        // polygons. Internal render diagonals are cancelled, never authored edges.
        internal List<FaceStroke> SurfaceSlice(Plane plane)
        {
            var triangles=BevelSourceTriangles(sourceMeshForSlice);
            var result=new List<FaceStroke>();float epsilon=CutTolerance;
            for(int f=0;f<Faces.Count;f++)
                for(int i=0;i<triangles[f].Count;i+=3)
                {
                    var ids=triangles[f].Skip(i).Take(3).ToArray();var points=new List<Vector3>();
                    bool positive=ids.Any(v=>plane.GetDistanceToPoint(Positions[v])>epsilon),negative=ids.Any(v=>plane.GetDistanceToPoint(Positions[v])<-epsilon);
                    if(!positive||!negative)continue;
                    for(int j=0;j<3;j++)
                    {
                        var a=Positions[ids[j]];var b=Positions[ids[(j+1)%3]];
                        float da=plane.GetDistanceToPoint(a),db=plane.GetDistanceToPoint(b);
                        if(Mathf.Abs(da)<=epsilon)points.Add(a);
                        else if((da>0)!=(db>0))points.Add(Vector3.LerpUnclamped(a,b,da/(da-db)));
                    }
                    if(points.Count>=2&&(points[0]-points[1]).sqrMagnitude>epsilon*epsilon)result.Add(new FaceStroke(f,points[0],points[1]));
                }
            return result;
        }
        private static void SplitSliceBoundaryTriangles(List<int> triangles,int a,int b,int midpoint)
        {
            for(int i=0;i<triangles.Count;i+=3)for(int edge=0;edge<3;edge++)
            {
                int x=triangles[i+edge],y=triangles[i+(edge+1)%3],z=triangles[i+(edge+2)%3];
                if(!((x==a&&y==b)||(x==b&&y==a)))continue;
                triangles[i]=x;triangles[i+1]=midpoint;triangles[i+2]=z;
                triangles.AddRange(new[]{midpoint,y,z});return;
            }
            throw new InvalidOperationException("A fill boundary is missing its source render edge.");
        }
        private readonly Mesh sourceMeshForSlice;

        internal bool ApplyPlaneSlice(StudioModelDocument.Node node,Plane plane,Action beforeWrite=null)
        {
            if(node.skinned||node.blendShape!=null||node.mesh.blendShapeCount>0||node.mesh.boneWeights.Length>0)
                throw new InvalidOperationException("Bake skinning and blend shapes before cutting.");
            var buffer=new BisectBuffer(node,false);var source=BevelSourceTriangles(node.mesh);float epsilon=CutTolerance;
            var distance=Positions.Select(plane.GetDistanceToPoint).ToArray();
            var rawIntersections=new Dictionary<(int,int),int>();var intersectionEdges=new Dictionary<int,(int,int)>();var geometryIntersections=new Dictionary<(int,int),(int id,Vector3 position)>();
            int Intersection(int a,int b)
            {
                if(Mathf.Abs(distance[a])<=epsilon)return a;if(Mathf.Abs(distance[b])<=epsilon)return b;
                if(a>b){int swap=a;a=b;b=swap;}
                var key=(a,b);if(rawIntersections.TryGetValue(key,out int v))return v;
                float t=(float)((double)distance[a]/((double)distance[a]-distance[b]));
                int ga=buffer.Geometry[a],gb=buffer.Geometry[b];var gkey=(Math.Min(ga,gb),Math.Max(ga,gb));
                if(!geometryIntersections.TryGetValue(gkey,out var geometry))
                {geometry=(buffer.NextGeometry++,Vector3.LerpUnclamped(Positions[a],Positions[b],t));geometryIntersections.Add(gkey,geometry);}
                v=buffer.Interpolate(a,b,geometry.id,t);buffer.Positions[v]=geometry.position;rawIntersections[key]=v;intersectionEdges[v]=key;return v;
            }
            var result=new List<Face>();var rendered=new List<List<int>>();bool changed=false;
            for(int f=0;f<Faces.Count;f++)
            {
                var face=Faces[f];bool positive=face.Vertices.Any(v=>distance[v]>epsilon),negative=face.Vertices.Any(v=>distance[v]<-epsilon);
                if(!positive||!negative){result.Add(face);rendered.Add(source[f]);continue;}
                changed=true;
                var faceNormal=CutFaceNormal(f);var origin=Positions[face.Vertices[0]];
                bool planar=face.Vertices.All(v=>Mathf.Abs(Vector3.Dot(Positions[v]-origin,faceNormal))<=epsilon*4);
                var authoredEdges=new HashSet<(int,int)>();for(int j=0;j<face.Vertices.Count;j++){int a=face.Vertices[j],b=face.Vertices[(j+1)%face.Vertices.Count];authoredEdges.Add((Math.Min(a,b),Math.Max(a,b)));}
                for(int side=-1;side<=1;side+=2)
                {
                    var cells=new List<List<int>>();var cellTriangles=new List<List<int>>();
                    for(int i=0;i<source[f].Count;i+=3)
                    {
                        var triangle=source[f].Skip(i).Take(3).ToArray();var polygon=new List<int>();
                        for(int j=0;j<3;j++)
                        {
                            int a=triangle[j],b=triangle[(j+1)%3];bool insideA=side*distance[a]>=-epsilon,insideB=side*distance[b]>=-epsilon;
                            if(insideA)polygon.Add(a);if(insideA!=insideB)polygon.Add(Intersection(a,b));
                        }
                        polygon=polygon.Where((v,j)=>j==0||v!=polygon[j-1]).ToList();if(polygon.Count>1&&polygon[0]==polygon[polygon.Count-1])polygon.RemoveAt(polygon.Count-1);
                        if(polygon.Count<3)continue;
                        var tris=new List<int>();
                        for(int j=1;j+1<polygon.Count;j++)
                        {
                            int a=polygon[0],b=polygon[j],c=polygon[j+1];
                            if(Vector3.Cross(buffer.Positions[b]-buffer.Positions[a],buffer.Positions[c]-buffer.Positions[a]).sqrMagnitude<=epsilon*epsilon*epsilon*epsilon)continue;
                            tris.AddRange(new[]{a,b,c});
                        }
                        if(tris.Count==0)continue;
                        cells.Add(polygon);cellTriangles.Add(tris);
                    }
                    var parent=Enumerable.Range(0,cells.Count).ToArray();
                    int Root(int c){while(parent[c]!=c){parent[c]=parent[parent[c]];c=parent[c];}return c;}
                    var edges=new Dictionary<(int,int),(int cell,int a,int b)>();
                    var internalEdges=new HashSet<(int,int)>();
                    for(int c=0;c<cells.Count;c++)for(int j=0;j<cells[c].Count;j++)
                    {
                        int a=cells[c][j],b=cells[c][(j+1)%cells[c].Count];var key=(Math.Min(a,b),Math.Max(a,b));
                        if(edges.TryGetValue(key,out var prior))
                        {
                            if(prior.a!=b||prior.b!=a||!internalEdges.Add(key))throw new InvalidOperationException("The source face has inconsistent render-triangle connectivity.");
                            parent[Root(c)]=Root(prior.cell);
                        }
                        else edges.Add(key,(c,a,b));
                    }
                    foreach(var component in Enumerable.Range(0,cells.Count).GroupBy(Root))
                    {
                        var perimeter=edges.Where(p=>!internalEdges.Contains(p.Key)&&Root(p.Value.cell)==component.Key).Select(p=>p.Value).ToArray();
                        var next=new Dictionary<int,int>();
                        foreach(var edge in perimeter)
                        {if(next.ContainsKey(edge.a))throw new InvalidOperationException("The slice meets a branching source face.");next.Add(edge.a,edge.b);}
                        if(next.Count<3)continue;
                        var contour=new List<int>();int start=perimeter[0].a,current=start;
                        do
                        {
                            if(contour.Count>=next.Count||!next.TryGetValue(current,out int following))throw new InvalidOperationException("The sliced face boundary is incomplete.");
                            contour.Add(current);current=following;
                        }while(current!=start);
                        if(contour.Count!=next.Count)throw new InvalidOperationException("The sliced source face contains an unsupported internal hole.");
                        var render=component.SelectMany(c=>cellTriangles[c]).ToList();
                        if(planar)
                        {
                            // A render-diagonal intersection in a flat face is not
                            // an authored boundary feature. Remove it from the cut
                            // contour instead of forcing unnecessary cap subdivisions.
                            var unsimplified=contour.ToList();bool simplified=false;
                            for(int j=contour.Count-1;j>=0&&contour.Count>3;j--)
                            {
                                int v=contour[j];if(!intersectionEdges.TryGetValue(v,out var edge)||authoredEdges.Contains(edge))continue;
                                var a=buffer.Positions[contour[(j+contour.Count-1)%contour.Count]];var b=buffer.Positions[contour[(j+1)%contour.Count]];var d=b-a;
                                float t=d.sqrMagnitude>0?Vector3.Dot(buffer.Positions[v]-a,d)/d.sqrMagnitude:0;
                                if(t>0&&t<1&&(a+d*t-buffer.Positions[v]).sqrMagnitude<=epsilon*epsilon){contour.RemoveAt(j);simplified=true;}
                            }
                            if(simplified){var simplifiedRender=new List<int>();try{Triangulate(contour,buffer.Positions,simplifiedRender);render=simplifiedRender;}catch(InvalidOperationException){contour=unsimplified;}}
                        }
                        result.Add(new Face{Submesh=face.Submesh,Vertices=contour});rendered.Add(render);
                    }
                }
            }
            if(!changed)return false;
            beforeWrite?.Invoke();buffer.Publish(node,result,rendered);return true;
        }
    }
}

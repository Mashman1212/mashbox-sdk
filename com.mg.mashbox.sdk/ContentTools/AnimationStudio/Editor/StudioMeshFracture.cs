using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    // Only Capture/Publish touch Unity objects. Compute operates on immutable arrays.
    // Surface partitioning and a single quad patch per interface make every cell share
    // the SAME piecewise-linear rough interface, including triple junctions.
    internal static partial class StudioMeshFracture
    {
        internal sealed class Settings
        {
            internal int Count = 12, Seed = 1;
            internal bool SimplifyInterior = true;
            internal float Variation = .35f, Bias, Roughness = .015f, Detail = .35f, UVScale = 1;
            internal float WeldTolerance = .00001f, Gap;
            internal Vector3 Direction = Vector3.up;
            internal Settings Copy() => (Settings)MemberwiseClone();
        }
        internal sealed class Input
        {
            internal Vector3[] Positions, Normals;
            internal Vector4[] Tangents;
            internal Color[] Colors;
            internal Vector4[][] UV = new Vector4[8][];
            internal int[] Dimensions = new int[8], Geometry;
            internal int[][] Triangles;
            internal int[] Materials, AuthoredFaces;
            internal int Submeshes;
        }
        internal sealed class Progress
        {
            internal readonly string Stage;
            internal readonly float Fraction;
            internal Progress(string stage, float fraction) { Stage = stage; Fraction = fraction; }
        }
        internal sealed class Face
        {
            internal int[] Vertices, CornerTriangles, RenderTriangles;
            internal Vector3 CutNormal;
            internal int Exterior; // source triangle, or -1 for generated interior
        }
        internal sealed class Piece
        {
            internal readonly List<Face> Faces = new List<Face>();
        }
        internal sealed class Result
        {
            internal Input Source;
            internal Settings Parameters;
            internal readonly List<Vector3> Positions = new List<Vector3>();
            internal readonly List<Vector3> Original = new List<Vector3>();
            internal readonly List<Vector3> SurfacePositions = new List<Vector3>();
            internal readonly List<Piece> Pieces = new List<Piece>();
        }
        // Double precision in normalized coordinates; Unity floats only at publication.
        private readonly struct V
        {
            internal readonly double x,y,z;
            internal V(double x,double y,double z) { this.x=x;this.y=y;this.z=z; }
            internal V(Vector3 v):this(v.x,v.y,v.z) { }
            internal double Length => Math.Sqrt(Dot(this,this));
            internal Vector3 Float => new Vector3((float)x,(float)y,(float)z);
            public static V operator +(V a,V b)=>new V(a.x+b.x,a.y+b.y,a.z+b.z);
            public static V operator -(V a,V b)=>new V(a.x-b.x,a.y-b.y,a.z-b.z);
            public static V operator *(V a,double b)=>new V(a.x*b,a.y*b,a.z*b);
            public static V operator /(V a,double b)=>a*(1/b);
            internal static double Dot(V a,V b)=>a.x*b.x+a.y*b.y+a.z*b.z;
            internal static V Cross(V a,V b)=>new V(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
        }
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        private static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
        internal static Input Capture(StudioModelDocument.Node node)
        {
            if (!node.mesh || !node.mesh.isReadable || node.skinned || node.blendShape != null || node.mesh.blendShapeCount != 0 || node.mesh.boneWeights.Length != 0)
                throw new InvalidOperationException("Fracture requires a readable static solid without skinning or blend shapes.");
            var topology=new StudioMeshCuts(node);topology.ValidateSplitSource(node);
            var input=new Input { Positions=node.mesh.vertices,Normals=node.mesh.normals,Tangents=node.mesh.tangents,Colors=node.mesh.colors,
                Geometry=StudioSubdivisionData.Geometry(node),Submeshes=node.mesh.subMeshCount };
            var tris=new List<int[]>();var materials=new List<int>();var authored=new List<int>();
            var render=topology.BevelSourceTriangles(node.mesh);
            for(int f=0;f<render.Length;f++)for(int i=0;i<render[f].Count;i+=3)
            {tris.Add(new[]{render[f][i],render[f][i+1],render[f][i+2]});materials.Add(topology.Faces[f].Submesh);authored.Add(f);}
            input.Triangles=tris.ToArray();input.Materials=materials.ToArray();input.AuthoredFaces=authored.ToArray();
            for(int c=0;c<8;c++){var values=new List<Vector4>();node.mesh.GetUVs(c,values);if(values.Count==0)continue;input.UV[c]=values.ToArray();input.Dimensions[c]=node.mesh.GetVertexAttributeDimension((VertexAttribute)((int)VertexAttribute.TexCoord0+c));}
            return input;
        }

        // Publication retains exterior streams by source-triangle barycentrics.
        // Generated interfaces share a quad patch; exterior faces retain source tessellation.
        internal static StudioModelDocument.Node Publish(Result result,Piece piece,float explode=0)
        {
            var source=result.Source;var points=new List<Vector3>();var normals=new List<Vector3>();var tangents=new List<Vector4>();var colors=new List<Color>();
            var uv=Enumerable.Range(0,8).Select(_=>new List<Vector4>()).ToArray();var geometry=new List<int>();var polygons=new List<int>();
            var triangles=Enumerable.Range(0,source.Submeshes+1).Select(_=>new List<int>()).ToArray();
            var center=piece.Faces.SelectMany(f=>f.Vertices).Distinct().Aggregate(Vector3.zero,(sum,v)=>sum+result.Positions[v])/piece.Faces.SelectMany(f=>f.Vertices).Distinct().Count();
            var allCenter=(source.Positions.Aggregate(Vector3.Min)+source.Positions.Aggregate(Vector3.Max))*.5f;var offset=(center-allCenter)*explode;
            foreach(var face in piece.Faces)
            {
                int first=points.Count,sub=face.Exterior<0?source.Submeshes:source.Materials[face.Exterior];var vs=face.Vertices;
                var normal=Vector3.zero;for(int k=1;k+1<vs.Length;k++)normal+=Vector3.Cross(result.Positions[vs[k]]-result.Positions[vs[0]],result.Positions[vs[k+1]]-result.Positions[vs[0]]);normal.Normalize();
                var uvNormal=face.Exterior<0&&face.CutNormal.sqrMagnitude>.5f?face.CutNormal:normal;
                var axis=Vector3.Cross(uvNormal,Mathf.Abs(uvNormal.x)<.8f?Vector3.right:Vector3.up).normalized;var up=Vector3.Cross(uvNormal,axis);
                polygons.Add(sub);polygons.Add(vs.Length);
                for(int corner=0;corner<vs.Length;corner++)
                {
                    int v=vs[corner];points.Add(result.Positions[v]+offset);geometry.Add(v);polygons.Add(points.Count-1);
                    if(face.Exterior>=0)
                    {
                        var t=source.Triangles[face.CornerTriangles==null?face.Exterior:face.CornerTriangles[corner]];var a=source.Positions[t[0]];var b=source.Positions[t[1]]-a;var c=source.Positions[t[2]]-a;var q=result.SurfacePositions[v]-a;
                        double bb=Vector3.Dot(b,b),bc=Vector3.Dot(b,c),cc=Vector3.Dot(c,c),qb=Vector3.Dot(q,b),qc=Vector3.Dot(q,c),den=bb*cc-bc*bc;
                        float y=(float)((cc*qb-bc*qc)/den),z=(float)((bb*qc-bc*qb)/den),x=1-y-z;
                        normals.Add(source.Normals.Length==source.Positions.Length?(source.Normals[t[0]]*x+source.Normals[t[1]]*y+source.Normals[t[2]]*z).normalized:normal);
                        tangents.Add(source.Tangents.Length==source.Positions.Length?source.Tangents[t[0]]*x+source.Tangents[t[1]]*y+source.Tangents[t[2]]*z:new Vector4(axis.x,axis.y,axis.z,1));
                        colors.Add(source.Colors.Length==source.Positions.Length?source.Colors[t[0]]*x+source.Colors[t[1]]*y+source.Colors[t[2]]*z:Color.white);
                        for(int channel=0;channel<8;channel++)uv[channel].Add(source.UV[channel]!=null?source.UV[channel][t[0]]*x+source.UV[channel][t[1]]*y+source.UV[channel][t[2]]*z:Vector4.zero);
                    }
                    else
                    {
                        normals.Add(normal);tangents.Add(new Vector4(axis.x,axis.y,axis.z,1));colors.Add(Color.white);
                        uv[0].Add(new Vector4(Vector3.Dot(result.Positions[v],axis)*result.Parameters.UVScale,Vector3.Dot(result.Positions[v],up)*result.Parameters.UVScale,0,0));for(int channel=1;channel<8;channel++)uv[channel].Add(Vector4.zero);
                    }
                }
                // Stable fan anchor gives both sides of every interface identical diagonals.
                if(face.RenderTriangles!=null)foreach(int v in face.RenderTriangles)triangles[sub].Add(first+Array.IndexOf(vs,v));
                else {int anchor=Array.IndexOf(vs,vs.Min());for(int k=1;k+1<vs.Length;k++){triangles[sub].Add(first+anchor);triangles[sub].Add(first+(anchor+k)%vs.Length);triangles[sub].Add(first+(anchor+k+1)%vs.Length);}}
            }
            var mesh=new Mesh {name="Fracture fragment",indexFormat=points.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            try
            {
                mesh.SetVertices(points);mesh.SetNormals(normals);mesh.SetTangents(tangents);if(source.Colors.Length>0)mesh.SetColors(colors);
                for(int c=0;c<8;c++){int dim=c==0?Math.Max(2,source.Dimensions[c]):source.Dimensions[c];if(dim==0)continue;if(dim==1)StudioSubdivisionData.SetUV1(mesh,c,uv[c]);else if(dim==2)mesh.SetUVs(c,uv[c].Select(v=>new Vector2(v.x,v.y)).ToList());else if(dim==3)mesh.SetUVs(c,uv[c].Select(v=>new Vector3(v.x,v.y,v.z)).ToList());else mesh.SetUVs(c,uv[c]);}
                mesh.subMeshCount=triangles.Length;for(int s=0;s<triangles.Length;s++)mesh.SetTriangles(triangles[s],s,false);mesh.RecalculateBounds();
                var dense=new Dictionary<int,int>();var groups=new Dictionary<(int,int,int,int),int>();var normalGroups=new int[points.Count];
                for(int v=0;v<points.Count;v++)
                {
                    if(!dense.TryGetValue(geometry[v],out int id))dense.Add(geometry[v],id=dense.Count);geometry[v]=id;
                    var n=normals[v];var key=(id,Mathf.RoundToInt(n.x*100000),Mathf.RoundToInt(n.y*100000),Mathf.RoundToInt(n.z*100000));
                    if(!groups.TryGetValue(key,out int group))groups.Add(key,group=groups.Count);normalGroups[v]=group;
                }
                return new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons.ToArray(),cutTriangles=mesh.triangles,geometryVertices=geometry.ToArray(),normalGroups=normalGroups};
            }
            catch{UnityEngine.Object.DestroyImmediate(mesh);throw;}
        }

        internal static StudioModelDocument.Node PublishInspection(Result result,float explode)
        {
            var inspection=new Result{Source=result.Source,Parameters=result.Parameters};var combined=new Piece();
            var center=(result.Source.Positions.Aggregate(Vector3.Min)+result.Source.Positions.Aggregate(Vector3.Max))*.5f;
            foreach(var piece in result.Pieces)
            {
                var used=piece.Faces.SelectMany(f=>f.Vertices).Distinct().ToArray();var middle=used.Aggregate(Vector3.zero,(sum,v)=>sum+result.Positions[v])/used.Length;
                var offset=(middle-center)*explode;var remap=new Dictionary<int,int>();
                foreach(int v in used){remap.Add(v,inspection.Positions.Count);inspection.Positions.Add(result.Positions[v]+offset);inspection.Original.Add(result.Original[v]);inspection.SurfacePositions.Add(result.SurfacePositions[v]);}
                foreach(var face in piece.Faces)combined.Faces.Add(new Face{Exterior=face.Exterior,CutNormal=face.CutNormal,Vertices=face.Vertices.Select(v=>remap[v]).ToArray(),CornerTriangles=face.CornerTriangles,RenderTriangles=face.RenderTriangles?.Select(v=>remap[v]).ToArray()});
            }
            return Publish(inspection,combined);
        }
    }
}




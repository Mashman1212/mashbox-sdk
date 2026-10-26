using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;
using BigInteger=System.Numerics.BigInteger;

public static class StudioBevelCornerPlanValidation
{
    static int cases;static string phase;
    static List<(int,int)> Edges(StudioMeshCuts top)
    {
        var seen=new HashSet<(Vector3,Vector3)>();var edges=new List<(int,int)>();
        foreach(var f in top.Faces)for(int c=0;c<f.Vertices.Count;c++){int a=f.Vertices[c],b=f.Vertices[(c+1)%f.Vertices.Count];if(seen.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(top.Positions[a],top.Positions[b])))edges.Add((a,b));}return edges;
    }
    static void Check(bool pass,string error){if(!pass)throw new Exception(error);}
    static BigInteger Exact(float value)
    {
        int bits=BitConverter.ToInt32(BitConverter.GetBytes(value),0),exponent=(bits>>23)&255,mantissa=bits&0x7fffff;
        BigInteger result=exponent==0?new BigInteger(mantissa):new BigInteger(mantissa|0x800000)<<(exponent-1);return bits<0?-result:result;
    }
    static void SimpleProjection(StudioMeshCuts top,StudioMeshCuts.Face face)
    {
        var points=face.Vertices.Select(i=>{var p=top.Positions[i];return new[]{Exact(p.x),Exact(p.y),Exact(p.z)};}).ToArray();var area=new BigInteger[3];
        for(int i=0;i<points.Length;i++){var a=points[i];var b=points[(i+1)%points.Length];area[0]+=a[1]*b[2]-a[2]*b[1];area[1]+=a[2]*b[0]-a[0]*b[2];area[2]+=a[0]*b[1]-a[1]*b[0];}
        int axis=0;if(BigInteger.Abs(area[1])>BigInteger.Abs(area[axis]))axis=1;if(BigInteger.Abs(area[2])>BigInteger.Abs(area[axis]))axis=2;
        int x=(axis+1)%3,y=(axis+2)%3;
        int Orient(int a,int b,int c)=>((points[b][x]-points[a][x])*(points[c][y]-points[a][y])-(points[b][y]-points[a][y])*(points[c][x]-points[a][x])).Sign;
        for(int a=0;a<points.Length;a++)for(int c=a+2;c<points.Length;c++)
        {
            int b=(a+1)%points.Length,d=(c+1)%points.Length;if(d==a)continue;
            Check(!(Orient(a,b,c)*Orient(a,b,d)<0&&Orient(c,d,a)*Orient(c,d,b)<0),"Self-crossing editable face boundary (exact predicate)");
        }
    }
    static void Validate(StudioModelDocument.Node node)
    {
        var top=new StudioMeshCuts(node);var uses=new Dictionary<(Vector3,Vector3),(int count,Vector3 start)>();var referenced=new HashSet<int>();double volume=0;
        foreach(var face in top.Faces)
        {
            SimpleProjection(top,face);
            var origin=top.Positions[face.Vertices[0]];var normal=Vector3.zero;
            for(int c=1;c+1<face.Vertices.Count;c++){var b=top.Positions[face.Vertices[c]];var d=top.Positions[face.Vertices[c+1]];normal+=Vector3.Cross(b-origin,d-origin);volume+=Vector3.Dot(origin,Vector3.Cross(b,d))/6d;}
            Check(normal.sqrMagnitude>0,"Degenerate authored face");normal/=Mathf.Sqrt(normal.sqrMagnitude);
            foreach(int id in face.Vertices){referenced.Add(id);Check(Mathf.Abs(Vector3.Dot(top.Positions[id]-origin,normal))<1e-5f,"Nonplanar flat face");}
            for(int c=0;c<face.Vertices.Count;c++)
            {
                var a=top.Positions[face.Vertices[c]];var b=top.Positions[face.Vertices[(c+1)%face.Vertices.Count]];Check(!a.Equals(b),"Zero-length authored edge");
                var key=StudioMeshCuts.NormalEdgePositionKeyForBevel(a,b);uses.TryGetValue(key,out var use);Check(use.count==0||use.start==b,"Inconsistent winding");uses[key]=(use.count+1,a);
            }
        }
        Check(uses.Values.All(e=>e.count==2),"Open/nonmanifold edge");
        Check(top.Positions.Distinct().Count()-uses.Count+top.Faces.Count==2,"Unexpected genus");Check(referenced.Count==node.mesh.vertexCount,"Unreferenced render vertex");Check(volume>1e-8,"Inverted/empty mesh");cases++;
    }
    static string Signature(StudioModelDocument.Node node)
    {
        var top=new StudioMeshCuts(node);string P(int i){var p=top.Positions[i];return $"{p.x:F5},{p.y:F5},{p.z:F5}";}
        return string.Join("|",top.Faces.Select(f=>string.Join(";",f.Vertices.Select(P).OrderBy(v=>v))).OrderBy(v=>v));
    }
    static StudioModelDocument.Node Pyramid(int sides)
    {
        var vertices=new List<Vector3>();var indices=new List<int>();var metadata=new List<int>();var ring=new List<Vector3>();
        for(int i=0;i<sides;i++){float angle=2*Mathf.PI*i/sides;ring.Add(new Vector3(Mathf.Cos(angle)*1.2f,0,Mathf.Sin(angle)*.8f));}
        var apex=new Vector3(.2f,1.4f,.1f);var inside=new Vector3(0,.3f,0);
        void Face(List<Vector3> polygon)
        {
            var normal=Vector3.Cross(polygon[1]-polygon[0],polygon[2]-polygon[0]);var center=polygon.Aggregate(Vector3.zero,(a,b)=>a+b)/polygon.Count;
            if(Vector3.Dot(normal,center-inside)<0)polygon.Reverse();int start=vertices.Count;vertices.AddRange(polygon);metadata.Add(0);metadata.Add(polygon.Count);for(int i=0;i<polygon.Count;i++)metadata.Add(start+i);
            for(int i=1;i+1<polygon.Count;i++)indices.AddRange(new[]{start,start+i,start+i+1});
        }
        Face(ring);for(int i=0;i<sides;i++)Face(new List<Vector3>{apex,ring[i],ring[(i+1)%sides]});
        var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return new StudioModelDocument.Node{mesh=mesh,cutPolygons=metadata.ToArray(),cutTriangles=mesh.triangles};
    }
    public static string Validate(){var report=new StringBuilder();try
    {
        foreach(float shear in new[]{-.65f,-.25f,0,.3f,.8f})foreach(float first in new[]{.08f,.65f,.994f})foreach(int selectedCount in new[]{1,2,3,12})
        {
            var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polys);
            try
            {
                var top=new StudioMeshCuts(new StudioModelDocument.Node{mesh=mesh,cutPolygons=polys,cutTriangles=mesh.triangles});var joint=new Vector3(.5f,.5f,-.5f);
                var edges=Edges(top);var selected=(selectedCount==12?edges:edges.Where(e=>top.Positions[e.Item1]==joint||top.Positions[e.Item2]==joint).Take(selectedCount)).ToArray();
                mesh.vertices=mesh.vertices.Select(p=>Quaternion.Euler(13,27,-19)*new Vector3(p.x+shear*p.y,p.y*1.3f+shear*.2f*p.z,p.z*1.7f)).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
                var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polys,cutTriangles=mesh.triangles};var reverse=Object.Instantiate(mesh);var reversed=new StudioModelDocument.Node{mesh=reverse,cutPolygons=polys,cutTriangles=reverse.triangles};
                try
                {
                    phase="first";var construction=new StudioMeshCuts(node);construction.BevelEdges(node,selected,new StudioMeshCuts.BevelOptions{Width=first});Check(typeof(StudioMeshCuts).GetField("bevelFlatClip",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(construction)!=null,"New construction was bypassed");Validate(node);
                    phase="reverse";new StudioMeshCuts(reversed).BevelEdges(reversed,selected.Reverse(),new StudioMeshCuts.BevelOptions{Width=first});Validate(reversed);Check(Signature(node)==Signature(reversed),"Selection enumeration changes topology");
                    top=new StudioMeshCuts(node);edges=Edges(top);
                    for(int index=0;index<edges.Count;index+=Math.Max(1,edges.Count/6))foreach(float width in new[]{.1f,.75f})
                    {
                        var copy=Object.Instantiate(mesh);try{var next=new StudioModelDocument.Node{mesh=copy,cutPolygons=node.cutPolygons,cutTriangles=node.cutTriangles,normalGroups=node.normalGroups};phase="repeat edge="+index+" width="+width;var nextConstruction=new StudioMeshCuts(next);nextConstruction.BevelEdges(next,new[]{edges[index]},new StudioMeshCuts.BevelOptions{Width=width});Check(typeof(StudioMeshCuts).GetField("bevelFlatClip",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(nextConstruction)!=null,"New repeat construction was bypassed");Validate(next);}finally{Object.DestroyImmediate(copy);}
                    }
                }
                finally{Object.DestroyImmediate(reverse);}
            }
            catch(Exception e){throw new Exception($"shear={shear} first={first} edges={selectedCount} phase={phase} cases={cases}: {e.Message}",e);}
            finally{Object.DestroyImmediate(mesh);}
        }
        foreach(int valence in new[]{4,5,6,8})foreach(float width in new[]{.08f,.4f,.85f})foreach(int count in new[]{1,2,3})
        {
            var node=Pyramid(valence);try{var top=new StudioMeshCuts(node);var selected=Edges(top).Where(e=>top.Positions[e.Item1].y>1||top.Positions[e.Item2].y>1).Take(count).ToArray();top.BevelEdges(node,selected,new StudioMeshCuts.BevelOptions{Width=width});Check(typeof(StudioMeshCuts).GetField("bevelFlatClip",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(top)!=null,"High-valence construction was bypassed");Validate(node);}catch(Exception e){throw new Exception("valence="+valence+" width="+width+" count="+count+": "+e.Message,e);}finally{Object.DestroyImmediate(node.mesh);}
        }
        report.AppendLine("PASS "+cases+" shared-corner oblique/repeated/high-valence meshes, plus reversed-selection topology comparisons.");
    }catch(Exception e){report.AppendLine(e.ToString());}File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../bevel-corner-plan-result.txt")),report.ToString());return report.ToString();}
    public static void Run(){EditorApplication.delayCall+=()=>{string report=Validate();EditorApplication.Exit(report.StartsWith("PASS")?0:1);};}
}

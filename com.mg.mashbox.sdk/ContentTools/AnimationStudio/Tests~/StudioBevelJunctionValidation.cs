using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
public static class StudioBevelJunctionValidation
{
    static int checks, cases;
    static string current;
    static void Check(bool valid, string message) { if (!valid) throw new Exception(current + ": " + message); checks++; }
    static List<(int,int)> Edges(StudioModelDocument.Node node)
    {
        var positions = node.mesh.vertices; var seen = new HashSet<(Vector3,Vector3)>(); var result = new List<(int,int)>();
        foreach (var face in new StudioMeshCuts(node).Faces) for(int i=0;i<face.Vertices.Count;i++)
        {
            int a=face.Vertices[i],b=face.Vertices[(i+1)%face.Vertices.Count];
            if(seen.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(positions[a],positions[b])))result.Add((a,b));
        }
        return result;
    }
    static void Validate(Mesh mesh)
    {
        var positions = mesh.vertices; var triangles = mesh.triangles; var ids = new Dictionary<Vector3,int>();
        var uses = new Dictionary<(int,int),(int count,int a,int b)>();
        for(int i=0;i<triangles.Length;i+=3)
        {
            Check(Vector3.Cross(positions[triangles[i+1]]-positions[triangles[i]],positions[triangles[i+2]]-positions[triangles[i]]).sqrMagnitude>1e-26f,"No collapsed render triangle");
            for(int e=0;e<3;e++)
            {
                var p=positions[triangles[i+e]];var q=positions[triangles[i+(e+1)%3]];
                if(!ids.ContainsKey(p))ids[p]=ids.Count;if(!ids.ContainsKey(q))ids[q]=ids.Count;
                int a=ids[p],b=ids[q];var key=(Math.Min(a,b),Math.Max(a,b));
                if(uses.TryGetValue(key,out var use)){Check(use.count==1&&use.a==b,"Opposed seam winding");uses[key]=(2,use.a,use.b);}else uses[key]=(1,a,b);
            }
        }
        Check(uses.Values.All(u=>u.count==2),"Closed seams without T junctions");
        Check(ids.Count-uses.Count+triangles.Length/3==2,"Euler topology preserved");
        Check(mesh.normals.Length==positions.Length&&mesh.normals.All(n=>!float.IsNaN(n.x)&&n.sqrMagnitude>.98f),"Finite unit normals");
    }
    public static void Run()
    {
        try
        {
            foreach(int count in new[]{1,2,3})foreach(int subdivisions in new[]{0,1,2,3,4,8,32})foreach(float tension in new[]{-1f,0,1})foreach(var style in new[]{StudioMeshCuts.BevelJunction.Uniform,StudioMeshCuts.BevelJunction.Patch})
            {
                current=$"{count} edges, {subdivisions} subdivisions, tension {tension}, {style}";
                var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
                try
                {
                    var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles};var corner=Vector3.one*.5f;var positions=mesh.vertices;
                    var selection=Edges(node).Where(e=>positions[e.Item1]==corner||positions[e.Item2]==corner).Take(count).ToArray();
                    Check(selection.Length==count,"Correct incident edge selection");
                    new StudioMeshCuts(node).BevelEdges(node,selection,new StudioMeshCuts.BevelOptions{Width=.2f,Subdivisions=subdivisions,Tension=tension,Junction=style});
                    Validate(mesh);positions=mesh.vertices;
                    var cap=new StudioMeshCuts(node).Faces.Where(f=>f.Vertices.All(i=>(positions[i]-corner).magnitude<.35f)).ToList();
                    if(count==1)
                    {
                        var resultFaces=new StudioMeshCuts(node).Faces;
                        Check(resultFaces.Count==6+subdivisions+1,"Single-edge ends merge into original faces without extra cap polygons");
                        Check(cap.Count==0,"No separate end-cap boundary remains at the corner");
                    }
                    if(count==3)
                    {
                        int n=subdivisions+1,m=n/2;bool odd=(n&1)!=0;
                        int expected=style==StudioMeshCuts.BevelJunction.Patch||subdivisions==0?1:3*(m*m+(odd?m:0))+(odd?1:0);
                        Check(cap.Count==expected,"Three-edge patch has expected connected face grid");
                        if(style==StudioMeshCuts.BevelJunction.Uniform&&subdivisions>0)
                        {
                            Check(cap.Count(f=>f.Vertices.Count==3)==(odd?1:0),"Only odd profile count needs one central triangle");
                            Check(cap.All(f=>f.Vertices.Count==4||(odd&&f.Vertices.Count==3)),"Grid junction contains quads and its one central face");
                        }
                    }
                    if(count==2&&subdivisions>0&&tension!=0&&style==StudioMeshCuts.BevelJunction.Uniform)
                        Check(cap.Count==subdivisions+1&&cap.Count(f=>f.Vertices.Count==3)==2,"Two-edge profile loops connect through a strip with triangle ends");
                    cases++;
                }
                finally{Object.DestroyImmediate(mesh);}
            }
            File.WriteAllText(Path.Combine(Application.dataPath,"../bevel-junction-result.txt"),$"PASS {checks} checks across {cases} one/two/three-edge junction cases");
        }
        catch(Exception e){File.WriteAllText(Path.Combine(Application.dataPath,"../bevel-junction-result.txt"),e.ToString());}
        finally{EditorApplication.Exit(0);}
    }
}

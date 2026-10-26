using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object = UnityEngine.Object;

public static class StudioBevelFlatTerminationValidation
{
    static List<(int,int)> Edges(StudioMeshCuts mesh)
    {
        var seen = new HashSet<(Vector3,Vector3)>();
        var result = new List<(int,int)>();
        foreach (var face in mesh.Faces) for (int c=0;c<face.Vertices.Count;c++)
        {
            int a=face.Vertices[c], b=face.Vertices[(c+1)%face.Vertices.Count];
            if (seen.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(mesh.Positions[a],mesh.Positions[b]))) result.Add((a,b));
        }
        return result;
    }
    static void Require(bool pass, string message) { if (!pass) throw new Exception(message); }
    public static void Validate()
    {
        int cases=0;
        foreach (var scale in new[]{Vector3.one,new Vector3(1,1.3f,2.1f)})
        foreach (float x in new[]{-.5f,.5f}) foreach (float y in new[]{-.5f,.5f}) foreach (float z in new[]{-.5f,.5f})
        for (int remaining=0;remaining<3;remaining++) foreach (float firstWidth in new[]{.1f,.449f,.994f})
        {
            var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
            try
            {
                var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles};
                var original=new StudioMeshCuts(node); var joint=new Vector3(x,y,z);
                var pair=Edges(original).Where(e=>(original.Positions[e.Item1]==joint||original.Positions[e.Item2]==joint)&&original.Positions[e.Item1][remaining]==original.Positions[e.Item2][remaining]).ToArray();
                Require(pair.Length==2,"Initial L selection");
                mesh.vertices=mesh.vertices.Select(p=>Vector3.Scale(p,scale)).ToArray(); mesh.RecalculateNormals();mesh.RecalculateBounds();
                original=new StudioMeshCuts(node);
                original.BevelEdges(node,pair,new StudioMeshCuts.BevelOptions{Width=firstWidth,Subdivisions=0,Tension=1});
                var source=new StudioMeshCuts(node); var scaledJoint=Vector3.Scale(joint,scale); var axis=Vector3.zero;axis[remaining]=1;
                var second=Edges(source).Where(e=>Vector3.Cross(source.Positions[e.Item1]-scaledJoint,axis).sqrMagnitude<1e-12f&&Vector3.Cross(source.Positions[e.Item2]-scaledJoint,axis).sqrMagnitude<1e-12f).ToArray();
                Require(source.Faces.Count==8&&second.Length==1,"First L bevel/remaining edge");
                foreach(float width in new[]{.1f,.5f,.994f}) foreach(float tension in new[]{-1f,0f,1f}) foreach(int junction in new[]{0,1})
                {
                    var copy=Object.Instantiate(mesh);
                    try
                    {
                        var output=new StudioModelDocument.Node{mesh=copy,cutPolygons=node.cutPolygons,cutTriangles=node.cutTriangles,normalGroups=node.normalGroups};
                        var options=new StudioMeshCuts.BevelOptions{Width=width,Subdivisions=0,Tension=tension,Junction=(StudioMeshCuts.BevelJunction)junction};
                        new StudioMeshCuts(output).BevelEdges(output,second,options);
                        var result=new StudioMeshCuts(output); var normals=copy.normals;
                        Require(result.Faces.Count==9&&result.Positions.Distinct().Count()==14,"Separate cap/divider retained");
                        Require(result.Faces.Last().Vertices.Count==5,"Expected continuous five-sided terminal strip");
                        var uses=new Dictionary<(Vector3,Vector3),int>();var referenced=new HashSet<int>();
                        foreach(var face in result.Faces)
                        {
                            var origin=result.Positions[face.Vertices[0]];var normal=Vector3.zero;
                            for(int c=0;c<face.Vertices.Count;c++) normal+=Vector3.Cross(result.Positions[face.Vertices[c]]-origin,result.Positions[face.Vertices[(c+1)%face.Vertices.Count]]-origin);
                            Require(normal.sqrMagnitude>1e-15f,"Degenerate face"); normal.Normalize();
                            foreach(int vertex in face.Vertices)
                            {
                                referenced.Add(vertex);
                                Require(Mathf.Abs(Vector3.Dot(normal,result.Positions[vertex]-origin))<3e-6f,"Supporting plane changed");
                                Require(Vector3.Dot(normal,normals[vertex])>.9999f,"Flat face normal mismatch");
                            }
                            for(int c=0;c<face.Vertices.Count;c++)
                            {
                                var key=StudioMeshCuts.NormalEdgePositionKeyForBevel(result.Positions[face.Vertices[c]],result.Positions[face.Vertices[(c+1)%face.Vertices.Count]]);
                                uses.TryGetValue(key,out int count);uses[key]=count+1;
                            }
                        }
                        Require(uses.Values.All(n=>n==2)&&referenced.Count==copy.vertexCount,"Nonmanifold or unreferenced geometry");cases++;
                    }
                    catch(Exception e){throw new Exception($"joint={joint} axis={remaining} scale={scale} first={firstWidth} second={width} tension={tension} junction={junction}: {e.Message}",e);}
                    finally{Object.DestroyImmediate(copy);}
                }
            }
            finally{Object.DestroyImmediate(mesh);}
        }
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../bevel-flat-termination-result.txt")),"PASS "+cases+" flat terminal cases: eight corners, three axes, uniform/nonuniform meshes, widths, tensions and junction styles; planar flat faces, continuous strip, closed edges and complete references.");
    }
    public static void Run(){try{Validate();StudioBevelProfileValidation.Validate();StudioBevelChainValidation.Validate();StudioPublicationValidation.Validate();EditorApplication.Exit(0);}catch(Exception e){File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../bevel-flat-termination-result.txt")),e.ToString());EditorApplication.Exit(1);}}
}

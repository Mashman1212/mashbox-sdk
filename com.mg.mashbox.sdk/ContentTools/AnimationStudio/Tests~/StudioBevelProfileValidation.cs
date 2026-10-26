using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;
public static class StudioBevelProfileValidation
{
 static List<(int,int)> Edges(StudioMeshCuts cuts)
 {
  var seen=new HashSet<(Vector3,Vector3)>();var result=new List<(int,int)>();
  foreach(var face in cuts.Faces)for(int i=0;i<face.Vertices.Count;i++)
  {int a=face.Vertices[i],b=face.Vertices[(i+1)%face.Vertices.Count];if(seen.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(cuts.Positions[a],cuts.Positions[b])))result.Add((a,b));}
  return result;
 }
 static void Apply(StudioModelDocument.Node node,(int,int) edge,float width,int subdivisions)
 {new StudioMeshCuts(node).BevelEdges(node,new[]{edge},new StudioMeshCuts.BevelOptions{Width=width,Subdivisions=subdivisions,Tension=1});}
 public static void Validate()
 {
  int checks=0;
  foreach(bool stretch in new[]{false,true})foreach(float firstWidth in new[]{.074f,.4f,.449f})
  {
   var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
   try
   {
    var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles};
    // Select by original positions, then bake scale before any operation.
    var initial=new StudioMeshCuts(node);var first=Edges(initial).First(e=>Mathf.Abs(initial.Positions[e.Item1].y-.5f)<1e-6f&&Mathf.Abs(initial.Positions[e.Item2].y-.5f)<1e-6f&&Mathf.Abs(initial.Positions[e.Item1].z+.5f)<1e-6f&&Mathf.Abs(initial.Positions[e.Item2].z+.5f)<1e-6f);
    if(stretch){mesh.vertices=mesh.vertices.Select(p=>Vector3.Scale(p,new Vector3(1,1.3f,2.1f))).ToArray();mesh.RecalculateBounds();}
    Apply(node,first,firstWidth,0);
    var source=new StudioMeshCuts(node);float z=stretch?-1.05f:-.5f;
    var second=Edges(source).First(e=>Mathf.Abs(source.Positions[e.Item1].x-.5f)<1e-6f&&Mathf.Abs(source.Positions[e.Item2].x-.5f)<1e-6f&&Mathf.Abs(source.Positions[e.Item1].z-z)<1e-6f&&Mathf.Abs(source.Positions[e.Item2].z-z)<1e-6f);
    Apply(node,second,firstWidth,0);source=new StudioMeshCuts(node);var all=Edges(source);int sourceVertices=source.Positions.Distinct().Count();
    foreach(var selected in all)foreach(float width in new[]{.074f,.449f,1f})foreach(int subdivisions in new[]{0,1,3,8})
    {
     var copy=Object.Instantiate(mesh);
     try
     {
      var output=new StudioModelDocument.Node{mesh=copy,cutPolygons=node.cutPolygons,cutTriangles=node.cutTriangles,normalGroups=node.normalGroups};Apply(output,selected,width,subdivisions);
      var result=new StudioMeshCuts(output);int expected=source.Faces.Count+subdivisions+1;
      if(result.Faces.Count!=expected)throw new Exception($"Terminal bevel retained a separate cap: stretched={stretch} first={firstWidth} third={width} subdivisions={subdivisions}, faces {result.Faces.Count}, expected {expected}, edge {source.Positions[selected.Item1]} -> {source.Positions[selected.Item2]}");
      if(result.Positions.Distinct().Count()!=sourceVertices+2*(subdivisions+1))throw new Exception("Unexpected terminal profile vertex count");
      var uses=new Dictionary<(Vector3,Vector3),int>();var referenced=new HashSet<int>();
      foreach(var face in result.Faces)
      {
       var origin=result.Positions[face.Vertices[0]];var normal=Vector3.zero;
       for(int i=1;i+1<face.Vertices.Count;i++)normal+=Vector3.Cross(result.Positions[face.Vertices[i]]-origin,result.Positions[face.Vertices[i+1]]-origin);
       normal.Normalize();
       foreach(int vertex in face.Vertices){referenced.Add(vertex);if(Mathf.Abs(Vector3.Dot(normal,result.Positions[vertex]-origin))>2e-6f)throw new Exception("A terminal bevel altered its supporting plane");}
       for(int i=0;i<face.Vertices.Count;i++){var key=StudioMeshCuts.NormalEdgePositionKeyForBevel(result.Positions[face.Vertices[i]],result.Positions[face.Vertices[(i+1)%face.Vertices.Count]]);uses.TryGetValue(key,out int count);uses[key]=count+1;}
      }
      if(uses.Values.Any(n=>n!=2)||referenced.Count!=copy.vertexCount)throw new Exception("Terminal bevel lost manifold references");checks++;
     }
     finally{Object.DestroyImmediate(copy);}
    }
   }
   finally{Object.DestroyImmediate(mesh);}
  }
  int shared=0;
  foreach(float width in new[]{.074f,.449f,1f})foreach(int subdivisions in new[]{1,3,8})
  {
   var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
   try
   {
    var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles};var cuts=new StudioMeshCuts(node);
    var joint=new Vector3(.5f,.5f,-.5f);var selected=Edges(cuts).Where(e=>cuts.Positions[e.Item1]==joint||cuts.Positions[e.Item2]==joint).Where(e=>cuts.Positions[e.Item1].y==.5f&&cuts.Positions[e.Item2].y==.5f).ToArray();
    cuts.BevelEdges(node,selected,new StudioMeshCuts.BevelOptions{Width=width,Subdivisions=subdivisions,Tension=1});var result=new StudioMeshCuts(node);
    if(result.Faces.Count!=6+2*(subdivisions+1)||result.Positions.Distinct().Count()!=11+3*subdivisions)throw new Exception("Two strips did not share a single miter profile");
    var uses=new Dictionary<(Vector3,Vector3),int>();
    foreach(var face in result.Faces)for(int i=0;i<face.Vertices.Count;i++){var key=StudioMeshCuts.NormalEdgePositionKeyForBevel(result.Positions[face.Vertices[i]],result.Positions[face.Vertices[(i+1)%face.Vertices.Count]]);uses.TryGetValue(key,out int count);uses[key]=count+1;}
    if(uses.Values.Any(n=>n!=2))throw new Exception("Shared miter profile is disconnected");shared++;
   }
   finally{Object.DestroyImmediate(mesh);}
  }
  File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../bevel-profile-validation.txt")),checks+" independent three-operation terminal profile cases and "+shared+" shared-miter profile cases PASS; supporting planes, face/vertex counts, closed edges and render references checked.");
 }
 public static void Run(){try{Validate();EditorApplication.Exit(0);}catch(Exception e){File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../bevel-profile-validation.txt")),e.ToString());EditorApplication.Exit(1);}}
}

#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
public static class StudioBevelContinuityValidation {
 static string label;static int failures,checks;static List<string> report=new List<string>();
 public static void Before(){label="before";Run();}
 public static void After(){label="after";Run();}
 public static void Safety(){Mesh mesh=null;try{
  int count=0;
  var test=typeof(StudioBevelValidation).GetMethod("Case",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
  foreach(int mask in new[]{1,3,7,15,63,255,1365,4095})foreach(int subdivisions in new[]{0,3})foreach(float tension in new[]{-.53f,.53f})foreach(float width in new[]{.2f,.79f,1f}){
   test.Invoke(null,new object[]{mask,subdivisions,tension,StudioMeshCuts.BevelJunction.Uniform,width});count++;
  }
  int sides=8;var vertices=new List<Vector3>();for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides;vertices.Add(new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)));}vertices.Add(new Vector3(0,1.5f,0));
  var faces=new List<int[]>{Enumerable.Range(0,sides).ToArray()};for(int i=0;i<sides;i++)faces.Add(new[]{(i+1)%sides,i,sides});
  var indices=new List<int>();var polygons=new List<int>();foreach(var f in faces){polygons.Add(0);polygons.Add(f.Length);polygons.AddRange(f);for(int i=1;i<f.Length-1;i++)indices.AddRange(new[]{f[0],f[i],f[i+1]});}
  mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
  var source=Object.Instantiate(mesh);try{var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons.ToArray(),cutTriangles=indices.ToArray()};var cuts=new StudioMeshCuts(node);
   var selected=new HashSet<(int,int)>();foreach(var f in faces)for(int i=0;i<f.Length;i++)selected.Add(Key(f[i],f[(i+1)%f.Length]));
   float previous=0;for(int step=1;step<=20;step++){
    StudioMeshToolPreview.CopyMesh(source,mesh);node.cutPolygons=polygons.ToArray();node.cutTriangles=indices.ToArray();node.normalGroups=null;
    float width=step/20f;var result=cuts.BevelEdges(node,selected,new StudioMeshCuts.BevelOptions{Width=width,Subdivisions=3,Tension=.53f,Junction=StudioMeshCuts.BevelJunction.Uniform});
    if(result.AppliedWidth+1e-4f<previous)throw new Exception("Overlap clamp shrank as width increased");
    report.Add($"pyramid requested={width:R} applied={result.AppliedWidth:R}");previous=result.AppliedWidth;count++;
   }
  }finally{Object.DestroyImmediate(source);}
  report.Add("PASS "+count+" safety cases");
 }catch(Exception e){report.Add("FAIL "+e);failures++;}finally{
  if(mesh)Object.DestroyImmediate(mesh);File.WriteAllText(Path.Combine(Application.dataPath,"../bevel-continuity-safety.txt"),string.Join("\n",report));EditorApplication.Exit(failures>0?1:0);
 }}
 static (int,int) Key(int a,int b)=>a<b?(a,b):(b,a);
 static void Run(){Mesh source=null,work=null;try{
  source=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);work=Object.Instantiate(source);
  var node=new StudioModelDocument.Node{mesh=work,cutPolygons=polygons,cutTriangles=source.triangles};var topology=new StudioMeshCuts(node);
  var sourcePoints=source.vertices;var seen=new HashSet<(Vector3,Vector3)>();var edges=new List<(int a,int b)>();
  foreach(var f in topology.Faces)for(int i=0;i<f.Vertices.Count;i++){int a=f.Vertices[i],b=f.Vertices[(i+1)%f.Vertices.Count];var k=StudioMeshCuts.NormalEdgePositionKeyForBevel(sourcePoints[a],sourcePoints[b]);if(seen.Add(k))edges.Add((a,b));}
  foreach(int subdivisions in new[]{0,3})foreach(var junction in new[]{StudioMeshCuts.BevelJunction.Uniform,StudioMeshCuts.BevelJunction.Patch}){
   int pops=0;float worst=0;Vector3[] previous=null;
   for(int step=1;step<=200;step++){
    float width=step/200f;StudioMeshToolPreview.CopyMesh(source,work);node.cutPolygons=polygons;node.cutTriangles=source.triangles;node.normalGroups=null;
    var result=topology.BevelEdges(node,edges,new StudioMeshCuts.BevelOptions{Width=width,Subdivisions=subdivisions,Tension=.53f,Junction=junction});
    checks++;if(Mathf.Abs(result.AppliedWidth-width)>1e-6f){failures++;pops++;if(pops<=8)report.Add($"sub={subdivisions} junction={junction} requested={width:R} applied={result.AppliedWidth:R}");}
    var points=work.vertices.Distinct().ToArray();if(previous!=null){float distance=0;foreach(var p in points)distance=Mathf.Max(distance,previous.Min(q=>Vector3.Distance(p,q)));foreach(var p in previous)distance=Mathf.Max(distance,points.Min(q=>Vector3.Distance(p,q)));worst=Mathf.Max(worst,distance);checks++;if(distance>.02f)failures++;}previous=points;
   }
   report.Add($"sub={subdivisions} junction={junction} reductions={pops} worst adjacent shape change={worst:R}");
  }
  report.Add($"COMPLETE checks={checks} failures={failures}");
 }catch(Exception e){failures++;report.Add("FAIL "+e);}finally{
  if(source)Object.DestroyImmediate(source);if(work)Object.DestroyImmediate(work);
  File.WriteAllText(Path.Combine(Application.dataPath,"../bevel-continuity-"+label+".txt"),string.Join("\n",report));EditorApplication.Exit(label=="after"&&failures>0?1:0);
 }}
}
#endif

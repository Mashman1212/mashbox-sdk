#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
public static class StudioBevelWavefrontValidation {
 public static void Run(){var report=new List<string>();int failures=0,cases=0;try{
 report.Add(StudioBevelEventValidation.RunCore()); StudioBevelParallelValidation.RunCore(false); var sequential=File.ReadAllText(Path.Combine(Application.dataPath,"../bevel-parallel-result.txt"));if(!sequential.Contains("cases=15 skew=0 warped=0"))throw new Exception(sequential);report.Add(sequential);
 var test=typeof(StudioBevelValidation).GetMethod("Case",BindingFlags.NonPublic|BindingFlags.Static);
 foreach(int mask in new[]{1,3,7,15,63,255,1365,4095})foreach(int subdivisions in new[]{0,3})foreach(float tension in new[]{-.53f,.53f})foreach(float width in new[]{.2f,.79f,1f}){
 try{test.Invoke(null,new object[]{mask,subdivisions,tension,StudioMeshCuts.BevelJunction.Uniform,width});cases++;}catch(Exception e){failures++;report.Add("FAIL "+e.GetBaseException().Message);}}
 var source=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);var mesh=Object.Instantiate(source);try{
 var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons,cutTriangles=source.triangles};var cuts=new StudioMeshCuts(node);var edges=new List<(int,int)>();var seen=new HashSet<(Vector3,Vector3)>();
 foreach(var f in cuts.Faces)for(int i=0;i<f.Vertices.Count;i++){int a=f.Vertices[i],b=f.Vertices[(i+1)%f.Vertices.Count];if(seen.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(cuts.Positions[a],cuts.Positions[b])))edges.Add((a,b));}
 var watch=new Stopwatch();Vector3[] previous=null;double ms=0;object plan=null;var field=typeof(StudioMeshCuts).GetField("bevelWavefront",BindingFlags.NonPublic|BindingFlags.Instance);
 for(int step=1;step<=100;step++){
 StudioMeshToolPreview.CopyMesh(source,mesh);node.cutPolygons=polygons;node.cutTriangles=source.triangles;node.normalGroups=null;watch.Restart();
 var result=cuts.BevelEdges(node,edges,new StudioMeshCuts.BevelOptions{Width=step/100f,Subdivisions=3,Tension=.53f});watch.Stop();if(step>1)ms+=watch.Elapsed.TotalMilliseconds;
 var nextPlan=field.GetValue(cuts);if(plan!=null&&!ReferenceEquals(plan,nextPlan))throw new Exception("Width scrubbing rebuilt wavefront plan");plan=nextPlan;
 if(Mathf.Abs(result.AppliedWidth-step/100f)>1e-5f)throw new Exception("Unexpected cube width clamp");
 var points=mesh.vertices.Distinct().ToArray();if(previous!=null&&points.Any(p=>previous.Min(q=>Vector3.Distance(p,q))>.02f))throw new Exception("Shape discontinuity");previous=points;cases++;
 }
 report.Add("Cached cube evaluations mean ms="+(ms/99).ToString("F3")+" (geometry/publication only)");
 }finally{Object.DestroyImmediate(mesh);Object.DestroyImmediate(source);}
 }catch(Exception e){failures++;report.Add("FAIL "+e);}finally{report.Add($"COMPLETE cases={cases} failures={failures}");File.WriteAllLines(Path.Combine(Application.dataPath,"../bevel-wavefront-result.txt"),report);EditorApplication.Exit(failures==0?0:1);}}
}
#endif

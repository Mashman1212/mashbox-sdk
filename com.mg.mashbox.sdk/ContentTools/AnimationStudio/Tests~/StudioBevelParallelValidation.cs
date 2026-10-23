#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
public static class StudioBevelParallelValidation {
 static List<(int,int)> Edges(StudioMeshCuts c){var r=new List<(int,int)>();var seen=new HashSet<(Vector3,Vector3)>();foreach(var f in c.Faces)for(int i=0;i<f.Vertices.Count;i++){int a=f.Vertices[i],b=f.Vertices[(i+1)%f.Vertices.Count];if(seen.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(c.Positions[a],c.Positions[b])))r.Add((a,b));}return r;}
 public static void Run(){RunCore(true);} public static void RunCore(bool quit){int cases=0,skew=0,warped=0;float worst=0;var report=new List<string>();try{
 var cube=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polys);
 try{var seed=new StudioModelDocument.Node{mesh=cube,cutPolygons=polys,cutTriangles=cube.triangles};var cut=new StudioMeshCuts(seed);
 cut.BevelEdges(seed,new[]{Edges(cut)[0]},new StudioMeshCuts.BevelOptions{Width=.6f,Tension=1,Subdivisions=0});
 var once=new StudioMeshCuts(seed);var edges=Edges(once);
 foreach(var e in edges){var mesh=Object.Instantiate(cube);try{
 var n=new StudioModelDocument.Node{mesh=mesh,cutPolygons=seed.cutPolygons,cutTriangles=seed.cutTriangles};var c=new StudioMeshCuts(n);var axis=(c.Positions[e.Item2]-c.Positions[e.Item1]).normalized;
 var result=c.BevelEdges(n,new[]{e},new StudioMeshCuts.BevelOptions{Width=.4f,Tension=1,Subdivisions=0});cases++;
 foreach(var rail in result.Selection){float err=Vector3.Cross((rail.Item2-rail.Item1).normalized,axis).magnitude;worst=Mathf.Max(worst,err);if(err>1e-5f){skew++;report.Add("skew edge="+e+" error="+err);}}
 var after=new StudioMeshCuts(n);foreach(var f in after.Faces){var v=f.Vertices;if(v.Count<4)continue;var p=after.Positions[v[0]];var normal=Vector3.Cross(after.Positions[v[1]]-p,after.Positions[v[2]]-p).normalized; if(v.Any(i=>Mathf.Abs(Vector3.Dot(after.Positions[i]-p,normal))>1e-5f))warped++;}
 }finally{Object.DestroyImmediate(mesh);}}
 }finally{Object.DestroyImmediate(cube);}
 report.Add($"COMPLETE cases={cases} skew={skew} warped={warped} worst={worst:R}");
 }catch(Exception ex){report.Add("FAIL "+ex);skew++;}finally{File.WriteAllLines(Path.Combine(Application.dataPath,"../bevel-parallel-result.txt"),report);if(quit)EditorApplication.Exit(skew+warped==0?0:1);}}
}
#endif

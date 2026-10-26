#if MASHBOX_STUDIO_TESTS
using System;using System.IO;using System.Linq;using System.Collections.Generic;
using MashBoxSDK.AnimationStudio;using MashBoxSDK.AnimationStudio.ThirdParty.StraightSkeleton;using Unity.Mathematics;using UnityEditor;using UnityEngine;using Object=UnityEngine.Object;
public static class StudioBevelEventValidation {
 sealed class Listener:IEventTimeChangeListener{public void AddOrUpdateEdgeEvent(int i){}public void AddOrUpdateVertexEvent(int i){}}
 public static string RunCore(){var report=new List<string>();int count=0;
 var polygon=new PolygonWithHoles(new[]{new float2(0,0),new float2(0,1),new float2(1,1),new float2(1,0)});
 foreach(var pair in new[]{(new float[]{1,1,1,1},.5f),(new float[]{2,2,2,2},.25f),(new float[]{1,0,1,0},.5f),(new float[]{1,0,0,0},1f),(new float[]{0,1,0,0},1f),(new float[]{0,0,1,0},1f),(new float[]{0,0,0,1},1f)}){
 var g=new WavefrontGraph(polygon,new Listener(),false,pair.Item1);g.GenerateInitialEvents();float t=float.PositiveInfinity;foreach(var e in g.edgeEvents)if(e.eventType.IsValid()&&e.eventTime>1e-6)t=Mathf.Min(t,e.eventTime);if(Mathf.Abs(t-pair.Item2)>1e-5)throw new Exception("Wrong collision time "+t+" expected "+pair.Item2);count++;}
 int sides=8;var vertices=new List<Vector3>();for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides;vertices.Add(new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)));}vertices.Add(new Vector3(0,1.5f,0));
 var faces=new List<int[]>{Enumerable.Range(0,sides).ToArray()};for(int i=0;i<sides;i++)faces.Add(new[]{(i+1)%sides,i,sides});var tris=new List<int>();var polys=new List<int>();var selected=new HashSet<(int,int)>();foreach(var f in faces){polys.Add(0);polys.Add(f.Length);polys.AddRange(f);for(int i=1;i<f.Length-1;i++)tris.AddRange(new[]{f[0],f[i],f[i+1]});for(int i=0;i<f.Length;i++)selected.Add(f[i]<f[(i+1)%f.Length]?(f[i],f[(i+1)%f.Length]):(f[(i+1)%f.Length],f[i]));}
 var source=new Mesh();source.SetVertices(vertices);source.SetTriangles(tris,0);source.RecalculateNormals();source.RecalculateBounds();var mesh=Object.Instantiate(source);try{var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polys.ToArray(),cutTriangles=tris.ToArray()};var cuts=new StudioMeshCuts(node);float previous=0;
 for(int step=1;step<=20;step++){StudioMeshToolPreview.CopyMesh(source,mesh);node.cutPolygons=polys.ToArray();node.cutTriangles=tris.ToArray();node.normalGroups=null;var result=cuts.BevelEdges(node,selected,new StudioMeshCuts.BevelOptions{Width=step/20f,Subdivisions=3,Tension=.53f});if(result.AppliedWidth+1e-5<previous)throw new Exception("Clamp shrank");previous=result.AppliedWidth;
 var topology=new StudioMeshCuts(node);var uses=new Dictionary<(Vector3,Vector3),(int count,Vector3 a)>();
 foreach(var face in topology.Faces)for(int k=0;k<face.Vertices.Count;k++){
 var a=topology.Positions[face.Vertices[k]];var b=topology.Positions[face.Vertices[(k+1)%face.Vertices.Count]];var key=StudioMeshCuts.NormalEdgePositionKeyForBevel(a,b);
 if(uses.TryGetValue(key,out var u)){if(u.count!=1||u.a!=b)throw new Exception("Pyramid edge winding/nonmanifold");uses[key]=(2,u.a);}else uses.Add(key,(1,a));}
 if(uses.Values.Any(u=>u.count!=2))throw new Exception("Pyramid has a hole or T junction");count++;}report.Add("Pyramid final clamped width="+previous);
 }finally{Object.DestroyImmediate(source);Object.DestroyImmediate(mesh);}
 report.Add($"PASS {count} event/clamping cases");return string.Join("\n",report);}
 public static void Run(){try{File.WriteAllText(Path.Combine(Application.dataPath,"../bevel-events-result.txt"),RunCore());EditorApplication.Exit(0);}catch(Exception e){File.WriteAllText(Path.Combine(Application.dataPath,"../bevel-events-result.txt"),e.ToString());EditorApplication.Exit(1);}}
}
#endif

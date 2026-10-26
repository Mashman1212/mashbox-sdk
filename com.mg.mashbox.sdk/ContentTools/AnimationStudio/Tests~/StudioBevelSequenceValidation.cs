#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
public static class StudioBevelSequenceValidation {
 static int checks,failures,cases;static List<string> report=new List<string>();static string label;
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool ok,string text){checks++;if(!ok){failures++;if(failures<10)report.Add(text);}}
 static List<(int a,int b)> Edges(StudioMeshCuts cuts){var result=new List<(int,int)>();var seen=new HashSet<(Vector3,Vector3)>();foreach(var face in cuts.Faces)for(int i=0;i<face.Vertices.Count;i++){int a=face.Vertices[i],b=face.Vertices[(i+1)%face.Vertices.Count];if(seen.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(cuts.Positions[a],cuts.Positions[b])))result.Add((a,b));}return result;}
 static Vector3 Normal(StudioMeshCuts cuts,StudioMeshCuts.Face face){var v=face.Vertices;var n=Vector3.zero;for(int i=1;i<v.Count-1;i++)n+=Vector3.Cross(cuts.Positions[v[i]]-cuts.Positions[v[0]],cuts.Positions[v[i+1]]-cuts.Positions[v[0]]);return n.normalized;}
 public static void Before(){label="before";Run();}
 public static void After(){label="after";Run();}
 static void Run(){Mesh original=null;try{
  original=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);var node0=new StudioModelDocument.Node{mesh=original,cutPolygons=polygons,cutTriangles=original.triangles};var sourceCuts=new StudioMeshCuts(node0);var sourceEdges=Edges(sourceCuts);var points=original.vertices;
  for(int first=0;first<sourceEdges.Count;first++)for(int second=0;second<sourceEdges.Count;second++){
   if(first==second)continue;var mesh=Object.Instantiate(original);try{
    var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons,cutTriangles=original.triangles};var cuts=new StudioMeshCuts(node);
    cuts.BevelEdges(node,new[]{sourceEdges[first]},new StudioMeshCuts.BevelOptions{Width=.6f,Subdivisions=0,Tension=1,Junction=StudioMeshCuts.BevelJunction.Uniform});
    cuts=new StudioMeshCuts(node);var wanted=sourceEdges[second];var key=StudioMeshCuts.NormalEdgePositionKeyForBevel(points[wanted.a],points[wanted.b]);var matches=Edges(cuts).Where(e=>StudioMeshCuts.NormalEdgePositionKeyForBevel(cuts.Positions[e.a],cuts.Positions[e.b]).Equals(key)).ToArray();if(matches.Length!=1)continue;
    var edge=matches[0];var adjacent=cuts.Faces.Where(f=>Enumerable.Range(0,f.Vertices.Count).Any(i=>StudioMeshCuts.NormalEdgePositionKeyForBevel(cuts.Positions[f.Vertices[i]],cuts.Positions[f.Vertices[(i+1)%f.Vertices.Count]]).Equals(key))).ToArray();if(adjacent.Length!=2||Mathf.Abs(Vector3.Dot(Normal(cuts,adjacent[0]),Normal(cuts,adjacent[1])))>1e-5f)continue;
    var axis=(cuts.Positions[edge.b]-cuts.Positions[edge.a]).normalized;
    var result=cuts.BevelEdges(node,new[]{edge},new StudioMeshCuts.BevelOptions{Width=.85f,Subdivisions=0,Tension=1,Junction=StudioMeshCuts.BevelJunction.Uniform});
    foreach(var rail in result.Selection){float taper=Vector3.Cross(rail.Item2-rail.Item1,axis).magnitude;Check(taper<1e-5f,$"first={first} second={second} taper={taper:R} source={key} rail={rail}");}cases++;
   }finally{Object.DestroyImmediate(mesh);}
  }
  if(label=="after")SelectionCheck();
  report.Add($"COMPLETE cases={cases} checks={checks} failures={failures}");
 }catch(Exception e){failures++;report.Add("FAIL "+e);}finally{if(original)Object.DestroyImmediate(original);File.WriteAllText(Path.Combine(Application.dataPath,"../bevel-sequence-"+label+".txt"),string.Join("\n",report));EditorApplication.Exit(label=="after"&&failures>0?1:0);}}
 static void SelectionCheck(){var doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);try{
  doc.nodes.Add(new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles});using(var editor=new StudioModelEditor(()=>{})){
   editor.Load(doc);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);editor.Components.Add(0);var active=editor.Transforms[0];
   var kind=Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",F),"Bevel");ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.2f);ToolChannelPane.SetFloatDefault("Bevel Edges","Subdivisions",0);
   typeof(StudioModelEditor).GetMethod("EvaluateLiveTool",F).Invoke(editor,new[]{kind});Check(editor.Components.Count>0,"Preview retains edge selection");
   typeof(StudioModelEditor).GetMethod("ApplyLiveTool",F).Invoke(editor,new[]{kind});Check(editor.Components.Count==0,"Applied bevel clears edge selection");Check(editor.Active==0&&editor.Transforms[0]==active,"Applied bevel retains active object");Check(editor.ToolbarComponentMode==StudioModelEditor.ComponentMode.Edge,"Applied bevel retains Edge mode");
  }
 }finally{Object.DestroyImmediate(mesh);Object.DestroyImmediate(doc);}}
}
#endif

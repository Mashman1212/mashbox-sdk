using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Diagnostics;using System.Reflection;using MashBoxSDK.AnimationStudio;using UnityEditor;using UnityEngine;using Object=UnityEngine.Object;
public static class StudioLiveToolPerformanceValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;static object Kind(string s)=>Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",F),s);
 static void Evaluate(StudioModelEditor e,string s)=>typeof(StudioModelEditor).GetMethod("EvaluateLiveTool",F).Invoke(e,new[]{Kind(s)});
 static void Set(object o,string s,object v)=>o.GetType().GetField(s,F).SetValue(o,v);
 static StudioModelDocument.Node Grid(int n)
 {
  var p=new Vector3[(n+1)*(n+1)];for(int z=0;z<=n;z++)for(int x=0;x<=n;x++)p[z*(n+1)+x]=new Vector3(x*.1f,0,z*.1f);
  var polygons=new List<int>();var triangles=new List<int>();for(int z=0;z<n;z++)for(int x=0;x<n;x++){int a=z*(n+1)+x,b=a+1,c=a+n+2,d=a+n+1;polygons.AddRange(new[]{0,4,d,c,b,a});triangles.AddRange(new[]{d,c,b,d,b,a});}
  var mesh=new Mesh();mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;mesh.vertices=p;mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();return new StudioModelDocument.Node{name="Grid",mesh=mesh,cutPolygons=polygons.ToArray(),cutTriangles=mesh.triangles};
 }
 public static void Run()
 {
  StudioModelEditor editor=null;StudioModelDocument doc=null;
  try{
   doc=ScriptableObject.CreateInstance<StudioModelDocument>();var node=Grid(128);doc.nodes.Add(node);editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);for(int i=0;i<128*128;i++)editor.Components.Add(i);
   var timer=Stopwatch.StartNew();Evaluate(editor,"Extrude");timer.Stop();double first=timer.Elapsed.TotalMilliseconds;var times=new List<double>();var transforms=editor.Transforms.ToArray();
   for(int i=0;i<20;i++){Set(editor,"extrusionDistance",.2f+i*.01f);timer.Restart();Evaluate(editor,"Extrude");timer.Stop();times.Add(timer.Elapsed.TotalMilliseconds);if(!editor.Transforms.SequenceEqual(transforms))throw new Exception("Preview scene rebuilt");}
   times.Sort();string result="PASS 16,384-face extrusion; first topology build "+first.ToString("F2")+" ms; 20 distance edits median "+times[times.Count/2].ToString("F2")+" ms, max "+times.Last().ToString("F2")+" ms; output "+node.mesh.vertexCount+" vertices. Includes geometry, normals, UVs, publication and selection invalidation; excludes screen rendering.";
   File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../live-tools-performance-result.txt")),result);editor.Dispose();Object.DestroyImmediate(node.mesh);Object.DestroyImmediate(doc);EditorApplication.Exit(0);
  }catch(Exception e){File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../live-tools-performance-result.txt")),"FAIL "+e);editor?.Dispose();EditorApplication.Exit(1);}
 }
}

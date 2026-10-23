#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
public static class StudioBevelLatencyValidation {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static AnimationStudioWindow owner;static MashBoxStudioViewport view;static StudioModelEditor editor;static StudioModelDocument doc;static ToolChannelPane pane;
 static double due,submitted,started;static int samples;static string errors="",label;static bool pending,dragging;static float requested;
 static List<double> cpu=new List<double>(),latency=new List<double>();
 static object Get(object t,string n)=>t.GetType().GetField(n,F).GetValue(t);
 static void Set(object t,string n,object v)=>t.GetType().GetField(n,F).SetValue(t,v);
 static void Call(string n,params object[] args)=>typeof(StudioModelEditor).GetMethod(n,F).Invoke(editor,args);
 static object Kind()=>Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",F),"Bevel");
 static string Stats(List<double> values){var a=values.OrderBy(x=>x).ToArray();return $"median={a[a.Length/2]:F3} p95={a[(int)((a.Length-1)*.95)]:F3} max={a.Last():F3} ms";}
 const string PendingKey="Studio.Tests.BevelLatency.Pending";
 public static void Before(){Schedule("before");}
 public static void After(){Schedule("after");}
 static void Schedule(string value){SessionState.SetString(PendingKey,value);Resume();}
 [InitializeOnLoadMethod]static void Resume(){if(SessionState.GetString(PendingKey,"")=="")return;due=EditorApplication.timeSinceStartup+3;EditorApplication.update-=Wait;EditorApplication.update+=Wait;}
 static void Wait(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||!UnityEditorInternal.InternalEditorUtility.isApplicationActive){due=EditorApplication.timeSinceStartup+3;return;}if(EditorApplication.timeSinceStartup<due)return;EditorApplication.update-=Wait;label=SessionState.GetString(PendingKey,"");Begin();}
 static void Log(string s,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors+=s+"\n"+stack;}
 static void Begin(){try{
  doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
  doc.nodes.Add(new StudioModelDocument.Node{name="Latency cube",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles});
  editor=new StudioModelEditor(()=>{});editor.Load(doc);
  editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);
  var edges=(System.Collections.ICollection)typeof(StudioModelEditor).GetMethod("Edges",F).Invoke(editor,null);for(int i=0;i<edges.Count;i++)editor.Components.Add(i);
  ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.1f);ToolChannelPane.SetFloatDefault("Bevel Edges","Subdivisions",3);ToolChannelPane.SetFloatDefault("Bevel Edges","Tension",1);ToolChannelPane.SetFloatDefault("Bevel Edges","Junction",0);
  // Measure complete evaluation separately from event scheduling and screen rendering.
  for(int i=0;i<35;i++){ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.1f+i*.002f);var timer=Stopwatch.StartNew();Call("EvaluateLiveTool",Kind());timer.Stop();if(i>=5)cpu.Add(timer.Elapsed.TotalMilliseconds);}
  Call("OpenBevelTool");pane=(ToolChannelPane)Get(editor,"bevelToolPane");pane.position=new Rect(100,100,340,235);Set(pane,"dragSensitivity",.1f);pane.Focus();
  Application.logMessageReceived+=Log;due=EditorApplication.timeSinceStartup+1;EditorApplication.update+=Tick;
 }catch(Exception e){Finish(e);}}
 static void Tick(){try{
  double now=EditorApplication.timeSinceStartup;if(now<due)return;
  if(pending&&now-submitted>5)throw new Exception("Preview delivery timed out");
  if(pending){if((bool)Get(editor,"previewQueued"))return;
   float actual=((Vector4)Get(editor,"liveParameters")).x;if(actual!=requested)throw new Exception("Wrong delivered width "+actual+" expected "+requested);
   latency.Add((now-submitted)*1000);pending=false;samples++;
   if(samples==30){Finish(null);return;}
  }
  if(!dragging){dragging=true;started=now;}
  float previous=ToolChannelPane.GetFloatDefault("Bevel Edges","Width",0);
  ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.1f+samples*.003f);Call("QueueLiveTool",Kind());
  requested=ToolChannelPane.GetFloatDefault("Bevel Edges","Width",0);
  submitted=EditorApplication.timeSinceStartup;pending=true;
 }catch(Exception e){Finish(e);}}
 static void Finish(Exception error){
  SessionState.EraseString(PendingKey);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
  string text=error==null?$"PASS cube, 12 selected edges, 3 subdivisions; 30 full evaluations {Stats(cpu)}; 30 queued deliveries {Stats(latency)}; delivery rate {samples/(EditorApplication.timeSinceStartup-started):F2}/sec; errors={errors}":"FAIL "+error+"\n"+errors+"\nCPU "+(cpu.Count>0?Stats(cpu):"none")+"\nDelivered="+samples+" "+(latency.Count>0?Stats(latency):"");
  File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../bevel-latency-"+label+".txt")),text);
  if(pane)pane.Close();editor?.Dispose();if(view)view.Close();if(owner)owner.Close();
  if(doc){foreach(var n in doc.nodes)if(n.mesh)Object.DestroyImmediate(n.mesh);Object.DestroyImmediate(doc);}EditorApplication.Exit(error==null?0:1);
 }
}
#endif

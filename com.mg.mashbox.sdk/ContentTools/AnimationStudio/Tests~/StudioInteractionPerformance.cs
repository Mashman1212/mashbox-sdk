using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Reflection;using System.Diagnostics;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;
public static class StudioInteractionPerformance {
 public static void Run(){try{Validate();UnityEditor.EditorApplication.Exit(0);}catch(Exception e){UnityEngine.Debug.LogException(e);UnityEditor.EditorApplication.Exit(1);}}

 static readonly BindingFlags F=BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 public static void Validate(){var report=new StringBuilder();int exit=0;string saved=StudioPreferenceStore.GetString("MashBox.Studio.ToolChannelDefaults.v1","");
 try{
 var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FrameProbe.fbx");var doc=StudioModelDocument.Import(model,"Assets/PerformanceFrame.asset");
 using(var editor=new StudioModelEditor(()=>{})){
 editor.Load(doc);int active=doc.nodes.FindIndex(n=>n.name=="Body"&&n.mesh);editor.SelectObject(active);var mesh=editor.ActiveMesh;var points=mesh.vertices;var topology=new StudioMeshCuts(doc.nodes[active]);
 var uses=new Dictionary<(Vector3,Vector3),List<(int a,int b,Vector3 normal)>>();
 foreach(var face in topology.Faces){var origin=points[face.Vertices[0]];var normal=Vector3.zero;for(int i=1;i+1<face.Vertices.Count;i++)normal+=Vector3.Cross(points[face.Vertices[i]]-origin,points[face.Vertices[i+1]]-origin);normal.Normalize();for(int i=0;i<face.Vertices.Count;i++){int a=face.Vertices[i],b=face.Vertices[(i+1)%face.Vertices.Count];var key=StudioMeshCuts.NormalEdgePositionKeyForBevel(points[a],points[b]);if(!uses.TryGetValue(key,out var list))uses[key]=list=new List<(int,int,Vector3)>();list.Add((a,b,normal));}}
 var sharp=uses.Where(p=>p.Value.Count==2&&Vector3.Dot(p.Value[0].normal,p.Value[1].normal)<.8f).Select(p=>p.Key).ToArray();var links=new Dictionary<Vector3,List<int>>();for(int i=0;i<sharp.Length;i++)foreach(var v in new[]{sharp[i].Item1,sharp[i].Item2}){if(!links.TryGetValue(v,out var list))links[v]=list=new List<int>();list.Add(i);}var seen=new HashSet<int>();List<int> chosen=null;
 for(int seed=0;seed<sharp.Length&&chosen==null;seed++){if(!seen.Add(seed))continue;var queue=new Queue<int>();queue.Enqueue(seed);var group=new List<int>();bool closed=true;while(queue.Count>0){int i=queue.Dequeue();group.Add(i);foreach(var v in new[]{sharp[i].Item1,sharp[i].Item2}){closed&=links[v].Count==2;foreach(int j in links[v])if(seen.Add(j))queue.Enqueue(j);}}if(closed&&group.Count==40)chosen=group;}
 if(chosen==null)throw new Exception("No frame rim found");
 var edges=(List<(int a,int b)>)typeof(StudioModelEditor).GetMethod("Edges",F).Invoke(editor,null);var keys=new HashSet<(Vector3,Vector3)>(chosen.Take(21).Select(i=>sharp[i]));
 typeof(StudioModelEditor).GetField("mode",F).SetValue(editor,StudioModelEditor.ComponentMode.Edge);for(int i=0;i<edges.Count;i++)if(keys.Contains(StudioMeshCuts.NormalEdgePositionKeyForBevel(points[edges[i].a],points[edges[i].b])))editor.Components.Add(i);
 report.AppendLine("Body vertices="+mesh.vertexCount+" selected geometric edges="+keys.Count+" render edge indices="+editor.Components.Count);
 ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.65f);ToolChannelPane.SetFloatDefault("Bevel Edges","Subdivisions",0);ToolChannelPane.SetFloatDefault("Bevel Edges","Tension",1);
 var kind=Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",F),"Bevel");var evaluate=typeof(StudioModelEditor).GetMethod("EvaluateLiveTool",F);var totals=new List<double>();var allocation=new List<double>();var stages=new Dictionary<string,List<double>>();long previous=0;bool capture=false;
 Action<string> stage=name=>{long now=Stopwatch.GetTimestamp();if(capture){if(!stages.TryGetValue(name,out var values))stages[name]=values=new List<double>();values.Add((now-previous)*1000d/Stopwatch.Frequency);}previous=now;};
 StudioMeshCuts.BevelProfileStage=stage;StudioModelEditor.LiveProfileStage=stage;
 for(int i=0;i<100;i++){ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.65f+i*.003f);capture=i>=10;previous=Stopwatch.GetTimestamp();long before=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();evaluate.Invoke(editor,new object[]{kind});watch.Stop();if(capture){totals.Add(watch.Elapsed.TotalMilliseconds);allocation.Add((GC.GetAllocatedBytesForCurrentThread()-before)/1024d);}}
 capture=false;StudioMeshCuts.BevelProfileStage=null;StudioModelEditor.LiveProfileStage=null;
 double Median(List<double> values)=>values.OrderBy(v=>v).ElementAt(values.Count/2);
 report.AppendLine("Live Width median ms="+Median(totals)+" p95 ms="+totals.OrderBy(v=>v).ElementAt((int)Math.Ceiling(totals.Count*.95)-1)+" max ms="+totals.Max());foreach(var pair in stages)report.AppendLine(pair.Key+" median ms="+Median(pair.Value)+" p95 ms="+pair.Value.OrderBy(v=>v).ElementAt((int)Math.Ceiling(pair.Value.Count*.95)-1));
 ToolChannelPane.SetFloatDefault("Bevel Edges","Width",0);evaluate.Invoke(editor,new object[]{kind});if(!editor.ActiveMesh.vertices.SequenceEqual(points))throw new Exception("Width zero did not restore source");
 ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.4f);evaluate.Invoke(editor,new object[]{kind});
 typeof(StudioModelEditor).GetMethod("CancelLiveTool",F,null,new[]{typeof(bool)},null).Invoke(editor,new object[]{false});
 if(!editor.ActiveMesh.vertices.SequenceEqual(points))throw new Exception("Live cancel did not restore source");report.AppendLine("Live zero-width/restart/cancel lifecycle PASS");
 var captureOccluders=typeof(StudioModelEditor).GetMethod("CaptureOccluders",F);var occluders=captureOccluders.Invoke(editor,null);var occlusion=typeof(StudioModelEditor).GetMethod("OccludedBatch",F);var rayOrigin=new Vector3(.5f,.35f,.7f);var rays=new Ray[points.Length];var limits=new float[points.Length];
 for(int i=0;i<points.Length;i++){var point=editor.Transforms[active].TransformPoint(points[(i*13)%points.Length]);var direction=point-rayOrigin;rays[i]=new Ray(rayOrigin,direction.normalized);limits[i]=direction.magnitude-.001f;}
 var timer=Stopwatch.StartNew();var hidden=(bool[])occlusion.Invoke(null,new object[]{rays,limits,occluders});report.AppendLine("Full Body batch cold visibility ms="+timer.Elapsed.TotalMilliseconds+" queries="+points.Length+" hidden="+hidden.Count(v=>v));
 occluders=captureOccluders.Invoke(editor,null);timer.Restart();var warmHidden=(bool[])occlusion.Invoke(null,new object[]{rays,limits,occluders});report.AppendLine("Full Body batch warm visibility ms="+timer.Elapsed.TotalMilliseconds+" hidden="+warmHidden.Count(v=>v));if(!hidden.SequenceEqual(warmHidden)||hidden.Count(v=>v)!=8355)throw new Exception("Frame visibility changed");

 }
 }catch(Exception e){exit=1;report.AppendLine(e.ToString());}
 File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../interaction-performance.txt")),report.ToString());if(exit != 0) throw new Exception("Interaction performance/lifecycle failed; see report");
 }
}



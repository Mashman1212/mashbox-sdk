using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
public static class StudioFrameValidation {
 public static void Run(){try{Validate();UnityEditor.EditorApplication.Exit(0);}catch(Exception e){UnityEngine.Debug.LogException(e);UnityEditor.EditorApplication.Exit(1);}}

 public static void Validate(){var report=new StringBuilder();int exit=0;
 try{
 var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FrameProbe.fbx");
 var doc=StudioModelDocument.Import(model,"Assets/FrameValidation.asset");
 using(var editor=new StudioModelEditor(()=>{})){
 editor.Load(doc);int n=doc.nodes.FindIndex(node=>node.name=="Body"&&node.mesh);editor.SelectObject(n);
 var flags=BindingFlags.Instance|BindingFlags.NonPublic;var et=typeof(StudioModelEditor);
 et.GetField("mode",flags).SetValue(editor,StudioModelEditor.ComponentMode.Edge);editor.Components.Add(0);
 report.AppendLine("New import readable="+editor.ActiveMesh.isReadable+" CanBevel="+et.GetProperty("CanBevelEdges",flags).GetValue(editor));
 var sourceNode=doc.nodes[n];var source=editor.ActiveMesh;var points=source.vertices;
 var topo=new StudioMeshCuts(sourceNode);
 var uses=new Dictionary<(Vector3,Vector3),List<(int a,int b)>>();
 foreach(var face in topo.Faces)for(int e=0;e<face.Vertices.Count;e++){
 int a=face.Vertices[e],b=face.Vertices[(e+1)%face.Vertices.Count];var key=StudioMeshCuts.NormalEdgePositionKeyForBevel(points[a],points[b]);
 if(!uses.TryGetValue(key,out var list))uses[key]=list=new List<(int,int)>();list.Add((a,b));}
 var candidates=uses.Where(pair=>pair.Value.Count==2).Take(8).Select(pair=>pair.Value[0]).ToArray();
 foreach(var edge in candidates){
 var watch=Stopwatch.StartNew();
 try{using(var preview=new StudioMeshToolPreview(doc,new[]{n})){
 var target=preview.Targets[0];var result=target.Topology.BevelEdges(target.Work,new[]{edge},new StudioMeshCuts.BevelOptions{Width=.1f,Subdivisions=0,Tension=1,Junction=StudioMeshCuts.BevelJunction.Uniform});
 report.AppendLine("Bevel "+edge+" OK edges="+result.Edges+" mean ms="+watch.Elapsed.TotalMilliseconds);
 }}catch(Exception ex){report.AppendLine("Bevel "+edge+" FAILED "+ex.Message);throw;}
 }

 var edgeNormals=new Dictionary<(Vector3,Vector3),List<Vector3>>();
 foreach(var face in topo.Faces){var origin=points[face.Vertices[0]];var normal=Vector3.zero;for(int i=1;i+1<face.Vertices.Count;i++)normal+=Vector3.Cross(points[face.Vertices[i]]-origin,points[face.Vertices[i+1]]-origin);normal.Normalize();
 for(int i=0;i<face.Vertices.Count;i++){var key=StudioMeshCuts.NormalEdgePositionKeyForBevel(points[face.Vertices[i]],points[face.Vertices[(i+1)%face.Vertices.Count]]);if(!edgeNormals.TryGetValue(key,out var list))edgeNormals[key]=list=new List<Vector3>();list.Add(normal);}}
 var sharp=edgeNormals.Where(pair=>pair.Value.Count==2&&Vector3.Dot(pair.Value[0],pair.Value[1])<.8f).Select(pair=>pair.Key).ToArray();
 var links=new Dictionary<Vector3,List<int>>();for(int i=0;i<sharp.Length;i++)foreach(var v in new[]{sharp[i].Item1,sharp[i].Item2}){if(!links.TryGetValue(v,out var list))links[v]=list=new List<int>();list.Add(i);}
 var seen=new HashSet<int>();int loops=0;
 for(int seed=0;seed<sharp.Length;seed++){
 if(!seen.Add(seed))continue;var queue=new Queue<int>();queue.Enqueue(seed);var group=new List<int>();bool closed=true;
 while(queue.Count>0){int i=queue.Dequeue();group.Add(i);foreach(var v in new[]{sharp[i].Item1,sharp[i].Item2}){closed&=links[v].Count==2;foreach(int j in links[v])if(seen.Add(j))queue.Enqueue(j);}}
 if(!closed||group.Count!=40)continue;loops++;
 using(var preview=new StudioMeshToolPreview(doc,new[]{n})){
 var target=preview.Targets[0];var selected=group.Select(i=>uses[sharp[i]][0]).ToArray();
 var watch=Stopwatch.StartNew();var result=target.Topology.BevelEdges(target.Work,selected,new StudioMeshCuts.BevelOptions{Width=.1f,Subdivisions=0,Tension=1,Junction=StudioMeshCuts.BevelJunction.Uniform});
 report.AppendLine("40-edge rim loop "+loops+" OK selected="+result.Edges+" evaluation ms="+watch.Elapsed.TotalMilliseconds); foreach(int divisions in new[]{1,3}){target.Reset();var round=target.Topology.BevelEdges(target.Work,selected,new StudioMeshCuts.BevelOptions{Width=.1f,Subdivisions=divisions,Tension=1,Junction=StudioMeshCuts.BevelJunction.Uniform});if(round.Edges!=40)throw new Exception("Rim bevel lost selection");report.AppendLine("Rim "+loops+" subdivisions="+divisions+" OK");}
 }
 }
 report.AppendLine("Closed 40-edge rim loops checked="+loops);
 if(loops==0){
 var all=uses.Keys.Where(k=>k.Item1!=k.Item2).ToArray();var byPoint=new Dictionary<Vector3,List<int>>();
 for(int i=0;i<all.Length;i++)foreach(var v in new[]{all[i].Item1,all[i].Item2}){if(!byPoint.TryGetValue(v,out var list))byPoint[v]=list=new List<int>();list.Add(i);}
 var signatures=new HashSet<string>();var lengths=new HashSet<int>();int tested=0;
 for(int seed=0;seed<all.Length;seed++){
 var path=new List<int>{seed};var start=all[seed].Item1;var previous=start;var current=all[seed].Item2;bool closed=false;
 for(int step=1;step<81;step++){
 if(current==start){closed=true;break;}
 var direction=(current-previous).normalized;int best=-1;float score=.8f;
 foreach(int next in byPoint[current]){if(next==path[path.Count-1])continue;var end=all[next].Item1==current?all[next].Item2:all[next].Item1;float dot=Vector3.Dot(direction,(end-current).normalized);if(dot>score){score=dot;best=next;}}
 if(best<0||path.Contains(best))break;var point=all[best].Item1==current?all[best].Item2:all[best].Item1;path.Add(best);previous=current;current=point;
 }
 if(!closed||path.Count<16)continue;lengths.Add(path.Count);if(path.Count!=40||tested>=4)continue;
 string signature=string.Join(",",path.OrderBy(i=>i));if(!signatures.Add(signature))continue;tested++;
 using(var preview=new StudioMeshToolPreview(doc,new[]{n})){
 var target=preview.Targets[0];var selected=path.Select(i=>uses[all[i]][0]).ToArray();var watch=Stopwatch.StartNew();
 var sum=Vector3.zero;foreach(var pair in selected)sum+=points[pair.Item1];report.AppendLine("Rim center="+(sum/selected.Length).ToString("R"));
 var result=target.Topology.BevelEdges(target.Work,selected,new StudioMeshCuts.BevelOptions{Width=.1f,Subdivisions=0,Tension=1,Junction=StudioMeshCuts.BevelJunction.Uniform});
 report.AppendLine("Continuous 40-edge rim "+tested+" OK selected="+result.Edges+" evaluation ms="+watch.Elapsed.TotalMilliseconds);
 foreach(int divisions in new[]{1,3}){target.Reset();var round=target.Topology.BevelEdges(target.Work,selected,new StudioMeshCuts.BevelOptions{Width=.1f,Subdivisions=divisions,Tension=1,Junction=StudioMeshCuts.BevelJunction.Uniform});if(round.Edges!=40)throw new Exception("Rim bevel lost selection");report.AppendLine("Rim "+tested+" subdivisions="+divisions+" OK");}

 }
 }
 report.AppendLine("Continuous ring lengths found="+string.Join(",",lengths.OrderBy(i=>i)));report.AppendLine("Continuous 40-edge rim tests="+tested);
 }

 var referenceEdges=new HashSet<(Vector3,Vector3)>(uses.Keys.Where(k=>k.Item1!=k.Item2));var uv=new List<Vector2>();source.GetUVs(0,uv);
 var expected=new long[]{points.Distinct().Count(),referenceEdges.Count,topo.Faces.Count,source.triangles.Length/3,uv.Count};
 var actual=(long[])et.GetMethod("CountMesh",flags|BindingFlags.Static).Invoke(null,new object[]{sourceNode});
 if(!actual.SequenceEqual(expected))throw new Exception("Statistics differ from topology reference");report.AppendLine("Statistics equal full-topology reference");
 var caseMethod=typeof(StudioBevelValidation).GetMethod("Case",BindingFlags.Static|BindingFlags.NonPublic);int cubeCases=0;
 foreach(int mask in new[]{1,3,7,15,31,63,255,1365,2730,4095})foreach(int divisions in new[]{0,1,3})foreach(var junction in new[]{StudioMeshCuts.BevelJunction.Uniform,StudioMeshCuts.BevelJunction.Patch}){caseMethod.Invoke(null,new object[]{mask,divisions,.53f,junction,.2f});cubeCases++;}
 report.AppendLine("Cube bevel geometry regressions passed="+cubeCases);
 var plain=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var plainPolys);
 try{plain.normals=Array.Empty<Vector3>();var plainNode=new StudioModelDocument.Node{mesh=plain,cutPolygons=plainPolys,cutTriangles=plain.triangles};var plainCuts=new StudioMeshCuts(plainNode);var face=plainCuts.Faces[0];plainCuts.BevelEdges(plainNode,new[]{(face.Vertices[0],face.Vertices[1])},new StudioMeshCuts.BevelOptions{Width=.1f,Subdivisions=0,Tension=1});if(plain.normals.Any(normal=>normal.sqrMagnitude<.9f))throw new Exception("Missing-source normal fallback failed");report.AppendLine("Missing-source normals regression passed");}finally{UnityEngine.Object.DestroyImmediate(plain);}
 var orient=typeof(StudioMeshCuts).GetMethod("BevelOrientation",BindingFlags.Static|BindingFlags.NonPublic);int orientationCases=0;
 for(int exponent=-149;exponent<=120;exponent+=5){float scale=(float)Math.Pow(2,exponent);var a=Vector2.zero;var b=new Vector2(scale,scale);var c=new Vector2(scale*2,scale*2);
 if((int)orient.Invoke(null,new object[]{a,b,c})!=0)throw new Exception("Exact collinearity failed");
 float next=BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(c.y)+1);c.y=next;
 if((int)orient.Invoke(null,new object[]{a,b,c})!=1||(int)orient.Invoke(null,new object[]{a,c,b})!=-1)throw new Exception("Exact orientation sign failed");orientationCases+=3;}
 report.AppendLine("Orientation scale/collinearity regressions passed="+orientationCases);
 foreach(var name in new[]{"CountMesh","SelectedVertices"}){
 var method=et.GetMethod(name,flags|BindingFlags.Static);var watch=Stopwatch.StartNew();
 for(int k=0;k<20;k++)method.Invoke(name=="CountMesh"?null:editor,name=="CountMesh"?new object[]{sourceNode}:null);
 report.AppendLine(name+" mean ms="+watch.Elapsed.TotalMilliseconds/20);
 }

 var statistics=(System.Collections.IDictionary)et.GetField("statisticsCache",flags).GetValue(editor);statistics[sourceNode]=actual;
 var positionChanged=et.GetMethod("PositionsChanged",flags);positionChanged.Invoke(editor,new object[]{true});
 if(!statistics.Contains(sourceNode))throw new Exception("Statistics invalidated during drag");
 et.GetMethod("FlushPositionStatistics",flags).Invoke(editor,null);if(statistics.Contains(sourceNode))throw new Exception("Statistics not invalidated on drag completion");
 statistics[sourceNode]=actual;positionChanged.Invoke(editor,new object[]{false});if(statistics.Contains(sourceNode))throw new Exception("Immediate edit failed to invalidate statistics");
 report.AppendLine("Drag statistics deferral and release invalidation passed");
 var legacy=UnityEngine.Object.Instantiate(model.GetComponentsInChildren<MeshFilter>(true).First(m=>m.name=="Body").sharedMesh);sourceNode.mesh=legacy;editor.Load(doc);editor.SelectObject(n);et.GetField("mode",flags).SetValue(editor,StudioModelEditor.ComponentMode.Edge);editor.Components.Add(0);
 report.AppendLine("Legacy readable="+legacy.isReadable+" CanBevel="+et.GetProperty("CanBevelEdges",flags).GetValue(editor));
 using(var preview=new StudioMeshToolPreview(doc,new[]{n})){
 var target=preview.Targets[0];target.Topology.BevelEdges(target.Work,new[]{candidates[0]},new StudioMeshCuts.BevelOptions{Width=.1f,Subdivisions=0,Tension=1,Junction=StudioMeshCuts.BevelJunction.Uniform});
 preview.Publish(true);report.AppendLine("Legacy publication OK");preview.Restore();report.AppendLine("Legacy cancel restores source="+legacy.vertices.SequenceEqual(points));}
 }
 }catch(Exception e){exit=1;report.AppendLine(e.ToString());foreach(var key in e.Data.Keys)report.AppendLine(key+"="+e.Data[key]);}
 File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../frame-validation.txt")),report.ToString());if(exit != 0) throw new Exception("StudioFrameValidation failed; see result file");
 }
}



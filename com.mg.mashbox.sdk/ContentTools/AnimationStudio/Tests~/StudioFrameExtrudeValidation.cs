using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
public static class StudioFrameExtrudeValidation {
 public static void Run() { var log=new StringBuilder(); int exit=0; try {
 var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FrameProbe.fbx");
 var doc=StudioModelDocument.Import(asset,"Assets/ExtrudeFrameValidation.asset");
 int node=doc.nodes.FindIndex(n=>n.name=="Body"&&n.mesh); if(node<0)throw new Exception("No Body"); doc=UnityEngine.Object.Instantiate(doc); doc.nodes[node].mesh=UnityEngine.Object.Instantiate(doc.nodes[node].mesh);
 using(var preview=new StudioMeshToolPreview(doc,new[]{node})) {
 var t=preview.Targets[0];var topo=t.Topology;var p=topo.Positions;
 log.AppendLine("Body faces="+topo.Faces.Count+" vertices="+p.Length);
 var tri=typeof(StudioMeshCuts).GetMethod("Triangulate",BindingFlags.NonPublic|BindingFlags.Static);
 int fails=0;for(int f=0;f<topo.Faces.Count;f++)try {tri.Invoke(null,new object[]{topo.Faces[f].Vertices,p.ToList(),new List<int>()});}catch(Exception e){if(fails++<8)log.AppendLine("Source triangulation fails face="+f+" count="+topo.Faces[f].Vertices.Count+" "+e.GetBaseException().Message);}
 log.AppendLine("Source triangulation failures="+fails);
 var faces=Enumerable.Range(0,topo.Faces.Count).Where(f=>topo.Faces[f].Vertices.Count==4).OrderByDescending(f=> {var v=topo.Faces[f].Vertices;return (p[v[1]]-p[v[0]]).sqrMagnitude;}).Take(8).ToArray();
 if(faces.Length==0)throw new Exception("No quad test cases"); int passed=0;foreach(int f in faces)foreach(float d in new[]{.01f,.19f,-.19f})foreach(bool handle in new[]{false,true}) {
 t.Reset();try{var r=topo.Extrude(t.Work,new[]{f},null,d,true,true,handle?(Func<Vector3,Vector3>)(v=>v+new Vector3(.05f,.02f,0)):null);if(r.MovingVertices.Length==0)throw new Exception("No moving vertices"); CheckTriangles(t.Source,t.Work.mesh,r);passed++;}catch(Exception e){log.AppendLine("FAIL face="+f+" distance="+d+" handle="+handle+" "+e.GetBaseException().Message);throw;}
 }log.AppendLine("PASS frame extrusion cases="+passed+" with authored triangle preservation");
 }
 CheckLive(doc,node,log); CheckPrimitives(log);
 }catch(Exception e){log.AppendLine(e.ToString());exit=1;}File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../frame-extrude-result.txt")),log.ToString());EditorApplication.Exit(exit); }
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static void CheckTriangles(Mesh source,Mesh result,StudioMeshCuts.ExtrusionResult extrusion) {
 var p=source.vertices;
 for(int s=0;s<source.subMeshCount;s++) {
 var original=source.GetTriangles(s);var actual=result.GetTriangles(s);
 var counts=new Dictionary<(Vector3,Vector3,Vector3),int>();
 for(int i=0;i<original.Length;i+=3){var key=(p[original[i]],p[original[i+1]],p[original[i+2]]);counts.TryGetValue(key,out int n);counts[key]=n+1;}
 for(int i=0;i<original.Length;i+=3){var key=(extrusion.SourceVertices[actual[i]],extrusion.SourceVertices[actual[i+1]],extrusion.SourceVertices[actual[i+2]]);if(!counts.TryGetValue(key,out int n)||n==0)throw new Exception("Source triangle/diagonal changed");counts[key]=n-1;}
 if(counts.Values.Any(n=>n!=0))throw new Exception("Source triangle missing");
 }
 }
 static object Kind()=>Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",F),"Extrude");
 static void Distance(StudioModelEditor editor,float value){typeof(StudioModelEditor).GetField("extrusionDistance",F).SetValue(editor,value);typeof(StudioModelEditor).GetMethod("EvaluateLiveTool",F).Invoke(editor,new[]{Kind()});}
 static void CheckLive(StudioModelDocument doc,int node,StringBuilder log) {
 using(var editor=new StudioModelEditor(()=>{})) {
 editor.Load(doc);editor.SelectObject(node);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);
 var mesh=doc.nodes[node].mesh;var positions=mesh.vertices;var indices=mesh.triangles;
 var topology=new StudioMeshCuts(doc.nodes[node]);int face=Enumerable.Range(0,topology.Faces.Count).Where(f=>topology.Faces[f].Vertices.Count==4).OrderByDescending(f=>(positions[topology.Faces[f].Vertices[1]]-positions[topology.Faces[f].Vertices[0]]).sqrMagnitude).First();editor.Components.Add(face);
 Distance(editor,0);if(!mesh.vertices.SequenceEqual(positions)||!mesh.triangles.SequenceEqual(indices))throw new Exception("Initial zero changed mesh");
 Distance(editor,.01f);if(mesh.vertexCount<=positions.Length)throw new Exception("No live extrusion");
 Distance(editor,.19f);Distance(editor,-.19f);Distance(editor,0);
 if(!mesh.vertices.SequenceEqual(positions)||!mesh.triangles.SequenceEqual(indices))throw new Exception("Zero failed to restore source");
 Distance(editor,.02f);typeof(StudioModelEditor).GetMethod("CancelLiveTool",F,null,new[]{typeof(bool)},null).Invoke(editor,new object[]{false});
 if(!mesh.vertices.SequenceEqual(positions)||!mesh.triangles.SequenceEqual(indices)||!editor.Components.Contains(face))throw new Exception("Cancel failed to restore source/selection");
 Distance(editor,.02f);StudioUndo.IncrementCurrentGroup();typeof(StudioModelEditor).GetMethod("ApplyLiveTool",F).Invoke(editor,new[]{Kind()});UnityEditor.Undo.FlushUndoRecordObjects();var committed=mesh.vertices;
 editor.PerformWorkspaceHistory(false);if(!doc.nodes[node].mesh.vertices.SequenceEqual(positions))throw new Exception("Undo failed");
 editor.PerformWorkspaceHistory(true);if(!doc.nodes[node].mesh.vertices.SequenceEqual(committed))throw new Exception("Redo failed");
 log.AppendLine("PASS imported-frame live zero/nonzero/negative/restart/cancel/selection and commit/Undo/Redo");
 }
 }
 static void CheckPrimitives(StringBuilder log) {
 int cases=0;foreach(var primitive in new[]{PrimitiveType.Cube,PrimitiveType.Quad})foreach(bool together in new[]{false,true})foreach(float distance in new[]{.01f,.19f,-.19f}) {
 var mesh=StudioModelDocument.BuildPrimitive(primitive,out var polygons);try{var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles};var topo=new StudioMeshCuts(node);var face=topo.Faces[0];
 topo.Extrude(node,primitive==PrimitiveType.Cube?new[]{0}:null,primitive==PrimitiveType.Quad?new[]{(face.Vertices[0],face.Vertices[1])}:null,distance,together,true);cases++;
 }finally{UnityEngine.Object.DestroyImmediate(mesh);}}
 log.AppendLine("PASS cube face/open-quad boundary regressions="+cases);
 } public static void RunRender() {
 int exit=0;var log=new StringBuilder();StudioModelEditor editor=null;Material material=null;RenderTexture rt=null;
 try{
 var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FrameProbe.fbx");var doc=StudioModelDocument.Import(model,"Assets/ExtrudeFrameRenderValidation.asset");int node=doc.nodes.FindIndex(n=>n.name=="Body"&&n.mesh);
 editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(node);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);
 var mesh=doc.nodes[node].mesh;var sourcePositions=mesh.vertices;var cuts=new StudioMeshCuts(doc.nodes[node]);int face=Enumerable.Range(0,cuts.Faces.Count).Where(f=>cuts.Faces[f].Vertices.Count==4).OrderByDescending(f=>(sourcePositions[cuts.Faces[f].Vertices[1]]-sourcePositions[cuts.Faces[f].Vertices[0]]).sqrMagnitude).First();editor.Components.Add(face);
 var filter=editor.Transforms[node].GetComponent<MeshFilter>();var bounds=mesh.bounds;float radius=bounds.extents.magnitude*1.25f;
 material=new Material(Shader.Find("Hidden/StudioValidationSolid"));rt=new RenderTexture(384,384,24);rt.Create();
 Color32[] Render(Mesh value){var old=RenderTexture.active;RenderTexture.active=rt;GL.PushMatrix();try{GL.Clear(true,true,Color.black);GL.LoadProjectionMatrix(Matrix4x4.Ortho(-radius,radius,-radius,radius,.01f,radius*10));GL.modelview=Matrix4x4.Translate(new Vector3(0,0,-radius*3));material.SetPass(0);Graphics.DrawMeshNow(value,Matrix4x4.Rotate(Quaternion.Euler(20,35,0))*Matrix4x4.Translate(-bounds.center));var tex=new Texture2D(384,384,TextureFormat.RGBA32,false);try{tex.ReadPixels(new Rect(0,0,384,384),0,0);tex.Apply();return tex.GetPixels32();}finally{UnityEngine.Object.DestroyImmediate(tex);}}finally{GL.PopMatrix();RenderTexture.active=old;}}
 var source=Render(filter.sharedMesh);if(source.Count(c=>c.r>200)<100)throw new Exception("No frame pixels rendered");
 int changed=0;foreach(float distance in new[]{radius*.1f,radius*.3f,-radius*.2f}){
 Distance(editor,distance);var preview=(StudioMeshToolPreview)typeof(StudioModelEditor).GetField("livePreview",F).GetValue(editor);var displayed=Render(filter.sharedMesh);var expected=Render(preview.Targets[0].Work.mesh);if(!displayed.SequenceEqual(expected))throw new Exception("Stale rendered extrusion");if(!displayed.SequenceEqual(source))changed++;
 }
 if(changed==0)throw new Exception("No extrusion silhouette change");
 Distance(editor,0);if(!Render(filter.sharedMesh).SequenceEqual(source))throw new Exception("Zero distance render differs from source");
 Distance(editor,radius*.3f);typeof(StudioModelEditor).GetMethod("CancelLiveTool",F,null,new[]{typeof(bool)},null).Invoke(editor,new object[]{false});if(!Render(filter.sharedMesh).SequenceEqual(source))throw new Exception("Canceled render differs from source");
 log.AppendLine("PASS imported frame rendered publication at three distances, visible extrusion, zero-distance restoration and cancellation");
 }catch(Exception e){log.AppendLine(e.ToString());exit=1;}finally{editor?.Dispose();if(material)UnityEngine.Object.DestroyImmediate(material);if(rt)UnityEngine.Object.DestroyImmediate(rt);}
 File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../frame-extrude-render-result.txt")),log.ToString());EditorApplication.Exit(exit);
 }}
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Reflection;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;using Object=UnityEngine.Object;
public static class StudioExtrudeInsetValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;static int checks;static StudioModelEditor editor;static StudioModelDocument doc;static ToolChannelPane pane;static double ready;static readonly List<Mesh> meshes=new List<Mesh>();
 static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../..",n));
 static void Check(bool b,string s){if(!b)throw new Exception(s);checks++;}
 static object Call(string n,params object[] a)=>typeof(StudioModelEditor).GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==a.Length&&m.GetParameters().Select((p,i)=>a[i]==null||p.ParameterType.IsInstanceOfType(a[i])).All(v=>v)).Invoke(editor,a);
 static object Get(string n)=>typeof(StudioModelEditor).GetField(n,F).GetValue(editor);
 static void Set(string n,object v)=>typeof(StudioModelEditor).GetField(n,F).SetValue(editor,v);
 static object Kind=>Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",BindingFlags.NonPublic),"Extrude");
 static StudioModelDocument.Node Node(Vector3[] p,int[][] faces)
 {
  var m=new Mesh();meshes.Add(m);m.vertices=p;var triangles=new List<int>();var polygons=new List<int>();
  foreach(var face in faces){polygons.Add(0);polygons.Add(face.Length);polygons.AddRange(face);if(face.Length==6){triangles.AddRange(new[]{0,1,3,1,2,3,0,3,5,3,4,5});}else for(int i=1;i+1<face.Length;i++)triangles.AddRange(new[]{face[0],face[i],face[i+1]});}
  m.triangles=triangles.ToArray();m.uv=p.Select(v=>new Vector2(v.x,v.y)).ToArray();m.colors=Enumerable.Repeat(Color.cyan,p.Length).ToArray();m.RecalculateNormals();m.RecalculateBounds();return new StudioModelDocument.Node{name="Inset",mesh=m,cutPolygons=polygons.ToArray(),cutTriangles=m.triangles};
 }
 static StudioModelDocument.Node Quad()=>Node(new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)},new[]{new[]{0,1,2,3}});
 static StudioMeshCuts.ExtrusionResult Extrude(StudioModelDocument.Node n,int[] faces,float d,float inset,bool together=true,(int,int)[] edges=null)=>new StudioMeshCuts(n).Extrude(n,faces,edges,d,together,true,inset:inset);
 static void Cap(StudioModelDocument.Node n,StudioMeshCuts.ExtrusionResult r,Vector3 source,Vector3 expected,string label){var p=n.mesh.vertices;var ids=r.MovingVertices.Where(i=>(r.SourceVertices[i]-source).sqrMagnitude<1e-8f).ToArray();Check(ids.Length>0,label+" source missing");foreach(int i in ids)Check((p[i]-expected).sqrMagnitude<1e-8f,label+" incorrect offset: "+p[i]+" expected "+expected);}
 public static void Run(){EditorApplication.delayCall+=()=>{try{if(!Application.dataPath.Replace("\\","/").Contains("/obj/StudioControlsValidation/"))throw new Exception("Disposable project only");Geometry();Live();Multi();Capture();}catch(Exception e){Finish(e);}};}
 static void Geometry()
 {
  foreach(float inset in new[]{.1f,-.15f})foreach(float height in new[]{.2f,0f,-.2f}){var n=Quad();var r=Extrude(n,new[]{0},height,inset);Cap(n,r,new Vector3(-.5f,-.5f,0),new Vector3(-.5f-inset,-.5f-inset,height),"signed quad");Check(n.mesh.colors.All(c=>c==Color.cyan),"Colors lost");Check(new StudioMeshCuts(n).Faces.Count==5,"Inset extrusion must retain cap and four quad walls");foreach(int id in r.MovingVertices)Check(!float.IsNaN(n.mesh.vertices[id].sqrMagnitude),"Nonfinite output");}
  var adjacent=Node(new[]{new Vector3(-1,-.5f,0),new Vector3(0,-.5f,0),new Vector3(1,-.5f,0),new Vector3(-1,.5f,0),new Vector3(0,.5f,0),new Vector3(1,.5f,0)},new[]{new[]{0,1,4,3},new[]{1,2,5,4}});
  var ar=Extrude(adjacent,new[]{0,1},.2f,-.1f);Cap(adjacent,ar,new Vector3(0,-.5f,0),new Vector3(0,-.4f,.2f),"Grouped shared boundary");Check(new StudioMeshCuts(adjacent).Faces.Count==8,"Internal edge must not create a wall");
  var separate=Node(new[]{new Vector3(-1,-.5f,0),new Vector3(0,-.5f,0),new Vector3(1,-.5f,0),new Vector3(-1,.5f,0),new Vector3(0,.5f,0),new Vector3(1,.5f,0)},new[]{new[]{0,1,4,3},new[]{1,2,5,4}});var sr=Extrude(separate,new[]{0,1},.2f,-.1f,false);var tips=sr.MovingVertices.Where(i=>sr.SourceVertices[i]==new Vector3(0,-.5f,0)).Select(i=>separate.mesh.vertices[i].x).ToArray();Check(tips.Any(x=>Mathf.Abs(x-.1f)<1e-5f)&&tips.Any(x=>Mathf.Abs(x+.1f)<1e-5f),"Individual caps must inset independently");
  var concave=Node(new[]{Vector3.zero,new Vector3(2,0,0),new Vector3(2,1,0),new Vector3(1,1,0),new Vector3(1,2,0),new Vector3(0,2,0)},new[]{new[]{0,1,2,3,4,5}});var cr=Extrude(concave,new[]{0},.2f,-.1f);Cap(concave,cr,new Vector3(1,1,0),new Vector3(.9f,.9f,.2f),"Concave reflex miter");
  var points=new List<Vector3>();for(int y=0;y<4;y++)for(int x=0;x<4;x++)points.Add(new Vector3(x,y,0));var faces=new List<int[]>();for(int y=0;y<3;y++)for(int x=0;x<3;x++)if(x!=1||y!=1)faces.Add(new[]{y*4+x,y*4+x+1,(y+1)*4+x+1,(y+1)*4+x});var ring=Node(points.ToArray(),faces.ToArray());var rr=Extrude(ring,Enumerable.Range(0,8).ToArray(),.2f,-.1f);Cap(ring,rr,new Vector3(0,0,0),new Vector3(.1f,.1f,.2f),"Ring outer");Cap(ring,rr,new Vector3(1,1,0),new Vector3(.9f,.9f,.2f),"Hole expands during region inset");
  var edge=Quad();var er=Extrude(edge,null,.2f,.1f,edges:new[]{(0,1),(1,2)});Cap(edge,er,new Vector3(.5f,-.5f,0),new Vector3(.6f,-.6f,.2f),"Open edge chain");Check(new StudioMeshCuts(edge).Faces.Count==3,"Open edge chain topology");
  var collapse=Quad();var original=collapse.mesh.vertices;bool rejected=false;try{Extrude(collapse,new[]{0},.2f,-.6f);}catch(InvalidOperationException){rejected=true;}Check(rejected&&collapse.mesh.vertices.SequenceEqual(original),"Invalid inset must fail atomically");
 }
 static void Live()
 {
  doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(Quad());editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);editor.Components.Add(0);Set("extrusionDistance",.2f);Set("extrusionInset",.1f);var source=editor.ActiveMesh.vertices;
  Call("EvaluateLiveTool",Kind);var cached=Get("liveExtrusion");var indices=editor.ActiveMesh.triangles;Set("extrusionInset",.15f);Set("extrusionDistance",.3f);Call("EvaluateLiveTool",Kind);Check(ReferenceEquals(cached,Get("liveExtrusion")),"Scrubbing rebuilt cached extrusion/wavefront");Check(indices.SequenceEqual(editor.ActiveMesh.triangles),"Scrubbing changed index buffer");var r=(StudioMeshCuts.ExtrusionResult)cached;foreach(int id in r.MovingVertices){var expected=r.SourceVertices[id]+r.NormalDirections[id]*.3f+r.InsetDirections[id]*.15f;Check((editor.ActiveMesh.vertices[id]-expected).sqrMagnitude<1e-8f,"Live edits stacked instead of using captured source");}
  Set("extrusionDistance",0f);Call("EvaluateLiveTool",Kind);Check(editor.ActiveMesh.vertices.All(v=>Mathf.Abs(v.z)<1e-6f),"Zero-height inset failed");Call("CancelLiveTool",false);Check(editor.ActiveMesh.vertices.SequenceEqual(source),"Cancel failed to restore source");
  UnityEditor.Undo.ClearAll();StudioUndo.ClearForTests();StudioUndo.IncrementCurrentGroup();Set("extrusionDistance",.2f);Set("extrusionInset",.1f);Call("EvaluateLiveTool",Kind);Call("CommitLiveTool",false);Check(StudioUndo.RetainedActions==1,"Extrude inset must commit one history action");UnityEditor.Undo.FlushUndoRecordObjects();StudioUndo.IncrementCurrentGroup();StudioUndo.PerformUndo();Check(editor.ActiveMesh.vertices.SequenceEqual(source),"Inset Undo geometry");StudioUndo.PerformRedo();Check(editor.ActiveMesh.vertexCount>source.Length,"Inset Redo geometry");
  editor.Dispose();editor=null;Object.DestroyImmediate(doc);doc=null;
 }
 static void Multi()
 {
  doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(Quad());doc.nodes.Add(Quad());editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);editor.SelectObject(1,true);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);
  for(int node=0;node<2;node++){Call("ActivateComponentTarget",node,StudioModelEditor.ComponentMode.Face);editor.Components.Add(0);}
  Set("extrusionDistance",.2f);Set("extrusionInset",-.1f);Call("EvaluateLiveTool",Kind);
  var cohort=(System.Collections.IEnumerable)Get("componentLiveTargets");var cached=new List<object>();
  foreach(var target in cohort){var state=target.GetType().GetField("state",F).GetValue(target);cached.Add(state.GetType().GetField("liveExtrusion",F).GetValue(state));}
  Set("extrusionDistance",.3f);Set("extrusionInset",-.2f);Call("EvaluateLiveTool",Kind);int targetId=0;
  foreach(var target in cohort){var state=target.GetType().GetField("state",F).GetValue(target);Check(ReferenceEquals(cached[targetId++],state.GetType().GetField("liveExtrusion",F).GetValue(state)),"Multi-target cache rebuilt");}
  foreach(var node in doc.nodes){var top=node.mesh.vertices.Where(v=>v.z>.29f).ToArray();Check(top.Length>0&&top.All(v=>Mathf.Abs(v.x)<=.30001f&&Mathf.Abs(v.y)<=.30001f),"Multi-object inset values incorrect");}
  Call("CancelLiveTool",false);foreach(var node in doc.nodes)Check(node.mesh.vertexCount==4,"Multi-target cancel failed");
  editor.Dispose();editor=null;Object.DestroyImmediate(doc);doc=null;
 }
 static void Capture(){doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(Quad());editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);editor.Components.Add(0);Set("extrusionDistance",.2f);Set("extrusionInset",.1f);editor.ExtrudeSelected();pane=(ToolChannelPane)Get("extrudeToolPane");pane.position=new Rect(100,100,330,210);ready=EditorApplication.timeSinceStartup+1;EditorApplication.update+=CaptureTick;}
 static void CaptureTick(){if(EditorApplication.timeSinceStartup<ready)return;EditorApplication.update-=CaptureTick;try{var parent=typeof(EditorWindow).GetField("m_Parent",F).GetValue(pane);var rect=new Rect(Vector2.zero,pane.position.size);var rt=new RenderTexture((int)rect.width,(int)rect.height,0);rt.Create();var old=RenderTexture.active;try{parent.GetType().GetMethod("GrabPixels",F).Invoke(parent,new object[]{rt,rect});RenderTexture.active=rt;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();Check(t.GetPixels32().Select(p=>p.r).Distinct().Count()>20,"Tool pane failed to render");File.WriteAllBytes(Out("extrude-inset-options.png"),t.EncodeToPNG());Object.DestroyImmediate(t);}finally{RenderTexture.active=old;Object.DestroyImmediate(rt);}Finish(null);}catch(Exception e){Finish(e);}}
 static void Finish(Exception e){EditorApplication.update-=CaptureTick;File.WriteAllText(Out("extrude-inset-result.txt"),e==null?"PASS "+checks+" extrusion inset geometry/cache/history/render checks":e.ToString());EditorApplication.delayCall+=()=>{if(pane)pane.Close();editor?.Dispose();if(doc)Object.DestroyImmediate(doc);foreach(var m in meshes)if(m)Object.DestroyImmediate(m);EditorApplication.Exit(e==null?0:1);};}
}

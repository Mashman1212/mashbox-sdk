#if MASHBOX_STUDIO_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
public static class StudioMultiCutValidation
{
 static int checks;static AnimationStudioWindow host;static MashBoxStudioViewport view;static StudioModelDocument document;static StudioModelEditor editor;static double ready;static int warm,phase;static bool probe,capturePreview;static Exception previewFailure;static Vector2 sent,nativeOffset;static Vector2[] screenPoints;
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool v,string text){if(!v)throw new Exception(text);checks++;}
 static StudioModelDocument.Node Quad()
 {
  var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.normals=Enumerable.Repeat(Vector3.forward,4).ToArray();mesh.tangents=Enumerable.Repeat(new Vector4(1,0,0,1),4).ToArray();mesh.colors=mesh.vertices.Select(p=>new Color((p.x+1)*.5f,(p.y+1)*.5f,.7f,1)).ToArray();
  for(int c=0;c<8;c++){var uv=mesh.vertices.Select(p=>new Vector4(p.x+c,p.y-c,c+.5f,1)).ToList();if(c%3==0)mesh.SetUVs(c,uv.Select(v=>new Vector2(v.x,v.y)).ToList());else if(c%3==1)mesh.SetUVs(c,uv.Select(v=>new Vector3(v.x,v.y,v.z)).ToList());else mesh.SetUVs(c,uv);}
  return new StudioModelDocument.Node{mesh=mesh,cutPolygons=new[]{0,4,0,1,2,3},cutTriangles=mesh.triangles,normalGroups=new[]{7,7,7,7}};
 }
 static StudioMeshCuts.FaceStroke Stroke(params Vector3[] p)=>new StudioMeshCuts.FaceStroke(0,p);
 static double Area(StudioModelDocument.Node node)
 {var v=node.mesh.vertices;var t=node.mesh.triangles;double area=0;for(int i=0;i<t.Length;i+=3)area+=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).magnitude*.5;return area;}
 static void Attributes(StudioModelDocument.Node node)
 {
  var mesh=node.mesh;var positions=mesh.vertices;Check(mesh.normals.Length==positions.Length&&mesh.tangents.Length==positions.Length&&mesh.colors.Length==positions.Length,"Attribute lengths");
  for(int c=0;c<8;c++){var uv=new List<Vector4>();mesh.GetUVs(c,uv);Check(uv.Count==positions.Length,"UV count "+c);Check(mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord0+c)==(c%3==0?2:c%3==1?3:4),"UV dimension "+c);for(int i=0;i<positions.Length;i++)Check(Mathf.Abs(uv[i].x-positions[i].x-c)<1e-5f&&Mathf.Abs(uv[i].y-positions[i].y+c)<1e-5f,"UV interpolation "+c+":"+i);}
  Check(node.normalGroups.Length==positions.Length&&node.normalGroups.All(g=>g==7),"Normal groups");
 }
 public static void Run(){EditorApplication.delayCall+=Start;}
 static void Start()
 {
  try{
   ShaderUtil.allowAsyncCompilation=false;
   var a=Quad();var kernel=new StudioMeshCuts(a);kernel.ApplyMultiCut(a,new[]{Stroke(new Vector3(-.5f,-1,0),new Vector3(-.5f,0,0),new Vector3(.5f,0,0),new Vector3(.5f,-1,0))});
   var topology=new StudioMeshCuts(a);Check(topology.Faces.Count==2,"Same-edge interior path must make two faces");Check(topology.Faces.Any(f=>f.Vertices.Count>4),"N-gon boundary must survive");Check(Math.Abs(Area(a)-4)<1e-5,"Same-edge path changed surface area");Attributes(a);
   var anchor=Quad();new StudioMeshCuts(anchor).ApplyMultiCut(anchor,new[]{Stroke(new Vector3(-.5f,-1,0),new Vector3(-.5f,0,0),new Vector3(.5f,0,0),new Vector3(-.5f,-1,0))});Check(new StudioMeshCuts(anchor).Faces.Count==2&&Math.Abs(Area(anchor)-4)<1e-5,"Exact boundary anchor return");UnityEngine.Object.DestroyImmediate(anchor.mesh);
   var b=Quad();new StudioMeshCuts(b).ApplyMultiCut(b,new[]{Stroke(new Vector3(-1,0,0),new Vector3(1,0,0)),Stroke(new Vector3(0,-1,0),new Vector3(0,1,0))});Check(new StudioMeshCuts(b).Faces.Count==4,"Crossing paths must create four cells");Check(b.mesh.vertices.Count(p=>p.sqrMagnitude<1e-10f)==1,"Crossing vertex must be shared");Attributes(b);
   var c=Quad();var original=c.mesh.vertices;bool rejected=false;try{new StudioMeshCuts(c).ApplyMultiCut(c,new[]{Stroke(new Vector3(-1,0,0),Vector3.zero)});}catch(InvalidOperationException){rejected=true;}Check(rejected&&c.mesh.vertices.SequenceEqual(original)&&c.cutPolygons.SequenceEqual(new[]{0,4,0,1,2,3}),"Incomplete path must reject atomically");
   var d=Quad();new StudioMeshCuts(d).ApplyMultiCut(d,new[]{Stroke(new Vector3(-1,0,0),new Vector3(0,.5f,0),new Vector3(1,0,0))});var dk=new StudioMeshCuts(d);int shared=Enumerable.Range(0,d.mesh.vertexCount).First(i=>(d.mesh.vertices[i]-new Vector3(0,.5f,0)).sqrMagnitude<1e-10f);int face=dk.Faces.FindIndex(f=>f.Vertices.Contains(shared)&&f.Vertices.Contains(0));dk.ApplyMultiCut(d,new[]{new StudioMeshCuts.FaceStroke(face,d.mesh.vertices[shared],new Vector3(0,-1,0))});Check(new StudioMeshCuts(d).Faces.Count==3,"A placed vertex must support a subsequent perimeter cut");Check(Math.Abs(Area(d)-4)<1e-5,"Follow-up cut changed area");
   var cube=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);var node=new StudioModelDocument.Node{mesh=cube,cutPolygons=polygons,cutTriangles=cube.triangles};var ck=new StudioMeshCuts(node);var slice=ck.SliceCut(new Plane(new Vector3(1,.23f,.17f).normalized,Vector3.zero));Check(slice.Count>=4,"Plane must intersect front and hidden cube faces");ck.ApplyMultiCut(node,slice);Check(new StudioMeshCuts(node).Faces.Count>=10,"Through slice missing faces");Check(Math.Abs(Area(node)-6)<1e-4,"Slice changed cube surface area");
   // Every geometrical cube boundary remains shared by exactly two authored faces.
   var uses=new Dictionary<(Vector3,Vector3),int>();foreach(var f in new StudioMeshCuts(node).Faces)for(int i=0;i<f.Vertices.Count;i++){var p=cube.vertices[f.Vertices[i]];var q=cube.vertices[f.Vertices[(i+1)%f.Vertices.Count]];if(Compare(p,q)>0){var tmp=p;p=q;q=tmp;}var key=(p,q);uses.TryGetValue(key,out int count);uses[key]=count+1;}Check(uses.Values.All(count=>count==2),"Through slice left T junctions or cracks");
   foreach(var n in new[]{b,c,d})UnityEngine.Object.DestroyImmediate(n.mesh);
   document=ScriptableObject.CreateInstance<StudioModelDocument>();a.materials=new[]{StudioMeshMaterialPreview.DefaultMaterial};document.nodes.Add(a);document.lighting.studioLights=false;document.lighting.ground=false;
   AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(host,new object[]{true});typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{document});editor=host.ToolbarModelEditor;editor.SelectObject(0);
   view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);view.position=new Rect(40,40,1000,720);view.modelGridVisible=false;view.LookAtDirect(Vector3.zero,Quaternion.Euler(0,180,0),2);editor.SetModelDrawMode(view,DrawCameraMode.TexturedWire);editor.SetDisplayMode(false,false);
   // Exercise the real grouped editor publication and native history on a fresh cut.
   editor.ActivateCutTool(StudioModelEditor.CutTool.MultiCut);var current=new StudioMeshCuts(a);int bottomFace=current.Faces.FindIndex(f=>f.Vertices.Any(i=>(a.mesh.vertices[i]-new Vector3(-.5f,0,0)).sqrMagnitude<1e-10f)&&f.Vertices.Any(i=>a.mesh.vertices[i].y==1));
   typeof(StudioModelEditor).GetMethod("CommitMultiCut",F).Invoke(editor,new object[]{new[]{new StudioMeshCuts.FaceStroke(bottomFace,new Vector3(-.5f,0,0),new Vector3(-1,0,0))}});
   Check(new StudioMeshCuts(document.nodes[0]).Faces.Count==3,"Editor publication failed");UnityEditor.Undo.PerformUndo();Check(new StudioMeshCuts(document.nodes[0]).Faces.Count==2,"Native Undo did not restore placed path");UnityEditor.Undo.PerformRedo();Check(new StudioMeshCuts(document.nodes[0]).Faces.Count==3,"Native Redo did not restore cut");
   document.nodes[0]=Quad();document.nodes[0].materials=new[]{StudioMeshMaterialPreview.DefaultMaterial};typeof(StudioModelEditor).GetMethod("Rebuild",F).Invoke(editor,null);editor.SelectObject(0);editor.ActivateCutTool(StudioModelEditor.CutTool.MultiCut);
   SceneView.beforeSceneGui+=Before;SceneView.duringSceneGui+=After;ready=EditorApplication.timeSinceStartup+1;EditorApplication.update+=Tick;
  }catch(Exception e){Finish(e.ToString());}
 }
 static int Compare(Vector3 a,Vector3 b){int c=a.x.CompareTo(b.x);if(c==0)c=a.y.CompareTo(b.y);if(c==0)c=a.z.CompareTo(b.z);return c;}
 static Vector2 Project(Vector3 world){var p=view.camera.WorldToScreenPoint(world);return new Vector2(p.x,view.camera.pixelHeight-p.y)/EditorGUIUtility.pixelsPerPoint;}
 static void Before(SceneView scene){if(scene==view&&probe)Event.current.mousePosition=sent;}
 static void After(SceneView scene)
 {
  if(scene!=view||Event.current.type!=EventType.Repaint)return;
  if(capturePreview){capturePreview=false;try{RenderPreview();}catch(Exception e){previewFailure=e;}}
  if(probe)return;
  screenPoints=new[]{new Vector3(-.5f,-1,0),new Vector3(-.5f,0,0),new Vector3(.5f,0,0),new Vector3(.5f,-1,0)}.Select(p=>Project(editor.Transforms[0].TransformPoint(p))+view.CanvasInputOrigin).ToArray();
 }
 static void Send(EventType type,Vector2 pointer,KeyCode key=KeyCode.None)
 {sent=pointer;probe=true;try{view.SendEvent(new Event{type=type,button=0,mousePosition=pointer+nativeOffset,keyCode=key});}finally{probe=false;}}
 static void Click(Vector2 p){Send(EventType.MouseMove,p);Send(EventType.MouseDown,p);Send(EventType.MouseUp,p);}
 static void RenderPreview()
 {
  var camera=view.camera;var oldTarget=camera.targetTexture;var oldRect=camera.pixelRect;var oldActive=RenderTexture.active;var rt=new RenderTexture(camera.pixelWidth,camera.pixelHeight,24);rt.Create();var oldXray=typeof(StudioModelEditor).GetField("displayXray",F).GetValue(editor);
  GL.PushMatrix();try{camera.targetTexture=rt;RenderTexture.active=rt;GL.Clear(true,true,Color.black);Handles.SetCamera(new Rect(0,0,rt.width,rt.height),camera);GL.LoadProjectionMatrix(camera.projectionMatrix);GL.modelview=camera.worldToCameraMatrix;typeof(StudioModelEditor).GetField("displayXray",F).SetValue(editor,true);typeof(StudioModelEditor).GetMethod("DrawMultiCutPreview",F).Invoke(editor,new object[]{view});var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);try{image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../multi-cut-preview.png")),image.EncodeToPNG());Check(image.GetPixels32().Count(p=>p.b>140&&p.b>p.r*1.3f)>30,"Blue placed-path/cursor preview did not render");}finally{UnityEngine.Object.DestroyImmediate(image);}}
  finally{typeof(StudioModelEditor).GetField("displayXray",F).SetValue(editor,oldXray);GL.PopMatrix();camera.targetTexture=oldTarget;camera.pixelRect=oldRect;RenderTexture.active=oldActive;UnityEngine.Object.DestroyImmediate(rt);}
 }
 static void Tick()
 {
  if(EditorApplication.timeSinceStartup<ready)return;
  try{if(warm++<2){view.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);view.Repaint();ready=EditorApplication.timeSinceStartup+.7;return;}
   if(phase==0)
   {
    Check(screenPoints!=null,"Missing GUI projection");view.Focus();EditorGUIUtility.editingTextField=false;Send(EventType.MouseMove,screenPoints[0]);nativeOffset=screenPoints[0]-view.CanvasInputOrigin-(view.CanvasMousePosition??Vector2.zero);Send(EventType.MouseMove,screenPoints[0]);Check(Vector2.Distance(view.CanvasMousePosition??Vector2.zero,screenPoints[0]-view.CanvasInputOrigin)<.1f,"Native pointer calibration");Click(screenPoints[0]);Check(((System.Collections.IList)typeof(StudioModelEditor).GetField("multiPoints",F).GetValue(editor)).Count==1,"First edge click failed at "+screenPoints[0]+" canvas "+view.ModelCanvasRect+" error "+editor.StatusText);Click(screenPoints[1]);Click(screenPoints[2]);Check(((System.Collections.IList)typeof(StudioModelEditor).GetField("multiPoints",F).GetValue(editor)).Count==3,"Interior clicks failed: "+editor.StatusText);
    Check(new StudioMeshCuts(document.nodes[0]).Faces.Count==1,"Preview mutated live topology");Send(EventType.MouseMove,screenPoints[3]);capturePreview=true;Send(EventType.Repaint,screenPoints[3]);Check(!capturePreview&&previewFailure==null,"Preview render failed: "+previewFailure);
    Send(EventType.KeyDown,screenPoints[2],KeyCode.W);Check(new StudioMeshCuts(document.nodes[0]).Faces.Count==1,"Unfinished W modified mesh");
    var points=(System.Collections.IList)typeof(StudioModelEditor).GetField("multiPoints",F).GetValue(editor);Check(points.Count==3,"Unfinished W lost pending points");
    Click(screenPoints[3]);Send(EventType.KeyDown,screenPoints[3],KeyCode.W);Check(new StudioMeshCuts(document.nodes[0]).Faces.Count==2,"Native clicks/W did not make same-edge cut");
    Check((StudioModelEditor.CutTool)typeof(StudioModelEditor).GetField("cutTool",F).GetValue(editor)==StudioModelEditor.CutTool.None,"W did not return to Move");
    phase++;warm=0;ready=EditorApplication.timeSinceStartup+.5;return;
   }
   if(phase==1){view.LookAtDirect(Vector3.zero,Quaternion.Euler(0,180,0),3);editor.ActivateCutTool(StudioModelEditor.CutTool.MultiCut);phase++;warm=0;ready=EditorApplication.timeSinceStartup+.5;return;}
   if(phase==2)
   {
    var bounds=(Rect)typeof(StudioModelEditor).GetMethod("MultiProjectedBounds",F).Invoke(editor,new object[]{view});var start=new Vector2(bounds.xMin-25,bounds.center.y-20);var end=new Vector2(bounds.xMax+25,start.y);var origin=view.CanvasInputOrigin;
    var miss=(List<StudioMeshCuts.FaceStroke>)typeof(StudioModelEditor).GetMethod("MultiSlice",F).Invoke(editor,new object[]{view,new Vector2(bounds.xMin-40,start.y),new Vector2(bounds.xMin-20,start.y)});Check(miss.Count==0,"Short off-mesh segment cut beyond its endpoint");
    var previous=(int[])document.nodes[0].cutPolygons.Clone();Send(EventType.MouseDown,start+origin);Check((bool)typeof(StudioModelEditor).GetField("multiSlicing",F).GetValue(editor),"Off-mesh drag did not start slice");Send(EventType.MouseDrag,end+origin);Check(previous.SequenceEqual(document.nodes[0].cutPolygons),"Slice preview changed topology");Send(EventType.MouseUp,end+origin);Check(new StudioMeshCuts(document.nodes[0]).Faces.Count>2,"Off-mesh drag did not apply slice: "+editor.StatusText);
    phase++;warm=0;ready=EditorApplication.timeSinceStartup+.5;return;
   }
   if(phase==3)
   {
    var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygon);document.nodes[0]=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygon,cutTriangles=mesh.triangles,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial}};typeof(StudioModelEditor).GetMethod("Rebuild",F).Invoke(editor,null);editor.SelectObject(0);editor.ActivateCutTool(StudioModelEditor.CutTool.MultiCut);view.LookAtDirect(Vector3.zero,Quaternion.Euler(20,35,0),2);phase++;warm=0;ready=EditorApplication.timeSinceStartup+.5;return;
   }
   if(phase==4)
   {
    var node=document.nodes[0];var topology=new StudioMeshCuts(node);var vertices=node.mesh.vertices;var visible=Enumerable.Range(0,topology.Faces.Count).Where(f=>Vector3.Dot(topology.CutFaceNormal(f),view.camera.transform.position-topology.Faces[f].Vertices.Aggregate(Vector3.zero,(sum,v)=>sum+vertices[v])/topology.Faces[f].Vertices.Count)>0).ToArray();int fa=visible[0],fb=visible[1];var common=topology.Faces[fa].Vertices.Select(v=>vertices[v]).Intersect(topology.Faces[fb].Vertices.Select(v=>vertices[v])).ToArray();Check(common.Length==2,"Visible faces not adjacent");var center=(common[0]+common[1])*.5f;
    object Point(int f){var t=typeof(StudioModelEditor).GetNestedType("MultiPoint",BindingFlags.NonPublic);var value=Activator.CreateInstance(t);t.GetField("Face",F).SetValue(value,f);t.GetField("Local",F).SetValue(value,(topology.Faces[f].Vertices.Aggregate(Vector3.zero,(sum,v)=>sum+vertices[v])/topology.Faces[f].Vertices.Count+center)*.5f);return value;}
    var traced=(List<StudioMeshCuts.FaceStroke>)typeof(StudioModelEditor).GetMethod("TraceMultiSegment",F).Invoke(editor,new[]{(object)view,Point(fa),Point(fb)});Check(traced.Count==2&&traced[0].Face==fa&&traced[1].Face==fb,"Adjacent visible face continuation failed");Check((traced[0].Points.Last()-traced[1].Points[0]).sqrMagnitude<1e-10f,"Adjacent face crossing has a gap");phase++;
   }
   var rt=typeof(MashBoxStudioViewport).GetField("cachedScene",F).GetValue(view) as RenderTexture;Check(rt&&rt.IsCreated(),"No native render target");var old=RenderTexture.active;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);try{RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../multi-cut-native.png")),image.EncodeToPNG());Check(image.GetPixels32().Count(p=>p.r>70&&Math.Abs(p.r-p.g)<5)>1000,"Cut surface missing in native render");}finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}Finish("PASS "+checks+" multi-cut topology, all UV channels, intersections, through slice, grouped publication, Undo/Redo, native clicks/W/finite-slice drag, adjacent face continuation and rendered preview checks");
  }catch(Exception e){Finish(e.ToString());}
 }
 static void Finish(string text)
 {EditorApplication.update-=Tick;SceneView.beforeSceneGui-=Before;SceneView.duringSceneGui-=After;File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../multi-cut-result.txt")),text);EditorApplication.delayCall+=()=>{if(view)view.Close();if(host)UnityEngine.Object.DestroyImmediate(host);EditorApplication.delayCall+=()=>EditorApplication.Exit(text.StartsWith("PASS")?0:1);};}
}
#endif

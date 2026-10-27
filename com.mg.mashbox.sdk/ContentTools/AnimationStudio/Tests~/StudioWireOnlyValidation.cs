#if MASHBOX_STUDIO_TESTS
using System;using System.IO;using System.Linq;using System.Reflection;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;
public static class StudioWireOnlyValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;static AnimationStudioWindow host;static MashBoxStudioViewport view;static StudioModelEditor editor;static StudioModelDocument doc;static int checks;static bool pending;static double stop;
 static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
 static bool PickVisible(int i)=>(bool)typeof(StudioModelEditor).GetMethod("IsolationNodeVisible",F).Invoke(editor,new object[]{i});
 static void Mode(DrawCameraMode mode)=>editor.SetModelDrawMode(view,mode);
 public static void Run(){EditorApplication.delayCall+=Start;}
 static void Start()
 {
  try{
   doc=ScriptableObject.CreateInstance<StudioModelDocument>();
   for(int i=0;i<3;i++){var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polys);doc.nodes.Add(new StudioModelDocument.Node{name="Wire cube "+i,parent=i==1?0:-1,position=Vector3.right*i*2,mesh=mesh,cutPolygons=polys,cutTriangles=mesh.triangles,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial}});}
   AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(host,new object[]{true});typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});editor=host.ToolbarModelEditor;
   view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);view.position=new Rect(70,70,900,650);view.LookAtDirect(Vector3.zero,Quaternion.Euler(25,35,0),2.2f);view.modelGridVisible=false;
   var hidden=editor.Transforms[2].GetComponent<Renderer>();hidden.forceRenderingOff=true;
   for(int pass=0;pass<3;pass++)foreach(var mode in new[]{DrawCameraMode.Wireframe,DrawCameraMode.TexturedWire,DrawCameraMode.Textured})
   {
    Mode(mode);Check(editor.VisibleWireframeNodes().SequenceEqual(new[]{0,1}),"Mode switch dropped visible wires or revealed hidden mesh");
    Check(PickVisible(0)&&PickVisible(1)&&!PickVisible(2),"Wire-only surface suppression disabled picking visibility");
    Check(hidden.forceRenderingOff,"Mode switch reset an unrelated hidden renderer");
    Check(editor.Transforms[0].GetComponent<Renderer>().forceRenderingOff==(mode==DrawCameraMode.Wireframe),"Surface suppression not restored on mode switch");
   }
   Mode(DrawCameraMode.Wireframe);editor.SelectObject(1);editor.IsolateViewportSelection();
   Check(editor.VisibleWireframeNodes().SequenceEqual(new[]{1}),"Wire-only isolation leaked ancestor surface wires");
   Check(!PickVisible(0)&&PickVisible(1),"Isolation picking visibility did not distinguish surface suppression");
   Mode(DrawCameraMode.TexturedWire);Check(editor.VisibleWireframeNodes().SequenceEqual(new[]{1}),"Shaded switch lost isolation");
   Mode(DrawCameraMode.Wireframe);Check(editor.VisibleWireframeNodes().SequenceEqual(new[]{1}),"Wire switch lost isolation");
   editor.RunViewportIsolation(true);Check(editor.VisibleWireframeNodes().SequenceEqual(new[]{0,1}),"Show All did not restore logical visibility");
   Check(editor.Transforms[0].GetComponent<Renderer>().forceRenderingOff,"Show All incorrectly enabled wire-only surfaces");
   ((System.Collections.Generic.HashSet<int>)typeof(StudioModelEditor).GetField("objects",F).GetValue(editor)).Clear();Mode(DrawCameraMode.Wireframe);SceneView.duringSceneGui+=Draw;pending=true;view.Repaint();stop=EditorApplication.timeSinceStartup+20;EditorApplication.update+=Tick;
  }catch(Exception e){Finish(e.ToString());}
 }
 static void Tick(){if(EditorApplication.timeSinceStartup>stop)Finish("FAIL native wire callback timed out");}
 static void Draw(SceneView scene)
 {
  if(scene!=view||!pending||Event.current.type!=EventType.Repaint)return;pending=false;
  var rt=new RenderTexture(900,650,24);rt.Create();var previous=RenderTexture.active;var target=view.camera.targetTexture;var rect=view.camera.pixelRect;Texture2D texture=null;GL.PushMatrix();string result;
  try{
   view.camera.targetTexture=rt;view.camera.pixelRect=new Rect(0,0,rt.width,rt.height);Handles.SetCamera(new Rect(0,0,rt.width,rt.height),view.camera);RenderTexture.active=rt;
   GL.Clear(true,true,Color.black);GL.LoadProjectionMatrix(view.camera.projectionMatrix);GL.modelview=view.camera.worldToCameraMatrix;
   // No surface/depth prepass: render precisely the pure-wire overlay path.
   typeof(StudioModelEditor).GetMethod("DrawVisibleObjectWireframes",F).Invoke(editor,new object[]{view.camera});
   texture=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);texture.Apply();var pixels=texture.GetPixels32();
   Check(pixels.Count(p=>p.r>8||p.g>8||p.b>8)>300,"Pure wireframe emitted no visible native pixels");Check(editor.ObjectWireDrawCalls==2,"Expected two visible GPU wire batches");
   File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../wire-only-native.png")),texture.EncodeToPNG());
   Mode(DrawCameraMode.TexturedWire);Check(!editor.Transforms[0].GetComponent<Renderer>().forceRenderingOff&&editor.Transforms[2].GetComponent<Renderer>().forceRenderingOff,"Surface restoration lost original hidden state");
   result="PASS "+checks+" pure-wire visibility/picking, hidden states, isolation, toggling and native GPU pixel checks";
  }catch(Exception e){result=e.ToString();}
  finally{GL.PopMatrix();view.camera.targetTexture=target;view.camera.pixelRect=rect;RenderTexture.active=previous;if(texture)UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(rt);}
  Finish(result);
 }
 static void Finish(string result)
 {
  SceneView.duringSceneGui-=Draw;EditorApplication.update-=Tick;File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../wire-only-result.txt")),result);
  EditorApplication.delayCall+=()=>{if(view)view.Close();if(host)UnityEngine.Object.DestroyImmediate(host);EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};
 }
}
#endif

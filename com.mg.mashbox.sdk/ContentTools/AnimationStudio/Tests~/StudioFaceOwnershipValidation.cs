#if MASHBOX_STUDIO_TESTS
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Reflection;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;
public static class StudioFaceOwnershipValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;static int checks,phase;static AnimationStudioWindow host;static MashBoxStudioViewport view;static StudioModelEditor editor;static StudioModelDocument doc;static double next,deadline;static bool capture,awaiting;static Exception failure;
 static void Check(bool v,string message){if(!v)throw new Exception(message);checks++;}
 static StudioModelDocument.Node Shape(bool reverse=false,bool trianglesReverse=false)
 {
  var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.normals=Enumerable.Repeat(Vector3.forward,4).ToArray();var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=new[]{0,4,0,1,2,3},cutTriangles=mesh.triangles};
  new StudioMeshCuts(node).ApplyMultiCut(node,new[]{new StudioMeshCuts.FaceStroke(0,new Vector3(-1,-1,0),new Vector3(-.5f,.15f,0),new Vector3(.25f,.35f,0),new Vector3(.65f,-.1f,0),new Vector3(1,-1,0))});
  if(reverse){var faces=new StudioMeshCuts(node).Faces;faces.Reverse();node.cutPolygons=faces.SelectMany(f=>new[]{f.Submesh,f.Vertices.Count}.Concat(f.Vertices)).ToArray();}
  if(trianglesReverse){var t=mesh.triangles;mesh.triangles=Enumerable.Range(0,t.Length/3).Reverse().SelectMany(i=>t.Skip(i*3).Take(3)).ToArray();node.cutTriangles=mesh.triangles;}
  node.materials=new[]{StudioMeshMaterialPreview.DefaultMaterial};return node;
 }
 static object Call(string method,params object[] args)=>typeof(StudioModelEditor).GetMethods(F).Single(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(editor,args);
 public static void Run(){EditorApplication.delayCall+=Start;}
 static void Start()
 {
  try{
   ShaderUtil.allowAsyncCompilation=false;doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(Shape(true));AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(host,new object[]{true});typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});editor=host.ToolbarModelEditor;editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);
   view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);view.position=new Rect(60,60,1200,800);view.LookAtDirect(Vector3.zero,Quaternion.Euler(0,180,0),2);view.modelGridVisible=false;
   foreach(bool reverse in new[]{false,true})foreach(bool tr in new[]{false,true})
   {
    doc.nodes[0]=Shape(reverse,tr);Call("Rebuild");editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);var faces=(List<StudioMeshCuts.Face>)Call("Faces");var mapped=(List<int>[])typeof(StudioModelEditor).GetField("cachedFaceTriangles",F).GetValue(editor);Check(faces.Count==2,"Expected two authored n-gons");
    for(int f=0;f<faces.Count;f++)Check(mapped[f].Count==(faces[f].Vertices.Count-2)*3,"Triangle ownership crossed cut: face="+f+" reverse="+reverse+" triangleReverse="+tr+" expected="+(faces[f].Vertices.Count-2)*3+" actual="+mapped[f].Count);
    Check(mapped.Sum(t=>t.Count)==doc.nodes[0].mesh.triangles.Length,"Lost/duplicated triangles");
    var kernel=new StudioMeshCuts(doc.nodes[0]);var source=kernel.BevelSourceTriangles(doc.nodes[0].mesh);for(int f=0;f<faces.Count;f++)Check(source[f].Count==mapped[f].Count,"Modeling and selection disagree on triangle ownership");
   }
   // Native pixel picking and highlighting use one immutable authored face each.
   next=EditorApplication.timeSinceStartup+1;SceneView.duringSceneGui+=Draw;EditorApplication.update+=Tick;
  }catch(Exception e){Finish(e.ToString());}
 }
 static Vector2 Project(Vector3 point){var p=view.camera.WorldToScreenPoint(editor.Transforms[0].TransformPoint(point));return new Vector2(p.x,view.camera.pixelHeight-p.y)/EditorGUIUtility.pixelsPerPoint;}
 static void Tick()
 {
  if(EditorApplication.timeSinceStartup<next)return;
  try{
   if(!awaiting)
   {
    var node=doc.nodes[0];var faces=(List<StudioMeshCuts.Face>)Call("Faces");int inner=editor.ReferencePaintHit(view,Project(new Vector3(0,-.6f,0))),outer=editor.ReferencePaintHit(view,Project(new Vector3(0,.8f,0)));Check(inner>=0&&outer>=0&&inner!=outer,"Both sides picked the same authored face");
    editor.Components.Clear();editor.Components.Add(phase==0?inner:outer);capture=true;awaiting=true;deadline=EditorApplication.timeSinceStartup+10;view.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);view.Focus();view.Repaint();next=EditorApplication.timeSinceStartup+.2;return;
   }
   if(failure!=null)throw failure;
   if(capture){if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Highlight capture did not execute");view.Repaint();return;}
   Check(!capture,"Highlight capture did not execute");awaiting=false;if(phase++==0){next=EditorApplication.timeSinceStartup+.3;return;}Finish("PASS "+checks+" shared-boundary n-gon ownership, reordered metadata/triangles, independent picking and native fill checks");
  }catch(Exception e){Finish(e.ToString());}
 }
 static void Draw(SceneView scene)
 {
  if(scene!=view||Event.current.type!=EventType.Repaint||!capture)return;capture=false;
  var camera=view.camera;var oldTarget=camera.targetTexture;var oldRect=camera.pixelRect;var oldActive=RenderTexture.active;var rt=new RenderTexture(camera.pixelWidth,camera.pixelHeight,24);rt.Create();GL.PushMatrix();
  try{
   camera.targetTexture=rt;RenderTexture.active=rt;GL.Clear(true,true,Color.black);Handles.SetCamera(new Rect(0,0,rt.width,rt.height),camera);GL.LoadProjectionMatrix(camera.projectionMatrix);GL.modelview=camera.worldToCameraMatrix;
   Call("EnsureComponentDrawing");var highlight=(Mesh)Call("ComponentHighlight",editor.Components);var material=new Material(Shader.Find("Hidden/MashBox/ModelerComponents"));try{material.SetColor("_Color",Color.yellow);material.SetFloat("_Mode",0);material.SetFloat("_ZTest",(float)UnityEngine.Rendering.CompareFunction.Always);material.SetVector("_Viewport",new Vector4(rt.width,rt.height,0,0));material.SetPass(0);Graphics.DrawMeshNow(highlight,editor.Transforms[0].localToWorldMatrix);}finally{UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(highlight);}
   var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);try{image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../face-ownership-"+phase+".png")),image.EncodeToPNG());bool Hit(Vector3 local){var p=camera.WorldToScreenPoint(editor.Transforms[0].TransformPoint(local));var color=image.GetPixel((int)p.x,(int)p.y);return color.r>.5f&&color.g>.5f;}Check(Hit(phase==0?new Vector3(0,-.6f,0):new Vector3(0,.8f,0)),"Selected n-gon fill is missing");Check(!Hit(phase==0?new Vector3(0,.8f,0):new Vector3(0,-.6f,0)),"Selected fill crossed onto the other n-gon");}finally{UnityEngine.Object.DestroyImmediate(image);}
  }catch(Exception e){failure=e;}finally{GL.PopMatrix();camera.targetTexture=oldTarget;camera.pixelRect=oldRect;RenderTexture.active=oldActive;UnityEngine.Object.DestroyImmediate(rt);}
 }
 static void Finish(string result){EditorApplication.update-=Tick;SceneView.duringSceneGui-=Draw;File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../face-ownership-result.txt")),result);EditorApplication.delayCall+=()=>{if(view)view.Close();if(host)UnityEngine.Object.DestroyImmediate(host);EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};}
}
#endif

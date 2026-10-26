#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
internal static class StudioSaveCameraValidation {
 static int checks;
 static BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
 static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,flags).SetValue(obj,value);
 static void Call(object obj,string name)=>obj.GetType().GetMethod(name,flags).Invoke(obj,null);
 static void CameraUnchanged(MashBoxStudioViewport view,Action action,string name){
  Vector3 pivot=view.pivot;Quaternion rotation=view.rotation;float size=view.size;bool ortho=view.orthographic;
  var sceneProperty=typeof(SceneView).GetProperty("customScene",flags|BindingFlags.Public);var scene=sceneProperty.GetValue(view);
  action();
  Check(Vector3.Distance(view.pivot,pivot)<.00001f,name+" pivot");
  Check(Quaternion.Angle(view.rotation,rotation)<.0001f,name+" rotation");
  Check(Mathf.Abs(view.size-size)<.00001f,name+" zoom");
  Check(view.orthographic==ortho,name+" projection");
  Check(Equals(sceneProperty.GetValue(view),scene),name+" bound preview scene");
 }
 public static void Run(){
  string result=Path.Combine(Application.dataPath,"../save-camera-result.txt");
  try {
   var controller=ScriptableObject.CreateInstance<AnimationStudioWindow>();
   var view=ScriptableObject.CreateInstance<MashBoxStudioViewport>();view.Show();
   var doc=ScriptableObject.CreateInstance<StudioModelDocument>();
   doc.SetWorkspacePath(Path.Combine(StudioModelDocument.WorkspaceFolder,".Temporary","Untitled Workspace.workspace"));doc.Save();doc.AddPrimitive(PrimitiveType.Cube);
   Set(controller,"modelDocument",doc);Set(controller,"viewport",view);Call(controller,"EnsureModelEditor");
   var editor=(StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor",flags).GetValue(controller);
   view.Bind(editor.Scene);view.orthographic=false;view.LookAtDirect(new Vector3(7.3f,-2.1f,4.8f),Quaternion.Euler(23,64,9),8.3f);
   string first=StudioModelDocument.NewWorkspacePath("Camera preservation first");editor.WorkspaceSavePathChoice=()=>first;
   CameraUnchanged(view,editor.Save,"First Save");
   Check(File.Exists(first)&&string.Equals(Path.GetFullPath(editor.Document.WorkspacePath),Path.GetFullPath(first),StringComparison.OrdinalIgnoreCase),"First Save adopted file");
   Check(ReferenceEquals(typeof(AnimationStudioWindow).GetField("modelDocument",flags).GetValue(controller),editor.Document),"Controller follows saved document");
   editor.Document.nodes[0].name="Saved mesh";editor.Changed();
   CameraUnchanged(view,editor.Save,"Save named file");
   string second=StudioModelDocument.NewWorkspacePath("Camera preservation copy");editor.WorkspaceSavePathChoice=()=>second;
   CameraUnchanged(view,()=>Call(editor,"SaveAs"),"Save As perspective");Check(File.Exists(second),"Save As writes copy");
   view.orthographic=true;view.LookAtDirect(new Vector3(-6,5,2),Quaternion.Euler(80,12,1),3.7f);
   string third=StudioModelDocument.NewWorkspacePath("Camera preservation ortho");editor.WorkspaceSavePathChoice=()=>third;
   CameraUnchanged(view,()=>Call(editor,"SaveAs"),"Save As orthographic");
   CameraUnchanged(view,()=>Call(editor,"SaveAs"),"Save As current path");
   editor.WorkspaceSavePathChoice=()=>"";var prior=editor.Document;
   CameraUnchanged(view,()=>Call(editor,"SaveAs"),"Save As cancelled");Check(editor.Document==prior,"Cancel keeps document");
   editor.WorkspaceReplacementChoice=()=>2;
   view.in2DMode=true;view.orthographic=true;view.pivot=new Vector3(50,20,-30);view.size=100;
   var load=typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",flags);
   typeof(StudioModelEditor).GetMethod("NewWorkspace",flags).Invoke(editor,new object[]{(Action<StudioModelDocument>)(d=>load.Invoke(controller,new object[]{d}))});
   Check(editor.Document.nodes.Count==0,"New Workspace is empty");
   Check(view.pivot==Vector3.zero,"New Workspace targets grid center");
   Check(!view.in2DMode&&!view.orthographic,"New Workspace uses 3D perspective");
   Check(Quaternion.Angle(view.rotation,Quaternion.Euler(37,38,0))<.001f,"New Workspace has 37-degree pitch and 38-degree yaw");
   float distance=view.size/Mathf.Sin(view.cameraSettings.fieldOfView*.5f*Mathf.Deg2Rad);
   Vector3 eye=view.pivot-view.rotation*Vector3.forward*distance;
   Check(Mathf.Abs(distance-10f)<.001f,"New Workspace eye is ten meters from grid center");
   Check(Vector3.Dot((view.pivot-eye).normalized,view.rotation*Vector3.forward)>.999f,"New Workspace looks at grid origin");
   editor.Dispose();Set(controller,"modelEditor",null);Set(controller,"modelDocument",null);Set(controller,"viewport",null);
   view.Close();UnityEngine.Object.DestroyImmediate(controller);
   controller=ScriptableObject.CreateInstance<AnimationStudioWindow>();Set(controller,"modelingMode",true);
   Call(controller,"OpenViewport");view=(MashBoxStudioViewport)typeof(AnimationStudioWindow).GetField("viewport",flags).GetValue(controller);
   Check(view.pivot==Vector3.zero,"Fresh Studio targets grid origin");
   Check(Quaternion.Angle(view.rotation,Quaternion.Euler(37,38,0))<.001f,"Fresh Studio initializes diagonal camera");
   Check(Mathf.Abs(view.size/Mathf.Sin(view.cameraSettings.fieldOfView*.5f*Mathf.Deg2Rad)-10)<.001f,"Fresh Studio initializes 10m distance");
   view.LookAtDirect(new Vector3(3,4,5),Quaternion.Euler(21,88,0),6.5f);
   CameraUnchanged(view,()=>Call(controller,"OpenViewport"),"Rebinding existing Studio");
   view.Close();UnityEngine.Object.DestroyImmediate(controller);
   File.WriteAllText(result,"PASS "+checks+" save-camera assertions");EditorApplication.Exit(0);
  }catch(Exception e){File.WriteAllText(result,"FAIL "+e);EditorApplication.Exit(1);}
 }
}
}
#endif

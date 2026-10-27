using System;using System.IO;using System.Reflection;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;using Object=UnityEngine.Object;
public static class StudioPlaneHandlesValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;static StudioModelEditor editor;static SceneView view;static int checks,plane;static bool scale,busy;static Vector2 displacement;static Vector3 result;static string capture;static Color32[] pixels;static Vector2 sample;static double next;
 static string Root=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
 static void Check(bool b,string m){if(!b)throw new Exception(m);checks++;}
 public static void Run(){if(!Application.dataPath.Replace("\\","/").Contains("/obj/StudioControlsValidation/"))throw new InvalidOperationException("Run only in a disposable StudioControlsValidation project.");EditorApplication.delayCall+=Start;}
 static void Start(){try{editor=new StudioModelEditor(()=>{});view=ScriptableObject.CreateInstance<SceneView>();view.position=new Rect(40,40,700,600);view.Show();view.LookAtDirect(Vector3.zero,Quaternion.Euler(25,35,0),3);SceneView.duringSceneGui+=Draw;next=EditorApplication.timeSinceStartup+1;EditorApplication.update+=Tick;}catch(Exception e){Finish(e);}}
 static void Draw(SceneView v)
 {
  if(v!=view||!busy)return;var evt=Event.current;var oldMouse=evt.mousePosition;var cam=view.camera;var target=cam.targetTexture;var rect=cam.pixelRect;var old=RenderTexture.active;RenderTexture rt=null;
  try{cam.pixelRect=new Rect(0,0,700,600);Handles.SetCamera(new Rect(0,0,700,600),cam);float size=HandleUtility.GetHandleSize(Vector3.zero);int a=plane==2?1:0,b=plane==0?1:2;var first=a==0?Vector3.right:Vector3.up;var second=b==1?Vector3.up:Vector3.forward;var point=(first+second)*size*.23f;sample=cam.WorldToScreenPoint(point);evt.mousePosition=HandleUtility.WorldToGUIPoint(point)+displacement;
   if(evt.type==EventType.Repaint){rt=new RenderTexture(700,600,24,RenderTextureFormat.ARGB32);rt.Create();cam.targetTexture=rt;RenderTexture.active=rt;GL.PushMatrix();GL.Clear(true,true,Color.clear);GL.LoadProjectionMatrix(cam.projectionMatrix);GL.modelview=cam.worldToCameraMatrix;}
   result=(Vector3)typeof(StudioModelEditor).GetMethod(scale?"DrawScaleHandle":"DrawMoveHandle",F).Invoke(editor,new object[]{Vector3.zero,Quaternion.identity,1f});
   if(rt&&capture!=null){var t=new Texture2D(700,600,TextureFormat.RGBA32,false);t.ReadPixels(new Rect(0,0,700,600),0,0);t.Apply();pixels=t.GetPixels32();File.WriteAllBytes(Path.Combine(Root,capture+".png"),t.EncodeToPNG());Object.DestroyImmediate(t);capture=null;}
  }finally{if(rt)GL.PopMatrix();cam.targetTexture=target;cam.pixelRect=rect;Handles.SetCamera(cam);RenderTexture.active=old;if(rt)Object.DestroyImmediate(rt);evt.mousePosition=oldMouse;}
 }
 static void Send(EventType t){busy=true;try{view.SendEvent(new Event{type=t,button=0,mousePosition=new Vector2(350,300),delta=displacement});}finally{busy=false;}}
 static int Interior(){int n=0;foreach(var p in pixels)if(p.a>100)n++;return n;}
 static void Tick(){if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.2;try{
  displacement=new Vector2(-500,-500);Send(EventType.Layout);capture="plane-"+(scale?"scale":"move")+plane+"-idle";Send(EventType.Repaint);int idle=Interior();
  displacement=Vector2.zero;Send(EventType.Layout);capture="plane-"+(scale?"scale":"move")+plane+"-hover";Send(EventType.Repaint);Check(Interior()>idle,"Plane interior must fill on hover: "+scale+"/"+plane);
  Send(EventType.MouseDown);Check(GUIUtility.hotControl!=0,"Plane must capture mouse");displacement=new Vector2(20,-15);Send(EventType.MouseDrag);Check((result-(scale?Vector3.one:Vector3.zero)).sqrMagnitude>1e-6f,"Plane drag must change transform");
  int a=plane==2?1:0,b=plane==0?1:2,c=3-a-b;if(scale){Check(Mathf.Abs(result[c]-1)<1e-6f,"Scale changed locked axis");Check(Mathf.Abs(result[a]-result[b])<1e-6f,"Pair scale must be uniform");}else Check(Mathf.Abs(result[c])<1e-5f,"Move changed locked axis");
  capture="plane-"+(scale?"scale":"move")+plane+"-drag";Send(EventType.Repaint);Check(Interior()>idle,"Captured plane must remain filled");Send(EventType.MouseUp);Check(GUIUtility.hotControl==0,"Plane must release mouse");
  if(++plane==3){plane=0;if(scale){Finish(null);return;}scale=true;}
 }catch(Exception e){Finish(e);}}
 static void Finish(Exception e){EditorApplication.update-=Tick;SceneView.duringSceneGui-=Draw;File.WriteAllText(Path.Combine(Root,"plane-handles-result.txt"),e==null?"PASS "+checks+" native plane rendering and drag checks":e.ToString());EditorApplication.delayCall+=()=>{if(view)view.Close();editor?.Dispose();EditorApplication.Exit(e==null?0:1);};}
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
namespace MashBoxSDK.AnimationStudio {
 internal static class StudioQuadViewsValidation {
  const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../QuadViews/"+n));
  static AnimationStudioWindow controller; static MashBoxStudioViewport view; static double next; static int stage;
  static System.Collections.Generic.HashSet<int> picked=new System.Collections.Generic.HashSet<int>(); static string pickError;
  static System.Collections.Generic.List<string> log=new System.Collections.Generic.List<string>();
  static object Get(object o,string f)=>o.GetType().GetField(f,F).GetValue(o);
  static void Set(object o,string f,object v)=>o.GetType().GetField(f,F).SetValue(o,v);
  static object Call(object o,string f,params object[] args)=>o.GetType().GetMethod(f,F).Invoke(o,args);
  static void Check(bool ok,string text){if(!ok)throw new Exception(text);log.Add("PASS "+text);}
  [InitializeOnLoadMethod] static void Boot(){if(Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.update+=Start;}
  static void Start(){EditorApplication.update-=Start;File.Delete(Out("request.txt"));try{
   if(!Path.GetFullPath(StudioPreferenceStore.DirectoryPath).StartsWith(Out("IsolatedDocuments"),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Quad-view validation requires the isolated test Documents root.");
   foreach(var oldView in Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Where(v=>!v.IsEmbeddedView).ToArray()) oldView.Close();
   foreach(var oldController in Resources.FindObjectsOfTypeAll<AnimationStudioWindow>()) UnityEngine.Object.DestroyImmediate(oldController);
   controller=ScriptableObject.CreateInstance<AnimationStudioWindow>();
   Set(controller,"modelingMode",true);Set(controller,"paintingMode",false);Set(controller,"clothMode",false);
   var doc=ScriptableObject.CreateInstance<StudioModelDocument>();
   doc.nodes.Add(new StudioModelDocument.Node{name="Quad view cube",mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube),scale=new Vector3(2,1,1)});
   Set(controller,"modelDocument",doc); Call(controller,"OpenViewport");view=controller.ActiveViewport;
   view.position=new Rect(30,40,1300,850);view.pivot=Vector3.zero;view.size=3;view.rotation=Quaternion.Euler(25,35,0);
   Selection.objects=Array.Empty<UnityEngine.Object>();view.SetFourViews(false);view.modelGridVisible=true; controller.ToolbarModelEditor.SetModelDrawMode(view,DrawCameraMode.TexturedWire);controller.ToolbarModelEditor.SetXRayMode(false);controller.ToolbarModelEditor.SetDisplayMode(true,false);controller.ToolbarModelEditor.SelectObject(0); Check(!view.SplitLayout,"Initial single layout"); SendMaximize(view); view.MarkDirty(MashBoxStudioViewport.DirtyState.All);
   SceneView.beforeSceneGui+=ProbePick;
   next=EditorApplication.timeSinceStartup+3;stage=0;EditorApplication.update+=Tick;
  }catch(Exception e){log.Add("FAIL "+e);File.WriteAllLines(Out("result.txt"),log);}}
  static void Tick(){if(EditorApplication.timeSinceStartup<next)return;try{
   var tiles=(MashBoxStudioViewport[])Get(view,"quadViews");
   if(stage==0){
    Check(view.SplitLayout,"Alt+W opens 2x2 directly from initial single view"); SceneView.beforeSceneGui-=ProbePick;Check(pickError==null&&picked.Count==4,"Native GUI picking in all four views: "+pickError);
    Check(tiles.Length==4&&tiles.Select(t=>t.camera).Distinct().Count()==4,"Four distinct native cameras");
    foreach(var t in tiles){log.Add("tile "+t.ViewKind+" rect="+t.cameraViewport+" root="+t.rootVisualElement.worldBound+" cache="+t.ViewportCacheReady); Check(t.cameraViewport.width>100&&t.cameraViewport.height>100,"Tile has native viewport area "+t.ViewKind); Check(t.orthographic==(t.ViewKind!=1)&&t.camera.orthographic==(t.ViewKind!=1),"Actual native projection "+t.ViewKind);}
    CheckRecovery(tiles);
    Capture("split.png");
    controller.ToolbarModelEditor.ShowCameraOptions(tiles[0],true);Check(ReferenceEquals(Get(controller.ToolbarModelEditor,"cameraOptionsView"),tiles[0]),"Camera icon targets its own view");
    float otherExposure=tiles[1].ViewLighting.exposure;controller.ToolbarModelEditor.ShowLightingOptions(tiles[0],true);controller.ToolbarModelEditor.EditLighting(l=>l.exposure=otherExposure+1);Check(tiles[0].ViewLighting.exposure==otherExposure+1&&tiles[1].ViewLighting.exposure==otherExposure,"Lighting settings are independent");
    controller.ToolbarModelEditor.ShowLightingOptions(false);
    Check(tiles.All(t=>t.ViewportCacheReady),"All four native scene and overlay caches rendered");
    tiles[0].SetModelGridVisible(false);Check(!tiles[0].modelGridVisible&&tiles.Skip(1).All(t=>t.modelGridVisible),"Grid changes one viewport only");
    var pivot=tiles[0].pivot; tiles[0].ApplyStudioNavigation(StudioShortcut.Pan,new Vector2(25,10));Check(tiles[0].pivot!=pivot&&tiles[2].pivot==pivot,"Independent orthographic pan");
    var rotation=tiles[0].rotation;tiles[0].ApplyStudioNavigation(StudioShortcut.Orbit,new Vector2(30,30));Check(tiles[0].rotation==rotation,"Orthographic orbit stays flat");
    var oldCanvas=(IMGUIContainer)Get(tiles[0],"cameraInputCanvas");oldCanvas.Focus();
    Check(ReferenceEquals(view.rootVisualElement.focusController.focusedElement,oldCanvas),"Start with keyboard focus in a different tile");
    Call(view,"TrackQuadPointer",tiles[2].rootVisualElement.worldBound.center);SendMaximize(tiles[2]);stage=1;
   }else if(stage==1){
    Check((int)Get(view,"maximizedQuad")==2,"Maximize targets hovered Side view");
    Check(ReferenceEquals(view.rootVisualElement.focusController.focusedElement,Get(tiles[2],"cameraInputCanvas")),"Maximize moves focus out of hidden tile without a click");
    Check(tiles.Where((t,i)=>i!=2).All(t=>t.rootVisualElement.resolvedStyle.display==DisplayStyle.None),"Other three view roots and input trees hidden");
    Check(tiles[2].cameraViewport.width>view.ModelCanvasRect.width-5,"Maximized native camera fills canvas");Capture("maximized.png");
    SendMaximize(tiles[2]);stage=2;
   }else{
    Check((int)Get(view,"maximizedQuad")==-1&&tiles.All(t=>t.rootVisualElement.resolvedStyle.display!=DisplayStyle.None),"Restore returns four views");
    Check(!tiles[0].modelGridVisible,"View state retained on restore");Capture("restored.png");
    Check(ReferenceEquals(view.rootVisualElement.focusController.focusedElement,Get(tiles[2],"cameraInputCanvas")),"Restore retains usable canvas focus without a click");
    Check((int)Get(view,"hoveredQuad")==2,"Stationary cursor resolves to restored Side tile");
    var savedPivot=tiles[0].pivot; var savedExposure=tiles[0].ViewLighting.exposure; view.SetFourViews(false); Check(!view.SplitLayout && view.EditingView==tiles[2],"Single View menu retains active camera"); view.SetFourViews(true); Check(view.SplitLayout && ReferenceEquals(Get(view,"quadViews"),tiles) && tiles[0].pivot==savedPivot && tiles[0].ViewLighting.exposure==savedExposure && !tiles[0].modelGridVisible,"Menu layouts retain cameras, pan, lighting and grid"); EditorApplication.update-=Tick;File.WriteAllLines(Out("result.txt"),log);return;
   }
   view.MarkDirty(MashBoxStudioViewport.DirtyState.All);next=EditorApplication.timeSinceStartup+2;
  }catch(Exception e){EditorApplication.update-=Tick;log.Add("FAIL "+e);File.WriteAllLines(Out("result.txt"),log);}}
  static void CheckRecovery(MashBoxStudioViewport[] tiles){
   int oldActive=(int)Get(view,"activeQuad");
   try{
    foreach(var state in new[]{(MashBoxStudioViewport[])null,Array.Empty<MashBoxStudioViewport>(),new MashBoxStudioViewport[4],new[]{tiles[0]}}){
     Set(view,"quadViews",state);Set(view,"activeQuad",99);
     Check(ReferenceEquals(view.EditingView,view),"Incomplete tile array falls back to host: "+(state==null?"null":state.Length.ToString()));
     controller.ToolbarModelEditor.ToolbarCommands(view);Check(true,"Toolbar menu refresh tolerates incomplete tile array");
    }
    Set(view,"quadViews",tiles);
    foreach(int bad in new[]{-1,4,int.MaxValue}){Set(view,"activeQuad",bad);Check(ReferenceEquals(view.EditingView,tiles[1]),"Invalid active index falls back to Perspective: "+bad);controller.ToolbarModelEditor.ToolbarCommands(view);}
    Set(view,"activeQuad",oldActive);Set(view,"queuedQuad",-2);Call(view,"QueueQuadMaximize",99);Check((int)Get(view,"queuedQuad")==-2,"Reject out-of-range queued maximize target");
    Set(view,"queuedQuad",99);Call(view,"ApplyQueuedQuad");Check((int)Get(view,"maximizedQuad")==-1&&(int)Get(view,"activeQuad")==oldActive,"Reject stale deferred maximize target before mutation");
    string json=EditorJsonUtility.ToJson(view);Check(!json.Contains("\"quadViews\"")&&!json.Contains("\"activeQuad\""),"Transient camera array and indices excluded from Unity serialization");
   }finally{Set(view,"quadViews",tiles);Set(view,"activeQuad",oldActive);Set(view,"queuedQuad",-2);}
  }
  static void ProbePick(SceneView scene){if(!(scene is MashBoxStudioViewport tile)||!tile.IsEmbeddedView||tile.QuadOwner!=view||picked.Contains(tile.ViewKind)||Event.current.type!=EventType.Repaint)return;try{var point=HandleUtility.WorldToGUIPoint(Vector3.zero);var editor=controller.ToolbarModelEditor;Set(editor,"end",point);Call(editor,"Pick",scene,new Rect(point,Vector2.one),true);if(editor.Active!=0)throw new Exception("Missed cube in "+tile.ViewKind+" at "+point);picked.Add(tile.ViewKind);}catch(Exception e){pickError=e.ToString();}}
  static void SendMaximize(MashBoxStudioViewport tile){using(var key=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.W,modifiers=EventModifiers.Alt})){var target=tile.rootVisualElement.focusController.focusedElement as VisualElement ?? tile.rootVisualElement;key.target=target;target.SendEvent(key);}}
  static void Capture(string file){var parent=typeof(EditorWindow).GetField("m_Parent",F).GetValue(view);var rt=new RenderTexture((int)view.position.width,(int)view.position.height,0);rt.Create();var old=RenderTexture.active;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);try{parent.GetType().GetMethod("GrabPixels",F).Invoke(parent,new object[]{rt,new Rect(0,0,rt.width,rt.height)});RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);var px=image.GetPixels(); var flipped=new Color[px.Length]; for(int y=0;y<image.height;y++)Array.Copy(px,y*image.width,flipped,(image.height-1-y)*image.width,image.width); image.SetPixels(flipped);image.Apply();File.WriteAllBytes(Out(file),image.EncodeToPNG());}finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}}
 }
}

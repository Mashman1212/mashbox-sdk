using System;using System.IO;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using UnityEngine.UIElements;
namespace MashBoxSDK.AnimationStudio {
public static class StudioQuadChromeValidation {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static AnimationStudioWindow controller;static MashBoxStudioViewport view;static int stage;static double next;static float previousWidth;static List<string> log=new List<string>();
 static string Out(string s)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../../QuadChrome/"+s));
 static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
 static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
 static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,F).Invoke(o,a);
 static void Check(bool value,string text){if(!value)throw new Exception(text);log.Add("PASS "+stage+" "+text);}
 public static void Begin(){if(!Application.dataPath.Replace('\\','/').EndsWith("/SplineCut/Project/Assets"))throw new Exception("Disposable project only");try{
 controller=ScriptableObject.CreateInstance<AnimationStudioWindow>();Set(controller,"modelingMode",true);Set(controller,"paintingMode",false);Set(controller,"clothMode",false);Set(controller,"modelDocument",ScriptableObject.CreateInstance<StudioModelDocument>());Call(controller,"OpenViewport");view=controller.ActiveViewport;view.position=new Rect(20,20,1300,850);view.SetFourViews(true);next=EditorApplication.timeSinceStartup+3;EditorApplication.update+=Tick;
 }catch(Exception e){Finish(e);}}
 static void Tick(){if(EditorApplication.timeSinceStartup<next)return;try{
 var stats=(VisualElement)Get(view,"meshStatistics");var fps=(VisualElement)Get(view,"workspaceStats");var isolate=(VisualElement)Get(view,"viewportIsolateButton");var all=(VisualElement)Get(view,"viewportShowAllButton");var quad=(VisualElement)Get(view,"quadRoot");
 Check(Mathf.Abs(quad.layout.y-view.ModelCanvasRect.y)<.1f&&Mathf.Abs(quad.layout.height-view.ModelCanvasRect.height)<.1f,"Scene fills canvas without a reserved header strip");
 var tiles=(MashBoxStudioViewport[])Get(view,"quadViews");
 foreach(var tile in tiles){
 var buttons=new List<VisualElement>();foreach(var field in new[]{"viewportCameraButton","viewportLightingButton","viewportIsolateButton","viewportShowAllButton","quadHeader"})buttons.Add((VisualElement)Get(tile,field));
 Check(buttons.TrueForAll(b=>b!=null&&b.resolvedStyle.display!=DisplayStyle.None),"Every tile retains all five controls");
 Check(buttons.TrueForAll(b=>Mathf.Abs(b.resolvedStyle.opacity-.7f)<.01f&&Mathf.Abs(b.resolvedStyle.borderTopLeftRadius-4)<.01f),"Every tile uses original fading and rounded corners");
 if(tile.rootVisualElement.resolvedStyle.display==DisplayStyle.None)continue;
 Check(buttons.TrueForAll(b=>tile.rootVisualElement.worldBound.Contains(b.worldBound.center)),"Buttons stay within their resized viewport");
 Check(Mathf.Abs(tile.rootVisualElement.layout.width-(view.SplitLayout?(quad.layout.width-2)/2:quad.layout.width))<.1f,"Visible viewport width follows canvas resize");
 Check(Mathf.Abs(tile.rootVisualElement.layout.height-(view.SplitLayout?(quad.layout.height-2)/2:quad.layout.height))<.1f,"Visible viewport height follows canvas resize");
 }
 Check(isolate.resolvedStyle.display==DisplayStyle.None&&all.resolvedStyle.display==DisplayStyle.None,"No duplicated host action buttons");
 if(stage==3)Check(Mathf.Abs(quad.layout.width-previousWidth)>50,"Window resize changed the viewport canvas width");
 Check(stats.worldBound.xMin>=quad.worldBound.xMin&&stats.worldBound.xMax<=quad.worldBound.xMax,"Full statistics width stays inside viewport canvas");
 Check(stats.worldBound.yMin>=quad.worldBound.yMin+28,"Statistics sit below tile camera header");
 Check(view.rootVisualElement.IndexOf(stats)>view.rootVisualElement.IndexOf(quad),"Statistics render above retained native viewport roots");
 if(stage++==0)view.SetFourViews(false);else if(stage==2)view.SetFourViews(true);else if(stage==3){previousWidth=quad.layout.width;view.position=new Rect(20,20,1480,960);}else{Capture("buttons-resized.png");Finish(null);return;}next=EditorApplication.timeSinceStartup+2;
 }catch(Exception e){Finish(e);}}
  static void Capture(string file){var parent=typeof(EditorWindow).GetField("m_Parent",F).GetValue(view);var rt=new RenderTexture((int)view.position.width,(int)view.position.height,0);rt.Create();var old=RenderTexture.active;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);try{parent.GetType().GetMethod("GrabPixels",F).Invoke(parent,new object[]{rt,new Rect(0,0,rt.width,rt.height)});RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);var px=image.GetPixels(); var flipped=new Color[px.Length]; for(int y=0;y<image.height;y++)Array.Copy(px,y*image.width,flipped,(image.height-1-y)*image.width,image.width); image.SetPixels(flipped);image.Apply();File.WriteAllBytes(Out(file),image.EncodeToPNG());}finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}}
 static void Finish(Exception e){EditorApplication.update-=Tick;if(e!=null)log.Add("FAIL "+e);File.WriteAllLines(Out("result.txt"),log);EditorApplication.Exit(e==null?0:1);}
}}

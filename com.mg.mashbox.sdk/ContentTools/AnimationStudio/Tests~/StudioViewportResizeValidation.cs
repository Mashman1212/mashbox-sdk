using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
namespace MashBoxSDK.AnimationStudio {
public static class StudioViewportResizeValidation {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static AnimationStudioWindow controller;static MashBoxStudioViewport view;static int stage,failures;static double next;static readonly List<string> log=new List<string>();
 static object Get(object o,string name)=>o.GetType().GetField(name,F).GetValue(o);
 static void Set(object o,string name,object value)=>o.GetType().GetField(name,F).SetValue(o,value);
 static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,F).Invoke(o,args);
 static void Check(bool pass,string text){log.Add((pass?"PASS ":"FAIL ")+stage+" "+text);if(!pass)failures++;}
 public static void Begin(){if(!Application.dataPath.Replace('\\','/').EndsWith("/HolePuncher/Project/Assets"))throw new Exception("Disposable project only");try{
 controller=ScriptableObject.CreateInstance<AnimationStudioWindow>();Set(controller,"modelingMode",true);Set(controller,"paintingMode",false);Set(controller,"clothMode",false);Set(controller,"modelDocument",ScriptableObject.CreateInstance<StudioModelDocument>());
 view=ScriptableObject.CreateInstance<MashBoxStudioViewport>();view.dockableHost=!StudioSettings.Current.floatingStudioWindow;
 // Hot reload restores scalar initialization state but drops managed UI callbacks.
 Set(view,"panelsInitialized",true);Set(controller,"viewport",view);view.position=new Rect(20,20,1300,850);if(view.dockableHost)view.Show();else view.ShowUtility();Call(controller,"OpenViewport");view.SetFourViews(true);view.SetFourViews(false);next=EditorApplication.timeSinceStartup+3;EditorApplication.update+=Tick;
 }catch(Exception e){Finish(e);}}
 static void Tick(){if(EditorApplication.timeSinceStartup<next)return;try{
 var quad=(VisualElement)Get(view,"quadRoot");var area=view.ModelCanvasRect;
 log.Add("STATE "+stage+" host="+view.rootVisualElement.contentRect+" canvas="+area+" quad="+quad.layout);
 Check(Mathf.Abs(quad.layout.width-area.width)<1&&Mathf.Abs(quad.layout.height-area.height)<1,"Retained viewport fills the current canvas");
 foreach(var tile in (MashBoxStudioViewport[])Get(view,"quadViews")){if(tile.rootVisualElement.resolvedStyle.display==DisplayStyle.None)continue;var bounds=tile.rootVisualElement.layout;var expected=view.SplitLayout?new Vector2((area.width-2)/2,(area.height-2)/2):area.size;Check((bounds.size-expected).sqrMagnitude<2,"Visible camera root follows resize");Check((tile.camera.pixelRect.size/EditorGUIUtility.pixelsPerPoint-expected).sqrMagnitude<4,"Native camera render area follows resize");log.Add("CAMERA "+tile.ViewKind+" root="+bounds+" pixel="+tile.camera.pixelRect);tile.rootVisualElement.Query<IMGUIContainer>().ForEach(c=>{if(c.name.Contains("Camera")||c.name.Contains("Scene")||c.name.Contains("scene"))log.Add("GUI "+c.name+" "+c.layout);});}
 foreach(var field in new[]{"leftDivider","rightDivider"}){var divider=(VisualElement)Get(view,field);Check(!quad.worldBound.Contains(divider.worldBound.center),"Viewport does not cover the panel resize handle");}
 if(stage==0)view.position=new Rect(20,20,1500,1050);
 else if(stage==1){view.ResizePanel(1,340);view.ResizePanel(2,430);}
 else if(stage==2)view.SetFourViews(true);
 else if(stage==3)view.position=new Rect(20,20,1100,800);
 else if(stage==4)view.SetFourViews(false);
 else{Finish(null);return;}
 stage++;next=EditorApplication.timeSinceStartup+2;
 }catch(Exception e){Finish(e);}}
 static void Finish(Exception e){EditorApplication.update-=Tick;if(e!=null){log.Add("FAIL "+e);failures++;}File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath,"../../resize-result.txt")),log);EditorApplication.Exit(failures==0?0:1);}
}}


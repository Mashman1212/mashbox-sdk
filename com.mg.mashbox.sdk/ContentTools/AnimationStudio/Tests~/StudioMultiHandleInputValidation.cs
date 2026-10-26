using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;using Object=UnityEngine.Object;
public static class StudioMultiHandleInputValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static AnimationStudioWindow controller;static MashBoxStudioViewport view;static StudioModelEditor editor;static Vector2 sent,start,last;static Vector2[] candidates;static int hot,nearest,phase,scenario,samples;static double next;static Vector3[][] before;static int checks;static string stage;static readonly List<string> results=new List<string>();
 static object Get(string name)=>typeof(StudioModelEditor).GetField(name,F).GetValue(editor);static void Set(string name,object value)=>typeof(StudioModelEditor).GetField(name,F).SetValue(editor,value);
 static void Check(bool value,string message){if(!value)throw new Exception(stage+": "+message);checks++;}
 static void Target(int node){typeof(StudioModelEditor).GetMethod("ActivateComponentTarget",F).Invoke(editor,new object[]{node,StudioModelEditor.ComponentMode.Face});editor.Components.Clear();editor.Components.Add(0);}
 public static void Run(){EditorApplication.delayCall+=()=>{try{
  var doc=ScriptableObject.CreateInstance<StudioModelDocument>();for(int i=0;i<2;i++){var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);doc.nodes.Add(new StudioModelDocument.Node{name="Mesh "+i,mesh=mesh,position=Vector3.right*(i==0?-1:1),cutPolygons=polygons,cutTriangles=mesh.triangles});}
  controller=ScriptableObject.CreateInstance<AnimationStudioWindow>();typeof(AnimationStudioWindow).GetField("modelingMode",F).SetValue(controller,true);typeof(AnimationStudioWindow).GetField("modelDocument",F).SetValue(controller,doc);typeof(AnimationStudioWindow).GetMethod("OpenViewport",F).Invoke(controller,null);view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Single(v=>v.Studio==controller);view.position=new Rect(100,100,1000,750);editor=(StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor",F).GetValue(controller);Target(0);Target(1);Set("local",false);view.LookAtDirect(Vector3.zero,Quaternion.Euler(20,30,0),4);
  StudioModelEditor.TransformHandleProbe=(tool,center,orientation,size)=>{Event.current.mousePosition=sent;var point=HandleUtility.WorldToGUIPoint(center);candidates=tool==Tool.Rotate?new[]{point}:new[]{HandleUtility.WorldToGUIPoint(center+Vector3.right*size*.7f),HandleUtility.WorldToGUIPoint(center+Vector3.right*size*.9f),point};};SceneView.duringSceneGui+=After;EditorApplication.update+=Tick;next=EditorApplication.timeSinceStartup+2;
 }catch(Exception ex){Finish(ex);}};}
 static void After(SceneView scene){if(scene!=view)return;hot=GUIUtility.hotControl;nearest=HandleUtility.nearestControl;}
 static void Send(EventType type,Vector2 pointer,Vector2 delta=default){sent=pointer;view.SendEvent(new Event{type=type,button=0,mousePosition=new Vector2(500,350),delta=delta});}
 static void Tick()
 {
  if(EditorApplication.timeSinceStartup<next)return;
  try
  {
   var tool=scenario<2?Tool.Move:scenario==2?Tool.Rotate:Tool.Scale;stage=tool+(scenario==1?" free":"");
   if(phase==0)
   {
    Set("tool",tool);Target(0);Target(1);UnityEditor.Undo.ClearAll();StudioUndo.ClearForTests();StudioUndo.IncrementCurrentGroup();
    Send(EventType.Layout,new Vector2(500,350));Send(EventType.Repaint,new Vector2(500,350));
    var options=candidates.ToArray();bool found=false;
    foreach(var candidate in scenario==1?options.Skip(2):options)
    {
     Send(EventType.Layout,candidate);if(nearest==0)continue;
     before=editor.Document.nodes.Select(n=>n.mesh.vertices).ToArray();Send(EventType.MouseDown,candidate);
     if(hot!=0&&Get("multiComponentGesture")!=null){start=last=candidate;found=true;break;}
     Send(EventType.MouseUp,candidate);
    }
    Check(found,"No native shared handle captured");samples=0;phase=1;return;
   }
   if(phase==1)
   {
    var point=start+new Vector2(++samples*5,samples*2);Send(EventType.MouseDrag,point,point-last);Send(EventType.Layout,point);Send(EventType.Repaint,point);last=point;if(samples<5)return;
    for(int i=0;i<2;i++)Check(!before[i].SequenceEqual(editor.Document.nodes[i].mesh.vertices),"Native gesture did not edit mesh "+i);
    Send(EventType.MouseUp,last);Check(hot==0,"Handle retained capture");Check(StudioUndo.RetainedActions==1,"Gesture created multiple actions");UnityEditor.Undo.FlushUndoRecordObjects();StudioUndo.IncrementCurrentGroup();StudioUndo.PerformUndo();for(int i=0;i<2;i++)Check(before[i].SequenceEqual(editor.Document.nodes[i].mesh.vertices),"Native gesture Undo missed mesh "+i);
    results.Add(stage+" passed");if(++scenario<4){phase=0;return;}Finish(null);
   }
  }catch(Exception ex){Finish(ex);}
 }
 static void Finish(Exception ex){EditorApplication.update-=Tick;SceneView.duringSceneGui-=After;StudioModelEditor.TransformHandleProbe=null;results.Add(ex==null?"PASS "+checks+" native shared Move axis/free, Rotate free and Scale input/history checks":ex.ToString());File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../multi-handle-input-result.txt")),string.Join("\n",results));EditorApplication.delayCall+=()=>{if(view)view.Close();if(controller)Object.DestroyImmediate(controller);EditorApplication.delayCall+=()=>EditorApplication.Exit(ex==null?0:1);};}
}

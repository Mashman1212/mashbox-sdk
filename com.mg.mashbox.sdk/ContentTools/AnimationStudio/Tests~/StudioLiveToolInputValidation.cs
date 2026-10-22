using System;using System.IO;using System.Linq;using System.Reflection;using MashBoxSDK.AnimationStudio;using UnityEditor;using UnityEngine;using Object=UnityEngine.Object;
public static class StudioLiveToolInputValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static AnimationStudioWindow owner;static MashBoxStudioViewport view;static StudioModelEditor editor;static StudioModelDocument doc;static ToolChannelPane pane;static double due;static int phase,checks;static string original,preview,errors="";static float width;
 static void Check(bool b,string s){if(!b)throw new Exception(s);checks++;}
 static void Set(object t,string n,object v)=>t.GetType().GetField(n,F).SetValue(t,v);
 static object Get(object t,string n)=>t.GetType().GetField(n,F).GetValue(t);
 static string Shape()=>string.Join(";",doc.nodes[0].mesh.vertices.Select(p=>p.ToString("R")));
 static void Log(string s,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors+=s+"\n"+stack;}
 public static void Run(){EditorApplication.delayCall+=Begin;}
 static void Begin()
 {
  try{
   doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);doc.nodes.Add(new StudioModelDocument.Node{name="Live bevel",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles});
   owner=ScriptableObject.CreateInstance<AnimationStudioWindow>();Set(owner,"modelingMode",true);Set(owner,"modelDocument",doc);owner.Show();typeof(AnimationStudioWindow).GetMethod("OpenViewport",F).Invoke(owner,null);
   view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Single(v=>v.Studio==owner);view.position=new Rect(0,0,1200,800);editor=owner.ToolbarModelEditor;editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);editor.Components.Add(0);
   original=Shape();ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.1f);ToolChannelPane.SetFloatDefault("Bevel Edges","Subdivisions",3);ToolChannelPane.SetFloatDefault("Bevel Edges","Tension",1);ToolChannelPane.SetFloatDefault("Bevel Edges","Junction",0);
   typeof(StudioModelEditor).GetMethod("OpenBevelTool",F).Invoke(editor,null);pane=(ToolChannelPane)Get(editor,"bevelToolPane");pane.position=new Rect(100,100,340,235);pane.Focus();
   Application.logMessageReceived+=Log;due=EditorApplication.timeSinceStartup+2;EditorApplication.update+=Tick;
  }catch(Exception e){Finish(e);}
 }
 static void Tick()
 {
  if(EditorApplication.timeSinceStartup<due)return;due=EditorApplication.timeSinceStartup+.4;
  try{
   switch(phase++){
    case 0:
     preview=Shape();Check(preview!=original,"Initial live preview rendered");width=ToolChannelPane.GetFloatDefault("Bevel Edges","Width",0);
     pane.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=new Vector2(42,38)});
     pane.SendEvent(new Event{type=EventType.MouseDrag,button=0,mousePosition=new Vector2(52,38),delta=new Vector2(10,0)});
     pane.SendEvent(new Event{type=EventType.MouseDrag,button=0,mousePosition=new Vector2(62,38),delta=new Vector2(10,0)});
     pane.SendEvent(new Event{type=EventType.MouseDrag,button=0,mousePosition=new Vector2(72,38),delta=new Vector2(10,0)});break;
    case 1:
     Check(ToolChannelPane.GetFloatDefault("Bevel Edges","Width",0)!=width,"Native numeric label drag edits parameter (width="+ToolChannelPane.GetFloatDefault("Bevel Edges","Width",0)+", hot="+GUIUtility.hotControl+")");
     Check(Shape()!=preview,"Geometry updates before MouseUp while drag is held");
     Check(Get(editor,"livePreview")!=null,"Drag remains unapplied");
     pane.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=new Vector2(52,38)});pane.Close();pane=null;break;
    case 2:
     Check(Shape()==original,"Closing native pane rolls back unapplied drag");
     Check(!(bool)Get(editor,"previewQueued"),"No idle preview callback remains");
     Check(errors.Length==0,"No native errors: "+errors);Finish(null);break;
   }
  }catch(Exception e){Finish(e);}
 }
 static void Finish(Exception error)
 {
  EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
  File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../live-tools-input-result.txt")),error==null?"PASS "+checks+" assertions":"FAIL phase "+phase+"\n"+error+"\n"+errors);
  if(pane)pane.Close();if(view)view.Close();if(owner)owner.Close();if(doc){foreach(var n in doc.nodes)if(n.mesh)Object.DestroyImmediate(n.mesh);Object.DestroyImmediate(doc);}EditorApplication.Exit(error==null?0:1);
 }
}

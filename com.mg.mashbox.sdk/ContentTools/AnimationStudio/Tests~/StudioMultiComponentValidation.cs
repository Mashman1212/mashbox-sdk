using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;
public static class StudioMultiComponentValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static int checks;static string stage;static StudioModelEditor editor;static StudioModelDocument doc;static SceneView view;static bool pending;static int errors;
 static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length&&m.GetParameters().Select((p,i)=>args[i]==null||p.ParameterType.IsInstanceOfType(args[i])).All(v=>v)).Invoke(editor,args);
 static object Get(string name)=>typeof(StudioModelEditor).GetField(name,F).GetValue(editor);
 static void Set(string name,object value)=>typeof(StudioModelEditor).GetField(name,F).SetValue(editor,value);
 static void Check(bool value,string message){if(!value)throw new Exception(stage+": "+message);checks++;}
 static void Reset(){UnityEditor.Undo.ClearAll();StudioUndo.ClearForTests();StudioUndo.IncrementCurrentGroup();}
 static void History(bool redo=false){UnityEditor.Undo.FlushUndoRecordObjects();StudioUndo.IncrementCurrentGroup();if(redo)StudioUndo.PerformRedo();else StudioUndo.PerformUndo();}
 static void Target(int node,StudioModelEditor.ComponentMode mode,params int[] selection){Call("ActivateComponentTarget",node,mode);editor.Components.Clear();editor.Components.UnionWith(selection);}
 static int[] Selection(int node){var record=((Array)Call("CaptureComponentTargets")).Cast<object>().Single(r=>(int)r.GetType().GetField("node").GetValue(r)==node);return (int[])record.GetType().GetField("components").GetValue(record);}
 static void Pair(StudioModelEditor.ComponentMode mode,params int[] selection){Target(0,mode,selection);Target(1,mode,selection);}
 static Vector3[][] Vertices()=>doc.nodes.Select(n=>n.mesh.vertices).ToArray();
 static void Same(Vector3[][] expected,string label){for(int i=0;i<2;i++)Check(expected[i].SequenceEqual(doc.nodes[i].mesh.vertices),label+" mesh "+i);}
 public static void Run(){Application.logMessageReceived+=(s,t,k)=>{if(k==LogType.Error||k==LogType.Exception||k==LogType.Assert)errors++;};EditorApplication.delayCall+=()=>{try{
  doc=ScriptableObject.CreateInstance<StudioModelDocument>();for(int i=0;i<2;i++){var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);doc.nodes.Add(new StudioModelDocument.Node{name="Target "+i,mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,position=new Vector3(i==0?-1.5f:1.5f,0,0),rotation=Quaternion.Euler(0,i*23,0),scale=i==0?Vector3.one:new Vector3(.8f,1.2f,1)});}
  editor=new StudioModelEditor(()=>{});editor.Load(doc);view=ScriptableObject.CreateInstance<MashBoxStudioViewport>();((MashBoxStudioViewport)view).Bind(editor.Scene);view.position=new Rect(80,80,850,650);view.Show();view.LookAtDirect(Vector3.zero,Quaternion.Euler(15,20,0),5);pending=true;SceneView.duringSceneGui+=Draw;view.Repaint();
 }catch(Exception e){Finish(e.ToString());}};}
 static void Draw(SceneView scene)
 {
  if(scene!=view||!pending||Event.current.type!=EventType.Repaint)return;pending=false;
  try
  {
   stage="cursor mode acquisition";
   for(int i=0;i<2;i++)
   {
    Event.current.mousePosition=HandleUtility.WorldToGUIPoint(editor.Transforms[i].position);
    Check((int)Call("MeshAtModePointer",view,Event.current.mousePosition)==i,"Ray did not find target "+i);
    Call("ChangeComponentMode",StudioModelEditor.ComponentMode.Vertex,view);
    Check(editor.Active==i,"Mode command did not immediately activate hovered object");
    editor.Components.Add(0);
   }
   Check(Selection(0).Contains(0)&&Selection(1).Contains(0),"Second acquisition dropped first selection");
   Check(((HashSet<int>)Get("objects")).SetEquals(new[]{0,1}),"No-modifier acquisition did not retain both objects");
   stage="click across targets";
   Set("end",HandleUtility.WorldToGUIPoint(editor.Transforms[0].position));Call("Pick",view,new Rect(),true);
   Check(Selection(1).Contains(0),"Plain click discarded other mesh components");
   stage="marking start target";
   Call("ExecuteModeTargetAction",StudioModelEditor.MarkingAction.Edge,view,0);
   Check(editor.Active==0&&(StudioModelEditor.ComponentMode)Get("mode")==StudioModelEditor.ComponentMode.Edge,"Captured marking target ignored");
   Check(Selection(1).Contains(0),"Per-object mode change discarded other target");
   stage="marquee and history";Pair(StudioModelEditor.ComponentMode.Vertex);Set("xray",true);Reset();
   Call("Pick",view,new Rect(-100000,-100000,200000,200000),false);
   Check(Selection(0).Length>0&&Selection(1).Length>0,"Marquee did not select both meshes");var selected0=Selection(0);var selected1=Selection(1);
   Check(StudioUndo.RetainedActions==1,"Marquee made multiple actions");History();Check(Selection(0).Length==0&&Selection(1).Length==0,"Marquee Undo failed");History(true);Check(Selection(0).SequenceEqual(selected0)&&Selection(1).SequenceEqual(selected1),"Marquee Redo failed");
   editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.Clear,null);Check(Selection(0).Length==0&&Selection(1).Length==0,"Deselect left other target selected");
   editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.SelectAll,null);Check(Selection(0).Length==selected0.Length&&Selection(1).Length==selected1.Length,"Select All ignored secondary target");
   Call("InvertSelection");Check(Selection(0).Length==0&&Selection(1).Length==0,"Invert ignored secondary target");
   stage="shared transform";
   foreach(var tool in new[]{Tool.Move,Tool.Rotate,Tool.Scale})
   {
    Pair(StudioModelEditor.ComponentMode.Vertex,0);Set("tool",tool);Reset();var before=Vertices();var nodes=new List<int>{0,1};var center=(Vector3)Call("MultiComponentCenter",nodes);var gesture=Call("CaptureMultiComponentGesture",nodes,center,Quaternion.identity,0);
    var delta=new Vector3(.3f,.2f,-.1f);var turn=Quaternion.Euler(0,25,10);var factor=new Vector3(1.2f,.8f,1.1f);
    Check((bool)Call("ApplyMultiComponentGesture",gesture,center+delta,turn,factor),tool+" did not apply");
    for(int i=0;i<2;i++)
    {
     var matrix=editor.Transforms[i].localToWorldMatrix;var world=matrix.MultiplyPoint3x4(before[i][0]);var expected=tool==Tool.Move?world+delta:tool==Tool.Rotate?center+turn*(world-center):center+Vector3.Scale(world-center,factor);
     Check((matrix.MultiplyPoint3x4(doc.nodes[i].mesh.vertices[0])-expected).sqrMagnitude<1e-8f,tool+" wrong world result on "+i);
     Check(doc.nodes[i].mesh.vertices[1].Equals(before[i][1]),tool+" changed unselected vertex");
    }
    Check(StudioUndo.RetainedActions==1,tool+" made multiple actions");History();Same(before,tool+" Undo");History(true);
   }
   stage="mixed component transform";Target(0,StudioModelEditor.ComponentMode.Edge,0);Target(1,StudioModelEditor.ComponentMode.Face,0);Set("tool",Tool.Move);Reset();var mixedBefore=Vertices();var mixedNodes=new List<int>{0,1};var mixedCenter=(Vector3)Call("MultiComponentCenter",mixedNodes);var mixedGesture=Call("CaptureMultiComponentGesture",mixedNodes,mixedCenter,Quaternion.identity,0);Call("ApplyMultiComponentGesture",mixedGesture,mixedCenter+Vector3.up*.2f,Quaternion.identity,Vector3.one);Check(!doc.nodes[0].mesh.vertices.SequenceEqual(mixedBefore[0])&&!doc.nodes[1].mesh.vertices.SequenceEqual(mixedBefore[1]),"Mixed edge/face transform ignored target");History();Same(mixedBefore,"mixed Undo");
   stage="multi delete";Pair(StudioModelEditor.ComponentMode.Face,0);Reset();var deleteBefore=Vertices();var triangles=doc.nodes.Select(n=>n.mesh.triangles.Length).ToArray();editor.DeleteSelection();for(int i=0;i<2;i++)Check(doc.nodes[i].mesh.triangles.Length<triangles[i],"Delete missed mesh "+i);Check(StudioUndo.RetainedActions==1,"Delete made multiple actions");History();Same(deleteBefore,"Delete Undo");Check(Selection(0).Contains(0)&&Selection(1).Contains(0),"Delete Undo lost selections");History(true);History();
   ValidateLive("Extrude",StudioModelEditor.ComponentMode.Face);
   ValidateLive("Bevel",StudioModelEditor.ComponentMode.Edge);
   ValidateLive("Merge",StudioModelEditor.ComponentMode.Vertex);
   stage="multi soften";Pair(StudioModelEditor.ComponentMode.Edge,0);Reset();var normals=doc.nodes.Select(n=>n.mesh.normals).ToArray();var soften=Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",BindingFlags.NonPublic),"Soften");ToolChannelPane.SetFloatDefault("Soften Edges","Angle",180);Call("EvaluateLiveTool",soften);for(int i=0;i<2;i++)Check(!normals[i].SequenceEqual(doc.nodes[i].mesh.normals),"Soften preview missed mesh "+i);Call("CancelLiveTool",false);for(int i=0;i<2;i++)Check(normals[i].SequenceEqual(doc.nodes[i].mesh.normals),"Soften cancel missed mesh "+i);
   stage="atomic preview failure";Target(0,StudioModelEditor.ComponentMode.Face,0);Target(1,StudioModelEditor.ComponentMode.Edge,0);Reset();var failureBefore=Vertices();bool rejected=false;try{Call("EvaluateLiveTool",Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",BindingFlags.NonPublic),"Extrude"));}catch(TargetInvocationException){rejected=true;}Check(rejected,"Invalid closed-edge extrusion unexpectedly succeeded");Same(failureBefore,"Second-target failure rollback");Check(Selection(0).SequenceEqual(new[]{0})&&Selection(1).SequenceEqual(new[]{0}),"Failed preview lost selections");Check(StudioUndo.RetainedActions==0,"Failed preview registered history");
   stage="shift extrusion";Pair(StudioModelEditor.ComponentMode.Face,0);Set("tool",Tool.Move);Reset();var extrusionBefore=Vertices();var extNodes=new List<int>{0,1};var extCenter=(Vector3)Call("MultiComponentCenter",extNodes);var extGesture=Call("CaptureMultiComponentGesture",extNodes,extCenter,Quaternion.identity,0);Call("ExtrudeMultiComponentGesture",extGesture,extCenter+Vector3.up*.2f,Quaternion.identity,Vector3.one);for(int i=0;i<2;i++)Check(doc.nodes[i].mesh.vertexCount>extrusionBefore[i].Length,"Shift extrusion missed mesh "+i);Call("ApplyMultiComponentGesture",extGesture,extCenter+Vector3.up*.4f,Quaternion.identity,Vector3.one);History();Same(extrusionBefore,"Shift extrude Undo");
   stage="render cache";Pair(StudioModelEditor.ComponentMode.Vertex,0);Render();int uploads=editor.VertexMarkerSelectionUploads;int frames=editor.HandleFrameBuilds;for(int i=0;i<3;i++){Call("MultiComponentCenter",new List<int>{0,1});Render();}Check(editor.VertexMarkerSelectionUploads==uploads,"Unchanged redraw uploaded marker selection again");Check(editor.HandleFrameBuilds<=frames+2,"Per-target handle cache rebuilt each redraw");
   Check(errors==0,"Unexpected Unity errors: "+errors);Finish("PASS "+checks+" multi-object acquisition, mixed selection, transforms, deletion, previews, history and rendered-cache checks");
  }
  catch(Exception e){Finish(e.ToString());}
 }
 static void ValidateLive(string name,StudioModelEditor.ComponentMode mode)
 {
  stage="multi "+name;Pair(mode,name=="Merge"?new[]{0,1}:new[]{0});Reset();var before=Vertices();var kind=Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",BindingFlags.NonPublic),name);
  Set("extrusionDistance",.15f);ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.1f);ToolChannelPane.SetFloatDefault("Bevel Edges","Subdivisions",1);
  Call("EvaluateLiveTool",kind);for(int i=0;i<2;i++)Check(!doc.nodes[i].mesh.vertices.SequenceEqual(before[i]),name+" preview missed "+i);
  Call("CancelLiveTool",false);Same(before,name+" cancel");Check(Selection(0).Contains(0)&&Selection(1).Contains(0),name+" cancel lost source selection");
  Call("EvaluateLiveTool",kind);Call("CommitLiveTool",false);Check(StudioUndo.RetainedActions==1,name+" made multiple history entries");History();Same(before,name+" Undo");Check(Selection(0).Contains(0)&&Selection(1).Contains(0),name+" Undo lost selections");History(true);for(int i=0;i<2;i++)Check(!doc.nodes[i].mesh.vertices.SequenceEqual(before[i]),name+" Redo missed "+i);History();
 }
 static void Render()
 {
  var rt=new RenderTexture(640,400,24);rt.Create();var previous=RenderTexture.active;var target=view.camera.targetTexture;var rect=view.camera.pixelRect;GL.PushMatrix();
  try{view.camera.targetTexture=rt;view.camera.pixelRect=new Rect(0,0,640,400);Handles.SetCamera(new Rect(0,0,640,400),view.camera);RenderTexture.active=rt;GL.Clear(true,true,Color.black);GL.LoadProjectionMatrix(view.camera.projectionMatrix);GL.modelview=view.camera.worldToCameraMatrix;Call("DrawComponents",view);var image=new Texture2D(640,400,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,640,400),0,0);image.Apply();var pixels=image.GetPixels32();for(int half=0;half<2;half++){int marks=0;for(int y=0;y<400;y++)for(int x=half*320;x<(half+1)*320;x++){var c=pixels[y*640+x];if(c.r>100&&c.r>c.g*1.3f)marks++;}Check(marks>5,"Missing rendered vertex markers in half "+half);}File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../multi-component-render.png")),image.EncodeToPNG());Object.DestroyImmediate(image);}finally{GL.PopMatrix();view.camera.targetTexture=target;view.camera.pixelRect=rect;RenderTexture.active=previous;Object.DestroyImmediate(rt);}
 }
 static void Finish(string result){SceneView.duringSceneGui-=Draw;File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../multi-component-result.txt")),result);EditorApplication.delayCall+=()=>{if(view)view.Close();editor?.Dispose();if(doc){foreach(var n in doc.nodes)if(n.mesh)Object.DestroyImmediate(n.mesh);Object.DestroyImmediate(doc);}EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};}
}

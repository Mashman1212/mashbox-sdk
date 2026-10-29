#if MASHBOX_STUDIO_TESTS
using System; using System.IO; using System.Linq; using System.Reflection; using System.Collections.Generic;
using UnityEditor; using UnityEngine; using MashBoxSDK.AnimationStudio;
using Undo=MashBoxSDK.AnimationStudio.StudioUndo;
public static class EmbeddedToolValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic; static AnimationStudioWindow host; static MashBoxStudioViewport view; static StudioModelEditor editor; static StudioModelDocument doc;
 static Queue<Action> steps=new Queue<Action>(); static double ready; static int checks; static ToolChannelPane pane; static int originalCount; static List<string> errors=new List<string>();
 static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
 static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
 static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethod(name,F).Invoke(editor,args);
 static ToolChannelPane Pane(string field)=>(ToolChannelPane)typeof(StudioModelEditor).GetField(field,F).GetValue(editor);
 static void Select(StudioModelEditor.ComponentMode mode){editor.SelectObject(0);editor.SetToolbarComponentMode(mode);editor.Components.Clear();editor.Components.Add(0);}
 static void Open(string method,string field,string title){Call(method);pane=Pane(field);Check(pane&&pane.IsEmbedded,"Not embedded: "+title);Check(editor.ToolOptionsTitle==title,"Wrong panel title: "+editor.ToolOptionsTitle);Check(typeof(EditorWindow).GetField("m_Parent",F).GetValue(pane)==null,"Native popup created");Check(Resources.FindObjectsOfTypeAll<ToolChannelPane>().Length==1,"Multiple controllers alive");pane.Focus();Check(typeof(EditorWindow).GetField("m_Parent",F).GetValue(pane)==null,"Focusing created a popup");view.Repaint();}
 public static void Run(){EditorApplication.delayCall+=Start;}
 static void Start(){try{
  if(!Application.dataPath.Contains("StudioControlsValidation"))throw new Exception("Disposable project required");
  Application.logMessageReceived+=(m,s,t)=>{if(t==LogType.Exception||(t==LogType.Error&&m.Contains("GUILayout")))errors.Add(m);};
  doc=ScriptableObject.CreateInstance<StudioModelDocument>();
  for(int i=0;i<2;i++){var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polys);doc.nodes.Add(new StudioModelDocument.Node{name="First selected "+i,position=Vector3.right*i*2,mesh=mesh,cutPolygons=polys,cutTriangles=mesh.triangles,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial}});}
  host=ScriptableObject.CreateInstance<AnimationStudioWindow>();typeof(AnimationStudioWindow).GetField("modelingMode",F).SetValue(host,true);typeof(AnimationStudioWindow).GetMethod("OpenViewport",F).Invoke(host,null);typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});
  editor=host.ToolbarModelEditor;view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);view.position=new Rect(40,40,1100,760);view.LookAtDirect(Vector3.zero,Quaternion.Euler(20,30,0),4);originalCount=doc.nodes[0].mesh.vertexCount;
  ToolChannelPane.SetFloatDefault("Extrude","Distance",.1f);
  steps.Enqueue(()=>{Select(StudioModelEditor.ComponentMode.Face);Open("ExtrudeSelected","extrudeToolPane","Extrude");});
  steps.Enqueue(()=>{Capture("embedded-extrude.png");Check(doc.nodes[0].mesh.vertexCount>originalCount,"Extrude preview missing");view.SendEvent(new Event{type=EventType.KeyDown,keyCode=KeyCode.Escape});Check(!Pane("extrudeToolPane"),"Escape did not close embedded tool");Check(doc.nodes[0].mesh.vertexCount==originalCount,"Cancel failed to restore source");});
  steps.Enqueue(()=>{Open("ExtrudeSelected","extrudeToolPane","Extrude");});
  steps.Enqueue(()=>{Check(pane.ExecuteCommand(false),"Apply failed");Check(pane&&pane.IsEmbedded,"Apply closed controller");Check(doc.nodes[0].mesh.vertexCount>originalCount,"Apply did not commit");Call("PerformWorkspaceHistory",false);});
  steps.Enqueue(()=>{Check(doc.nodes[0].mesh.vertexCount==originalCount,"Native Undo lost source");if(pane)pane.Close();Select(StudioModelEditor.ComponentMode.Edge);ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.1f);Open("OpenBevelTool","bevelToolPane","Bevel");});
  steps.Enqueue(()=>{Capture("embedded-bevel.png");editor.ActivateTool(UnityEditor.Tool.Move);Check(!Pane("bevelToolPane"),"Transform did not leave Bevel");});
  steps.Enqueue(()=>{Select(StudioModelEditor.ComponentMode.Edge);Open("OpenSoftenTool","softenToolPane","Soften Edges");});
  steps.Enqueue(()=>{Capture("embedded-soften.png");pane.Close();Select(StudioModelEditor.ComponentMode.Edge);Open("OpenEdgeFlowTool","edgeFlowToolPane","Edit Edge Flow");});
  steps.Enqueue(()=>{Capture("embedded-flow.png");pane.Close();editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Object);editor.SelectObject(0);editor.SelectObject(1,true);Open("OpenCombineTool","combineToolPane","Combine Meshes");});
  steps.Enqueue(()=>{Capture("embedded-combine.png");Check(pane.ExecuteCommand(false),"Combine Apply failed");Check(pane&&doc.nodes.Count==1,"Combine Apply controller/data mismatch");editor.ActivateTool(UnityEditor.Tool.Move);Check(!Pane("combineToolPane"),"Combine persisted after changing tool");Check(editor.ToolOptionsTitle=="Move Tool","Transform options not restored");});
  steps.Enqueue(()=>{Select(StudioModelEditor.ComponentMode.Face);Open("ExtrudeSelected","extrudeToolPane","Extrude");});
  steps.Enqueue(()=>{Check(pane.ExecuteCommand(true),"Execute failed");Check(!Pane("extrudeToolPane"),"Execute did not close embedded controller");Check(errors.Count==0,"Native exceptions: "+string.Join(";",errors));});
  steps.Enqueue(()=>{Select(StudioModelEditor.ComponentMode.Face);Open("ExtrudeSelected","extrudeToolPane","Extrude");view.Close();view=null;Check(!pane&&Resources.FindObjectsOfTypeAll<ToolChannelPane>().Length==0,"Viewport shutdown retained embedded controller");});
  EditorApplication.update+=Tick;ready=EditorApplication.timeSinceStartup+1;
 }catch(Exception e){Finish(e.ToString());}}
 static void Capture(string name){var parent=typeof(EditorWindow).GetField("m_Parent",F).GetValue(view);var rect=new Rect(Vector2.zero,view.position.size);var rt=new RenderTexture((int)rect.width,(int)rect.height,0);rt.Create();var old=RenderTexture.active;try{parent.GetType().GetMethod("GrabPixels",F).Invoke(parent,new object[]{rt,rect});RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);image.ReadPixels(rect,0,0);image.Apply();File.WriteAllBytes(Out(name),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);checks++;}finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(rt);}}
 static void Tick(){if(EditorApplication.timeSinceStartup<ready)return;try{if(steps.Count==0){Finish("PASS "+checks+" embedded tool rendering, no-popup hosting, lifecycle, preview cancellation, Apply/Execute, Undo and tool switching checks");return;}UnityEditor.Undo.FlushUndoRecordObjects();steps.Dequeue()();UnityEditor.Undo.FlushUndoRecordObjects();ready=EditorApplication.timeSinceStartup+.8;}catch(Exception e){Finish(e.ToString());}}
 static void Finish(string result){EditorApplication.update-=Tick;File.WriteAllText(Out("embedded-tools-result.txt"),result);EditorApplication.delayCall+=()=>{if(editor!=null)editor.CloseToolOptions();if(view)view.Close();if(host)UnityEngine.Object.DestroyImmediate(host);EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};}
}
#endif


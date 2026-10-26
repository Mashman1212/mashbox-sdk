#if MASHBOX_STUDIO_TESTS
using System;using System.IO;using System.Reflection;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;using Object=UnityEngine.Object;
public static class StudioModalPaneValidation {
 static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;static int checks;
 static object Get(object obj,string name)=>obj.GetType().GetField(name,F).GetValue(obj);
 static void Call(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,F).Invoke(obj,args);
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
 static ToolChannelPane Pane(StudioModelEditor editor)=>(ToolChannelPane)Get(editor,"bevelToolPane");
 public static void Run(){var doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);string result=Path.Combine(Application.dataPath,"../modal-pane-result.txt");try{
 doc.nodes.Add(new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles});using(var editor=new StudioModelEditor(()=>{})){
 editor.Load(doc);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);editor.Components.Add(0);ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.2f);ToolChannelPane.SetFloatDefault("Bevel Edges","Subdivisions",0);
 Call(editor,"OpenBevelTool");Call(editor,"UpdateQueuedLiveTool");var pane=Pane(editor);Check(pane&&Get(editor,"livePreview")!=null,"Opening previews selected edge");
 Check(pane.ExecuteCommand(false),"Apply succeeds");Check(pane&&Pane(editor)==pane,"Apply retains the pane");Check(Get(editor,"livePreview")==null&&editor.Components.Count==0,"Apply finishes transaction and clears edges");
 editor.Components.Add(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);Check(((Func<bool>)Get(pane,"canExecute"))(),"New edge enables existing pane immediately");
 Call(editor,"OpenBevelTool");Check(Pane(editor)==pane&&(bool)Get(editor,"previewQueued"),"Repeat Bevel queues preview in the existing pane");Call(editor,"UpdateQueuedLiveTool");Check(Get(editor,"livePreview")!=null,"Repeat preview starts without touching width");
 Check(pane.ExecuteCommand(true),"Execute succeeds");Check(!pane&&!Pane(editor)&&Get(editor,"livePreview")==null,"Execute closes pane and finishes transaction");
 editor.Components.Add(0);Call(editor,"OpenBevelTool");Call(editor,"UpdateQueuedLiveTool");pane=Pane(editor);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Object);Check(!pane&&!Pane(editor),"Object mode closes active tool pane");Check(editor.ToolbarComponentMode==StudioModelEditor.ComponentMode.Object&&Get(editor,"livePreview")==null,"Object mode keeps completed geometry without preview");
 editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);editor.Components.Add(0);Call(editor,"OpenBevelTool");Call(editor,"UpdateQueuedLiveTool");pane=Pane(editor);Call(editor,"FinishLiveTool");Check(!pane&&!Pane(editor),"Implicit completion closes pane");
 editor.Components.Add(0);Call(editor,"OpenBevelTool");Call(editor,"UpdateQueuedLiveTool");pane=Pane(editor);Check(pane.ExecuteCommand(false),"Second Apply succeeds");editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Object);Check(!pane&&!Pane(editor),"Object mode closes an applied idle pane too"); editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);editor.Components.Add(0);ToolChannelPane.SetFloatDefault("Bevel Edges","Width",-1);Call(editor,"OpenBevelTool");Call(editor,"UpdateQueuedLiveTool");pane=Pane(editor);Check(Get(editor,"livePreview")!=null&&!(bool)Get(editor,"liveValid"),"Invalid preview remains cancellable");editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Object);Check(!pane&&Get(editor,"livePreview")==null&&editor.ToolbarComponentMode==StudioModelEditor.ComponentMode.Object,"Object mode cancels invalid preview and closes pane");ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.2f);
 }
 File.WriteAllText(result,"PASS "+checks+" modal pane lifecycle assertions");EditorApplication.Exit(0);
 }catch(Exception e){File.WriteAllText(result,"FAIL "+e);EditorApplication.Exit(1);}finally{StudioUndo.ClearUndo(doc);StudioUndo.ClearUndo(mesh);Object.DestroyImmediate(mesh);Object.DestroyImmediate(doc);}}
}
#endif

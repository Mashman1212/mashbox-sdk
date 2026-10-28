#if MASHBOX_STUDIO_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Undo=MashBoxSDK.AnimationStudio.StudioUndo;

public static class StudioRepeatValidation
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    static AnimationStudioWindow host; static MashBoxStudioViewport view; static StudioModelEditor editor;static StudioModelDocument doc;
    static int checks,history;static string stage;static Queue<(string,Action)> steps=new Queue<(string,Action)>();static double next;
    static object remembered;static Vector3[] beveled;static ToolChannelPane pane;
    static object Get(string name)=>typeof(StudioModelEditor).GetField(name,F).GetValue(editor);
    static void Set(string name,object value)=>typeof(StudioModelEditor).GetField(name,F).SetValue(editor,value);
    static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethods(F).Single(m=>m.Name==name && m.GetParameters().Length==args.Length && m.GetParameters().Select((p,i)=>args[i]==null || p.ParameterType.IsInstanceOfType(args[i])).All(v=>v)).Invoke(editor,args);
    static void Check(bool value,string message){if(!value)throw new Exception(stage+": "+message);checks++;}
    static object Live(string name)=>Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",BindingFlags.NonPublic),name);
    static void Action(Action action,bool finish=true)=>Call("Run",action,finish);
    static void Key(KeyCode key=KeyCode.G,EventModifiers modifiers=EventModifiers.None)
    {var evt=new Event{type=EventType.KeyDown,keyCode=key,modifiers=modifiers};Check(editor.TryExecuteModelCommand(evt,view),"Repeat shortcut not dispatched");Check(evt.type==EventType.Used,"Repeat key not consumed");}
    static void Ready()=>Check(!editor.StatusText.Contains("unavailable")&&!editor.StatusText.Contains("failed")&&!editor.StatusText.Contains("Select "),"Action failed: "+editor.StatusText);
    static void Setup(int count=4)
    {
        doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.SetWorkspacePath(Path.Combine(StudioModelDocument.WorkspaceFolder,"repeat-"+Guid.NewGuid().ToString("N")+".workspace"));
        for(int i=0;i<count;i++){var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polys);doc.nodes.Add(new StudioModelDocument.Node{name="Cube "+i,mesh=mesh,position=Vector3.right*(i*2),cutPolygons=polys,cutTriangles=mesh.triangles,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial}});}
        typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});editor=host.ToolbarModelEditor;editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Object);editor.RefreshModelCommandCatalog();
        Check(Get("lastRepeatAction")==null,"Workspace switch kept stale repeat state");
    }
    static void Face(int node)
    {
        editor.SelectObject(node);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);
        var faces=(List<StudioMeshCuts.Face>)Call("Faces");var points=editor.ActiveMesh.vertices;
        editor.Components.Add(faces.FindIndex(f=>f.Vertices.All(v=>points[v].y>.49f)));Call("EstablishSelectionBaseline");
    }
    static void Edges(int node)
    {
        editor.SelectObject(node);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);var edges=(List<(int,int)>)Call("Edges");for(int i=0;i<edges.Count;i++)editor.Components.Add(i);Call("EstablishSelectionBaseline");
    }
    public static void Run()=>EditorApplication.delayCall+=Start;
    static void Start()
    {
        try
        {
            if(!Application.dataPath.Contains("StudioControlsValidation"))throw new Exception("Disposable project required");
            StudioSettings.Current.undoLimit=200;StudioShortcuts.Reset();AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(host,new object[]{true});view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);
            void Step(string label,Action action)=>steps.Enqueue((label,action));
            Step("Fresh workspace",()=>{Setup();Check(StudioShortcuts.Bindings(StudioModelEditor.RepeatActionShortcut).Single().key==KeyCode.G,"G factory default missing");history=Undo.RetainedActions;Key();Check(Undo.RetainedActions==history&&doc.nodes.Count==4,"Empty repeat edited geometry/history");});
            Step("Duplicate action",()=>{Action(editor.DuplicateSelectedObjects);Check(doc.nodes.Count==5,"Duplicate failed");remembered=Get("lastRepeatAction");Check(remembered!=null,"Duplicate not remembered");});
            Step("Selection and transforms ignored",()=>{editor.SelectObject(1);editor.ActivateTool(Tool.Move);editor.ActivateTool(Tool.Rotate);editor.ActivateTool(Tool.Scale);editor.SetModelDrawMode(view,DrawCameraMode.Textured);Check(ReferenceEquals(Get("lastRepeatAction"),remembered),"Selection/view/tool mode replaced action");history=Undo.RetainedActions;Key();Check(doc.nodes.Count==6,"G did not duplicate current selection");Check(Undo.RetainedActions==history+1,"Repeated duplicate not one history action");});
            Step("Undo repeated duplicate",()=>Undo.PerformUndo());
            Step("Undo retains repeat",()=>{Check(doc.nodes.Count==5&&ReferenceEquals(Get("lastRepeatAction"),remembered),"Undo changed repeat state");Key();Check(doc.nodes.Count==6,"Repeat after Undo failed");});
            Step("Extrude setup",()=>{Setup();Face(0);Set("extrusionDistance",.2f);Set("extrusionInset",0f);Set("extrudeFacesTogether",true);Action(()=>Call("ApplyLiveTool",Live("Extrude")));Check(Mathf.Abs(editor.ActiveMesh.bounds.max.y-.7f)<1e-5,"First extrusion wrong");remembered=Get("lastRepeatAction");});
            Step("Repeat saved extrusion settings",()=>{Face(1);Set("extrusionDistance",1.3f);history=Undo.RetainedActions;Key();Ready();Check(Mathf.Abs(editor.ActiveMesh.bounds.max.y-.7f)<1e-5,"G used edited fields instead of last completed distance");Check((float)Get("extrusionDistance")==1.3f,"Repeat rewrote current tool defaults");Check(Undo.RetainedActions==history+1,"Repeated extrusion not one history action");});
            Step("Repeat again",()=>{history=Undo.RetainedActions;Key();Check(Mathf.Abs(editor.ActiveMesh.bounds.max.y-.9f)<1e-5,"Repeated cap extrusion did not extend again");Check(Undo.RetainedActions==history+1,"Second extrusion not one action");});
            Step("Undo repeat extrusion",()=>Undo.PerformUndo());Step("Check Undo extrusion",()=>Check(Mathf.Abs(editor.ActiveMesh.bounds.max.y-.7f)<1e-5,"Undo did not restore repeated extrusion"));
            Step("Redo repeat extrusion",()=>Undo.PerformRedo());Step("Check Redo extrusion",()=>Check(Mathf.Abs(editor.ActiveMesh.bounds.max.y-.9f)<1e-5,"Redo did not restore repeated extrusion"));
            Step("No-op does not replace",()=>{Face(2);Set("extrusionDistance",0f);Set("extrusionInset",0f);Action(()=>Call("ApplyLiveTool",Live("Extrude")));Check(ReferenceEquals(Get("lastRepeatAction"),remembered),"Zero extrusion replaced last successful action");});
            Step("Cancelled preview does not replace",()=>{Face(2);Set("extrusionDistance",.1f);Call("EvaluateLiveTool",Live("Extrude"));Call("CancelLiveTool",false);Check(ReferenceEquals(Get("lastRepeatAction"),remembered),"Cancelled preview became last action");});
            Step("G commits preview once",()=>{Face(2);Set("extrusionDistance",.15f);Call("EvaluateLiveTool",Live("Extrude"));Key();Check(Mathf.Abs(editor.ActiveMesh.bounds.max.y-.65f)<1e-5,"G doubled a pending preview");Check(Get("livePreview")==null,"G left pending preview");});
            Step("Bevel setup",()=>{Setup();Edges(0);ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.08f);ToolChannelPane.SetFloatDefault("Bevel Edges","Subdivisions",0);ToolChannelPane.SetFloatDefault("Bevel Edges","EdgeFlow",0);Action(()=>Call("ApplyLiveTool",Live("Bevel")));beveled=editor.ActiveMesh.vertices;Check(beveled.Length>24,"Bevel failed");});
            Step("Bevel repeats without pane",()=>{Edges(1);ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.2f);Key();Ready();Check(editor.ActiveMesh.vertices.Length==beveled.Length&&editor.ActiveMesh.vertices.Zip(beveled,Vector3.Distance).All(d=>d<1e-5),"G did not reproduce completed bevel settings");Check(ToolChannelPane.GetFloatDefault("Bevel Edges","Width",0)==.2f,"G rewrote stored bevel width");Check(!Resources.FindObjectsOfTypeAll<ToolChannelPane>().Any(),"Repeat opened an unnecessary pane");remembered=Get("lastRepeatAction");});
            Step("Unavailable action safe",()=>{history=Undo.RetainedActions;Key();Check(Undo.RetainedActions==history,"Unavailable bevel created history");Check(ReferenceEquals(remembered,Get("lastRepeatAction")),"Failed repeat replaced last action");});
            Step("Combine setup",()=>{Setup();editor.SelectObject(1,true);ToolChannelPane.SetFloatDefault("Combine Meshes","CombineUVs",1);ToolChannelPane.SetFloatDefault("Combine Meshes","Pivot",1);ToolChannelPane.SetFloatDefault("Combine Meshes","KeepFirstName",1);Call("OpenCombineTool");pane=(ToolChannelPane)Get("combineToolPane");Check(Get("lastRepeatAction")==null,"Opening pane remembered uncompleted combine");Check(pane.ExecuteCommand(true),"Combine pane execution failed");Check(doc.nodes.Count==3,"Combine failed");});
            Step("Repeat combine saved options",()=>{editor.SelectObject(0);editor.SelectObject(1,true);ToolChannelPane.SetFloatDefault("Combine Meshes","CombineUVs",0);ToolChannelPane.SetFloatDefault("Combine Meshes","Pivot",0);ToolChannelPane.SetFloatDefault("Combine Meshes","KeepFirstName",0);history=Undo.RetainedActions;Key();Ready();Check(doc.nodes.Count==2&&doc.nodes.Last().name=="Cube 2","G lost combine name option");Check(Mathf.Abs(editor.Transforms[editor.Active].position.x-5)<1e-5,"G lost combine average pivot");Check(!editor.ActiveMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1),"G lost combine UV option");Check(Undo.RetainedActions==history+1,"Repeated combine added extra selection history");});
            Step("Split setup",()=>{Setup();Face(0);Action(()=>Call("SplitSelectedComponents"));Check(doc.nodes.Count==5,"Split failed");remembered=Get("lastRepeatAction");});
            Step("Repeat split on current mesh",()=>{Face(1);Key();Ready();Check(doc.nodes.Count==6,"G did not split current source");Check(doc.nodes[1].mesh.triangles.Length==30,"Repeated split did not remove source face");});
            Step("Failed action retains repeat",()=>{Face(2);editor.Components.Clear();for(int i=0;i<6;i++)editor.Components.Add(i);Action(()=>Call("SplitSelectedComponents"));Check(editor.StatusText.Contains("empty mesh"),"Expected all-face rejection");Check(ReferenceEquals(Get("lastRepeatAction"),remembered),"Failed split replaced successful repeat record");});
            Step("Pane deferred setup",()=>{Setup();Face(0);Set("extrusionDistance",.1f);Call("ExtrudeSelected");pane=(ToolChannelPane)Get("extrudeToolPane");});
            Step("Pane text field guard",()=>{EditorGUIUtility.editingTextField=true;var evt=new Event{type=EventType.KeyDown,keyCode=KeyCode.G};bool handled=(bool)typeof(ToolChannelPane).GetMethod("HandlePaneRepeat",F).Invoke(pane,new object[]{evt});Check(!handled,"Pane stole G from text editing");EditorGUIUtility.editingTextField=false;});
            Step("Pane deferred G",()=>{var evt=new Event{type=EventType.KeyDown,keyCode=KeyCode.G};bool handled=(bool)typeof(ToolChannelPane).GetMethod("HandlePaneRepeat",F).Invoke(pane,new object[]{evt});Check(handled&&evt.type==EventType.Used,"Pane did not handle G");Check((bool)typeof(ToolChannelPane).GetField("repeatQueued",F).GetValue(pane),"Pane executed inside drawing instead of deferring");});
            Step("Pane repeat completion",()=>{Check(Get("livePreview")==null&&Get("lastRepeatAction")!=null,"Deferred pane G did not commit");Check(Mathf.Abs(editor.ActiveMesh.bounds.max.y-.6f)<1e-5,"Deferred G doubled preview");});
            Step("Shortcut overrides",Shortcuts);
            Step("Document reset",()=>{Setup();Check(Get("lastRepeatAction")==null,"New workspace retains repeat");});
            next=EditorApplication.timeSinceStartup+.5;EditorApplication.update+=Pump;
        }
        catch(Exception e){Finish(stage+"\n"+e);}
    }
    static void Shortcuts()
    {
        var id=StudioModelEditor.RepeatActionShortcut;Check(StudioModelCommands.Category(id)=="Edit","Repeat absent from menu shortcut registry");
        StudioShortcuts.Set(StudioShortcut.Move,new[]{new StudioShortcuts.Binding(KeyCode.G)});Check(StudioShortcuts.Bindings(id).Length==0,"Factory G stole custom Move assignment");
        StudioShortcuts.Reset();StudioShortcuts.Set(id,new[]{new StudioShortcuts.Binding(KeyCode.H)});StudioShortcuts.Reload();Check(StudioShortcuts.Bindings(id).Single().key==KeyCode.H,"Custom repeat binding did not reload");
        StudioShortcuts.Set(id,Array.Empty<StudioShortcuts.Binding>());StudioShortcuts.Reload();Check(StudioShortcuts.Bindings(id).Length==0,"Explicit repeat unassignment lost");StudioShortcuts.Reset();
        Check(StudioShortcuts.FactoryJson().Contains("Edit/Repeat Last Action"),"Factory export omits G before menu discovery");
    }
    static void Pump()
    {
        if(EditorApplication.timeSinceStartup<next)return;
        try{if(steps.Count==0){Finish("PASS "+checks+" repeat current selection/saved settings, native history, preview/pane lifecycle, failure safety and shortcut conflict/reload checks");return;}UnityEditor.Undo.FlushUndoRecordObjects();var step=steps.Dequeue();stage=step.Item1;step.Item2();UnityEditor.Undo.FlushUndoRecordObjects();next=EditorApplication.timeSinceStartup+.25;}
        catch(Exception e){Finish(stage+"\n"+e);}
    }
    static void Finish(string result)
    {
        EditorApplication.update-=Pump;File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../repeat-result.txt")),result);EditorApplication.delayCall+=()=>{if(view)view.Close();if(host)UnityEngine.Object.DestroyImmediate(host);EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};
    }
}
#endif

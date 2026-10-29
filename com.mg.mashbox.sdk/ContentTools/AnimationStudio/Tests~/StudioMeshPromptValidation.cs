#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Undo=MashBoxSDK.AnimationStudio.StudioUndo;
using Object=UnityEngine.Object;
public static class StudioMeshPromptValidation
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    static AnimationStudioWindow host;static MashBoxStudioViewport view;static StudioModelEditor editor;static StudioModelDocument doc;
    static int checks,notices;static string message,stage;static double next;static Queue<(string,Action)> steps=new Queue<(string,Action)>();static int[] triangles;static Vector3[] vertices;
    static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethod(name,F).Invoke(editor,args);
    static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
    static void Check(bool yes,string reason){if(!yes)throw new Exception(stage+": "+reason);checks++;}
    static StudioMenu.Item Command(string category,string label)
    {
        var menu=new StudioMenu(category,true);Call("PopulateModelMenu",category,menu,view,null);return menu.Items.Single(i=>i.Text==label);
    }
    static void Reset()
    {
        editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Object);editor.SelectObject(0);editor.ActivateTool(Tool.Move);notices=0;message=null;Call("EstablishSelectionBaseline");
    }
    static void Unchanged(){Check(doc.nodes.Count==1,"Prompt created an object");Check(doc.nodes[0].mesh.vertices.SequenceEqual(vertices)&&doc.nodes[0].mesh.triangles.SequenceEqual(triangles),"Prompt changed geometry");}
    public static void Run()=>EditorApplication.delayCall+=Start;
    static void Start()
    {
        try
        {
            Check(Application.dataPath.Contains("SelectionUndoProject"),"Disposable project required");
            doc=ScriptableObject.CreateInstance<StudioModelDocument>();Directory.CreateDirectory(StudioModelDocument.WorkspaceFolder);doc.SetWorkspacePath(Path.Combine(StudioModelDocument.WorkspaceFolder,"mesh-prompts-"+Guid.NewGuid().ToString("N")+".workspace"));
            var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);doc.nodes.Add(new StudioModelDocument.Node{name="Cube",mesh=mesh,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial},cutPolygons=polygons,cutTriangles=mesh.triangles});vertices=mesh.vertices;triangles=mesh.triangles;
            AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(host,new object[]{true});typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});editor=host.ToolbarModelEditor;view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);
            editor.StatusMessage+=m=>{message=m;notices++;};editor.SetPivotLocked(false);
            void Step(string n,Action a)=>steps.Enqueue((n,a));
            var cases=new[]{("Mesh Tools","Bridge Edges",StudioModelEditor.ComponentMode.Edge,"edge"),("Mesh Tools","Split Selection",StudioModelEditor.ComponentMode.Face,"split"),("Mesh Tools","Bevel\u2026",StudioModelEditor.ComponentMode.Edge,"edges"),("Mesh Tools","Extrude",StudioModelEditor.ComponentMode.Face,"extrude"),("Mesh Tools","Merge to Center",StudioModelEditor.ComponentMode.Vertex,"vertices"),("Edit Mesh","Edit Edge Flow\u2026",StudioModelEditor.ComponentMode.Edge,"edges"),("Mesh Tools","Combine Meshes\u2026",StudioModelEditor.ComponentMode.Object,"two")};
            foreach(var c in cases)Step(c.Item2,()=>{Reset();var command=Command(c.Item1,c.Item2);Check(command.Enabled&&command.Action!=null,"Command is locked out");command.Action();Check(editor.ToolbarComponentMode==c.Item3,"Wrong selection mode");Check(editor.ToolbarToolSelected(Tool.None),"Transform tool was not cleared");Check(notices>0&&message.IndexOf(c.Item4,StringComparison.OrdinalIgnoreCase)>=0,"Missing specific notification: "+message);Unchanged();Check(c.Item2=="Bridge Edges"||c.Item2=="Split Selection" ? Resources.FindObjectsOfTypeAll<ToolChannelPane>().Any(p=>p.titleContent.text==c.Item2) : !Resources.FindObjectsOfTypeAll<ToolChannelPane>().Any(),"Incorrect ready Tool Options session");});
            foreach(var kind in new[]{StudioModelEditor.ComponentMode.Vertex,StudioModelEditor.ComponentMode.Edge,StudioModelEditor.ComponentMode.Face})Step("Split preserves "+kind,()=>{Reset();editor.SetToolbarComponentMode(kind);notices=0;Command("Mesh Tools","Split Selection").Action();Check(editor.ToolbarComponentMode==kind,"Split replaced valid component mode");Check(notices>0,"Empty split silently returned");Unchanged();});
            Step("shortcut",()=>{Reset();var evt=new Event{type=EventType.KeyDown,keyCode=KeyCode.B,control=true};Check(editor.TryExecuteModelCommand(evt,view),"Bevel shortcut not dispatched");Check(editor.ToolbarComponentMode==StudioModelEditor.ComponentMode.Edge&&notices>0,"Shortcut bypassed preparation/notification");Unchanged();});
            foreach(var label in new[]{"Insert Edge Loop","Multi-Cut","Target Weld"})Step(label+" setup",()=>{Reset();var cmd=Command("Mesh Tools",label);Check(cmd.Enabled,"Interactive tool disabled");cmd.Action();Check(editor.ToolbarComponentMode==(label=="Target Weld"?StudioModelEditor.ComponentMode.Vertex:StudioModelEditor.ComponentMode.Edge),"Wrong interactive component mode");Check(editor.ToolbarTransformState==0,"Interactive tool not activated");Unchanged();editor.ActivateTool(Tool.None);});
            Step("normal menu entries",()=>{Reset();foreach(var label in new[]{"Soften","Harden","Soften\u2026"})Check(Command("Mesh Display",label).Enabled,"Normal action disabled");});
            Step("valid split",()=>{Reset();editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);editor.Components.Add(0);Call("EstablishSelectionBaseline");int history=Undo.RetainedActions;Command("Mesh Tools","Split Selection").Action();Check(doc.nodes.Count==2,"Valid split did not execute");Check(Undo.RetainedActions==history+1,"Split is not one history action");Check(doc.nodes.All(n=>n.mesh.triangles.Length>0),"Split left empty mesh");});
            Step("Undo split",()=>Undo.PerformUndo());Step("verify Undo split",()=>{Check(doc.nodes.Count==1,"Undo split failed");Check(editor.ToolbarComponentMode==StudioModelEditor.ComponentMode.Face&&editor.Components.Contains(0),"Undo selection changed");Unchanged();});
            Step("no workspace",()=>{editor.Load(null);var command=Command("Mesh Tools","Bridge Edges");Check(command.Enabled,"Missing workspace locks menu");notices=0;command.Action();Check(notices>0&&editor.ToolbarComponentMode==StudioModelEditor.ComponentMode.Edge,"Missing workspace not guided");});
            next=EditorApplication.timeSinceStartup+.3;EditorApplication.update+=Pump;
        }catch(Exception e){Finish(e.ToString());}
    }
    static void Pump(){if(EditorApplication.timeSinceStartup<next)return;try{if(steps.Count==0){Finish("PASS "+checks+" enabled menu/shortcut commands, notification callbacks, automatic selection/tool modes, preserved compatible modes, unchanged invalid geometry, valid Split and native Undo checks");return;}UnityEditor.Undo.FlushUndoRecordObjects();var step=steps.Dequeue();stage=step.Item1;step.Item2();UnityEditor.Undo.FlushUndoRecordObjects();next=EditorApplication.timeSinceStartup+.25;}catch(Exception e){Finish(stage+"\n"+e);}}
    static void Finish(string result){EditorApplication.update-=Pump;File.WriteAllText(Out("mesh-prompts-result.txt"),result);EditorApplication.delayCall+=()=>{if(view)view.Close();if(host)Object.DestroyImmediate(host);EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};}
}
#endif

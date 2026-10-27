using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;

public static class StudioPivotUndoOrderValidation
{
    const BindingFlags Members=BindingFlags.Instance|BindingFlags.NonPublic;
    static AnimationStudioWindow host;
    static MashBoxStudioViewport view;
    static StudioModelEditor editor;
    static StudioModelDocument document;
    static int checks;
    static string stage;
    sealed class Snapshot
    {
        internal Vector3 pivot;internal Quaternion orientation;internal Vector3[] points;internal int[] triangles,selection;
    }
    static object Call(string name,params object[] args)
    {
        var method=typeof(StudioModelEditor).GetMethod(name,Members);var parameters=method.GetParameters();var supplied=new object[parameters.Length];
        for(int i=0;i<supplied.Length;i++)supplied[i]=i<args.Length?args[i]:parameters[i].DefaultValue;
        return method.Invoke(editor,supplied);
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(stage+": "+message);checks++;}
    static Snapshot Capture()=>new Snapshot{pivot=editor.EditingPivotPosition,orientation=editor.PivotOrientation,points=editor.ActiveMesh.vertices,triangles=editor.ActiveMesh.triangles,selection=editor.Components.OrderBy(i=>i).ToArray()};
    static void Matches(Snapshot state)
    {
        Check(Vector3.Distance(editor.EditingPivotPosition,state.pivot)<.0001f,"Pivot moved at the wrong history step");
        Check(Quaternion.Angle(editor.PivotOrientation,state.orientation)<.01f,"Pivot orientation restored out of order");
        var restored=editor.ActiveMesh.vertices; float maxError=restored.Length==state.points.Length?restored.Zip(state.points,Vector3.Distance).DefaultIfEmpty(0).Max():float.PositiveInfinity;
        Check(maxError<.00001f,"Vertex positions restored out of order; count="+restored.Length+" expected="+state.points.Length+" maxError="+maxError);
        Check(editor.ActiveMesh.triangles.SequenceEqual(state.triangles),"Extrusion topology restored out of order");
        Check(editor.Components.SetEquals(state.selection),"Component selection restored out of order");
        Check(editor.Transforms[0].GetComponent<MeshFilter>().sharedMesh==editor.ActiveMesh,"Renderer bound to stale mesh after history");
    }
    static void Action(Action action)=>Call("Run",action,true);
    static void Pivot(Vector3 point,Quaternion orientation)=>Action(()=>Call("SetComponentPivot",point,orientation,"Move component pivot"));
    static void Extrude(float angle)
    {
        var center=editor.EditingPivotPosition;var orientation=editor.PivotOrientation;
        Action(()=>Call("BeginHandleExtrusion",center,center,Quaternion.AngleAxis(angle,Vector3.forward)*orientation,orientation,Vector3.one));
    }
    public static void Run()=>EditorApplication.delayCall+=Start;
    static void Start()
    {
        try
        {
            document=ScriptableObject.CreateInstance<StudioModelDocument>();
            Directory.CreateDirectory(StudioModelDocument.WorkspaceFolder);
            document.SetWorkspacePath(Path.Combine(StudioModelDocument.WorkspaceFolder,"pivot-undo-"+Guid.NewGuid().ToString("N")+".workspace"));
            var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
            document.nodes.Add(new StudioModelDocument.Node{name="Cube",mesh=mesh,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial},cutPolygons=polygons,cutTriangles=mesh.triangles});
            AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",Members).Invoke(host,new object[]{true});
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",Members).Invoke(host,new object[]{document});
            editor=host.ToolbarModelEditor;view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);
            editor.SelectObject(0);Call("ChangeComponentMode",StudioModelEditor.ComponentMode.Face,null);editor.Components.Add(0);editor.ActivateTool(Tool.Rotate);Call("EstablishSelectionBaseline");
            EditorApplication.delayCall+=Validate;
        }
        catch(Exception error){Finish(error.ToString());}
    }
    static System.Collections.Generic.Queue<(string label,Action action)> plan;
    static double next;
    static Snapshot baseline,pivot1,extrusion1,pivot2,extrusion2;
    static void Validate()
    {
        plan=new System.Collections.Generic.Queue<(string,Action)>();
        void Step(string name,Action action)=>plan.Enqueue((name,action));
        Step("baseline",()=>baseline=Capture());
        Step("first pivot edit",()=>{Pivot(baseline.pivot+new Vector3(-.7f,.2f,.3f),Quaternion.Euler(5,10,15));pivot1=Capture();});
        Step("first Shift Rotate extrusion",()=>{Extrude(15);extrusion1=Capture();Check(Vector3.Distance(extrusion1.pivot,pivot1.pivot)<.0001f,"Extrusion lost the custom pivot");Check(extrusion1.triangles.Length>baseline.triangles.Length,"Extrusion did not publish topology");});
        Step("second pivot edit",()=>{Pivot(pivot1.pivot+new Vector3(.2f,-.3f,.15f),extrusion1.orientation);pivot2=Capture();});
        Step("second Shift Rotate extrusion",()=>{Extrude(-12);extrusion2=Capture();Check(Vector3.Distance(extrusion2.pivot,pivot2.pivot)<.0001f,"Repeated extrusion lost the custom pivot");Check(extrusion2.triangles.Length>extrusion1.triangles.Length,"Repeated extrusion did not publish topology");});
        Step("later unrelated edit",()=>Action(()=>{Undo.RegisterCompleteObjectUndo(document,"Rename model object");document.nodes[0].name="Renamed";editor.Changed(false);}));
        Step("Undo later edit",()=>Undo.PerformUndo());
        Step("Check Undo later edit",()=>Matches(extrusion2));
        Step("Undo second extrusion",()=>Undo.PerformUndo());
        Step("Check Undo second extrusion",()=>Matches(pivot2));
        Step("Undo second pivot move",()=>Undo.PerformUndo());
        Step("Check Undo second pivot move",()=>Matches(extrusion1));
        Step("Undo first extrusion",()=>Undo.PerformUndo());
        Step("Check Undo first extrusion",()=>Matches(pivot1));
        Step("Undo first pivot move",()=>Undo.PerformUndo());
        Step("Check Undo first pivot move",()=>Matches(baseline));
        Step("Redo first pivot move",()=>Undo.PerformRedo());
        Step("Check Redo first pivot move",()=>Matches(pivot1));
        Step("Redo first extrusion",()=>Undo.PerformRedo());
        Step("Check Redo first extrusion",()=>Matches(extrusion1));
        Step("Redo second pivot move",()=>Undo.PerformRedo());
        Step("Check Redo second pivot move",()=>Matches(pivot2));
        Step("Redo second extrusion",()=>Undo.PerformRedo());
        Step("Check Redo second extrusion",()=>Matches(extrusion2));
        Step("Redo later edit",()=>Undo.PerformRedo());
        Step("Check Redo later edit",()=>Matches(extrusion2));
        Step("selection invalidation",()=>{Action(()=>{int other=editor.Components.Contains(0)?1:0;editor.Components.Clear();editor.Components.Add(other);});Check(!(bool)typeof(StudioModelEditor).GetProperty("HasComponentPivot",Members).GetValue(editor),"A different selection kept an unrelated custom pivot");});
        Step("Undo selection change",()=>Undo.PerformUndo());
        Step("Check Undo selection change",()=>Matches(extrusion2));
        Step("pure context query",()=>
        {
            var pivot=(StudioComponentPivotState)typeof(StudioModelEditor).GetField("componentPivot",Members).GetValue(editor);
            var saved=editor.Components.ToArray();editor.Components.Clear();
            Check(!(bool)typeof(StudioModelEditor).GetProperty("HasComponentPivot",Members).GetValue(editor),"Mismatched context returned a pivot");
            Check(pivot.custom,"Context query destroyed stored Undo state");editor.Components.UnionWith(saved);Matches(extrusion2);
        });
        next=EditorApplication.timeSinceStartup+.2;EditorApplication.update+=Pump;
    }
    static void Pump()
    {
        if(EditorApplication.timeSinceStartup<next)return;
        try
        {
            if(plan.Count==0){Finish("PASS "+checks+" chronological pivot/extrusion Undo-Redo, unrelated edit, selection history, pure context query and renderer-binding checks");return;}
            UnityEditor.Undo.FlushUndoRecordObjects();var item=plan.Dequeue();stage=item.label;item.action();UnityEditor.Undo.FlushUndoRecordObjects();
            next=EditorApplication.timeSinceStartup+.2;
        }
        catch(Exception error){Finish(error.ToString());}
    }
    static void Finish(string result)
    {
        EditorApplication.update-=Pump;
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../pivot-undo-order-result.txt")),result);
        EditorApplication.delayCall+=()=>
        {
            if(view)view.Close();if(host)UnityEngine.Object.DestroyImmediate(host);
            double until=EditorApplication.timeSinceStartup+1;
            void Exit(){if(EditorApplication.timeSinceStartup<until)return;EditorApplication.update-=Exit;EditorApplication.Exit(result.StartsWith("PASS")?0:1);}
            EditorApplication.update+=Exit;
        };
    }
}

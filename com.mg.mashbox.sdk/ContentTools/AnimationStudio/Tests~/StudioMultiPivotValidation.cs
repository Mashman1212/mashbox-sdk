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
public static class StudioMultiPivotValidation
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    static AnimationStudioWindow host;static MashBoxStudioViewport view;static StudioModelEditor editor;static StudioModelDocument doc;
    static int checks;static string stage;static double next;static Queue<(string,Action)> steps=new Queue<(string,Action)>();
    static Vector3[] pivots;static Vector3[][] points;static Quaternion[] frames;static Mesh shared;static int history;static Vector3 delta=new Vector3(.37f,-.23f,.81f);
    static object Call(string name,params object[] args)
    {
        var m=typeof(StudioModelEditor).GetMethods(F).Single(x=>x.Name==name&&x.GetParameters().Length>=args.Length&&x.GetParameters().Skip(args.Length).All(p=>p.IsOptional));
        return m.Invoke(editor,m.GetParameters().Select((p,i)=>i<args.Length?args[i]:p.DefaultValue).ToArray());
    }
    static void Act(Action a)=>Call("Run",a,true);
    static void Check(bool yes,string msg){if(!yes)throw new Exception(stage+": "+msg);checks++;}
    static Vector3[] World(int i)=>doc.nodes[i].mesh.vertices.Select(editor.Transforms[i].TransformPoint).ToArray();
    static Quaternion Frame(int i)=>editor.Transforms[i].rotation*doc.nodes[i].pivotRotation;
    static void Capture(){pivots=editor.Transforms.Select(t=>t.position).ToArray();points=Enumerable.Range(0,doc.nodes.Count).Select(World).ToArray();frames=Enumerable.Range(0,doc.nodes.Count).Select(Frame).ToArray();}
    static void Verify(Vector3 shift,bool turned=false)
    {
        for(int i=0;i<doc.nodes.Count;i++)
        {
            bool selected=i<3;
            Check(Vector3.Distance(editor.Transforms[i].position,(selected && shift != Vector3.zero ? pivots[editor.Active]+shift : pivots[i]))<.0001f,"Wrong pivot translation "+i);
            Check(World(i).Zip(points[i],Vector3.Distance).Max()<.0001f,"Surface moved "+i);
            Check(editor.Transforms[i].GetComponent<MeshFilter>().sharedMesh==doc.nodes[i].mesh,"Stale renderer "+i);
            if(!turned)Check(Quaternion.Angle(Frame(i),frames[i])<.01f,"Unexpected orientation "+i);
        }
    }
    public static void Run()=>EditorApplication.delayCall+=Start;
    static void Start()
    {
        try
        {
            Check(Application.dataPath.Contains("SelectionUndoProject"),"Disposable project required");
            doc=ScriptableObject.CreateInstance<StudioModelDocument>();Directory.CreateDirectory(StudioModelDocument.WorkspaceFolder);
            doc.SetWorkspacePath(Path.Combine(StudioModelDocument.WorkspaceFolder,"multi-pivot-"+Guid.NewGuid().ToString("N")+".workspace"));
            shared=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
            for(int i=0;i<5;i++)doc.nodes.Add(new StudioModelDocument.Node{name="Pivot "+i,parent=i==1||i==3?0:-1,position=new Vector3(i*2,.2f*i,.1f*i),rotation=Quaternion.Euler(i*7,i*11,i*3),scale=i==2?new Vector3(-1,2,.7f):Vector3.one,mesh=i==0||i==2||i==4?shared:Object.Instantiate(shared),materials=new[]{StudioMeshMaterialPreview.DefaultMaterial},cutPolygons=polygons,cutTriangles=shared.triangles,pivotRotation=Quaternion.Euler(i,2*i,0)});
            AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(host,new object[]{true});typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});
            editor=host.ToolbarModelEditor;view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);editor.SetPivotLocked(false);
            editor.SelectObject(0);editor.SelectObject(1,true);editor.SelectObject(2,true);Call("EstablishSelectionBaseline");typeof(StudioModelEditor).GetField("pivot",F).SetValue(editor,true);
            void Step(string n,Action a)=>steps.Enqueue((n,a));
            Step("baseline",Capture);
            Step("move cohort",()=>{history=Undo.RetainedActions;Act(()=>Call("MoveEditingPivot",pivots[editor.Active]+delta));Verify(delta);Check(Undo.RetainedActions==history+1,"Not one action");Check(doc.nodes[0].mesh!=doc.nodes[2].mesh&&doc.nodes[0].mesh!=doc.nodes[4].mesh,"Shared compensation not isolated");});
            Step("Undo",()=>Undo.PerformUndo());Step("verify Undo",()=>Verify(Vector3.zero));
            Step("Redo",()=>Undo.PerformRedo());Step("verify Redo",()=>Verify(delta));
            Step("channels",()=>{Capture();var n=doc.nodes[editor.Active];var desired=editor.Transforms[editor.Active].parent?editor.Transforms[editor.Active].parent.InverseTransformPoint(pivots[editor.Active]+delta)-n.positionOffset:pivots[editor.Active]+delta-n.positionOffset;Act(()=>editor.SetObjectChannels(desired,n.RotationChannels,n.scale,n.active));Verify(delta);});
            Step("target position",()=>{Capture();Act(()=>editor.ApplyPivotTarget(new StudioModelEditor.PivotTarget{position=pivots[editor.Active]+delta},StudioModelEditor.PivotAction.Position));Verify(delta);});
            Step("target orient",()=>{Capture();var q=Quaternion.Euler(20,45,12);var turn=q*Quaternion.Inverse(editor.PivotOrientation);Act(()=>editor.ApplyPivotTarget(new StudioModelEditor.PivotTarget{orientation=q},StudioModelEditor.PivotAction.Orient));Verify(Vector3.zero,true);for(int i=0;i<3;i++)Check(Quaternion.Angle(Frame(i),turn*frames[i])<.01f,"Cohort orientation delta wrong");});
            Step("reject entire cohort",()=>{Capture();doc.nodes[0].blendShape=new StudioBlendShape();history=Undo.RetainedActions;Act(()=>Call("MoveEditingPivot",pivots[editor.Active]+delta));Verify(Vector3.zero);Check(editor.StatusText.Contains("Bake"),"Missing invalid-cohort warning");Check(Undo.RetainedActions==history,"Rejected cohort created history");doc.nodes[0].blendShape=null;});
            next=EditorApplication.timeSinceStartup+.3;EditorApplication.update+=Pump;
        }catch(Exception e){Finish(e.ToString());}
    }
    static void Pump()
    {
        if(EditorApplication.timeSinceStartup<next)return;
        try{if(steps.Count==0){Finish("PASS "+checks+" multi-object pivot translation, hierarchy/geometry preservation, shared-instance isolation, native Undo/Redo, channel/target edits and atomic rejection checks");return;}UnityEditor.Undo.FlushUndoRecordObjects();var step=steps.Dequeue();stage=step.Item1;step.Item2();UnityEditor.Undo.FlushUndoRecordObjects();next=EditorApplication.timeSinceStartup+.25;}catch(Exception e){Finish(stage+"\n"+e);}
    }
    static void Finish(string result){EditorApplication.update-=Pump;File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../multi-pivot-result.txt")),result);EditorApplication.delayCall+=()=>{if(view)view.Close();if(host)Object.DestroyImmediate(host);EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};}
}
#endif

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;

public static class StudioPivotLockValidation
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
            editor.SetPivotLocked(false);
            editor.SelectObject(0);Call("ChangeComponentMode",StudioModelEditor.ComponentMode.Face,null);editor.Components.Add(0);editor.ActivateTool(Tool.Rotate);Call("EstablishSelectionBaseline");
            EditorApplication.delayCall+=Validate;
        }
        catch(Exception error){Finish(error.ToString());}
    }
    static System.Collections.Generic.Queue<(string label,Action action)> plan;
    static double next;
    static Snapshot baseline,pivot1,extrusion1,pivot2,extrusion2,lockedBeforeExtrusion;
    static Vector3 anchor;static Quaternion anchorOrientation;
    static string probeError;static bool checkProbe;
    static readonly System.Collections.Generic.HashSet<Tool> observed=new System.Collections.Generic.HashSet<Tool>();
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

        Step("enable lock",()=>
        {
            editor.SetToolbarComponentAxes();anchor=editor.EditingPivotPosition;anchorOrientation=editor.PivotOrientation;
            editor.SetPivotLocked(true);Pinned();
            Check(StudioPreferenceStore.GetFloat(StudioModelEditor.PivotLockKey,0)==1,"Lock preference was not stored");
            checkProbe=true;
            StudioModelEditor.TransformHandleProbe=(tool,center,orientation,size)=>
            {
                if(SceneView.currentDrawingSceneView!=view || !checkProbe)return;
                if(Vector3.Distance(center,anchor)>.0001f)probeError="Drawn "+tool+" handle ignored the locked world pivot";
                observed.Add(tool);
            };
        });
        foreach(Tool tool in new[]{Tool.Move,Tool.Rotate,Tool.Scale})
        {
            var current=tool;
            Step("locked handle "+current,()=>{editor.ActivateTool(current);view.Repaint();Pinned();});
            Step("check locked handle "+current,()=>Check(observed.Contains(current),"Native handle was not drawn for "+current));
        }
        Step("deliberate locked pivot edit",()=>
        {
            anchor+=new Vector3(.2f,.1f,-.2f);anchorOrientation=Quaternion.Euler(12,23,34);
            Pivot(anchor,anchorOrientation);Pinned();
        });
        Step("Undo locked pivot edit",()=>Undo.PerformUndo());
        Step("check lock survives Undo pivot",()=>Pinned());
        Step("Redo locked pivot edit",()=>Undo.PerformRedo());
        Step("check lock survives Redo pivot",()=>Pinned());
        Step("selection changes while locked",()=>{Action(()=>{int other=editor.Components.Contains(0)?1:0;editor.Components.Clear();editor.Components.Add(other);});Pinned();});
        Step("Undo locked selection",()=>Undo.PerformUndo());
        Step("check lock after selection Undo",()=>Pinned());
        Step("locked extrusion",()=>
        {
            editor.ActivateTool(Tool.Rotate);lockedBeforeExtrusion=Capture();Extrude(10);Pinned();
            Check(editor.ActiveMesh.vertexCount>lockedBeforeExtrusion.points.Length,"Locked extrusion did not run");
        });
        Step("Undo locked extrusion",()=>Undo.PerformUndo());
        Step("check locked extrusion Undo",()=>{Pinned();Matches(lockedBeforeExtrusion);});
        Step("locked topology rebuild",()=>{Call("MeshTopologyChanged");Pinned();});
        Step("locked object mode",()=>{editor.SelectObject(0);Call("ChangeComponentMode",StudioModelEditor.ComponentMode.Object,null);editor.ActivateTool(Tool.Move);Pinned();});
        Step("locked object Move continuity",()=>
        {
            Vector3 dragPoint=anchor+new Vector3(.4f,.2f,0);
            Check(Vector3.Distance((Vector3)Call("LockedObjectMoveHandleCenter",anchor,123),anchor)<.0001f,"New object drag did not start at the locked point");
            Call("TrackLockedObjectMove",dragPoint,123);
            Check(Vector3.Distance((Vector3)Call("LockedObjectMoveHandleCenter",anchor,123),dragPoint)<.0001f,"Active Move drag lost its moving handle reference");
            Check(Vector3.Distance((Vector3)Call("LockedObjectMoveHandleCenter",anchor,0),anchor)<.0001f,"Released Move handle did not return to the locked point");
            Pinned();
        });
        Step("explicit object pivot edit while locked",()=>
        {
            anchor+=new Vector3(.1f,.1f,.1f);Action(()=>Call("MoveEditingPivot",anchor));
            Pinned();
        });
        Step("Undo locked object pivot",()=>Undo.PerformUndo());
        Step("check lock after object pivot Undo",()=>Pinned());
        Step("reload lock preference",()=>{editor.ReloadStoredToolDefaults();Check(editor.PivotLocked,"Lock flag lost on reload");Pinned();});
        Step("unlock",()=>
        {
            checkProbe=false;editor.SetPivotLocked(false);
            Check(!editor.PivotLocked && StudioPreferenceStore.GetFloat(StudioModelEditor.PivotLockKey,1)==0,"Unlock not stored");
            Check(Vector3.Distance(editor.EditingPivotPosition,editor.Transforms[0].position)<.0001f,"Unlock did not restore ordinary object pivot");
        });
        next=EditorApplication.timeSinceStartup+.2;EditorApplication.update+=Pump;
    }
    static void Pinned()
    {
        Check(editor.PivotLocked,"Lock unexpectedly disabled");
        Check(Vector3.Distance(editor.EditingPivotPosition,anchor)<.0001f,"Locked position moved");
        Check(Quaternion.Angle(editor.PivotOrientation,anchorOrientation)<.01f,"Locked orientation moved");
        Check(Vector3.Distance((Vector3)Call("ResolveLockedPivotCenter",new Vector3(11,22,33)),anchor)<.0001f,"Transform center ignored locked pivot");
    }
    static void Pump()
    {
        if(EditorApplication.timeSinceStartup<next)return;
        try
        {
            if(probeError!=null)throw new Exception(probeError);
            if(plan.Count==0){Finish("PASS "+checks+" pivot lock preference, native Move/Rotate/Scale centers, explicit edits, selection/topology/Undo retention, unlock and unlocked history regression checks");return;}
            UnityEditor.Undo.FlushUndoRecordObjects();var item=plan.Dequeue();stage=item.label;item.action();UnityEditor.Undo.FlushUndoRecordObjects();
            next=EditorApplication.timeSinceStartup+.2;
        }
        catch(Exception error){Finish(error.ToString());}
    }
    static void Finish(string result)
    {
        EditorApplication.update-=Pump;
        StudioModelEditor.TransformHandleProbe=null;
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../pivot-lock-result.txt")),result);
        EditorApplication.delayCall+=()=>
        {
            if(view)view.Close();if(host)UnityEngine.Object.DestroyImmediate(host);
            double until=EditorApplication.timeSinceStartup+1;
            void Exit(){if(EditorApplication.timeSinceStartup<until)return;EditorApplication.update-=Exit;EditorApplication.Exit(result.StartsWith("PASS")?0:1);}
            EditorApplication.update+=Exit;
        };
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object = UnityEngine.Object;
public static class StudioSelectionUndoValidation
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks; static string stage; static int errors;
    static void Check(bool value, string message) { if (!value) throw new Exception(stage + ": " + message); checks++; }
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethods(F).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static object Get(object target, string name) => target.GetType().GetField(name, F).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, F).SetValue(target,value);
    static void Reset() { UnityEditor.Undo.ClearAll(); StudioUndo.ClearForTests(); StudioUndo.IncrementCurrentGroup(); }
    static void History(bool redo = false) { Debug.Log("SELECTION HISTORY " + stage + " redo=" + redo); UnityEditor.Undo.FlushUndoRecordObjects(); StudioUndo.IncrementCurrentGroup(); if (redo) StudioUndo.PerformRedo(); else StudioUndo.PerformUndo(); }
    public static void Run()
    {
        string report = Path.GetFullPath(Path.Combine(Application.dataPath,"../../selection-undo-validation.txt"));
        Application.logMessageReceived += (s,t,k) => { if(k==LogType.Assert || k==LogType.Error || k==LogType.Exception) errors++; };
        try { Validate(); Check(errors==0,"Unexpected Unity errors: "+errors); File.WriteAllText(report, "PASS " + checks + " selection/import Undo assertions"); EditorApplication.Exit(0); }
        catch (Exception e) { File.WriteAllText(report,e.ToString()); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static void Validate()
    {
        Reset();
        var doc = StudioModelDocument.CreateEmpty(StudioModelDocument.NewWorkspacePath("Selection Undo Validation"));
        doc.AddPrimitive(PrimitiveType.Cube);
        using (var editor = new StudioModelEditor(()=>{}))
        {
            editor.Load(doc); editor.SelectObject(0);
            foreach(var mode in new[]{StudioModelEditor.ComponentMode.Vertex,StudioModelEditor.ComponentMode.Edge,StudioModelEditor.ComponentMode.Face})
            {
                stage = mode + " selection";
                editor.SetToolbarComponentMode(mode); editor.Components.Clear(); Reset();
                var transforms = editor.Transforms.ToArray(); var vertices = editor.ActiveMesh.vertices;
                editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.SelectAll,null);
                var all = editor.Components.ToArray(); Check(all.Length > 0,"Select All did nothing");
                Check(StudioUndo.RetainedActions == 1,"One completed selection must be one action");
                editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.SelectAll,null);
                Check(StudioUndo.RetainedActions == 1,"No-op selection added history");
                History(); Check(editor.Components.Count == 0,"Undo selection");
                Check(editor.Transforms.SequenceEqual(transforms),"Selection Undo rebuilt preview scene");
                Check(editor.ActiveMesh.vertices.SequenceEqual(vertices),"Selection Undo changed geometry");
                History(true); Check(editor.Components.SetEquals(all),"Redo selection");
                Call(editor,"InvertSelection"); Check(editor.Components.Count == 0,"Invert All");
                History(); Check(editor.Components.SetEquals(all),"Undo invert");
                History(true); Check(editor.Components.Count == 0,"Redo invert");
                editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.SelectAll,null);
                editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.Clear,null);
                History(); Check(editor.Components.SetEquals(all),"Undo deselect");
                History(true); Check(editor.Components.Count == 0,"Redo deselect");
            }
            stage = "mode history"; editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);
            editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.SelectAll,null); var selected = editor.Components.ToArray(); Reset();
            editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face); History();
            Check((StudioModelEditor.ComponentMode)Get(editor,"mode") == StudioModelEditor.ComponentMode.Edge && editor.Components.SetEquals(selected),"Undo component mode lost selection");
            History(true); Check((StudioModelEditor.ComponentMode)Get(editor,"mode") == StudioModelEditor.ComponentMode.Face && editor.Components.Count == 0,"Redo component mode");
            stage = "completed marquee";
            editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Vertex); editor.Components.Clear(); Reset();
            var view = ScriptableObject.CreateInstance<SceneView>(); view.Show();
            try
            {
                view.camera.transform.position = new Vector3(0,0,-5); view.camera.transform.rotation = Quaternion.identity;
                view.camera.pixelRect = new Rect(0,0,800,600); Handles.SetCamera(view.camera);
                Call(editor,"Pick",view,new Rect(-100000,-100000,200000,200000),false);
                var picked=editor.Components.ToArray(); Check(picked.Length>0,"Marquee selected no vertices");
                Check(StudioUndo.RetainedActions==1,"Marquee creates one action"); History(); Check(editor.Components.Count==0,"Undo marquee"); History(true); Check(editor.Components.SetEquals(picked),"Redo marquee");
                Set(editor,"subtract",true); Call(editor,"Pick",view,new Rect(-100000,-100000,200000,200000),false);
                Check(editor.Components.Count==0,"Subtract marquee"); History(); Check(editor.Components.SetEquals(picked),"Undo subtract marquee"); History(true); Check(editor.Components.Count==0,"Redo subtract marquee");
                Set(editor,"subtract",false); Set(editor,"additive",true); Call(editor,"Pick",view,new Rect(-100000,-100000,200000,200000),false);
                History(); Check(editor.Components.Count==0,"Undo additive marquee"); History(true); Check(editor.Components.SetEquals(picked),"Redo additive marquee"); Set(editor,"additive",false);
            }
            finally { view.Close(); }
            stage = "bevel transaction";
            editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge); editor.Components.Clear(); editor.Components.Add(0); Reset();
            var original = editor.ActiveMesh.vertices; int sourceCount=editor.ActiveMesh.vertexCount;
            ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.12f); ToolChannelPane.SetFloatDefault("Bevel Edges","Subdivisions",0);
            Call(editor,"OpenBevelTool"); Call(editor,"UpdateQueuedLiveTool");
            var pane = (ToolChannelPane)Get(editor,"bevelToolPane"); Check(pane.ExecuteCommand(false),"Apply bevel");
            int bevelCount=editor.ActiveMesh.vertexCount; Check(bevelCount!=sourceCount && editor.Components.Count==0,"Bevel result/selection");
            Check(StudioUndo.RetainedActions==1,"Bevel selection was a separate Undo step");
            History(); Check(editor.ActiveMesh.vertices.SequenceEqual(original) && editor.Components.SetEquals(new[]{0}),"Bevel Undo did not restore source selection and mesh together");
            History(true); Check(editor.ActiveMesh.vertexCount==bevelCount && editor.Components.Count==0,"Bevel Redo selection");
            pane.Close();
            stage="selection after bevel";
            editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.SelectAll,null); var postBevel=editor.Components.ToArray();
            editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.Clear,null);
            History(); Check(editor.Components.SetEquals(postBevel) && editor.ActiveMesh.vertexCount==bevelCount,"Selection Undo affected committed bevel");
            History(); Check(editor.Components.Count==0 && editor.ActiveMesh.vertexCount==bevelCount,"Selection Undo skipped a step");
            History(); Check(editor.Components.SetEquals(new[]{0}) && editor.ActiveMesh.vertices.SequenceEqual(original),"Mixed history bevel Undo");
            History(true); History(true); History(true); Check(editor.Components.Count==0 && editor.ActiveMesh.vertexCount==bevelCount,"Mixed history Redo");
            stage = "import baseline"; Reset();
            var other=ScriptableObject.CreateInstance<StudioSelectionUndoProbe>();
            UnityEditor.Undo.IncrementCurrentGroup(); UnityEditor.Undo.RegisterCompleteObjectUndo(other,"Unrelated Unity edit"); other.value=7; UnityEditor.Undo.IncrementCurrentGroup();
            var mesh=editor.ActiveMesh; var meshBefore=mesh.vertices; StudioUndo.RegisterCompleteObjectUndo(mesh,"Earlier mesh edit"); var moved=mesh.vertices; moved[0]+=Vector3.right; mesh.vertices=moved; StudioUndo.IncrementCurrentGroup();
            StudioUndo.RegisterCompleteObjectUndo(doc,"Earlier document edit"); doc.nodes[0].name="Renamed"; StudioUndo.IncrementCurrentGroup();
            var source=GameObject.CreatePrimitive(PrimitiveType.Cube); int prior=doc.nodes.Count;
            try { doc.AppendModel(source); } finally { Object.DestroyImmediate(source); }
            Check(doc.nodes.Count==prior+1,"Append failed"); Check(StudioUndo.RetainedActions==1,"Import retained incompatible document action or added an action");
            History(); Check(doc.nodes.Count==prior+1 && mesh.vertices.SequenceEqual(meshBefore),"Import removed nodes or destroyed mesh-only history");
            History(); Check(doc.nodes.Count==prior+1 && other.value==0,"Import removed nodes or cleared unrelated history");
            History(true); Check(doc.nodes.Count==prior+1 && other.value==7,"Unrelated Redo lost");
            History(true); Check(doc.nodes.Count==prior+1 && mesh.vertices.SequenceEqual(moved),"Mesh-only Redo lost");
            int retained=StudioUndo.RetainedActions; try { doc.AppendModel(null); } catch(ArgumentException) { }
            Check(doc.nodes.Count==prior+1 && StudioUndo.RetainedActions==retained,"Failed import damaged history");
            stage="FBX import";
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FrameProbe.fbx");
            Check(fbx,"FBX fixture missing"); int beforeFbx=doc.nodes.Count; Reset();
            Call(editor,"Run",(Action)(()=>editor.ImportModel(fbx,_=>{})),true); int importedNodes=doc.nodes.Count;
            Check(importedNodes>beforeFbx && StudioUndo.RetainedActions==0,"FBX import added Undo action");
            History(); Check(doc.nodes.Count==importedNodes,"Undo removed imported FBX");
            stage="reopen selection"; editor.Load(doc); editor.SelectObject(0); editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge); editor.Components.Clear(); Reset();
            editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.SelectAll,null); var final=editor.Components.ToArray();
            editor.Load(doc); Check(editor.Components.SetEquals(final),"Reopening lost session selection");
            StudioUndo.ReloadSessionForTests(); History(); Check(editor.Components.Count==0,"Session restore Undo"); History(true); Check(editor.Components.SetEquals(final),"Session restore Redo");
            stage="selection budget"; Reset();
            int maximum=StudioSettings.Current.undoLimit;
            for(int i=0;i<maximum+5;i++) editor.ExecuteMarkingAction(i%2==0?StudioModelEditor.MarkingAction.Clear:StudioModelEditor.MarkingAction.SelectAll,null);
            Check(StudioUndo.RetainedActions==maximum,"Selection actions bypassed configured Undo limit");
            Object.DestroyImmediate(other);
        }
        Reset();
    }
}
public sealed class StudioSelectionUndoProbe : ScriptableObject { public int value; }

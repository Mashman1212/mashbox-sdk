using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
public static class StudioLiveToolValidation
{
    static int checks; static string stage = "initial"; static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value, string message) { if (!value) throw new Exception(stage + ": " + message); checks++; }
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethods(F).Single(m => m.Name == method && m.GetParameters().Length == args.Length).Invoke(target,args);
    static object Get(object target, string field) => target.GetType().GetField(field,F).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field,F).SetValue(target,value);
    static string Shape(Mesh mesh) => string.Join(";",mesh.vertices.Select(p=>p.ToString("R")))+"/"+string.Join(",",mesh.triangles);
    static StudioModelDocument.Node Cube(string name)
    {
        var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
        return new StudioModelDocument.Node{name=name,mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles};
    }
    static void Width(float value) => ToolChannelPane.SetFloatDefault("Bevel Edges","Width",value);
    static void Flush(StudioModelEditor editor) => Call(editor,"UpdateQueuedLiveTool");
    static void Mode(StudioModelEditor editor, StudioModelEditor.ComponentMode mode, params int[] selection)
    { editor.SetToolbarComponentMode(mode); editor.Components.Clear(); foreach(int i in selection)editor.Components.Add(i); }
    public static void Run()
    {
        StudioModelEditor editor=null; StudioModelDocument document=null; var meshes=new List<Mesh>();
        var messages=new List<string>(); Application.LogCallback log=(s,t,l)=>{if(l==LogType.Error||l==LogType.Exception||l==LogType.Assert)messages.Add(s);};
        Application.logMessageReceived+=log;
        try
        {
            document=ScriptableObject.CreateInstance<StudioModelDocument>();var node=Cube("Primary");meshes.Add(node.mesh);
            var other=Cube("Untouched");meshes.Add(other.mesh);
            document.nodes.Add(node);document.nodes.Add(new StudioModelDocument.Node{name="Shared",mesh=node.mesh,cutPolygons=node.cutPolygons,cutTriangles=node.cutTriangles});document.nodes.Add(other);
            editor=new StudioModelEditor(()=>{});editor.Load(document);editor.SelectObject(0);
            var original=Shape(node.mesh);var untouched=Shape(other.mesh);var transforms=editor.Transforms.ToArray();
            Mode(editor,StudioModelEditor.ComponentMode.Edge,0);
            ToolChannelPane.SetFloatDefault("Bevel Edges","Subdivisions",3);ToolChannelPane.SetFloatDefault("Bevel Edges","Tension",1);ToolChannelPane.SetFloatDefault("Bevel Edges","Junction",0);Width(.1f);
            EditorUtility.ClearDirty(document);EditorUtility.ClearDirty(node.mesh);
            stage="live bevel";Call(editor,"OpenBevelTool");Flush(editor);
            var pane=(ToolChannelPane)Get(editor,"bevelToolPane");int count=node.mesh.vertexCount;int faceCount=new StudioMeshCuts(node).Faces.Count;
            Check(Shape(node.mesh)!=original,"Opening tool publishes live geometry");Check(ReferenceEquals(node.mesh,document.nodes[1].mesh),"Shared live Mesh identity retained");
            foreach(float width in new[]{.2f,.3f,.15f,.25f})
            {
                Width(width);Call(editor,"QueueLiveTool",Get(editor,"liveTool"));Flush(editor);
                Check(new StudioMeshCuts(node).Faces.Count==faceCount,"Edits do not stack bevel operations (width="+width+", vertices="+node.mesh.vertexCount+"/"+count+", faces="+new StudioMeshCuts(node).Faces.Count+"/"+faceCount+")");
                Check(editor.Transforms.SequenceEqual(transforms),"Scene transforms stay alive");Check(Shape(other.mesh)==untouched,"Unselected geometry untouched");
                Check(node.cutPolygons.SequenceEqual(document.nodes[1].cutPolygons),"Shared polygon metadata updated");
            }
            var expected=Cube("Expected");meshes.Add(expected.mesh);var topology=new StudioMeshCuts(expected);
            
            // Source selection was captured before the preview changes edge indices.
            var sourceEdges=((ValueTuple<int,int>[])Get(editor,"liveEdges"));
            topology.BevelEdges(expected,sourceEdges,new StudioMeshCuts.BevelOptions{Width=.25f,Subdivisions=3,Tension=1,Junction=StudioMeshCuts.BevelJunction.Uniform});
            Check(Shape(node.mesh)==Shape(expected.mesh),"Final live result equals one source operation");
            stage="invalid preview";var valid=Shape(node.mesh);Width(float.NaN); // Setter intentionally rejects NaN.
            Width(2);Call(editor,"QueueLiveTool",Get(editor,"liveTool"));Flush(editor);
            Check(Shape(node.mesh)==valid,"Invalid settings retain last valid published geometry");
            Check(!pane.ExecuteCommand(false),"Invalid preview cannot commit stale geometry");Width(.25f);
            stage="close rollback";pane.Close();Check(Shape(node.mesh)==original,"Close restores original geometry");Check(editor.Components.SetEquals(new[]{0}),"Close restores source selection");Check(!EditorUtility.IsDirty(document)&&!EditorUtility.IsDirty(node.mesh),"Cancellation preserves clean saved state");
            stage="apply and execute";Call(editor,"OpenBevelTool");Flush(editor);pane=(ToolChannelPane)Get(editor,"bevelToolPane");
            Check(pane.ExecuteCommand(false),"Apply succeeds");Check(pane&&Get(editor,"livePreview")==null,"Apply commits and leaves pane open");
            Check(editor.Components.Count==0,"Bevel Apply clears edge selection");
            var applied=Shape(node.mesh);Width(.05f);var bevelKind=Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",BindingFlags.NonPublic),"Bevel");Call(editor,"QueueLiveTool",bevelKind);Flush(editor);
            Check(Shape(node.mesh)==applied,"Parameter edit without reselecting does not rebevel");
            editor.Components.Add(0);Call(editor,"QueueLiveTool",bevelKind);Flush(editor);
            Check(Shape(node.mesh)!=applied,"Reselected edge edit has new baseline after Apply");pane.Close();Check(Shape(node.mesh)==applied,"Close preserves prior Apply");
            editor.PerformWorkspaceHistory(false);Check(Shape(node.mesh)==original,"One Undo restores pre-bevel source");editor.PerformWorkspaceHistory(true);Check(Shape(node.mesh)==applied,"Redo restores committed bevel");
            // Undo serializes document nodes; reacquire their references.
            node=document.nodes[0];editor.SelectObject(0);Mode(editor,StudioModelEditor.ComponentMode.Face,0);
            stage="live extrusion";var preExtrude=Shape(node.mesh);editor.ExtrudeSelected();Flush(editor);pane=(ToolChannelPane)Get(editor,"extrudeToolPane");
            var extrusion=Get(editor,"liveExtrusion");count=node.mesh.vertexCount;faceCount=new StudioMeshCuts(node).Faces.Count;
            foreach(float distance in new[]{.2f,.5f,-.3f,.8f})
            {
                Set(editor,"extrusionDistance",distance);Call(editor,"QueueLiveTool",Get(editor,"liveTool"));Flush(editor);
                Check(ReferenceEquals(extrusion,Get(editor,"liveExtrusion")),"Distance edit reuses expanded topology");
                Check(node.mesh.vertexCount==count&&new StudioMeshCuts(node).Faces.Count==faceCount,"Distance edits do not accumulate walls");
                var target=((StudioMeshToolPreview)Get(editor,"livePreview")).Targets[0];Check(Shape(target.Work.mesh)==Shape(node.mesh),"Vertex buffer publication updates CPU positions and indices");
                Check(target.Work.mesh.normals.SequenceEqual(node.mesh.normals),"Vertex buffer publication preserves normals");
            }
            Set(editor,"extrusionDistance",0f);Call(editor,"QueueLiveTool",Get(editor,"liveTool"));Flush(editor);Check(Shape(node.mesh)==preExtrude,"Zero distance restores source without degenerate walls");
            Set(editor,"extrusionDistance",.4f);Call(editor,"QueueLiveTool",Get(editor,"liveTool"));Flush(editor);
            Check(pane.ExecuteCommand(true),"Execute commits extrusion");Check(!pane&&Get(editor,"livePreview")==null,"Execute closes pane");
            var extruded=Shape(node.mesh);editor.PerformWorkspaceHistory(false);Check(Shape(node.mesh)==preExtrude,"One extrusion Undo");editor.PerformWorkspaceHistory(true);Check(Shape(node.mesh)==extruded,"One extrusion Redo");
            stage="zero-width commit";
            node=document.nodes[0];editor.SelectObject(0);Mode(editor,StudioModelEditor.ComponentMode.Edge,0);EditorUtility.ClearDirty(document);EditorUtility.ClearDirty(node.mesh);Width(0);Call(editor,"OpenBevelTool");Flush(editor);pane=(ToolChannelPane)Get(editor,"bevelToolPane");pane.ExecuteCommand(true);
            Check(!EditorUtility.IsDirty(document)&&!EditorUtility.IsDirty(node.mesh),"Zero-width apply does not dirty saved mesh");Width(.1f);
            stage="pending Undo";node=document.nodes[0];editor.SelectObject(0);Mode(editor,StudioModelEditor.ComponentMode.Face,0);editor.ExtrudeSelected();Flush(editor);pane=(ToolChannelPane)Get(editor,"extrudeToolPane");
            editor.PerformWorkspaceHistory(false);Check(Shape(node.mesh)==extruded,"Undo cancels unapplied preview first");pane.Close();
            stage="external Undo";
            editor.PerformWorkspaceHistory(false);var beforeExternal=Shape(node.mesh);editor.PerformWorkspaceHistory(true);node=document.nodes[0];
            editor.SelectObject(0);Mode(editor,StudioModelEditor.ComponentMode.Face,0);editor.ExtrudeSelected();Flush(editor);pane=(ToolChannelPane)Get(editor,"extrudeToolPane");
            Undo.PerformUndo();Check(Shape(node.mesh)==beforeExternal,"Native Undo wins over an unapplied snapshot");pane.Close();
            Undo.PerformRedo();node=document.nodes[0];editor.SelectObject(0);
            stage="soften preview";editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Object);Call(editor,"OpenSoftenTool");Flush(editor);pane=(ToolChannelPane)Get(editor,"softenToolPane");
            int softCount=node.mesh.vertexCount;ToolChannelPane.SetFloatDefault("Soften Edges","Angle",180);Call(editor,"QueueLiveTool",Get(editor,"liveTool"));Flush(editor);
            Check(node.normalGroups.Length==node.mesh.vertexCount,"Smoothing updates editable normal groups");pane.Close();Check(Shape(node.mesh)==extruded,"Soften close restores geometry");
            stage="merge preview";Mode(editor,StudioModelEditor.ComponentMode.Vertex,0,1,2,3);Set(editor,"mergeDistance",.001f);Call(editor,"MergeSelectedVertices");Flush(editor);pane=(ToolChannelPane)Get(editor,"mergeToolWindow");
            Check(pane,"Multi-vertex merge uses live pane");if(pane){Set(editor,"mergeDistance",.2f);Call(editor,"QueueLiveTool",Get(editor,"liveTool"));Flush(editor);pane.Close();Check(Shape(node.mesh)==extruded,"Merge close restores source");}
            stage="extrusion UV dimensions";
            foreach(int dimension in new[]{3,4})
            {
                var uv=Enumerable.Range(0,node.mesh.vertexCount).Select(i=>new Vector4(i*.01f,i*.02f,.37f,.61f)).ToList();
                if(dimension==3)node.mesh.SetUVs(0,uv.Select(v=>new Vector3(v.x,v.y,v.z)).ToList());else node.mesh.SetUVs(0,uv);
                editor.SelectObject(0);Mode(editor,StudioModelEditor.ComponentMode.Face,0);editor.ExtrudeSelected();Flush(editor);pane=(ToolChannelPane)Get(editor,"extrudeToolPane");
                Set(editor,"extrusionDistance",.63f);Call(editor,"QueueLiveTool",Get(editor,"liveTool"));Flush(editor);
                Check(node.mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord0)==dimension,"Cached distance edits retain UV0 dimensions");
                var values=new List<Vector4>();node.mesh.GetUVs(0,values);Check(values.All(v=>Mathf.Approximately(v.z,.37f)),"Cached distance edits retain UV0 z");
                if(dimension==4)Check(values.All(v=>Mathf.Approximately(v.w,.61f)),"Cached distance edits retain UV0 w");pane.Close();
            }
            stage="lifecycle";editor.SelectObject(0);Mode(editor,StudioModelEditor.ComponentMode.Face,0);editor.ExtrudeSelected();Flush(editor);editor.Load(document);
            Check(Get(editor,"livePreview")==null&&!(bool)Get(editor,"previewQueued"),"Load cancels pending work and snapshots");
            Check(Shape(document.nodes[0].mesh)==extruded,"Load cancellation preserves source");
            Check(messages.Count==0,"No Unity errors: "+string.Join(";",messages));
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../live-tools-result.txt")),"PASS "+checks+" assertions");
            editor.Dispose();editor=null;Object.DestroyImmediate(document);document=null;foreach(var mesh in meshes)if(mesh)Object.DestroyImmediate(mesh);Application.logMessageReceived-=log;EditorApplication.Exit(0);
        }
        catch(Exception exception)
        {
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../live-tools-result.txt")),"FAIL "+stage+"\n"+exception+"\n"+string.Join("\n",messages));
            editor?.Dispose();Application.logMessageReceived-=log;EditorApplication.Exit(1);
        }
    }
}

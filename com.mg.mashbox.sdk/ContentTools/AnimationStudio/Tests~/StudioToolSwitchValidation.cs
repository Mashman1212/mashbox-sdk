using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioToolSwitchValidation
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../GizmoCompletion/"+name));
        static object Get(object o,string name)=>o.GetType().GetField(name,F) is FieldInfo field?field.GetValue(o):o.GetType().GetProperty(name,F).GetValue(o);
        static void Set(object o,string name,object value)=>o.GetType().GetField(name,F).SetValue(o,value);
        static object Call(object o,string name,params object[] args)=>o.GetType().GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(o,args);
        [InitializeOnLoadMethod] static void Boot()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets")||!File.Exists(Out("request.txt")))return;
            EditorApplication.delayCall+=Run;
        }
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets"))return;
            File.Delete(Out("request.txt"));var log=new List<string>();
            void Check(bool valid,string name){if(!valid)throw new Exception(name);log.Add("PASS "+name);}
            try
            {
                foreach(bool invalid in new[]{false,true})foreach(var next in new[]{Tool.Move,Tool.Rotate,Tool.Scale,Tool.None})
                {
                    var doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
                    var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles};doc.nodes.Add(node);var original=mesh.vertices;var triangles=mesh.triangles;
                    try
                    {
                        using(var editor=new StudioModelEditor(()=>{}))
                        {
                            editor.Load(doc);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);editor.Components.Add(0);
                            editor.ActivateTool(next);editor.ExtrudeSelected();
                            Check(editor.ToolbarToolSelected(next==Tool.None?Tool.Move:next),"Extrude retains active gizmo or supplies Move from Select "+next+" invalid="+invalid);
                            Check(editor.SelectedVertices().Count>0,"Extrude gizmo has selected handle vertices "+next+" invalid="+invalid);
                            Set(editor,"extrusionDistance",.2f);Call(editor,"UpdateQueuedLiveTool");
                            Check(mesh.vertexCount>original.Length,"Extrude preview exists before switch "+next+" invalid="+invalid);
                            if(invalid)
                            {
                                Set(editor,"extrusionInset",-1000f);Call(editor,"QueueLiveTool",Get(editor,"liveTool"));Call(editor,"UpdateQueuedLiveTool");
                                Check(!(bool)Get(editor,"liveValid"),"Actual invalid Extrude preview stays cancellable "+next);
                            }
                            if(next==Tool.None)editor.ActivateTool(next);
                            else editor.ToolbarCommands(null).Single(c=>c.Key=="Modify/"+next).Execute();
                            Check(editor.ToolbarToolSelected(next)&&editor.ToolOptionsTitle==(next==Tool.None?"Selection Tool":next+" Tool"),"Requested tool activates through shared command "+next+" invalid="+invalid);
                            Check(invalid ? mesh.vertices.SequenceEqual(original)&&mesh.triangles.SequenceEqual(triangles) : mesh.vertexCount>original.Length,
                                "Switch completes valid geometry and cancels invalid geometry "+next+" invalid="+invalid);
                            Check(Get(editor,"livePreview")==null&&!(bool)Get(editor,"previewQueued")&&(invalid?editor.Components.SetEquals(new[]{0}):editor.Components.Count>0),"Preview and queue cleared; usable handle selection retained "+next+" invalid="+invalid);
                            Check((string)Get(editor,"error")==null&&(invalid?node.modifiers?.HasHistory!=true:node.modifiers?.HasHistory==true),"No blocking warning; modifier only for valid completion "+next+" invalid="+invalid);
                            Check(ToolChannelPane.GetFloatDefault("Extrude","Distance",0)==0&&(float)Get(editor,"extrusionDistance")==0,"Session values reset "+next+" invalid="+invalid);
                        }
                    }
                    finally{UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(doc);}
                }
                var document=ScriptableObject.CreateInstance<StudioModelDocument>();var source=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var faces);
                document.nodes.Add(new StudioModelDocument.Node{mesh=source,cutPolygons=faces,cutTriangles=source.triangles});
                try
                {
                    using(var editor=new StudioModelEditor(()=>{}))
                    {
                        editor.Load(document);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);editor.Components.Add(0);
                        editor.ExtrudeSelected();Set(editor,"extrusionDistance",.2f);Call(editor,"UpdateQueuedLiveTool");
                        var pane=(ToolChannelPane)Get(editor,"extrudeToolPane");Check(pane.ExecuteCommand(false),"Apply still commits and retains pane");var applied=source.vertices;var appliedTriangles=source.triangles;
                        editor.ActivateTool(Tool.Rotate);Check(source.vertices.SequenceEqual(applied)&&source.triangles.SequenceEqual(appliedTriangles),"Switch preserves already-applied Extrude geometry");
                        editor.Components.Clear();editor.Components.Add(0);editor.ExtrudeSelected();Call(editor,"UpdateQueuedLiveTool");
                        editor.ToolbarCommands(null).Single(c=>c.Key=="Mesh Tools/Bridge Edges").Execute();
                        Check(Get(editor,"extrudeToolPane")==null&&Get(editor,"bridgeToolPane")!=null&&editor.ToolOptionsTitle=="Bridge Edges","Procedure-to-procedure switch opens requested guidance pane");
                        editor.ActivateTool(Tool.Move);Check(Get(editor,"bridgeToolPane")==null&&editor.ToolbarToolSelected(Tool.Move),"Exit invalid Bridge guidance returns to Move");
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(document);}
                // Leave a disposable native viewport open for visual inspection of
                // the actual Extrude pane and Move gizmo together.
                var visualDoc=ScriptableObject.CreateInstance<StudioModelDocument>();var visualMesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var visualFaces);
                visualDoc.nodes.Add(new StudioModelDocument.Node{name="Extrude gizmo check",mesh=visualMesh,cutPolygons=visualFaces,cutTriangles=visualMesh.triangles});
                var host=ScriptableObject.CreateInstance<AnimationStudioWindow>();Set(host,"modelingMode",true);Set(host,"modelDocument",visualDoc);Call(host,"OpenViewport");
                var visualEditor=(StudioModelEditor)Get(host,"modelEditor");visualEditor.SelectObject(0);visualEditor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);visualEditor.Components.Add(0);visualEditor.ActivateTool(Tool.Move);visualEditor.ExtrudeSelected();
                var viewport=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Single(v=>v.Studio==host);viewport.position=new Rect(100,100,1200,850);viewport.LookAtDirect(Vector3.zero,Quaternion.Euler(20,-35,0),2);viewport.Repaint();
                Check(visualEditor.ToolOptionsTitle=="Extrude"&&visualEditor.ToolbarToolSelected(Tool.Move),"Native viewport has Extrude options and active Move gizmo");
            }
            catch(Exception e){log.Add("FAIL "+e);}
            File.WriteAllLines(Out("result.txt"),log);
        }
    }
}

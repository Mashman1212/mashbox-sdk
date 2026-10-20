using System;
using System.Reflection;
using System.Diagnostics;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
using System.Collections;
using System.Collections.Generic;
public static class StudioPositionUpdateValidation
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    public static void Run()
    {
        int result=0;
        var doc=ScriptableObject.CreateInstance<StudioModelDocument>();
        var editor=new StudioModelEditor(()=>{});
        try
        {
            for(int i=0;i<32;i++)doc.nodes.Add(new StudioModelDocument.Node{name="Allocation cube "+i,mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube),position=new Vector3(i*2,0,0)});
            editor.Load(doc);editor.SelectObject(0);editor.Components.Add(0);
            typeof(StudioModelEditor).GetField("mode",Flags).SetValue(editor,StudioModelEditor.ComponentMode.Face);
            typeof(StudioModelEditor).GetMethod("PreserveFaceTopology",Flags).Invoke(editor,null);
            var ensure=typeof(StudioModelEditor).GetMethod("EnsureComponentDrawing",Flags);
            var positionChange=typeof(StudioModelEditor).GetMethod("PositionsChanged",Flags);
            void Refresh()
            {
                if(positionChange!=null)positionChange.Invoke(editor,null);else editor.Changed();
                for(int i=0;i<doc.nodes.Count;i++)editor.SelectionWireframeLines(i);
                ensure.Invoke(editor,null);editor.MeshStatistics();
            }
            Refresh();
            var untouched=editor.SelectionWireframeLines(1);
            var changed=editor.SelectionWireframeLines(0);
            var overlay=typeof(StudioModelEditor).GetField("componentHighlight",Flags).GetValue(editor);
            int builds=editor.ComponentOverlayBuilds;
            var points=doc.nodes[0].mesh.vertices;
            long before=GC.GetAllocatedBytesForCurrentThread();
            var watch=Stopwatch.StartNew();
            for(int i=0;i<120;i++){points[0]+=new Vector3(.001f,0,0);doc.nodes[0].mesh.vertices=points;Refresh();}
            watch.Stop();long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
            UnityEngine.Debug.Log($"POSITION_UPDATE_MEASUREMENT optimized={positionChange!=null} iterations=120 allocationCounter={(bytes==0?"unavailable":bytes.ToString())} ms={watch.Elapsed.TotalMilliseconds:F2} overlayBuilds={editor.ComponentOverlayBuilds-builds}");
            if(positionChange!=null)
            {
                if(!ReferenceEquals(untouched,editor.SelectionWireframeLines(1)))throw new Exception("Unchanged mesh outline was rebuilt");
                if(!ReferenceEquals(changed,editor.SelectionWireframeLines(0)))throw new Exception("Moved mesh outline buffer was replaced");
                if(!ReferenceEquals(overlay,typeof(StudioModelEditor).GetField("componentHighlight",Flags).GetValue(editor)))throw new Exception("Selected face overlay was recreated");
                if(editor.ComponentOverlayBuilds!=builds)throw new Exception("Overlay topology rebuilt during a position-only drag");
                if(!Array.Exists(editor.SelectionWireframeLines(0),p=>(p-points[0]).sqrMagnitude<1e-10f))throw new Exception("Outline positions did not follow edited vertices");
                var wires=(IDictionary)typeof(StudioModelEditor).GetField("selectionWireframes",Flags).GetValue(editor);
                foreach(DictionaryEntry pair in wires)
                {
                    var wire=pair.Value;var type=wire.GetType();
                    var indices=(int[])type.GetField("indices",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(wire);
                    var ordered=(int[])type.GetField("orderedIndices",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(wire);
                    var expected=new HashSet<(int,int)>();var actual=new HashSet<(int,int)>();
                    for(int i=0;i<indices.Length;i+=2)expected.Add((Math.Min(indices[i],indices[i+1]),Math.Max(indices[i],indices[i+1])));
                    for(int i=0;i<ordered.Length;i+=2)actual.Add((Math.Min(ordered[i],ordered[i+1]),Math.Max(ordered[i],ordered[i+1])));
                    if(ordered.Length!=indices.Length||!expected.SetEquals(actual))throw new Exception("Batched outline changed or duplicated source edges");
                }
                foreach(var mode in new[]{StudioModelEditor.ComponentMode.Vertex,StudioModelEditor.ComponentMode.Edge,StudioModelEditor.ComponentMode.Face})
                {
                    typeof(StudioModelEditor).GetField("mode",Flags).SetValue(editor,mode);ensure.Invoke(editor,null);
                    var highlight=(Mesh)typeof(StudioModelEditor).GetField("componentHighlight",Flags).GetValue(editor);
                    int built=editor.ComponentOverlayBuilds;
                    for(int i=0;i<points.Length;i++)points[i]+=Vector3.up*.1f;
                    doc.nodes[0].mesh.vertices=points;positionChange.Invoke(editor,null);ensure.Invoke(editor,null);
                    if(!ReferenceEquals(highlight,typeof(StudioModelEditor).GetField("componentHighlight",Flags).GetValue(editor))||editor.ComponentOverlayBuilds!=built)throw new Exception("Component position refresh rebuilt "+mode+" geometry");
                    var data=highlight.vertices;
                    if(mode==StudioModelEditor.ComponentMode.Face)
                    {for(int i=0;i<points.Length;i++)if(data[i]!=points[i])throw new Exception("Face highlight position not updated");}
                    else if(mode==StudioModelEditor.ComponentMode.Vertex&&data[0]!=points[0])throw new Exception("Vertex highlight position not updated");
                    UnityEngine.Debug.Log("PASS retained and refreshed "+mode+" overlay");
                }
                UnityEngine.Debug.Log("STUDIO_POSITION_UPDATE_VALIDATION_SUCCESS");
            }
        }
        catch(Exception error){UnityEngine.Debug.LogException(error);result=1;}
        finally
        {
            editor.Dispose();Undo.ClearUndo(doc);
            foreach(var node in doc.nodes){Undo.ClearUndo(node.mesh);Object.DestroyImmediate(node.mesh);}
            Object.DestroyImmediate(doc);
        }
        if(Application.isBatchMode)EditorApplication.Exit(result);
    }
}

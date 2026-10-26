using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class StudioShiftExtrudeValidation
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(StudioModelEditor editor, string name, object value) => typeof(StudioModelEditor).GetField(name, Flags).SetValue(editor, value);
    static T Get<T>(StudioModelEditor editor, string name) => (T)typeof(StudioModelEditor).GetField(name, Flags).GetValue(editor);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); Debug.Log("PASS " + message); }
    public static void RunInput()
    {
        var host=ScriptableObject.CreateInstance<StudioControlsValidationHost>();host.position=new Rect(100,100,800,600);host.ShowUtility();
        var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube);
        var doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(new StudioModelDocument.Node{name="Handle input",mesh=mesh});
        var editor=new StudioModelEditor(()=>{});var cameraObject=new GameObject("Handle input camera",typeof(Camera));
        var camera=cameraObject.GetComponent<Camera>();
        try
        {
            editor.Load(doc);editor.SelectObject(0);editor.Components.Add(0);Set(editor,"mode",StudioModelEditor.ComponentMode.Face);Set(editor,"tool",Tool.Move);Set(editor,"local",false);
            var center=editor.SelectedVertices().Select(i=>mesh.vertices[i]).Aggregate(Vector3.zero,(a,b)=>a+b)/editor.SelectedVertices().Count;
            camera.transform.position=center+new Vector3(3,2,-5);camera.transform.LookAt(center);camera.pixelRect=new Rect(0,0,800,600);
            var draw=typeof(StudioModelEditor).GetMethod("DrawHandle",Flags);
            host.Drive(()=>{Handles.SetCamera(new Rect(0,0,800,600),camera);draw.Invoke(editor,null);},EventType.Layout);
            int id=Get<int[]>(editor,"moveHandleIds")[0];
            host.Drive(()=>{
                Handles.SetCamera(new Rect(0,0,800,600),camera);
                HandleUtility.nearestControl=id;
                Event.current=new Event{type=EventType.MouseDown,button=0,modifiers=EventModifiers.Shift,mousePosition=new Vector2(400,300)};
                draw.Invoke(editor,null);
                Debug.Log("HANDLE_INPUT_AFTER_DOWN raw="+Event.current.rawType+" shift="+Event.current.shift+" hot="+GUIUtility.hotControl+" expected="+id);
                Check(GUIUtility.hotControl==id,"Real move handle captures Shift mouse-down");
                Check(Get<bool>(editor,"handleExtrudePending"),"Shift mouse-down arms extrusion after native handle consumption");
            },EventType.MouseDown);
            int originalCount=mesh.vertexCount;
            host.Drive(()=>{
                Handles.SetCamera(new Rect(0,0,800,600),camera);
                Event.current=new Event{type=EventType.MouseDrag,button=0,mousePosition=new Vector2(500,300),delta=new Vector2(100,0)};
                draw.Invoke(editor,null);
                Check(mesh.vertexCount>originalCount,"Actual handle drag creates extrusion after Shift is released");
                Check(GUIUtility.hotControl==id,"Extrusion retains the dragging handle");
            },EventType.MouseDrag);
            int extrudedCount=mesh.vertexCount;
            var firstPositions=mesh.vertices;
            host.Drive(()=>{
                Handles.SetCamera(new Rect(0,0,800,600),camera);
                Event.current=new Event{type=EventType.MouseDrag,button=0,mousePosition=new Vector2(550,300),delta=new Vector2(50,0)};
                draw.Invoke(editor,null);
                Check(mesh.vertexCount==extrudedCount && !mesh.vertices.SequenceEqual(firstPositions),"Continued drag transforms the same extrusion");
            },EventType.MouseDrag);
            host.Drive(()=>{
                Handles.SetCamera(new Rect(0,0,800,600),camera);
                Event.current=new Event{type=EventType.MouseUp,button=0,mousePosition=new Vector2(550,300)};
                draw.Invoke(editor,null);
                Check(GUIUtility.hotControl==0,"Mouse-up releases the extrusion handle");
            },EventType.MouseUp);
            Debug.Log("STUDIO_SHIFT_EXTRUDE_INPUT_VALIDATION_SUCCESS");
        }
        finally{GUIUtility.hotControl=0;editor.Dispose();Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(doc);Object.DestroyImmediate(mesh);host.Close();}
    }
    public static void Run()
    {
        var host = ScriptableObject.CreateInstance<StudioControlsValidationHost>(); host.ShowUtility();
        try { host.Drive(() => {
            foreach (bool face in new[] { false, true })
            foreach (bool local in new[] { false, true })
            foreach (Tool tool in new[] { Tool.Move, Tool.Rotate, Tool.Scale })
                CheckGesture(face, local, tool);
            CheckRejectedInteriorEdge();
        }); Debug.Log("STUDIO_SHIFT_EXTRUDE_VALIDATION_SUCCESS"); }
        finally { GUIUtility.hotControl = 0; host.Close(); }
    }
    static void CheckGesture(bool face, bool local, Tool tool)
    {
        var mesh = StudioModelDocument.BuildPrimitive(face ? PrimitiveType.Cube : PrimitiveType.Quad);
        var doc = ScriptableObject.CreateInstance<StudioModelDocument>();
        doc.nodes.Add(new StudioModelDocument.Node { name = "Extrude test", mesh = mesh,
            position = new Vector3(3, -2, 5), rotation = Quaternion.Euler(20, 35, 10), scale = new Vector3(2, 1, .7f) });
        var editor = new StudioModelEditor(() => {});
        try
        {
            editor.Load(doc); editor.SelectObject(0); Set(editor, "mode", face ? StudioModelEditor.ComponentMode.Face : StudioModelEditor.ComponentMode.Edge);
            Set(editor, "tool", tool); editor.Components.Add(0);
            var source = mesh.vertices; int originalTriangles = mesh.triangles.Length;
            var t = editor.Transforms[0]; var orientation = local ? t.rotation : Quaternion.identity;
            var selected = editor.SelectedVertices();
            var center = selected.Select(i => t.TransformPoint(source[i])).Aggregate(Vector3.zero, (a,b) => a+b) / selected.Count;
            var position = center + orientation * Vector3.up * .6f;
            var rotation = Quaternion.AngleAxis(23, orientation * Vector3.forward) * orientation;
            var scale = new Vector3(1.25f, .8f, 1.1f);
            Vector3 Expected(Vector3 sourcePoint) {
                var world = t.TransformPoint(sourcePoint);
                if (tool == Tool.Move) world += position - center;
                else if (tool == Tool.Rotate) world = center + rotation * Quaternion.Inverse(orientation) * (world-center);
                else world = center + orientation * Vector3.Scale(Quaternion.Inverse(orientation)*(world-center),scale);
                return t.InverseTransformPoint(world);
            }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            GUIUtility.hotControl = 8123; Set(editor, "handleExtrudeControl", 8123);
            typeof(StudioModelEditor).GetMethod("BeginHandleExtrusion", Flags).Invoke(editor, new object[] { position, center, rotation, orientation, scale });
            var moving = Get<HashSet<int>>(editor, "handleExtrudeVertices");
            Check(moving.Count > 0 && mesh.triangles.Length > originalTriangles, "New extrusion topology: " + face + "/" + local + "/" + tool);
            Check(Get<Tool>(editor,"tool") == tool && GUIUtility.hotControl == 8123, "Tool and captured handle preserved");
            Check(moving.All(i => source.Any(p => (Expected(p)-mesh.vertices[i]).sqrMagnitude < 1e-8f)), "New tips follow the handle transformation");
            Check(Enumerable.Range(0,mesh.vertexCount).Where(i => !moving.Contains(i)).All(i => source.Contains(mesh.vertices[i])), "Original geometry remains stationary");
            if (tool == Tool.Scale)
            {
                var next = scale * 1.2f;
                typeof(StudioModelEditor).GetMethod("ApplyScaleDrag", Flags).Invoke(editor,new object[]{next});
                var once = mesh.vertices;
                typeof(StudioModelEditor).GetMethod("ApplyScaleDrag", Flags).Invoke(editor,new object[]{next});
                Check(mesh.vertices.SequenceEqual(once), "Scale uses total factor without accumulating or re-extruding");
            }
            GUIUtility.hotControl = 0; Undo.CollapseUndoOperations(group); Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Check(mesh.vertices.SequenceEqual(source) && mesh.triangles.Length == originalTriangles, "One undo restores pre-extrusion mesh");
        }
        finally { GUIUtility.hotControl = 0; editor.Dispose(); Undo.ClearUndo(doc); Undo.ClearUndo(mesh); Object.DestroyImmediate(doc); Object.DestroyImmediate(mesh); }
    }
    static void CheckRejectedInteriorEdge()
    {
        var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube);
        var node=new StudioModelDocument.Node {mesh=mesh};
        var before=mesh.vertices; var triangles=mesh.triangles;
        try
        {
            var topology=new StudioMeshCuts(node);
            var face=topology.Faces[0].Vertices;
            bool rejected=false;
            try {topology.Extrude(node,null,new[]{(face[0],face[1])},.1f,true,true,p=>p+Vector3.up);}
            catch(InvalidOperationException){rejected=true;}
            Check(rejected && mesh.vertices.SequenceEqual(before) && mesh.triangles.SequenceEqual(triangles), "Interior edge rejected without mesh changes");
        }
        finally {Object.DestroyImmediate(mesh);}
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public static class StudioComponentOrientationValidation
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> results = new List<string>();
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); results.Add("PASS: " + message); }
    static void Mode(StudioModelEditor editor, StudioModelEditor.ComponentMode mode, params int[] selected)
    {
        editor.SelectObject(0); typeof(StudioModelEditor).GetField("mode",Flags).SetValue(editor,mode);
        editor.Components.Clear(); editor.Components.UnionWith(selected);
    }
    static Vector3 Normal(StudioModelEditor editor)
    {
        var t=editor.Transforms[0]; var v=editor.ActiveMesh.vertices;
        return Vector3.Cross(t.TransformPoint(v[1])-t.TransformPoint(v[0]),t.TransformPoint(v[2])-t.TransformPoint(v[0])).normalized;
    }
    public static void Run()
    {
        int code=0;
        try
        {
            var mesh=new Mesh();
            var tilt=Quaternion.Euler(25,10,30);
            mesh.vertices=new[]{tilt*new Vector3(-1,-1,0),tilt*new Vector3(1,-1,0),tilt*new Vector3(1,1,0),tilt*new Vector3(-1,1,0)};
            mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();
            var doc=ScriptableObject.CreateInstance<StudioModelDocument>();
            doc.nodes.Add(new StudioModelDocument.Node{name="Tilted quad",mesh=mesh,cutPolygons=new[]{0,4,0,1,2,3},cutTriangles=mesh.triangles});
            using(var editor=new StudioModelEditor(()=>{}))
            {
                editor.Load(doc);
                editor.Transforms[0].rotation=Quaternion.Euler(12,35,8);
                editor.SetToolbarLocalAxes(true);
                foreach(var selectionMode in new[]{StudioModelEditor.ComponentMode.Vertex,StudioModelEditor.ComponentMode.Edge,StudioModelEditor.ComponentMode.Face})
                {
                    Mode(editor,selectionMode,0);
                    Check(Quaternion.Angle(editor.PivotOrientation,editor.Transforms[0].rotation)<.01f,"Object orientation follows object in "+selectionMode);
                    editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.ComponentAxes,null);
                    Check(editor.ToolbarComponentAxes&&editor.ToolbarOrientationLabel=="Component","Marking menu activates Component orientation");
                    editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.ObjectAxes,null);
                    Check(!editor.ToolbarComponentAxes&&editor.ToolbarLocalAxes&&editor.ToolbarOrientationLabel=="Object","Marking menu restores Object orientation");
                }
                editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.WorldAxes,null);
                Check(!editor.ToolbarLocalAxes&&editor.ToolbarOrientationLabel=="World","World remains independent");
                editor.SetToolbarComponentAxes();
                Check(StudioPreferenceStore.GetFloat("Model.ComponentAxes",0)==1,"Component orientation saved through preference store");
                using(var reloaded=new StudioModelEditor(()=>{}))
                    Check(reloaded.ToolbarComponentAxes,"Component orientation reloads");
                Mode(editor,StudioModelEditor.ComponentMode.Face,0);
                Check(Vector3.Dot(editor.PivotOrientation*Vector3.right,Normal(editor))>.9999f,"Face gizmo follows the face normal instead of the object axes");
                Mode(editor,StudioModelEditor.ComponentMode.Vertex,0,1,2);
                Check(Vector3.Dot(editor.PivotOrientation*Vector3.right,Normal(editor))>.9999f,"Multiple vertices follow their adjoining surface normal");
                Mode(editor,StudioModelEditor.ComponentMode.Edge,0);
                var edge=(editor.Transforms[0].TransformPoint(mesh.vertices[1])-editor.Transforms[0].TransformPoint(mesh.vertices[0])).normalized;
                Check(Mathf.Abs(Vector3.Dot(editor.PivotOrientation*Vector3.right,edge))>.9999f,"Edge gizmo follows the edge direction");
                foreach(var scale in new[]{new Vector3(2,.5f,1.3f),new Vector3(-2,.5f,1.3f)})
                {
                    editor.Transforms[0].rotation=Quaternion.Euler(20,40,15);editor.Transforms[0].localScale=scale;
                    Mode(editor,StudioModelEditor.ComponentMode.Face,0);
                    Check(Vector3.Dot(editor.PivotOrientation*Vector3.right,Normal(editor))>.9999f,"Correct world normal with rotated, nonuniform or mirrored object scale "+scale);
                }
                var changed=mesh.vertices.Select(p=>Quaternion.Euler(30,0,0)*p).ToArray();mesh.vertices=changed;editor.Changed();
                Check(Vector3.Dot(editor.PivotOrientation*Vector3.right,Normal(editor))>.9999f,"Geometry edits invalidate the orientation cache");
                var custom=Quaternion.Euler(17,63,29);
                editor.ApplyPivotTarget(new StudioModelEditor.PivotTarget{orientation=custom},StudioModelEditor.PivotAction.Orient);
                Check(Quaternion.Angle(editor.PivotOrientation,custom)<.01f,"Explicit component pivot orientation overrides automatic alignment");
                editor.SetToolbarLocalAxes(true);
                Check(Quaternion.Angle(editor.PivotOrientation,editor.Transforms[0].rotation)<.01f,"Object orientation ignores custom component frame");
                editor.SetToolbarComponentAxes();
                Check(Quaternion.Angle(editor.PivotOrientation,custom)<.01f,"Component orientation retains custom frame across axis changes");
                Mode(editor,StudioModelEditor.ComponentMode.Object);
                Check(Quaternion.Angle(editor.PivotOrientation,editor.Transforms[0].rotation)<.01f,"Object mode retains object-local axes");
                Mode(editor,StudioModelEditor.ComponentMode.Face,0);
                mesh.vertices=new Vector3[4];editor.Changed();
                Check(Quaternion.Angle(editor.PivotOrientation,editor.Transforms[0].rotation)<.01f,"Degenerate component geometry falls back to a valid object frame");
            }
            UnityEngine.Object.DestroyImmediate(doc);UnityEngine.Object.DestroyImmediate(mesh);
        }
        catch(Exception e){results.Add("FAIL: "+e);code=1;}
        File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath,"../../component-orientation-result.txt")),results);EditorApplication.Exit(code);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioExtrudeDistanceGizmoValidation
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../ExtrudeGizmo/"+name));
        static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
        static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
        static object Call(object o,string n,params object[] a)=>o.GetType().GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==a.Length).Invoke(o,a);
        static StudioModelDocument Cube()
        {var d=ScriptableObject.CreateInstance<StudioModelDocument>();var m=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var f);d.nodes.Add(new StudioModelDocument.Node{name="Extrude distance gizmo",mesh=m,cutPolygons=f,cutTriangles=m.triangles});return d;}
        static void SelectFace(StudioModelEditor e,StudioModelDocument d)
        {
            e.SelectObject(0);e.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);
            var t=new StudioMeshCuts(d.nodes[0]);int f=Enumerable.Range(0,t.Faces.Count).OrderByDescending(i=>t.Faces[i].Vertices.Average(v=>t.Positions[v].x)).First();e.Components.Add(f);e.SetToolbarLocalAxes(false);e.ActivateTool(Tool.Move);e.ExtrudeSelected();Call(e,"UpdateQueuedLiveTool");
        }
        [InitializeOnLoadMethod] static void Boot()
        {if(Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.delayCall+=Run;}
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets"))return;
            File.Delete(Out("request.txt"));var log=new List<string>();void Check(bool v,string n){if(!v)throw new Exception(n);log.Add("PASS "+n);}
            try
            {
                var d=Cube();var mesh=d.nodes[0].mesh;var original=mesh.vertices;
                try
                {
                    using(var e=new StudioModelEditor(()=>{}))
                    {
                        e.Load(d);SelectFace(e,d);
                        e.Transforms[0].rotation=Quaternion.Euler(20,35,10);e.Transforms[0].localScale=new Vector3(2,3,.5f);
                        object[] frame={Vector3.zero,Vector3.zero};Check((bool)Call(e,"TryExtrudeDistanceFrame",frame),"Distance frame available at zero extrusion");
                        var center=(Vector3)frame[0];var step=(Vector3)frame[1];Check((step-e.Transforms[0].TransformVector(Vector3.right)).magnitude<1e-5f,"World step accounts for rotated nonuniform object scale");
                        foreach(float distance in new[]{.25f,.75f,-.2f,0f,.4f})
                        {
                            var perpendicular=Vector3.Cross(step,Vector3.up).normalized*.15f;
                            Call(e,"SetExtrudeDistanceFromGizmo",center+step*distance+perpendicular,center,step,0f);Call(e,"UpdateQueuedLiveTool");
                            Check(Mathf.Abs((float)Get(e,"extrusionDistance")-distance)<1e-5f&&Mathf.Abs(ToolChannelPane.GetFloatDefault("Extrude","Distance",0)-distance)<1e-5f,"Gizmo scalar and Distance field agree "+distance);
                            Check((bool)Get(e,"liveValid")&&Get(e,"extrudeToolPane")!=null&&d.nodes[0].modifiers?.HasHistory!=true,"Drag remains valid uncommitted Extrude preview "+distance);
                            Check(distance==0?mesh.vertices.SequenceEqual(original):mesh.vertexCount>original.Length,"Preview updates from captured source, including zero and negative "+distance);
                        }
                        e.ActivateTool(Tool.Rotate);Check(d.nodes[0].modifiers.operations.Last().kind=="Extrude"&&Mathf.Abs(d.nodes[0].modifiers.operations.Last().parameters.x-.4f)<1e-5f,"Tool switch commits gizmo distance as one Extrude modifier");
                        var committed=mesh.vertices;e.Components.Clear();e.Components.Add(0);e.ActivateTool(Tool.Move);e.ExtrudeSelected();Call(e,"UpdateQueuedLiveTool");
                        object[] nextFrame={Vector3.zero,Vector3.zero};Call(e,"TryExtrudeDistanceFrame",nextFrame);var c=(Vector3)nextFrame[0];var s=(Vector3)nextFrame[1];Call(e,"SetExtrudeDistanceFromGizmo",c+s*.2f,c,s,0f);Call(e,"UpdateQueuedLiveTool");((ToolChannelPane)Get(e,"extrudeToolPane")).Close();
                        Check(mesh.vertices.SequenceEqual(committed),"Cancel restores geometry after gizmo adjustment");
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(d);}
                var visual=Cube();var host=ScriptableObject.CreateInstance<AnimationStudioWindow>();Set(host,"modelingMode",true);Set(host,"modelDocument",visual);Call(host,"OpenViewport");
                var editor=(StudioModelEditor)Get(host,"modelEditor");SelectFace(editor,visual);
                var viewport=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Single(v=>v.Studio==host);viewport.position=new Rect(100,100,1200,850);viewport.LookAtDirect(Vector3.zero,Quaternion.Euler(20,-35,0),2);viewport.Repaint();
                Check(editor.ToolOptionsTitle=="Extrude"&&editor.ToolbarToolSelected(Tool.Move),"Native distance-drag scene prepared");
            }
            catch(Exception ex){log.Add("FAIL "+ex);}
            File.WriteAllLines(Out("result.txt"),log);
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public sealed class StudioSplineSelectionExitValidation:EditorWindow
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static string Root=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../../SplineSelectionExit"));
        static readonly List<string> report=new List<string>();bool ran;
        static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,F).Invoke(o,a);
        static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
        static T Get<T>(object o,string n)=>(T)o.GetType().GetField(n,F).GetValue(o);
        static void Check(bool b,string n){if(!b)throw new Exception(n);report.Add("PASS "+n);}
        [InitializeOnLoadMethod] static void Boot()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/Splines/SplineProject/Assets"))return;
            EditorApplication.delayCall+=()=>{string path=Path.Combine(Root,"request.txt");if(File.Exists(path)){File.Delete(path);var w=CreateInstance<StudioSplineSelectionExitValidation>();w.titleContent=new GUIContent("Spline exit check");w.ShowUtility();w.Repaint();}};
        }
        static StudioSpline Curve()=>new StudioSpline{knots=new List<StudioSpline.Knot>{new StudioSpline.Knot(),new StudioSpline.Knot{position=Vector3.right}}};
        void OnGUI()
        {
            if(ran||Event.current.type!=EventType.Repaint)return;ran=true;
            StudioModelEditor editor=null;StudioModelDocument doc=null;Mesh mesh=null;
            var originalEvent=Event.current;
            try
            {
                report.Clear();mesh=new Mesh{vertices=new[]{Vector3.zero,Vector3.right,Vector3.up},triangles=new[]{0,1,2}};mesh.RecalculateNormals();
                doc=CreateInstance<StudioModelDocument>();doc.nodes.Add(new StudioModelDocument.Node{name="Spline A",spline=Curve()});doc.nodes.Add(new StudioModelDocument.Node{name="Spline B",spline=Curve()});doc.nodes.Add(new StudioModelDocument.Node{name="Mesh",mesh=mesh});
                editor=new StudioModelEditor(()=>{});editor.Load(doc);
                editor.SelectObject(0);Call(editor,"EditSpline");Call(editor,"ReconcileSplineEditingSelection");Check(Get<bool>(editor,"splineEditing"),"Unchanged spline selection retains knot editing");
                editor.SelectObject(2);Check(!Get<bool>(editor,"splineEditing"),"Hierarchy/shared object selection exits spline editing");
                editor.SelectObject(0);Call(editor,"EditSpline");
                var objects=Get<HashSet<int>>(editor,"objects");objects.Clear();objects.Add(1);typeof(StudioModelEditor).GetProperty("Active",F).SetValue(editor,1);
                Call(editor,"SelectionDisplayChanged",true);Check(!Get<bool>(editor,"splineEditing")&&Get<int>(editor,"splineOwner")==-1,"Viewport/history selection change exits instead of rebinding to another curve");
                foreach(var action in new[]{StudioShortcut.Move,StudioShortcut.Rotate,StudioShortcut.Scale,StudioShortcut.Vertex,StudioShortcut.Edge,StudioShortcut.Face,StudioShortcut.Object})
                {
                    editor.SelectObject(2);Set(editor,"splineEditing",true);Set(editor,"splineOwner",0);
                    var binding=StudioShortcuts.Bindings(action).First();var mods=EventModifiers.None;
                    if((binding.modifiers&StudioModifiers.Shift)!=0)mods|=EventModifiers.Shift;if((binding.modifiers&StudioModifiers.Action)!=0)mods|=Application.platform==RuntimePlatform.OSXEditor?EventModifiers.Command:EventModifiers.Control;if((binding.modifiers&StudioModifiers.Alt)!=0)mods|=EventModifiers.Alt;
                    Event.current=new Event{type=EventType.KeyDown,keyCode=binding.key,modifiers=mods};
                    Check(editor.HandleModelKeyboard(null),action+" configured key reaches model keyboard routing");
                    Check(!Get<bool>(editor,"splineEditing"),action+" key releases stale spline ownership");
                    if(action==StudioShortcut.Move||action==StudioShortcut.Rotate||action==StudioShortcut.Scale)
                        Check(Get<Tool>(editor,"tool")== (action==StudioShortcut.Move?Tool.Move:action==StudioShortcut.Rotate?Tool.Rotate:Tool.Scale),action+" activates requested transform tool");
                    else Check(editor.ToolbarComponentMode==(action==StudioShortcut.Vertex?StudioModelEditor.ComponentMode.Vertex:action==StudioShortcut.Edge?StudioModelEditor.ComponentMode.Edge:action==StudioShortcut.Face?StudioModelEditor.ComponentMode.Face:StudioModelEditor.ComponentMode.Object),action+" activates requested component mode");
                    Event.current=originalEvent;
                }
                Check(doc.nodes[0].spline.knots.Count==2&&doc.nodes[1].spline.knots.Count==2&&mesh.vertexCount==3,"Selection exits and shortcuts preserve geometry");
                report.Add("PASS "+report.Count+" checks");
            }
            catch(Exception e){report.Add("FAIL "+e);}
            finally{Event.current=originalEvent;File.WriteAllLines(Path.Combine(Root,"result.txt"),report);editor?.Dispose();if(doc)DestroyImmediate(doc);if(mesh)DestroyImmediate(mesh);EditorApplication.delayCall+=Close;}
        }
    }
}

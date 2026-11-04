using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioHierarchyVisibilityToggleValidation
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../HierarchyToggle/"+name));
        static StudioModelDocument.Node Cube(string name)
        {var m=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var p);return new StudioModelDocument.Node{name=name,mesh=m,cutPolygons=p,cutTriangles=m.triangles};}
        [InitializeOnLoadMethod]static void Boot(){if(Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.delayCall+=Run;}
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets"))return;
            File.Delete(Out("request.txt"));var log=new List<string>();void Check(bool value,string text){if(!value)throw new Exception(text);log.Add("PASS "+text);}
            try
            {
                var d=ScriptableObject.CreateInstance<StudioModelDocument>();var child=Cube("Child");child.parent=1;d.nodes.Add(child);d.nodes.Add(new StudioModelDocument.Node{name="Parent"});d.nodes.Add(Cube("Other"));var points=child.mesh.vertices;
                try
                {
                    using(var editor=new StudioModelEditor(()=>{}))
                    {
                        editor.Load(d);editor.SelectObject(0);editor.ToggleHierarchySelectionVisibility();
                        var selection=(HashSet<int>)typeof(StudioModelEditor).GetField("objects",F).GetValue(editor);
                        Check(!child.active&&!editor.Transforms[0].gameObject.activeInHierarchy&&selection.SetEquals(new[]{0}),"Hide synchronizes document/renderer and retains hierarchy selection");
                        editor.ToggleHierarchySelectionVisibility();Check(child.active&&editor.Transforms[0].gameObject.activeInHierarchy,"Second toggle restores selected object");
                        editor.SetHierarchyVisibility(1,false);editor.ToggleHierarchySelectionVisibility();Check(child.active&&d.nodes[1].active&&editor.Transforms[0].gameObject.activeInHierarchy,"Showing a hidden child enables required ancestors");
                        editor.SetHierarchyVisibility(2,false);selection.Add(2);editor.ToggleHierarchySelectionVisibility();Check(!child.active&&!d.nodes[2].active,"Mixed visible/hidden selection hides selected objects together");
                        editor.ToggleHierarchySelectionVisibility();Check(child.active&&d.nodes[2].active&&selection.SetEquals(new[]{0,2}),"Hidden multi-selection shows together and remains selected");
                        editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);editor.Components.Add(0);editor.ToggleHierarchySelectionVisibility();Check(!child.active&&editor.Components.Contains(0),"Hierarchy toggle uses object scope while retaining component selection");
                        Check(child.mesh.vertices.SequenceEqual(points),"Visibility toggling preserves source geometry");selection.Clear();var flags=d.nodes.Select(n=>n.active).ToArray();editor.ToggleHierarchySelectionVisibility();Check(flags.SequenceEqual(d.nodes.Select(n=>n.active)),"Empty hierarchy selection changes nothing");
                    }
                }
                finally{foreach(var n in d.nodes)if(n.mesh)UnityEngine.Object.DestroyImmediate(n.mesh);UnityEngine.Object.DestroyImmediate(d);}
                var visual=ScriptableObject.CreateInstance<StudioModelDocument>();visual.nodes.Add(Cube("H toggles this row"));var host=ScriptableObject.CreateInstance<AnimationStudioWindow>();typeof(AnimationStudioWindow).GetField("modelingMode",F).SetValue(host,true);typeof(AnimationStudioWindow).GetField("modelDocument",F).SetValue(host,visual);typeof(AnimationStudioWindow).GetMethod("OpenViewport",F).Invoke(host,null);var model=(StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor",F).GetValue(host);model.SelectObject(0);model.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Object);var view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Single(v=>v.Studio==host);view.position=new Rect(100,100,1200,850);view.LookAtDirect(Vector3.zero,Quaternion.Euler(20,-35,0),2);view.Repaint();
            }
            catch(Exception e){log.Add("FAIL "+e);}
            File.WriteAllLines(Out("result.txt"),log);
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;
public static class StudioIsolationOwnedPaneValidation
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
    static AnimationStudioWindow owner; static MashBoxStudioViewport view; static StudioModelDocument doc;
    static StudioModelEditor editor; static ToolChannelPane pane; static IntPtr ownerHandle,paneHandle;
    static double due; static int phase,checks; static string errors="";
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,F).SetValue(target,value);
    static void Flags(params bool[] expected)=>Check(doc.nodes.Select(n=>n.active).SequenceEqual(expected),"Visibility: "+string.Join(",",doc.nodes.Select(n=>n.active)));
    static void Log(string text,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors+=text+"\n"+stack+"\n";}
    public static void Run(){EditorApplication.delayCall+=Begin;}
    static void Begin()
    {
        try
        {
            doc=ScriptableObject.CreateInstance<StudioModelDocument>();
            var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
            doc.nodes.Add(new StudioModelDocument.Node{name="Root A",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles});
            doc.nodes.Add(new StudioModelDocument.Node{name="Child A",parent=0,active=false});
            doc.nodes.Add(new StudioModelDocument.Node{name="Root B"});
            doc.nodes.Add(new StudioModelDocument.Node{name="Child B",parent=2});
            doc.nodes.Add(new StudioModelDocument.Node{name="Hidden C",active=false});
            doc.nodes.Add(new StudioModelDocument.Node{name="Root D"});
            owner=ScriptableObject.CreateInstance<AnimationStudioWindow>();Set(owner,"modelingMode",true);Set(owner,"modelDocument",doc);
            typeof(AnimationStudioWindow).GetMethod("OpenViewport",F).Invoke(owner,null);
            view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Single(v=>v.Studio==owner);view.position=new Rect(0,0,1200,800);
            editor=owner.ToolbarModelEditor;editor.SelectObject(0);view.Focus();
            Application.logMessageReceived+=Log;due=EditorApplication.timeSinceStartup+2;EditorApplication.update+=Tick;
        }
        catch(Exception e){Finish(e);}
    }
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<due)return;due=EditorApplication.timeSinceStartup+.5;
        try
        {
            switch(phase++)
            {
                case 0:
                    Flags(true,false,true,true,false,true);
                    editor.ToggleHierarchyIsolation(0);Flags(true,true,false,false,false,false);
                    editor.ToggleHierarchyIsolation(0);Flags(true,false,true,true,false,true);
                    Undo.PerformUndo();Flags(true,true,false,false,false,false);
                    Undo.PerformRedo();Flags(true,false,true,true,false,true);
                    editor.SelectObject(0);editor.SelectObject(2,true);
                    editor.ToggleHierarchyIsolation(2);Flags(true,true,true,true,false,false);
                    editor.ToggleHierarchyIsolation(2);Flags(true,false,true,true,false,true);
                    editor.SelectObject(0);editor.ToggleHierarchyIsolation(0);editor.ToggleHierarchyIsolation(5);Flags(false,false,false,false,false,true);
                    editor.ToggleHierarchyIsolation(5);Flags(true,false,true,true,false,true);
                    editor.ToggleHierarchyIsolation(1);Flags(true,true,false,false,false,false);
                    editor.ToggleHierarchyIsolation(1);Flags(true,false,true,true,false,true);
                    editor.SelectObject(0);view.Focus();
                    var hierarchy=view.rootVisualElement.Q("mashbox-animation-rig-controls");
                    var point=new Vector2(hierarchy.worldBound.xMin+21,hierarchy.worldBound.yMin+68);
                    view.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=point,modifiers=EventModifiers.Alt});
                    view.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=point,modifiers=EventModifiers.Alt});
                    Flags(true,true,false,false,false,false);
                    view.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=point,modifiers=EventModifiers.Alt});
                    view.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=point,modifiers=EventModifiers.Alt});
                    Flags(true,false,true,true,false,true);
                    ownerHandle=GetActiveWindow();
                    pane=ToolChannelPane.Open("Owned pane",()=>GUILayout.Label("This stays above Studio"),owner:view);
                    paneHandle=GetActiveWindow();
                    Check(ownerHandle!=IntPtr.Zero&&paneHandle!=IntPtr.Zero&&ownerHandle!=paneHandle,"Separate native Studio and pane handles");
                    Check(GetWindow(paneHandle,4)==ownerHandle,"Pane is natively owned by Studio");
                    view.Focus();view.Repaint();break;
                case 1:
                    Check(pane&&GetWindow(paneHandle,4)==ownerHandle,"Ownership persists after Studio refocus");
                    bool above=false;for(var h=GetWindow(ownerHandle,3);h!=IntPtr.Zero;h=GetWindow(h,3))if(h==paneHandle){above=true;break;}
                    Check(above,"Pane remains above focused Studio in native Z order");
                    Check(string.IsNullOrEmpty(errors),"No native GUI errors: "+errors);
                    Finish(null);break;
            }
        }
        catch(Exception e){Finish(e);}
    }
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
        File.WriteAllText(Path.Combine(Application.dataPath,"../isolation-owned-pane-result.txt"),error==null?"PASS "+checks+" visibility/native ownership checks":"After "+checks+" checks: "+error+"\n"+errors);
        if(pane)pane.Close();if(editor!=null)editor.Load(null);if(view)view.Close();if(owner)Object.DestroyImmediate(owner);
        if(doc){foreach(var n in doc.nodes)if(n.mesh)Object.DestroyImmediate(n.mesh);Object.DestroyImmediate(doc);}
        Undo.ClearAll();EditorApplication.Exit(error==null?0:1);
    }
}

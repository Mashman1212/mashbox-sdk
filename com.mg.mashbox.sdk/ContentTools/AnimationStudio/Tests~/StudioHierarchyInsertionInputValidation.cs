using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public class StudioHierarchyInsertionTestWindow : EditorWindow
{
    internal StudioModelEditor editor;
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    void OnGUI()
    {
        if(editor==null)return;
        typeof(StudioModelEditor).GetMethod("HandleHierarchyDragCancellation",Flags).Invoke(editor,null);
        editor.HandleModelDrop(null,editor.Load);
        for(int i=0;i<editor.Document.nodes.Count;i++)
            typeof(StudioModelEditor).GetMethod("DrawHierarchyDragRow",Flags).Invoke(editor,new object[]{new Rect(10,30+i*24,300,22),new GUIContent(editor.Document.nodes[i].name),EditorStyles.miniButton,i});
        typeof(StudioModelEditor).GetMethod("HandleHierarchyDrop",Flags).Invoke(editor,new object[]{new Rect(10,180,300,180),-1});
    }
}
public static class StudioHierarchyInsertionInputValidation
{
    static readonly List<string> results=new List<string>();
    static StudioHierarchyInsertionTestWindow window;
    static StudioModelEditor editor;
    static int step,button;
    static double next;
    static string errors="";
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
    static void MatrixNear(Matrix4x4 a,Matrix4x4 b)
    {for(int i=0;i<16;i++)Check(Mathf.Abs(a[i]-b[i])<.001f,"World matrix changed at "+i+": "+a[i]+" vs "+b[i]);}
    public static void Run()
    {
        try
        {
            var uiDoc=ScriptableObject.CreateInstance<StudioModelDocument>();uiDoc.AddEmptyTransform();uiDoc.AddEmptyTransform();uiDoc.AddEmptyTransform();
            Debug.Log("Hierarchy drag validation: native UI checks");
            editor=new StudioModelEditor(()=>{if(window)window.Repaint();});editor.Load(uiDoc);
            // Native OLE dragging blocks this synthetic sender until a physical mouse
            // release. Exercise IMGUI capture/drop events with only that handoff stubbed.
            typeof(StudioModelEditor).GetField("beginHierarchyDrag",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(editor,(Action<string>)(_=>{}));
            window=ScriptableObject.CreateInstance<StudioHierarchyInsertionTestWindow>();window.editor=editor;window.position=new Rect(200,150,340,400);window.ShowUtility();
            Application.logMessageReceived+=Log;EditorApplication.update+=Tick;next=EditorApplication.timeSinceStartup+1;
        }
        catch(Exception e){Finish(e.ToString());}
    }
    static void Mouse(EventType type,Vector2 position)=>window.SendEvent(new Event{type=type,button=button,mousePosition=position,delta=new Vector2(12,0)});
    static void Log(string text,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors+=text+"\n"+stack;}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.15;
        try
        {
            Debug.Log("Hierarchy drag validation: button " + button + " step " + step);
            var source=new Vector2(80,40);var target=new Vector2(80,64);var blank=new Vector2(80,240);var outdent=new Vector2(5,51);
            switch(step++)
            {
                case 0:Mouse(EventType.MouseDown,source);break;
                case 1:Mouse(EventType.MouseDrag,source+new Vector2(15,0));Check(DragAndDrop.GetGenericData("MashBoxStudio.HierarchyDrag")!=null,"Native button "+button+" starts internal drag");break;
                case 2:Mouse(EventType.DragUpdated,target);Check(DragAndDrop.visualMode==DragAndDropVisualMode.Move,"Target accepts drag");break;
                case 3:Mouse(EventType.DragPerform,target);Check(editor.Document.nodes[0].parent==1,"Drop reparents for button "+button);break;
                case 4:Mouse(EventType.MouseDown,source);break;
                case 5:Mouse(EventType.MouseDrag,source+new Vector2(15,0));break;
                case 6:Mouse(EventType.DragUpdated,outdent);Mouse(EventType.DragPerform,outdent);Check(editor.Document.nodes[0].parent==-1&&editor.Document.OrderedChildren(-1).SequenceEqual(new[]{1,0,2}),"Leftward row-edge drop unparents and inserts after ancestor for button "+button);break;
                case 7:Mouse(EventType.MouseDown,source);break;
                case 8:Mouse(EventType.MouseDrag,source+new Vector2(15,0));break;
                case 9:window.SendEvent(new Event{type=EventType.KeyDown,keyCode=KeyCode.Escape});Check(DragAndDrop.GetGenericData("MashBoxStudio.HierarchyDrag")==null&&editor.Document.nodes[0].parent==-1,"Escape cancels without editing");break;
                case 10:editor.ReparentHierarchy(new[]{0},1);Mouse(EventType.MouseDown,source);break;
                case 11:Mouse(EventType.MouseDrag,source+new Vector2(15,0));break;
                case 12:Mouse(EventType.DragUpdated,blank);Mouse(EventType.DragPerform,blank);Check(editor.Document.nodes[0].parent==-1,"Blank area unparent failed");break;
                case 13:
                    results.Add("PASS: IMGUI "+(button==0?"left":"middle")+" mouse drag starts, parent drop, leftward outdent/insertion, root drop, and Escape cancel (OS handoff stubbed).");
                    if(button==0){button=2;step=0;break;}
                    Check(string.IsNullOrEmpty(errors),errors);Finish(null);break;
            }
        }
        catch(Exception e){Finish(e.ToString());}
    }
    static void Finish(string error)
    {
        EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
        if(error!=null)results.Add("FAIL: "+error+"\n"+errors);
        if(window)window.Close();editor?.Dispose();
        File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath,"../../hierarchy-insertion-input-result.txt")),results);EditorApplication.delayCall+=()=>EditorApplication.Exit(error==null?0:1);
    }
}

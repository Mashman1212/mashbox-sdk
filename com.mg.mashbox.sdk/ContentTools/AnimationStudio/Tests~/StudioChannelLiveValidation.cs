using System;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
public static class StudioChannelLiveValidation
{
    const BindingFlags Instance=BindingFlags.Instance|BindingFlags.NonPublic;
    static StudioModelDocument document;
    static StudioModelEditor editor;
    static ChannelWindow window;
    static int step,checks,repaints;
    static double next;
    static Vector2 origin;
    static float first,final;
    static void Check(bool condition,string text){if(!condition)throw new Exception(text);checks++;Debug.Log("PASS "+text);}
    public static void Run(){EditorApplication.delayCall+=Begin;}
    static void Begin()
    {
        try
        {
            document=ScriptableObject.CreateInstance<StudioModelDocument>();document.hideFlags=HideFlags.HideAndDontSave;
            document.nodes.Add(new StudioModelDocument.Node{name="Live channel cube",mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube)});
            editor=new StudioModelEditor(()=>repaints++);editor.Load(document);editor.SelectObject(0);
            Undo.ClearUndo(document);
            editor.SetObjectChannels(Vector3.one,new Vector3(0,30,0),Vector3.one*2,false);
            editor.PerformWorkspaceHistory(false);
            Check(document.nodes[0].position==Vector3.zero&&editor.Transforms[0].localScale==Vector3.one&&editor.Transforms[0].gameObject.activeSelf,"Workspace Undo restores position, scale and visibility");
            editor.PerformWorkspaceHistory(true);
            Check(document.nodes[0].position==Vector3.one&&Quaternion.Angle(editor.Transforms[0].localRotation,Quaternion.Euler(0,30,0))<.001f,"Workspace Redo restores channels and the preview transform");
            editor.SetObjectChannels(Vector3.zero,Vector3.zero,Vector3.one,true);Undo.ClearUndo(document);
            window=ScriptableObject.CreateInstance<ChannelWindow>();window.position=new Rect(100,100,440,200);window.ShowUtility();window.Focus();
            next=EditorApplication.timeSinceStartup+1;EditorApplication.update+=Tick;
        }
        catch(Exception error){Finish(error);}
    }
    static void Mouse(EventType type,Vector2 p,Vector2 delta=default)=>window.SendEvent(new Event{type=type,button=0,mousePosition=p,delta=delta});
    static void Key(KeyCode key,char character='\0',bool control=false)=>window.SendEvent(new Event{type=EventType.KeyDown,keyCode=key,character=character,control=control});
    static void Paint(){window.SendEvent(new Event{type=EventType.Layout});window.SendEvent(new Event{type=EventType.Repaint});}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.08;
        try
        {
            switch(step++)
            {
                case 0: Paint();origin=window.labelPoint;Mouse(EventType.MouseDown,origin);break;
                case 1:
                    int before=repaints;Mouse(EventType.MouseDrag,origin+Vector2.right*10,Vector2.right*10);Mouse(EventType.MouseDrag,origin+Vector2.right*60,Vector2.right*50);first=document.nodes[0].position.x;
                    Check(first!=0&&Mathf.Approximately(editor.Transforms[0].localPosition.x,first)&&repaints>before,"Label drag applies channels and requests viewport redraw before mouse-up");break;
                case 2:
                    Mouse(EventType.MouseDrag,origin+Vector2.right*120,Vector2.right*60);final=document.nodes[0].position.x;
                    Check(final!=first,"Successive drag events update the preview continuously");break;
                case 3:
                    Mouse(EventType.MouseUp,origin+Vector2.right*120);editor.PerformWorkspaceHistory(false);
                    Check(Mathf.Abs(document.nodes[0].position.x)<.0001f,"One Undo restores the start of the entire channel drag");
                    editor.PerformWorkspaceHistory(true);Check(Mathf.Approximately(document.nodes[0].position.x,final),"One Redo reapplies the whole channel drag");
                    window.focusChannel=true;Paint();break;
                case 4: Key(KeyCode.A,'a',true);Key(KeyCode.Alpha7,'7');
                    Check(Mathf.Approximately(document.nodes[0].position.x,7),"Typed numeric value applies before Enter or focus loss");break;
                case 5: Key(KeyCode.Z,'\0',true);
                    Check(Mathf.Approximately(document.nodes[0].position.x,final),"Ctrl+Z from the focused numeric field undoes the channel edit");break;
                case 6: Key(KeyCode.Y,'\0',true);
                    Check(Mathf.Approximately(document.nodes[0].position.x,7),"Ctrl+Y redoes the channel edit");
                    editor.PerformWorkspaceHistory(false);Paint();editor.PerformWorkspaceHistory(true);Paint();
                    Check(Mathf.Approximately(editor.Transforms[0].localPosition.x,7),"Edit menu history path refreshes the preview without stale field values");Finish(null);break;
            }
        }
        catch(Exception error){Finish(error);}
    }
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;
        if(error!=null)Debug.LogException(error);else Debug.Log("STUDIO_LIVE_CHANNEL_VALIDATION_SUCCESS "+checks+" checks");
        if(window)window.Close();editor?.Dispose();
        if(document){Undo.ClearUndo(document);foreach(var node in document.nodes)if(node.mesh)Object.DestroyImmediate(node.mesh);Object.DestroyImmediate(document);}
        if(Application.isBatchMode)EditorApplication.Exit(error==null?0:1);
    }
    public class ChannelWindow:EditorWindow
    {
        public Vector2 labelPoint;
        public bool focusChannel;
        void OnGUI()
        {
            var eventType=Event.current.type;var pointer=Event.current.mousePosition;
            typeof(StudioModelEditor).GetMethod("HandleChannelHistory",Instance).Invoke(editor,null);
            var node=document.nodes[0];EditorGUI.BeginChangeCheck();
            var value=(Vector3)typeof(StudioModelEditor).GetMethod("DrawChannels",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"Position",node.position});
            if(Event.current.type==EventType.Repaint){var row=GUILayoutUtility.GetLastRect();labelPoint=new Vector2(row.x+Mathf.Clamp(row.width*.4f,100,150)+5,row.center.y);}
            bool changed=EditorGUI.EndChangeCheck();
            if(eventType==EventType.MouseDown||eventType==EventType.MouseDrag||eventType==EventType.MouseUp)Debug.Log($"CHANNEL_EVENT {eventType} pointer={pointer} label={labelPoint} value={value} changed={changed} hot={GUIUtility.hotControl}");
            if(changed)editor.SetObjectChannels(value,node.rotation.eulerAngles,node.scale,node.active);
            typeof(StudioModelEditor).GetMethod("FinishInactiveChannelEdit",Instance).Invoke(editor,null);
            if(focusChannel){EditorGUI.FocusTextInControl("StudioChannel:Position:0");focusChannel=false;}
            // Match Studio's workspace shortcut routing when the text field releases focus.
            if(Event.current.isKey&&!EditorGUIUtility.editingTextField)editor.HandleModelKeyboard(null);
        }
    }
}

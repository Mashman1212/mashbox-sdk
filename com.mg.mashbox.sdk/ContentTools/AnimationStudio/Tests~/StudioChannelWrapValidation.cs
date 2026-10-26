using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
public static class StudioChannelWrapValidation
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static StudioModelEditor editor;
    static StudioModelDocument document;
    static WrapWindow window;
    static int scenario,step,checks;
    static double next;
    static Vector2 origin;
    static Vector3 initial;
    static float previous,increment;
    static void Check(bool pass,string text){if(!pass)throw new Exception(text);checks++;Debug.Log("PASS "+text);}
    public static void Run(){EditorApplication.delayCall+=Begin;}
    static void Begin()
    {
        document=ScriptableObject.CreateInstance<StudioModelDocument>();document.hideFlags=HideFlags.HideAndDontSave;
        document.nodes.Add(new StudioModelDocument.Node{name="Wrapping channel cube",mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube)});
        editor=new StudioModelEditor(()=>{});editor.Load(document);editor.SelectObject(0);
        window=ScriptableObject.CreateInstance<WrapWindow>();window.position=new Rect(100,100,500,200);window.ShowUtility();window.Focus();
        next=EditorApplication.timeSinceStartup+1;EditorApplication.update+=Tick;
    }
    static void Mouse(EventType type,Vector2 p,Vector2 delta=default)=>window.SendEvent(new Event{type=type,button=0,mousePosition=p,delta=delta});
    static void Paint(){window.SendEvent(new Event{type=EventType.Layout});window.SendEvent(new Event{type=EventType.Repaint});}
    static Vector3 Value=>window.rotation?document.nodes[0].RotationChannels:document.nodes[0].scale;
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.025;
        try
        {
            if(step==0)
            {
                window.axis=scenario%3;window.rotation=scenario<3;
                initial=window.rotation?new Vector3(17,-23,31):Vector3.one*2;
                if(window.rotation)initial[window.axis]=89;
                editor.SetObjectChannels(Vector3.zero,window.rotation?initial:Vector3.zero,window.rotation?Vector3.one:initial,true);
                Undo.ClearUndo(document);Paint();origin=window.labelPoint;
                Mouse(EventType.MouseDown,origin);
                Mouse(EventType.MouseDrag,origin+Vector2.right*10,Vector2.right*10);
                previous=Value[window.axis];step++;return;
            }
            if(step<=8)
            {
                // Absolute pointer coordinates teleport across both edges; each event
                // still carries the same real relative movement used by mouse jumping.
                float x=step%2==0?-2500:2500;
                float distance=window.rotation?500:20;
                Mouse(EventType.MouseDrag,new Vector2(x,origin.y),Vector2.right*distance);
                var value=Value;float delta=value[window.axis]-previous;
                Check(delta>0,"Wrapped "+(window.rotation?"rotation":"scale")+" axis "+window.axis+" continues forward at step "+step);
                if(step==1)increment=delta;else Check(Mathf.Abs(delta-increment)<Mathf.Max(.01f,increment*.01f),"Screen wrapping preserves the accumulated relative increment");
                for(int a=0;a<3;a++)if(a!=window.axis)Check(value[a]==initial[a],"Numeric edit retains untouched axis "+a);
                if(window.rotation)Check(Quaternion.Angle(editor.Transforms[0].localRotation,Quaternion.Euler(value))<.05f,"Preview follows the authored Euler channels through singularities and full turns");
                previous=value[window.axis];step++;return;
            }
            var final=Value;
            Mouse(EventType.MouseUp,new Vector2(2500,origin.y));
            editor.PerformWorkspaceHistory(false);Check(Vector3.Distance(Value,initial)<.001f,"One Undo restores the full wrapped drag");
            editor.PerformWorkspaceHistory(true);Check(Vector3.Distance(Value,final)<.001f,"Redo preserves channel values beyond canonical Euler ranges");
            if(++scenario<6){step=0;return;}
            GizmoHints();RoundTrip();Finish(null);
        }
        catch(Exception error){Finish(error);}
    }
    static void GizmoHints()
    {
        var authored=new Vector3(810,-450,721);
        editor.SetObjectChannels(Vector3.zero,authored,Vector3.one,true);
        editor.Transforms[0].localPosition=Vector3.right;
        document.ReadTransform(0,editor.Transforms[0]);
        Check(document.nodes[0].RotationChannels==authored,"Moving or scaling the object preserves authored rotation turns");
        editor.SetObjectChannels(Vector3.zero,Vector3.zero,Vector3.one,true);
        editor.Transforms[0].localRotation=Quaternion.Euler(.01f,0,0);
        document.ReadTransform(0,editor.Transforms[0]);
        Check(Mathf.Abs(document.nodes[0].RotationChannels.x-.01f)<.0005f,"Fine gizmo rotations update the channel values");
        editor.Transforms[0].localRotation*=Quaternion.AngleAxis(25,Vector3.up);
        document.ReadTransform(0,editor.Transforms[0]);
        Check(StudioModelDocument.Node.SameRotation(Quaternion.Euler(document.nodes[0].RotationChannels),editor.Transforms[0].localRotation),"Local-axis gizmo rotations can update the Euler representation without stale channel values");
    }
    static void RoundTrip()
    {
        var authored=new Vector3(810,-450,721);
        editor.SetObjectChannels(Vector3.zero,authored,Vector3.one,true);
        string folder=StudioModelDocument.WorkspaceFolder;Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"RotationWrapValidation-"+Guid.NewGuid().ToString("N")+".workspace");
        string legacy=path+".legacy.workspace";
        StudioModelDocument restored=null,old=null;
        try
        {
            StudioWorkspaceText.Write(document,path);restored=StudioWorkspaceText.Read(path);
            Check(restored.nodes[0].RotationChannels==authored,"Workspace save/load preserves authored turns and independent rotation channels");
            var text=File.ReadAllText(path);string member="\"rotationChannels\":[810,-450,721],";
            Check(text.Contains(member),"Workspace rotation hint is compact readable text");
            File.WriteAllText(legacy,text.Replace(member,""));old=StudioWorkspaceText.Read(legacy);
            Check(Quaternion.Angle(old.nodes[0].rotation,document.nodes[0].rotation)<.05f,"Existing workspaces without Euler hints still load with their original orientation");
        }
        finally
        {
            foreach(var doc in new[]{restored,old})if(doc){foreach(var mesh in doc.nodes.Select(n=>n.mesh).Where(m=>m).Distinct())Object.DestroyImmediate(mesh);Object.DestroyImmediate(doc);}
            if(File.Exists(path))File.Delete(path);if(File.Exists(legacy))File.Delete(legacy);
        }
    }
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;
        if(error!=null)Debug.LogException(error);else Debug.Log("STUDIO_CHANNEL_WRAP_VALIDATION_SUCCESS "+checks+" checks");
        if(window)window.Close();editor.Dispose();Undo.ClearUndo(document);
        foreach(var node in document.nodes)if(node.mesh)Object.DestroyImmediate(node.mesh);Object.DestroyImmediate(document);
        if(Application.isBatchMode)EditorApplication.Exit(error==null?0:1);
    }
    public class WrapWindow:EditorWindow
    {
        public int axis;public bool rotation;public Vector2 labelPoint;
        void OnGUI()
        {
            typeof(StudioModelEditor).GetMethod("HandleChannelHistory",Flags).Invoke(editor,null);
            var node=document.nodes[0];EditorGUI.BeginChangeCheck();
            string label=rotation?"Rotation":"Scale";
            var value=(Vector3)typeof(StudioModelEditor).GetMethod("DrawChannels",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{label,rotation?node.RotationChannels:node.scale});
            if(Event.current.type==EventType.Repaint){var row=GUILayoutUtility.GetLastRect();float title=Mathf.Clamp(row.width*.4f,100,150);float width=(row.width-title-8)/3;labelPoint=new Vector2(row.x+title+axis*(width+4)+5,row.center.y);}
            if(EditorGUI.EndChangeCheck())editor.SetObjectChannels(node.position,rotation?value:node.RotationChannels,rotation?node.scale:value,node.active);
            typeof(StudioModelEditor).GetMethod("FinishInactiveChannelEdit",Flags).Invoke(editor,null);
        }
    }
}

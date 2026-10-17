using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public static class StudioConstrainedSnapValidation
{
    const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    static AnimationStudioWindow controller;
    static AnimationStudioViewport view;
    static StudioModelEditor editor;
    static readonly StudioModelEditor.MoveAxes[] axes={StudioModelEditor.MoveAxes.X,StudioModelEditor.MoveAxes.Y,StudioModelEditor.MoveAxes.Z,StudioModelEditor.MoveAxes.XY,StudioModelEditor.MoveAxes.XZ,StudioModelEditor.MoveAxes.YZ};
    static int scenario,step;
    static bool vertex,local,pivot,components;
    static StudioModelEditor.MoveAxes constraint;
    static Vector3 origin,target;
    static Quaternion orientation;
    static Vector2 handlePoint,targetPoint;
    static double next;
    static string errors="";
    static Vector3[] before;
    static float oldSize,oldSpacing;
    static readonly List<string> results=new List<string>();
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static T Field<T>(string name)=>(T)typeof(StudioModelEditor).GetField(name,Flags).GetValue(editor);
    static void Set(string name,object value)=>typeof(StudioModelEditor).GetField(name,Flags).SetValue(editor,value);
    static void Near(Vector3 a,Vector3 b,string message)=>Check(Vector3.Distance(a,b)<.0005f,message+": "+a+" vs "+b);
    static void Key(KeyCode key,bool up=false)=>view.SendEvent(new Event{type=up?EventType.KeyUp:EventType.KeyDown,keyCode=key});
    static void Mouse(EventType type,Vector2 point)=>view.SendEvent(new Event{type=type,button=0,mousePosition=point});
    public static void Run()
    {
        try
        {
            var doc=StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/ConstrainedSnap.asset"));
            doc.AddPrimitive(PrimitiveType.Cube);doc.AddPrimitive(PrimitiveType.Cube);
            doc.nodes[0].position=new Vector3(.13f,.17f,.11f);doc.nodes[0].rotation=Quaternion.Euler(20,30,15);
            doc.nodes[1].position=new Vector3(1.3f,1.7f,.8f);
            AnimationStudioWindow.Open();controller=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",Flags).Invoke(controller,new object[]{true});
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",Flags).Invoke(controller,new object[]{doc});
            editor=(StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor",Flags).GetValue(controller);
            view=Resources.FindObjectsOfTypeAll<AnimationStudioViewport>().First();view.position=new Rect(100,100,1800,950);
            view.pivot=new Vector3(.5f,.5f,0);view.rotation=Quaternion.Euler(25,135,0);view.size=3.8f;view.orthographic=true;
            oldSize=StudioSettings.Current.handleSize;oldSpacing=StudioSettings.Current.gridSpacing;
            StudioSettings.Current.handleSize=1;StudioSettings.Current.gridSpacing=.5f;
            target=editor.Transforms[1].TransformPoint(new Vector3(.5f,.5f,-.5f));
            Setup();view.Focus();SceneView.duringSceneGui+=Capture;Application.logMessageReceived+=Log;EditorApplication.update+=Tick;next=EditorApplication.timeSinceStartup+1;
        }
        catch(Exception e){Finish(e.ToString());}
    }
    static void Setup()
    {
        editor.SelectObject(0);editor.ActivateTool(Tool.Move);
        components=scenario>=24&&scenario<28;pivot=scenario>=26&&scenario<28;
        Set("mode",components?StudioModelEditor.ComponentMode.Face:StudioModelEditor.ComponentMode.Object);
        if(components)editor.Components.Add(0);
        vertex=scenario%2==1||scenario==30;local=(scenario/2)%2==1;
        constraint=scenario<24?axes[scenario/4]:scenario<28?StudioModelEditor.MoveAxes.XY:StudioModelEditor.MoveAxes.Y;
        Set("local",local);Set("pivot",pivot);
        origin=editor.EditingPivotPosition;orientation=pivot||local?editor.PivotOrientation:Quaternion.identity;
        // Keep all three plane boxes separated in screen space for native picking.
        view.rotation=orientation*Quaternion.Euler(25,135,0);
        before=editor.ActiveMesh.vertices;
        view.Repaint();
    }
    static void Capture(SceneView scene)
    {
        if(scene!=view)return;
        using(new Handles.DrawingScope(Matrix4x4.identity))
        {
            if(Event.current.type==EventType.Repaint)
                targetPoint=GUIUtility.GUIToScreenPoint(HandleUtility.WorldToGUIPoint(target))-view.position.position;
            if(Event.current.type!=EventType.Layout)return;
            float factor=pivot?1.25f:1;
            float size=HandleUtility.GetHandleSize(origin/factor)*factor;
            var offset=Vector3.zero;int bits=(int)constraint;bool single=(bits&(bits-1))==0;
            for(int axis=0;axis<3;axis++)if((bits&(1<<axis))!=0)offset[axis]=size*(single?.8f:.23f);
            handlePoint=GUIUtility.GUIToScreenPoint(HandleUtility.WorldToGUIPoint(origin+orientation*offset))-view.position.position;
        }
    }
    static void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors+=message+"\n"+stack;}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.1;
        try
        {
            switch(step++)
            {
                case 0:
                    if(scenario<28 || scenario==30)
                    {
                        if(scenario==30)Key(KeyCode.X);
                        Key(vertex?KeyCode.V:KeyCode.X);Check(editor.SnapStatusText?.Contains(vertex?"Vertex Snap":"Grid Snap")==true,"Held key displays snap status");
                    }
                    Mouse(EventType.MouseMove,handlePoint);break;
                case 1:
                    Mouse(EventType.MouseDown,handlePoint);
                    Check(Field<int>("moveDragControl")!=0&&Field<StudioModelEditor.MoveAxes>("moveDragAxes")==constraint,"Captured correct constraint "+scenario+" "+constraint+", got "+Field<StudioModelEditor.MoveAxes>("moveDragAxes"));
                    if(scenario<28 || scenario==30)Check(editor.SnapStatusText.Contains(constraint.ToString()),"Status names active constraint");break;
                case 2:
                    if(scenario==28||scenario==29)Key(vertex?KeyCode.V:KeyCode.X);
                    Check(editor.SnapStatusText.Contains(constraint.ToString()),"Status updates while dragging");
                    Mouse(EventType.MouseDrag,targetPoint);
                    var actual=editor.EditingPivotPosition;var delta=Quaternion.Inverse(orientation)*(actual-origin);
                    for(int axis=0;axis<3;axis++)if(((int)constraint&(1<<axis))==0)Check(Mathf.Abs(delta[axis])<.0005f,"Locked coordinate stays fixed "+scenario);
                    Check(Vector3.Distance(actual,origin)>.001f,"Constrained drag moves selection "+scenario);
                    if(vertex)Near(actual,StudioModelEditor.ConstrainMove(origin,target,orientation,constraint),"Vertex projection matches active axes "+scenario);
                    else
                    {
                        var coordinates=Quaternion.Inverse(orientation)*actual;
                        for(int axis=0;axis<3;axis++)if(((int)constraint&(1<<axis))!=0)Check(Mathf.Abs(coordinates[axis]/.5f-Mathf.Round(coordinates[axis]/.5f))<.001f,"Grid alignment on active coordinate "+scenario);
                    }
                    if(pivot)Check(editor.ActiveMesh.vertices.SequenceEqual(before),"Snapping pivot does not edit geometry");
                    Mouse(EventType.MouseUp,targetPoint);Key(vertex?KeyCode.V:KeyCode.X,true);
                    if(scenario==30){Check(editor.SnapStatusText.Contains("Grid Snap"),"Releasing V returns to held X");Key(KeyCode.X,true);}
                    Check(editor.SnapStatusText==null,"Release hides snap status");break;
                case 3: Undo.PerformUndo();break;
                case 4:
                    Near(editor.EditingPivotPosition,origin,"Undo restores origin "+scenario);
                    results.Add("PASS: "+(pivot?"Component pivot":components?"Face":"Object")+" "+(local?"Local":"World")+" "+constraint+" "+(vertex?"Vertex":"Grid")+" snap, locked coordinates, status and Undo.");
                    if(++scenario<31){Setup();step=0;break;}
                    Check(string.IsNullOrEmpty(errors),errors);Finish(null);break;
            }
        }
        catch(Exception e){Finish(e.ToString());}
    }
    static void Finish(string error)
    {
        EditorApplication.update-=Tick;SceneView.duringSceneGui-=Capture;Application.logMessageReceived-=Log;
        StudioSettings.Current.handleSize=oldSize;StudioSettings.Current.gridSpacing=oldSpacing;
        if(error!=null)results.Add("FAIL: "+error+"\n"+errors);
        if(view)view.Close();if(controller)UnityEngine.Object.DestroyImmediate(controller);
        File.WriteAllLines("constrained-snap-result.txt",results);EditorApplication.delayCall+=()=>EditorApplication.Exit(error==null?0:1);
    }
}

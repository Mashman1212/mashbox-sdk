using System;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class StudioMouseMoveRedrawValidation
{
    public static void Run()
    {
        var host=ScriptableObject.CreateInstance<StudioControlsValidationHost>();host.wantsMouseMove=true;host.ShowUtility();
        var view=ScriptableObject.CreateInstance<MashBoxStudioViewport>();
        var editor=new StudioModelEditor(()=>{});
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var rect=typeof(SceneView).GetField("m_RectSelection",flags).GetValue(view);
        var native=rect.GetType().GetMethod("OnGUI",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        var oldTool=Tools.current;Tools.current=Tool.None;
        int expected=0;
        try
        {
            // Run Unity's real selection handler followed by Studio, in the same
            // order as SceneView. Empty canvas must keep the same hover control.
            foreach(var type in new[]{EventType.Layout,EventType.MouseMove,EventType.Layout,EventType.MouseMove})
                host.Drive(()=>{
                    Event.current.mousePosition=new Vector2(400,300);
                    native.Invoke(rect,new object[]{true});
                    editor.SceneGUI(view,null);
                    int actual=HandleUtility.nearestControl;
                    Debug.Log("STUDIO_MOUSE_CONTROL "+type+" "+actual);
                    if(expected==0)expected=actual;
                    if(actual==0 || actual!=expected)throw new Exception("Empty-canvas hover control changes between layout and mouse movement, triggering native repaints");
                },type);
            Debug.Log("STUDIO_MOUSE_MOVE_REDRAW_VALIDATION_SUCCESS stable native hover control across mouse movement");
        }
        finally{Tools.current=oldTool;editor.Dispose();Object.DestroyImmediate(view);host.Close();}
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public static class StudioObjectVertexSnapValidation
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static AnimationStudioWindow controller;
    static MashBoxStudioViewport view;
    static StudioModelEditor editor;
    static Vector3 source, destination, sourcePosition, targetPosition;
    static Vector2 sourceScreen, targetScreen;
    static Vector3[] meshBefore;
    static byte[] diskBefore;
    static int step;
    static double next;
    static string errors = "";
    static readonly List<string> results = new List<string>();
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void Near(Vector3 a, Vector3 b, string message) => Require(Vector3.Distance(a,b) < .0002f, message + ": " + a + " expected " + b);
    static T Field<T>(string name) => (T)typeof(StudioModelEditor).GetField(name, Flags).GetValue(editor);
    static Vector2 Screen(Camera camera, Vector3 world) { var p=camera.WorldToScreenPoint(world); return new Vector2(p.x,camera.pixelHeight-p.y); }
    public static void Run()
    {
        try
        {
            var doc=StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/ObjectSnap.asset"));
            doc.AddPrimitive(PrimitiveType.Cube); doc.AddPrimitive(PrimitiveType.Cube); doc.AddPrimitive(PrimitiveType.Cube);
            doc.nodes[1].position = new Vector3(0,0,2);
            doc.nodes[2].parent = 0; doc.nodes[2].position = new Vector3(0,0,1);
            using(var filterEditor=new StudioModelEditor(()=>{}))
            {
                filterEditor.Load(doc); filterEditor.SelectObject(0);
                var go=new GameObject("Snap filter camera"); var cam=go.AddComponent<Camera>();
                cam.transform.position=new Vector3(0,0,-6); cam.orthographic=true; cam.orthographicSize=3; cam.pixelRect=new Rect(0,0,900,600);
                var point=Screen(cam,new Vector3(.5f,.5f,-.5f));
                Require(filterEditor.TryObjectSnapTarget(cam,point,1,out var target), "External vertex remains eligible behind selected geometry");
                Near(target,new Vector3(.5f,.5f,1.5f),"Destination excludes both selected object and its unselected child");
                Require(filterEditor.TryPivotSnapVertex(cam,point,1,out var pivotTarget), "Pivot editing still finds own vertices");
                Near(pivotTarget,new Vector3(.5f,.5f,-.5f),"Pivot editing retains self-snap");
                filterEditor.SelectObject(1,true);
                Require(!filterEditor.TryObjectSnapTarget(cam,point,1,out _),"All moving objects and their descendants are excluded");
                filterEditor.SelectObject(0);
                filterEditor.Transforms[1].gameObject.SetActive(false);
                Require(!filterEditor.TryObjectSnapTarget(cam,point,1,out _),"Hidden objects are not snap destinations");
                UnityEngine.Object.DestroyImmediate(go);
            }
            results.Add("PASS: Destination filtering excludes the selected object, its children and multi-selection; hidden targets are excluded; pivot editing still snaps to its own mesh.");
            doc.nodes[0].position=new Vector3(-1.2f,0,0); doc.nodes[1].position=new Vector3(1.2f,0,0); doc.nodes[2].active=false;
            AnimationStudioWindow.Open(); controller=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",Flags).Invoke(controller,new object[]{true});
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",Flags).Invoke(controller,new object[]{doc});
            editor=(StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor",Flags).GetValue(controller);
            editor.SelectObject(0); editor.ActivateTool(Tool.Move);
            sourcePosition=editor.Transforms[0].position; targetPosition=editor.Transforms[1].position;
            source=sourcePosition+new Vector3(.5f,.5f,-.5f); destination=targetPosition+new Vector3(-.5f,.5f,-.5f);
            meshBefore=editor.ActiveMesh.vertices; diskBefore=File.ReadAllBytes(AssetDatabase.GetAssetPath(doc));
            view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(); view.position=new Rect(100,100,1600,850);
            view.pivot=Vector3.zero; view.rotation=Quaternion.identity; view.size=3; view.orthographic=true; view.Focus();
            SceneView.duringSceneGui+=Capture; Application.logMessageReceived+=Log; EditorApplication.update+=Tick;
            next=EditorApplication.timeSinceStartup+1;
        }
        catch(Exception e) { Finish(e.ToString()); }
    }
    static void Capture(SceneView scene)
    {
        if(scene!=view || Event.current.type!=EventType.Repaint) return;
        using(new Handles.DrawingScope(Matrix4x4.identity))
        {
            sourceScreen=GUIUtility.GUIToScreenPoint(HandleUtility.WorldToGUIPoint(source))-view.position.position;
            targetScreen=GUIUtility.GUIToScreenPoint(HandleUtility.WorldToGUIPoint(destination))-view.position.position;
        }
    }
    static void Key(KeyCode key, bool up=false) => view.SendEvent(new Event {type=up?EventType.KeyUp:EventType.KeyDown,keyCode=key});
    static void Mouse(EventType type, Vector2 point) => view.SendEvent(new Event {type=type,button=0,mousePosition=point});
    static void Log(string text,string stack,LogType type) {if(type==LogType.Exception || type==LogType.Error || type==LogType.Assert) errors+=text+"\n"+stack;}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next) return; next=EditorApplication.timeSinceStartup+.3;
        try
        {
            switch(step++)
            {
                case 0: Key(KeyCode.V); Require(Field<bool>("pivotSnapHeld"),"V is handled in object Move mode"); Mouse(EventType.MouseMove,sourceScreen); break;
                case 1: Mouse(EventType.MouseDown,sourceScreen); Require(Field<int>("pivotSnapControl")!=0,"Native V drag captures object snapping"); Near(editor.Transforms[0].position,sourcePosition,"Clicking own vertex chooses a source without moving object"); break;
                case 2: Mouse(EventType.MouseDrag,targetScreen); Near(editor.Transforms[0].position,sourcePosition+destination-source,"Source vertex lands exactly on external destination"); break;
                case 3:
                    Mouse(EventType.MouseDrag,targetScreen); Near(editor.Transforms[0].position,sourcePosition+destination-source,"Repeated drag does not stick to self or drift");
                    Mouse(EventType.MouseUp,targetScreen); Key(KeyCode.V,true); break;
                case 4:
                    Near(editor.Transforms[1].position,targetPosition,"Target object stays fixed");
                    Require(meshBefore.SequenceEqual(editor.ActiveMesh.vertices),"Snapping moves transform without modifying local mesh");
                    Require(diskBefore.SequenceEqual(File.ReadAllBytes(AssetDatabase.GetAssetPath(editor.Document))),"Snapping does not save workspace");
                    Undo.PerformUndo(); break;
                case 5: Near(editor.Transforms[0].position,sourcePosition,"Single Undo restores object"); Undo.PerformRedo(); break;
                case 6: Near(editor.Transforms[0].position,sourcePosition+destination-source,"Redo restores snapped object"); Undo.PerformUndo(); break;
                case 7: Key(KeyCode.V); Mouse(EventType.MouseDown,sourceScreen); break;
                case 8: Mouse(EventType.MouseDrag,targetScreen); break;
                case 9: Key(KeyCode.Escape); Key(KeyCode.V,true); break;
                case 10:
                    Near(editor.Transforms[0].position,sourcePosition,"Escape cancels object vertex snap");
                    Require(Field<int>("pivotSnapControl")==0,"Escape releases mouse capture");
                    Require(string.IsNullOrEmpty(errors),errors);
                    results.Add("PASS: Native V source-vertex drag to another object, repeated updates without self-snapping, unchanged target/mesh/disk, single Undo/Redo, and Escape cancellation.");
                    Finish(null); break;
            }
        }
        catch(Exception e) {Finish(e.ToString());}
    }
    static void Finish(string error)
    {
        EditorApplication.update-=Tick; SceneView.duringSceneGui-=Capture; Application.logMessageReceived-=Log;
        if(error!=null) results.Add("FAIL: "+error+"\n"+errors);
        if(view) view.Close(); if(controller) UnityEngine.Object.DestroyImmediate(controller);
        File.WriteAllLines("object-vertex-snap-result.txt",results);
        EditorApplication.delayCall+=()=>EditorApplication.Exit(error==null?0:1);
    }
}

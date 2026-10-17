using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public static class StudioScaleDragValidation
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static AnimationStudioWindow controller;
    static AnimationStudioViewport view;
    static StudioModelEditor editor;
    static int scenario, step;
    static double next;
    static Vector2 handlePoint, mouse, dragStart;
    static Vector3 center;
    static Quaternion orientation;
    static Vector3[] vertices, positions, scales;
    static HashSet<int> selected;
    static byte[] disk;
    static float oldHandleSize;
    static string errors = "";
    static readonly List<string> results = new List<string>();
    static T Field<T>(string name) => (T)typeof(StudioModelEditor).GetField(name, Flags).GetValue(editor);
    static void Set(string name, object value) => typeof(StudioModelEditor).GetField(name, Flags).SetValue(editor, value);
    static T Drag<T>(string name) { var drag = Field<object>("scaleDrag"); return (T)drag.GetType().GetField(name, Flags).GetValue(drag); }
    static void Check(bool value, string text) { if (!value) throw new Exception(text); }
    static void Near(Vector3 a, Vector3 b, string text) => Check(Vector3.Distance(a,b) < .001f * Mathf.Max(1,b.magnitude), text + ": " + a + " expected " + b);
    static bool Components => scenario >= 3;
    static bool Uniform => scenario % 2 == 1;
    public static void Run()
    {
        try
        {
            var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/ScaleDrag.asset"));
            doc.AddPrimitive(PrimitiveType.Cube); doc.AddPrimitive(PrimitiveType.Cube); doc.AddPrimitive(PrimitiveType.Cube);
            doc.nodes[0].position = new Vector3(.4f,.2f,.1f); doc.nodes[0].rotation = Quaternion.Euler(15,25,35);
            doc.nodes[0].scale = new Vector3(1.2f,.8f,1.1f);
            doc.nodes[1].position = new Vector3(2,1,0); doc.nodes[2].parent = 0; doc.nodes[2].position = Vector3.up;
            doc.Save(); disk = File.ReadAllBytes(AssetDatabase.GetAssetPath(doc));
            AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Flags).Invoke(controller,new object[]{true});
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Flags).Invoke(controller,new object[]{doc});
            editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Flags).GetValue(controller);
            view = Resources.FindObjectsOfTypeAll<AnimationStudioViewport>().First(); view.position = new Rect(100,100,1600,900);
            oldHandleSize = StudioSettings.Current.handleSize; StudioSettings.Current.handleSize = 1;
            Setup(); view.Focus(); SceneView.duringSceneGui += Capture; Application.logMessageReceived += Log;
            EditorApplication.update += Tick; next = EditorApplication.timeSinceStartup + 1;
        }
        catch(Exception e) { Finish(e.ToString()); }
    }
    static void Setup()
    {
        editor.SelectObject(0);
        if(scenario == 2) { editor.SelectObject(1,true); editor.SelectObject(2,true); editor.SelectObject(0,true); }
        Set("mode", !Components ? StudioModelEditor.ComponentMode.Object : scenario == 3 ? StudioModelEditor.ComponentMode.Face : scenario == 4 ? StudioModelEditor.ComponentMode.Edge : StudioModelEditor.ComponentMode.Vertex);
        if(Components) editor.Components.Add(0);
        Set("local", scenario != 0 && scenario != 3); editor.ActivateTool(Tool.Scale);
        selected = editor.SelectedVertices(); vertices = editor.ActiveMesh.vertices;
        center = editor.EditingPivotPosition;
        orientation = scenario != 0 && scenario != 3 ? editor.PivotOrientation : Quaternion.identity;
        positions = editor.Transforms.Select(t=>t.position).ToArray(); scales = editor.Transforms.Select(t=>t.localScale).ToArray();
        view.pivot = center; view.rotation = orientation * Quaternion.Euler(20,25,0); view.size = 3; view.orthographic = true; view.Repaint();
    }
    static void Capture(SceneView scene)
    {
        if(scene != view || Event.current.type != EventType.Layout) return;
        using(new Handles.DrawingScope(Matrix4x4.identity))
        {
            float size = HandleUtility.GetHandleSize(center);
            handlePoint = GUIUtility.GUIToScreenPoint(HandleUtility.WorldToGUIPoint(center + (Uniform ? Vector3.zero : orientation * Vector3.right * size * .9f))) - view.position.position;
        }
    }
    static void Mouse(EventType type,Vector2 point)
    { view.SendEvent(new Event{type=type,button=0,mousePosition=point,delta=point-mouse}); mouse=point; }
    static void CheckGeometry()
    {
        Check(Field<object>("scaleDrag") != null, "Native scale handle captures drag " + scenario);
        var factor = Drag<Vector3>("factor");
        Check((factor-Vector3.one).sqrMagnitude > .00001f, "Drag actually scales selection " + scenario);
        Vector3 Expected(Vector3 p) => center + orientation * Vector3.Scale(Quaternion.Inverse(orientation) * (p-center),factor);
        if(Components)
        {
            var actual=editor.ActiveMesh.vertices; var t=editor.Transforms[0];
            for(int i=0;i<actual.Length;i++) Near(actual[i],selected.Contains(i)?t.InverseTransformPoint(Expected(t.TransformPoint(vertices[i]))):vertices[i],"Component matches original geometry times total scale");
        }
        else
        {
            foreach(int i in scenario==2 ? new[]{0,1} : new[]{0})
            { Near(editor.Transforms[i].localScale,Vector3.Scale(scales[i],factor),"Scale does not compound"); Near(editor.Transforms[i].position,Expected(positions[i]),"Group pivot position"); }
            Near(editor.Transforms[2].localScale,scales[2],"Selected child is not scaled twice");
        }
    }
    static void Restored()
    {
        for(int i=0;i<scales.Length;i++) { Near(editor.Transforms[i].localScale,scales[i],"Undo restores scale"); Near(editor.Transforms[i].position,positions[i],"Undo restores placement"); }
        var actual=editor.ActiveMesh.vertices; for(int i=0;i<actual.Length;i++) Near(actual[i],vertices[i],"Undo restores geometry");
    }
    static void Log(string text,string stack,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors+=text+"\n"+stack; }
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next)return; next=EditorApplication.timeSinceStartup+.12;
        try
        {
            int current=step++;
            if(current==0) { Mouse(EventType.MouseMove,handlePoint); return; }
            if(current==1) { dragStart=handlePoint; Mouse(EventType.MouseDown,dragStart); Check(Field<object>("scaleDrag")!=null,"Scale capture "+scenario); return; }
            if(current>=2 && current<=13)
            {
                // Go well outside the viewport, then reverse while retaining capture.
                float distance=current<=10?(current-1)*260:current==11?400:current==12?200:400;
                Mouse(EventType.MouseDrag,dragStart+new Vector2(distance,-distance*.25f)); CheckGeometry(); return;
            }
            switch(current)
            {
                case 14:
                    var oldVertices=editor.ActiveMesh.vertices; var oldScales=editor.Transforms.Select(t=>t.localScale).ToArray();
                    var method=typeof(StudioModelEditor).GetMethod("ApplyScaleDrag",Flags);
                    foreach(var bad in new[]{new Vector3(float.NaN,1,1),new Vector3(float.PositiveInfinity,1,1),new Vector3(float.MaxValue,1,1),Vector3.zero})
                        Check(!(bool)method.Invoke(editor,new object[]{bad}),"Invalid or singular factor rejected");
                    Check(oldVertices.SequenceEqual(editor.ActiveMesh.vertices)&&oldScales.SequenceEqual(editor.Transforms.Select(t=>t.localScale)),"Invalid input leaves last valid state intact");
                    Mouse(EventType.MouseUp,mouse); break;
                case 15: Undo.PerformUndo(); break;
                case 16: Restored(); Undo.PerformRedo(); break;
                case 17:
                    Check(Components ? !vertices.SequenceEqual(editor.ActiveMesh.vertices) : editor.Transforms[0].localScale != scales[0],"Redo restores drag result");
                    Undo.PerformUndo(); break;
                case 18: Restored(); Mouse(EventType.MouseMove,handlePoint); break;
                case 19: dragStart=handlePoint; Mouse(EventType.MouseDown,dragStart); break;
                case 20: Mouse(EventType.MouseDrag,dragStart+new Vector2(120,-30)); CheckGeometry(); break;
                case 21: view.SendEvent(new Event{type=EventType.KeyDown,keyCode=KeyCode.Escape}); break;
                case 22:
                    Restored();
                    Check(File.ReadAllBytes(AssetDatabase.GetAssetPath(editor.Document)).SequenceEqual(disk),"No workspace writes while scaling");
                    results.Add("PASS: Scenario "+scenario+" "+(Components?"components":"objects")+" "+(Uniform?"uniform":"axis")+" scale: repeated offscreen drag, reversal, original baseline, invalid input, Undo/Redo, Escape and no disk writes.");
                    if(++scenario<6) { Setup();step=0;break; }
                    Check(string.IsNullOrEmpty(errors),errors); Finish(null); break;
            }
        }
        catch(Exception e) { Finish(e.ToString()); }
    }
    static void Finish(string error)
    {
        EditorApplication.update-=Tick;SceneView.duringSceneGui-=Capture;Application.logMessageReceived-=Log;
        StudioSettings.Current.handleSize=oldHandleSize;
        if(error!=null)results.Add("FAIL: "+error+"\n"+errors);
        if(view)view.Close();if(controller)UnityEngine.Object.DestroyImmediate(controller);
        File.WriteAllLines("scale-drag-result.txt",results);EditorApplication.delayCall+=()=>EditorApplication.Exit(error==null?0:1);
    }
}

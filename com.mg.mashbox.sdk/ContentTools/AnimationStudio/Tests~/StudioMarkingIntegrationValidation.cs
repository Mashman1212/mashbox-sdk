using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public static class StudioMarkingIntegrationValidation
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static AnimationStudioWindow controller;
    private static AnimationStudioViewport view;
    private static StudioModelEditor editor;
    private static double next;
    private static int step;
    private static bool done;
    private static string errors = "";
    private static (int wire, int handle) baseline;
    private static Vector3 originalPosition;
    private static (int wire, int handle) Capture(string name)
    {
        var target=view.camera.targetTexture;
        Require(target,"Viewport render target missing");
        var previous=RenderTexture.active; RenderTexture.active=target;
        var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply(); RenderTexture.active=previous;
        int wire=0,handle=0;
        foreach(var c in image.GetPixels())
        {
            if(c.g>c.r+.18f && c.b>c.r+.18f && Mathf.Abs(c.g-c.b)<.3f) wire++;
            if(c.g>.4f && c.g>c.r*1.6f && c.g>c.b*1.6f) handle++;
        }
        File.WriteAllBytes("marking-"+name+".png",image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
        return (wire,handle);
    }
    public static void Run()
    {
        AnimationStudioWindow.Open();
        controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
        view = Resources.FindObjectsOfTypeAll<AnimationStudioViewport>().First();
        view.position = new Rect(100, 100, 1300, 850);
        typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Flags).Invoke(controller, new object[] { true });
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        AssetDatabase.DeleteAsset("Assets/MarkingIntegration.asset");
        var doc = StudioModelDocument.Import(cube, "Assets/MarkingIntegration.asset");
        UnityEngine.Object.DestroyImmediate(cube);
        typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Flags).Invoke(controller, new object[] { doc });
        editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor", Flags).GetValue(controller);
        editor.SelectObject(0); view.Focus(); view.Repaint();
        originalPosition=editor.Transforms[0].position;
        next = EditorApplication.timeSinceStartup + 1;
        Application.logMessageReceived += Log;
        EditorApplication.update += Tick;
    }
    private static void Log(string text, string stack, LogType type)
    { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors += text + "\n" + stack + "\n"; }
    private static T Field<T>(string name) => (T)typeof(StudioModelEditor).GetField(name, Flags).GetValue(editor);
    private static void Require(bool test, string message) { if (!test) throw new Exception(message); }
    private static void Tick()
    {
        if (done || EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .6;
        try
        {
            switch (step++)
            {
                case 0:
                    baseline=Capture("before");
                    Require(baseline.wire>50 && baseline.handle>15,"Selection and handle must be visible before opening menu: "+baseline);
                    view.SendEvent(new Event { type = EventType.MouseDown, button = 1, mousePosition = new Vector2(600, 350) });
                    Require(Field<bool>("markingOpen"), "Native viewport RMB event failed to open marking menu");
                    break;
                case 1:
                    Require(Field<bool>("markingOpen"), "Menu lost capture/focus while awaiting drag");
                    var open=Capture("open");
                    Require(open.wire>=baseline.wire*.85f && open.handle>=baseline.handle*.85f,"Menu must retain wireframe and gizmo pixels: "+baseline+" -> "+open);
                    Require(editor.Active==0 && editor.Transforms[0].position==originalPosition,"Menu opening changed the active selection or transform");
                    view.SendEvent(new Event { type = EventType.MouseDrag, button = 1, mousePosition = new Vector2(706, 350) });
                    Require(Field<StudioModelEditor.MarkingLevel>("markingLevel") == StudioModelEditor.MarkingLevel.UV, "Native drag did not enter UV submenu");
                    break;
                case 2:
                    view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
                    Require(!Field<bool>("markingOpen"), "Native Escape did not dismiss menu");
                    Require(!view.drawGizmos, "Preview gizmo isolation was lost");
                    Require(string.IsNullOrEmpty(errors), errors);
                    Finish(0, "PASS: Real MashBox Studio SceneView retains selection wireframe and transform handle pixels under the RMB menu, captures input, opens UV on drag, cancels with Escape, and leaves selection/transform unchanged without GUI exceptions. Baseline: "+baseline);
                    break;
            }
        }
        catch (Exception e) { Finish(1, "FAIL: " + e + "\n" + errors); }
    }
    private static void Finish(int code, string result)
    {
        done = true; EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        editor.CancelMarkingMenu(); view.Close(); UnityEngine.Object.DestroyImmediate(controller);
        AssetDatabase.DeleteAsset("Assets/MarkingIntegration.asset");
        File.WriteAllText("marking-integration-result.txt", result);
        EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}

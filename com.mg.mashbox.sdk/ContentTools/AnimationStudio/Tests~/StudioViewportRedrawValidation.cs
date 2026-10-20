using System;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class StudioViewportRedrawValidation
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static AnimationStudioWindow controller;
    static MashBoxStudioViewport view;
    static StudioModelDocument document;
    static Mesh mesh;
    static double start;
    static int nativeFrames,sceneCallbacks,checks;
    static bool oldVSync,oldCache;
    static void Check(bool pass,string text){if(!pass)throw new Exception(text);checks++;Debug.Log("PASS "+text);}
    static void Send(){view.SendEvent(new Event{type=EventType.Layout,mousePosition=new Vector2(500,400)});view.SendEvent(new Event{type=EventType.Repaint,mousePosition=new Vector2(500,400)});}
    public static void Run(){EditorApplication.delayCall+=Begin;}
    static void Begin()
    {
        oldVSync=StudioSettings.Current.viewportVSync;StudioSettings.Current.viewportVSync=false;
        oldCache=StudioSettings.Current.disableViewportImageCache;StudioSettings.Current.disableViewportImageCache=true;
        controller=ScriptableObject.CreateInstance<AnimationStudioWindow>();
        typeof(AnimationStudioWindow).GetField("modelingMode",Flags).SetValue(controller,true);
        mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube);
        document=ScriptableObject.CreateInstance<StudioModelDocument>();document.nodes.Add(new StudioModelDocument.Node{name="Native cube",mesh=mesh});
        typeof(AnimationStudioWindow).GetField("modelDocument",Flags).SetValue(controller,document);
        typeof(AnimationStudioWindow).GetMethod("OpenViewport",Flags).Invoke(controller,null);
        view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Single(v=>v.Studio==controller);
        view.position=new Rect(0,0,1000,700);view.pivot=Vector3.zero;view.rotation=Quaternion.Euler(15,135,0);view.size=3;
        RenderPipelineManager.endCameraRendering+=Rendered;Camera.onPostRender+=BuiltinRendered;SceneView.duringSceneGui+=SceneGUI;
        start=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;
    }
    static void Rendered(ScriptableRenderContext context,Camera camera){if(view&&camera==view.camera)nativeFrames++;}
    static void BuiltinRendered(Camera camera){if(!GraphicsSettings.currentRenderPipeline&&view&&camera==view.camera)nativeFrames++;}
    static void SceneGUI(SceneView scene){if(scene==view&&Event.current.type==EventType.Repaint)sceneCallbacks++;}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup-start<3)return;
        EditorApplication.update-=Tick;int exit=0;
        try
        {
            Send();
            var row=StudioTheme.Styles.hierarchyRow;
            Check(ReferenceEquals(row,StudioTheme.Styles.hierarchyRow)&&row.normal.background==null,"Hierarchy reuses its transparent row style");
            string theme=StudioSettings.Current.theme;
            try
            {
                StudioSettings.Current.theme=theme=="graphite"?"ocean":"graphite";
                Check(!ReferenceEquals(row,StudioTheme.Styles.hierarchyRow),"Hierarchy style follows palette changes");
            }
            finally {StudioSettings.Current.theme=theme;}
            Check(ReferenceEquals(row,StudioTheme.Styles.hierarchyRow),"Returning to a palette reuses its row style");
            var initialEditor=(StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor",Flags).GetValue(controller);
            int edgeCount=initialEditor.SelectionWireframeLines(0).Length/2;
            Check(initialEditor.ObjectWireDrawCalls>0&&initialEditor.ObjectWireDrawCalls<edgeCount,"Native AA outline uses fewer draw calls than source edges: "+initialEditor.ObjectWireDrawCalls+" / "+edgeCount);
            int frames=nativeFrames,callbacks=sceneCallbacks;
            for(int i=0;i<4;i++)Send();
            Check(nativeFrames>frames&&sceneCallbacks>=callbacks+4,"Native camera and SceneView callbacks run on every repaint; no image replay");
            int renders=view.ViewportRenderCount;
            for(int i=0;i<20;i++)view.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
            Send();Check(view.ViewportRenderCount==renders+1,"Repeated dirty requests share Unity's next repaint");
            var editor=(StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor",Flags).GetValue(controller);
            editor.SelectObject(0);
            foreach(var tool in new[]{Tool.Move,Tool.Rotate,Tool.Scale})
            {
                typeof(StudioModelEditor).GetField("tool",Flags).SetValue(editor,tool);
                frames=nativeFrames;editor.Transforms[0].position+=Vector3.right*.1f;editor.Changed(false);Send();
                Check(nativeFrames>frames,"Native redraw updates "+tool+" tool and edited object");
            }
            frames=nativeFrames;view.pivot+=Vector3.up*.1f;view.MarkDirty(MashBoxStudioViewport.DirtyState.Camera);Send();
            Check(nativeFrames>frames,"Camera dirty action renders without a timer");
            view.RepaintUI();Send();Check(sceneCallbacks>callbacks,"UI redraw preserves the native SceneView lifecycle");
            var parent=typeof(EditorWindow).GetField("m_Parent",Flags).GetValue(view);
            var property=parent.GetType().GetProperty("vSyncEnabled",Flags);
            StudioSettings.Current.viewportVSync=true;view.RepaintUI();Send();Check((bool)property.GetValue(parent),"Studio VSync enabled");
            StudioSettings.Current.viewportVSync=false;view.RepaintUI();Send();Check(!(bool)property.GetValue(parent),"Studio VSync disabled after native redraw");
            Debug.Log("STUDIO_NATIVE_REDRAW_SUCCESS "+checks+" checks");
        }
        catch(Exception error){Debug.LogException(error);exit=1;}
        finally
        {
            RenderPipelineManager.endCameraRendering-=Rendered;Camera.onPostRender-=BuiltinRendered;SceneView.duringSceneGui-=SceneGUI;
            if(view)view.Close();if(controller)Object.DestroyImmediate(controller);if(document)Object.DestroyImmediate(document);if(mesh)Object.DestroyImmediate(mesh);
            StudioSettings.Current.viewportVSync=oldVSync;
            StudioSettings.Current.disableViewportImageCache=oldCache;
            if(Application.isBatchMode)EditorApplication.Exit(exit);
        }
    }
}


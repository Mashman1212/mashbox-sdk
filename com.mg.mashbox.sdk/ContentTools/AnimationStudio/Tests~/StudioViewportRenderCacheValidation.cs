using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class StudioViewportRenderCacheValidation
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static AnimationStudioWindow controller;
    static MashBoxStudioViewport view;
    static StudioModelDocument document;
    static Mesh mesh;
    static double deadline;
    static int cameraRenders,checks,inputEvents;
    static void Check(bool pass,string text){if(!pass)throw new Exception(text);checks++;Debug.Log("PASS "+text);}
    public static void Run(){EditorApplication.delayCall+=Begin;}
    static void Begin()
    {
        Check(!JsonUtility.FromJson<StudioSettings.Preferences>("{}").disableViewportImageCache,"Existing preferences enable the prototype without rewriting saved settings");
        StudioSettings.Current.disableViewportImageCache=false;
        controller=ScriptableObject.CreateInstance<AnimationStudioWindow>();
        typeof(AnimationStudioWindow).GetField("modelingMode",Flags).SetValue(controller,true);
        mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube);
        document=ScriptableObject.CreateInstance<StudioModelDocument>();
        document.nodes.Add(new StudioModelDocument.Node{name="Cached cube",mesh=mesh});
        typeof(AnimationStudioWindow).GetField("modelDocument",Flags).SetValue(controller,document);
        typeof(AnimationStudioWindow).GetMethod("OpenViewport",Flags).Invoke(controller,null);
        view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Single(v=>v.Studio==controller);
        view.position=new Rect(0,0,1000,700);view.pivot=Vector3.zero;view.rotation=Quaternion.Euler(15,135,0);view.size=3;
        deadline=EditorApplication.timeSinceStartup+3;EditorApplication.update+=Tick;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering+=CameraRendered;
        Camera.onPostRender+=BuiltinRendered;
    }
    static void CameraRendered(UnityEngine.Rendering.ScriptableRenderContext context,Camera camera){if(view&&camera==view.camera)cameraRenders++;}
    static void BuiltinRendered(Camera camera){if(!UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline&&view&&camera==view.camera)cameraRenders++;}
    static void Send(EventType type){view.SendEvent(new Event{type=type,mousePosition=new Vector2(500,400)});}
    static RenderTexture Layer(string field)=>(RenderTexture)typeof(MashBoxStudioViewport).GetField(field,Flags).GetValue(view);
    static Color[] Pixels(RenderTexture texture)
    {
        var previous=RenderTexture.active;var read=new Texture2D(texture.width,texture.height,TextureFormat.RGBAFloat,false,true);
        try{RenderTexture.active=texture;read.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);read.Apply();return read.GetPixels();}
        finally{RenderTexture.active=previous;Object.DestroyImmediate(read);}
    }
    static Color[] FramePixels(string name)
    {
        var parent=typeof(EditorWindow).GetField("m_Parent",Flags).GetValue(view);
        var texture=new RenderTexture(320,240,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        texture.Create();
        try
        {
            parent.GetType().GetMethod("GrabPixels",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(parent,new object[]{texture,new Rect(360,230,320,240)});
            var data=Pixels(texture);
            var image=new Texture2D(320,240,TextureFormat.RGBA32,false,true);image.SetPixels(data);image.Apply();
            File.WriteAllBytes(Path.Combine(Application.dataPath,"../../"+name+".png"),image.EncodeToPNG());Object.DestroyImmediate(image);
            return data;
        }
        finally{Object.DestroyImmediate(texture);}
    }
    static void ObserveInput(SceneView scene)
    {if(scene==view&&Event.current.rawType==EventType.MouseDrag)inputEvents++;}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<deadline)return;
        EditorApplication.update-=Tick;
        try
        {
            Debug.Log("CACHE_VALIDATION_VIEWPORT "+view.cameraViewport);
            view.Repaint();Send(EventType.Layout);Send(EventType.Repaint);
            Check(view.ViewportCacheReady,"Native viewport captures both scene color and overlay layers");
            var otherViews=Resources.FindObjectsOfTypeAll<SceneView>().Where(v=>v!=view).Select(v=>(view:v,refresh:v.sceneViewState.alwaysRefresh)).ToArray();
            var colors=Pixels(Layer("cachedScene"));var overlays=Pixels(Layer("cachedOverlay"));
            Check(colors.Any(c=>c.r+c.g+c.b>.1f)&&colors.Distinct().Count()>5,"Scene cache contains rendered shaded geometry");
            Check(overlays.Any(c=>c.a>.05f),"Overlay cache retains grid, wireframes and handles");
            var nativeImage=FramePixels("dirty-cache-native");
            int renders=view.ViewportRenderCount,cameras=cameraRenders,presents=view.ViewportCachePresentCount;
            for(int i=0;i<8;i++){Send(EventType.Layout);Send(EventType.Repaint);}
            Check(view.ViewportRenderCount==renders&&cameraRenders==cameras&&view.ViewportCachePresentCount==presents+8,"Repeated external redraws present the cache without rendering the Studio camera");
            Check(Pixels(Layer("cachedScene")).SequenceEqual(colors)&&Pixels(Layer("cachedOverlay")).SequenceEqual(overlays),"External redraws preserve both cached image layers exactly");
            var replayImage=FramePixels("dirty-cache-replay");
            double error=0;for(int i=0;i<nativeImage.Length;i++)error+=Math.Abs(nativeImage[i].r-replayImage[i].r)+Math.Abs(nativeImage[i].g-replayImage[i].g)+Math.Abs(nativeImage[i].b-replayImage[i].b);
            Check(nativeImage.Distinct().Count()>5&&error/(nativeImage.Length*3)<.002,"Cached presentation matches native scene brightness and overlay compositing: error="+error/(nativeImage.Length*3));
            var editor=(StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor",Flags).GetValue(controller);
            editor.SelectObject(0);Send(EventType.Layout);Send(EventType.Repaint);
            Check(view.ViewportRenderCount==renders+1&&cameraRenders>cameras,"Studio selection changes refresh the rendered image");
            renders=view.ViewportRenderCount;var selectedImage=Pixels(Layer("cachedScene"));
            editor.Transforms[0].position+=Vector3.right*.5f;editor.Changed(false);
            Send(EventType.Layout);Send(EventType.Repaint);
            Check(view.ViewportRenderCount==renders+1&&!Pixels(Layer("cachedScene")).SequenceEqual(selectedImage),"Object edits produce a new rendered image instead of a stale cached frame");
            renders=view.ViewportRenderCount;view.pivot+=Vector3.right;
            Send(EventType.Layout);Send(EventType.Repaint);
            Check(view.ViewportRenderCount==renders+1,"Camera changes invalidate the image even through SceneView APIs");
            renders=view.ViewportRenderCount;view.cameraSettings.fieldOfView+=5;
            Send(EventType.Layout);Send(EventType.Repaint);
            Check(view.ViewportRenderCount==renders+1,"Camera lens changes refresh the viewport");
            Check(otherViews.All(v=>!v.view||v.view.sceneViewState.alwaysRefresh==v.refresh),"Studio caching leaves every other Scene View refresh setting unchanged");
            renders=view.ViewportRenderCount;view.RepaintUI();Send(EventType.Repaint);
            Check(view.ViewportRenderCount==renders,"UI-only dirty state reuses the image");
            renders=view.ViewportRenderCount;view.MarkDirty(MashBoxStudioViewport.DirtyState.Overlay);Send(EventType.Repaint);
            Check(view.ViewportRenderCount==renders+1,"Overlay dirty state refreshes highlights immediately");
            SceneView.beforeSceneGui+=ObserveInput;
            view.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=new Vector2(500,400)});
            renders=view.ViewportRenderCount;
            int captures=view.ViewportCacheCaptureCount;
            for(int i=0;i<4;i++)
            {
                view.SendEvent(new Event{type=EventType.MouseDrag,button=0,mousePosition=new Vector2(500+i,400),delta=Vector2.right});Send(EventType.Repaint);
            }
            view.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=new Vector2(504,400)});
            SceneView.beforeSceneGui-=ObserveInput;
            Check(inputEvents==4&&view.ViewportRenderCount>=renders+4,"Native mouse drags and active-handle repaints are never cached or bypassed");
            Check(view.ViewportCacheCaptureCount==captures,"Active drags do not copy viewport images");
            var oldScene=Layer("cachedScene");var oldOverlay=Layer("cachedOverlay");
            StudioSettings.Current.disableViewportImageCache=true;renders=view.ViewportRenderCount;Send(EventType.Repaint);Send(EventType.Repaint);
            Check(view.ViewportRenderCount==renders+2&&!oldScene&&!oldOverlay,"Disabling the preference restores native rendering and releases cache textures");
            StudioSettings.Current.disableViewportImageCache=false;Send(EventType.Repaint);
            var colorTexture=Layer("cachedScene");var overlayTexture=Layer("cachedOverlay");
            view.Close();Check(!colorTexture&&!overlayTexture,"Closing the viewport releases its cached GPU textures");
            Debug.Log("STUDIO_VIEWPORT_RENDER_CACHE_VALIDATION_SUCCESS "+checks+" checks");Finish(0);
        }
        catch(Exception error){Debug.LogException(error);Finish(1);}
    }
    static void Finish(int result)
    {
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering-=CameraRendered;Camera.onPostRender-=BuiltinRendered;
        SceneView.beforeSceneGui-=ObserveInput;
        if(view)view.Close();if(controller)Object.DestroyImmediate(controller);if(document)Object.DestroyImmediate(document);if(mesh)Object.DestroyImmediate(mesh);
        if(Application.isBatchMode)EditorApplication.Exit(result);
    }
}



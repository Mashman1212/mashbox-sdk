using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class StudioHdrpOutlineValidation
{
    static StudioModelEditor editor;
    static StudioModelDocument document;
    static SceneView view;
    static double next,deadline;
    static int step,repaints;
    static Color32[] previous;
    static string variant;
    static Texture2D sceneColor;
    public static void Run()
    {
        try
        {
            variant=File.ReadAllText(Path.Combine(Application.dataPath,"Variant.txt")).Trim();
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/HDRenderPipelineAsset.asset");
            if(!pipeline)throw new Exception("HDRP pipeline asset was not imported");
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=null;
            EditorSettings.serializationMode=SerializationMode.ForceText;
            var template=AssetDatabase.LoadAssetAtPath<StudioModelDocument>("Assets/Template.asset");
            if(!template)template=StudioModelDocument.CreateEmpty("Assets/Template.asset");AssetDatabase.SaveAssets();
            string script=Regex.Match(File.ReadAllText("Assets/Template.asset"),@"m_Script: \{[^\r\n]+\}").Value;
            if(script.Length==0)throw new Exception("Could not locate the document MonoScript");
            string source=File.ReadAllText("Assets/WorkspaceSource.txt");
            source=Regex.Replace(source,@"m_Script: \{[^\r\n]+\}",script);
            File.WriteAllText("Assets/Workspace.asset",source);AssetDatabase.ImportAsset("Assets/Workspace.asset",ImportAssetOptions.ForceSynchronousImport);
            document=AssetDatabase.LoadAssetAtPath<StudioModelDocument>("Assets/Workspace.asset");
            if(!document||document.nodes.Count==0)throw new Exception("Saved workspace did not load");
            Debug.Log("HDRP_WORKSPACE_LOADED "+document.nodes.Count+" objects; meshes "+string.Join(",",document.nodes.Select(n=>n.name+":"+(n.mesh?n.mesh.vertexCount:0))));
            editor=new StudioModelEditor(()=>view?.Repaint());editor.Load(document);editor.SetDisplayMode(true,false);
            StudioSettings.Current.hideObjectWireframe=false;
            view=ScriptableObject.CreateInstance<StudioHdrpValidationView>();view.position=new Rect(40,40,900,1000);view.ShowUtility();
            typeof(SceneView).GetProperty("customScene",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(view,editor.Scene);
            view.drawGizmos=false;view.showGrid=false;view.sceneLighting=true;
            view.cameraMode=SceneView.GetBuiltinCameraMode(DrawCameraMode.Textured);
            view.sceneViewState.showSkybox=false;view.sceneViewState.showFog=false;view.sceneViewState.showImageEffects=false;
            foreach(var overlay in view.overlayCanvas.overlays)overlay.displayed=false;
            typeof(SceneView).GetProperty("overrideSceneCullingMask",BindingFlags.Instance|BindingFlags.NonPublic)?.SetValue(view, UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(editor.Scene));
            var bounds=new Bounds();bool first=true;
            foreach(var transform in editor.Transforms)
            {
                var renderer=transform.GetComponent<Renderer>();if(!renderer)continue;
                if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);
            }
            view.LookAtDirect(bounds.center,Quaternion.Euler(20,40,0),bounds.extents.magnitude*1.2f);
            SceneView.beforeSceneGui+=Before;SceneView.duringSceneGui+=Draw;
            RenderPipelineManager.endCameraRendering+=CaptureSceneColor;
            EditorApplication.update+=Tick;next=EditorApplication.timeSinceStartup+8;deadline=EditorApplication.timeSinceStartup+180;
        }
        catch(Exception e){Finish(e);}
    }
    static void Before(SceneView current)
    {
        if(current!=view)return;
        view.camera.cameraType=CameraType.Preview;editor.ConfigureLightingCamera(view);
    }
    static void Draw(SceneView current)
    {
        if(current!=view)return;
        editor.SceneGUI(view,editor.Load);
        if(Event.current.type==EventType.Repaint)repaints++;
    }
    static void CaptureSceneColor(ScriptableRenderContext context,Camera camera)
    {
        if(!view||camera!=view.camera||!camera.targetTexture)return;
        var target=camera.targetTexture;
        if(!sceneColor||sceneColor.width!=target.width||sceneColor.height!=target.height)
        {if(sceneColor)Object.DestroyImmediate(sceneColor);sceneColor=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);}
        var old=RenderTexture.active;RenderTexture.active=target;
        sceneColor.ReadPixels(new Rect(0,0,target.width,target.height),0,0);sceneColor.Apply();RenderTexture.active=old;
    }
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup>deadline){Finish(new Exception("HDRP render validation timed out"));return;}
        if(!view){Finish(new Exception("Validation SceneView closed unexpectedly"));return;}
        view.Repaint();
        if(EditorApplication.timeSinceStartup<next||repaints<4)return;
        try
        {
            var field=typeof(SceneView).GetField("m_SceneTargetTexture",BindingFlags.Instance|BindingFlags.NonPublic);
            var target=field?.GetValue(view) as RenderTexture;
            if(!target)target=view.camera.targetTexture;
            if(!target)
            {
                Debug.Log("SCENE_RT_FIELDS "+string.Join(",",typeof(SceneView).GetFields(BindingFlags.Instance|BindingFlags.NonPublic).Where(f=>f.FieldType==typeof(RenderTexture)).Select(f=>f.Name)));
                throw new Exception("No SceneView render texture available");
            }
            var old=RenderTexture.active;RenderTexture.active=target;
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();RenderTexture.active=old;
            var data=pixels.GetPixels32();
            var backgroundField=typeof(SceneView).GetField("m_LDRSceneTargetTexture",BindingFlags.Instance|BindingFlags.NonPublic);
            var background=backgroundField?.GetValue(view) as RenderTexture;
            if(background||sceneColor)
            {
                var basePixels=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
                Color[] baseColors;
                if(sceneColor)baseColors=sceneColor.GetPixels();
                else {RenderTexture.active=background;basePixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);basePixels.Apply();RenderTexture.active=old;baseColors=basePixels.GetPixels();}
                var overlayColors=pixels.GetPixels();
                for(int i=0;i<baseColors.Length;i++)
                {var color=(baseColors[i]*(1-overlayColors[i].a)+overlayColors[i]).gamma;color.a=1;baseColors[i]=color;}
                basePixels.SetPixels(baseColors);basePixels.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../..",variant+"-composite-"+step+".png")),basePixels.EncodeToPNG());
                Object.DestroyImmediate(basePixels);
            }
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..",variant+"-hdrp-"+step+".png"));
            File.WriteAllBytes(path,pixels.EncodeToPNG());Object.DestroyImmediate(pixels);
            Debug.Log("HDRP_CAPTURE "+path+" "+target.width+"x"+target.height+" frames="+repaints+" pipeline="+GraphicsSettings.currentRenderPipeline.GetType().Name);
            if(step==1)
            {
                int changed=0;for(int i=0;i<data.Length;i++)if(!data[i].Equals(previous[i]))changed++;
                Debug.Log("HDRP_STATIC_FRAME_CHANGED_PIXELS "+changed+" of "+data.Length);
                view.LookAtDirect(view.pivot,Quaternion.Euler(-25,140,0),view.size);next=EditorApplication.timeSinceStartup+2;
            }
            else if(step==2){Finish(null);return;}
            previous=data;step++;next=EditorApplication.timeSinceStartup+2;
        }
        catch(Exception e){Finish(e);}
    }
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;SceneView.beforeSceneGui-=Before;SceneView.duringSceneGui-=Draw;
        RenderPipelineManager.endCameraRendering-=CaptureSceneColor;
        if(view)view.Close();editor?.Dispose();
        if(sceneColor)Object.DestroyImmediate(sceneColor);
        if(error==null && variant=="final-native")
        {
            try
            {
                StudioEdgeRenderingValidation.Run();StudioWorkspaceStatsValidation.Run();
                StudioMenuBarValidation.Run();StudioControlsValidation.Run();
                StudioComponentHoverValidation.Run();StudioModelWorkflowValidation.Run();
            }
            catch(Exception e){error=e;}
        }
        if(error!=null)Debug.LogException(error);else Debug.Log("HDRP_OUTLINE_VALIDATION_SUCCESS "+variant);
        EditorApplication.Exit(error==null?0:1);
    }
}
public sealed class StudioHdrpValidationView : SceneView
{
    protected override bool SupportsStageHandling()=>false;
}

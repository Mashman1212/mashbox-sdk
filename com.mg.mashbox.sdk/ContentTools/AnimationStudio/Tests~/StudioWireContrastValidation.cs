#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using MashBoxSDK.AnimationStudio;

public static class StudioWireContrastValidation
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    static AnimationStudioWindow host;
    static MashBoxStudioViewport view;
    static SceneView ordinary;
    static Color ordinaryBackground;
    static StudioModelDocument document;
    static double ready;
    static int phase, checks;
    static Color32[] baseline;
    static StudioModelEditor editor; static int scenario; static bool probed; static int selectedComponent; static int[] sourceTriangles; static int[] sourcePolygons;
    static readonly Color Custom = new Color(.13f, .32f, .57f, 1);
    static string Output(string name) => Path.GetFullPath(Path.Combine(Application.dataPath, "../../" + name));
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }
    public static void Run() => EditorApplication.delayCall += Start;
    static void Start()
    {
        try
        {
            var global = Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings, Unity.RenderPipelines.HighDefinition.Runtime");
            global.GetMethod("Ensure", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { true });
            var pipeline = (RenderPipelineAsset)ScriptableObject.CreateInstance(Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset, Unity.RenderPipelines.HighDefinition.Runtime"));
            AssetDatabase.CreateAsset(pipeline, AssetDatabase.GenerateUniqueAssetPath("Assets/BackgroundValidationPipeline.asset"));
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            document = ScriptableObject.CreateInstance<StudioModelDocument>();
            Directory.CreateDirectory(StudioModelDocument.WorkspaceFolder);
            document.SetWorkspacePath(Path.Combine(StudioModelDocument.WorkspaceFolder, "background-"+Guid.NewGuid().ToString("N")+".workspace"));
            for(int i=0;i<3;i++)
            {
                var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
                document.nodes.Add(new StudioModelDocument.Node{name="Cube "+i,mesh=mesh,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial},cutPolygons=polygons,cutTriangles=mesh.triangles,position=new Vector3((i-1)*2,0,0)});
            }
            AnimationStudioWindow.Open();
            host = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Members).Invoke(host, new object[] { true });
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Members).Invoke(host, new object[] { document });
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v => v.Studio == host);
            editor=host.ToolbarModelEditor;
            editor.SetDisplayMode(true,false);
            sourceTriangles=document.nodes[0].mesh.triangles;
            view.modelGridVisible = false;
            view.position = new Rect(60,60,800,650);
            view.LookAtDirect(Vector3.zero, Quaternion.identity, 5);
            ready = EditorApplication.timeSinceStartup + 2;
            view.Repaint();
            EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(error.ToString()); }
    }
    static Color32[] Pixels()
    {
        var texture = typeof(MashBoxStudioViewport).GetField("cachedScene", Members).GetValue(view) as RenderTexture;
        if (!texture || !texture.IsCreated()) return null;
        var previous = RenderTexture.active;
        var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);
            image.Apply();
            File.WriteAllBytes(Output("isolation-"+scenario+"-"+phase+".png"), image.EncodeToPNG());
            var result=image.GetPixels32();
            var overlay=typeof(MashBoxStudioViewport).GetField("cachedOverlay",Members).GetValue(view) as RenderTexture;
            if(overlay && overlay.width==texture.width && overlay.height==texture.height) { RenderTexture.active=overlay;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();File.WriteAllBytes(Output("isolation-overlay-"+scenario+"-"+phase+".png"),image.EncodeToPNG()); }
            return result;
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
    }

    static object Call(string name,params object[] args) { var method=typeof(StudioModelEditor).GetMethod(name,Members); var parameters=method.GetParameters(); var supplied=new object[parameters.Length]; for(int i=0;i<supplied.Length;i++)supplied[i]=i<args.Length?args[i]:parameters[i].DefaultValue; return method.Invoke(editor,supplied); }

    static readonly Color[] Backgrounds={Color.black,Color.white,new Color(.5f,.5f,.5f,1),new Color(.85f,.25f,.1f,1)};
    static int uploads=-1;
    static void ShortcutChecks()
    {
        const string key="Select/Isolate Selected";
        var id=StudioModelCommands.Register("Select","Isolate Selected").Value;
        StudioShortcuts.Reset();
        Check(StudioModelCommands.StandardKeys.Contains(key),"Factory default key missing");
        var chord=StudioModelCommands.StandardBindings(key).Single();
        Check(chord.Matches(new Event{keyCode=KeyCode.W,shift=true})&&!chord.Matches(new Event{keyCode=KeyCode.W}),"Wrong isolation chord");
        StudioShortcuts.Set(StudioShortcut.Move,new[]{new StudioShortcuts.Binding(KeyCode.W,allowShift:true)});
        Check(StudioShortcuts.Bindings(id).Length==1,"Legacy tolerant Move stole exact isolation default");
        Call("SelectObject",0);
        Check(editor.TryExecuteModelCommand(new Event{type=EventType.KeyDown,keyCode=KeyCode.W,shift=true},view)&&editor.ViewportIsolationActive,"Shift+W did not isolate through shared dispatcher");
        Check(editor.TryExecuteModelCommand(new Event{type=EventType.KeyDown,keyCode=KeyCode.W,shift=true},view)&&!editor.ViewportIsolationActive,"Second Shift+W did not restore all");
        StudioShortcuts.Set(StudioShortcut.Rotate,new[]{new StudioShortcuts.Binding(KeyCode.W,StudioModifiers.Shift)});
        Check(StudioShortcuts.Bindings(id).Length==0,"New default stole an explicit custom chord");
        StudioShortcuts.Reset(StudioShortcut.Rotate);
        StudioShortcuts.Set(id,new[]{new StudioShortcuts.Binding(KeyCode.I)});StudioShortcuts.Reload();
        Check(StudioShortcuts.Bindings(id).Single().key==KeyCode.I,"Custom isolation binding lost on reload");
        StudioShortcuts.Set(id,Array.Empty<StudioShortcuts.Binding>());StudioShortcuts.Reload();
        Check(StudioShortcuts.Bindings(id).Length==0,"Explicit unassignment replaced by default");
        StudioShortcuts.Reset();
        Call("SelectObject",-1);
        StudioSettings.Current.hideObjectWireframe=false;
        StudioSettings.Save();
        editor.SetModelDrawMode(view,DrawCameraMode.TexturedWire);
    }
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<ready)return;
        try
        {
            if(phase==0)
            {
                ShortcutChecks();
                foreach(Color background in Backgrounds)
                {
                    StudioSettings.Current.sceneBackground=background;
                    Check(StudioTheme.TextContrast(StudioModelEditor.BackgroundWireColor,background)>=1.5f,"Dark-first wire fails contrast safeguard");
                    foreach(Color tint in new[]{new Color(1,.65f,.15f,1),new Color(.2f,.9f,.75f,1),StudioFaceSelectionStyle.Hover})
                        Check(StudioTheme.TextContrast(StudioModelEditor.ContrastingWireColor(tint),background)>=3,"Selected/hover wire lacks background contrast");
                }
                StudioSettings.Current.sceneBackground=new Color32(71,71,71,255);
                Check(StudioModelEditor.BackgroundWireColor==new Color(.12f,.15f,.18f,1),"Existing gray background did not retain dark wires");
                StudioSettings.Current.sceneBackground=Color.black;
                Check(StudioModelEditor.BackgroundWireColor.maxColorComponent<.35f,"Dark background wires became excessively bright");
                scenario=0;StudioSettings.Current.sceneBackground=Backgrounds[scenario];StudioSettings.Save();
                phase=1;ready=EditorApplication.timeSinceStartup+1;return;
            }
            var scene=Pixels();if(scene==null){view.Repaint();ready=EditorApplication.timeSinceStartup+.2;return;}
            Color32 expected=StudioModelEditor.BackgroundWireColor.gamma;
            var texture=typeof(MashBoxStudioViewport).GetField("cachedOverlay",Members).GetValue(view) as RenderTexture;
            var previous=RenderTexture.active;var image=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);
            try
            {
                RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();
                var pixels=image.GetPixels32();
                Check(pixels.Count(c=>c.a>180&&Math.Abs(c.r-expected.r)<25&&Math.Abs(c.g-expected.g)<25&&Math.Abs(c.b-expected.b)<25)>25,"Adaptive wire not visible in native overlay, background "+scenario);
                File.WriteAllBytes(Output("wire-contrast-"+scenario+".png"),image.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
            if(uploads<0)uploads=editor.WireBatchUploads;
            else Check(editor.WireBatchUploads==uploads,"Background color edit rebuilt/uploaded wire geometry");
            if(++scenario<Backgrounds.Length)
            {StudioSettings.Current.sceneBackground=Backgrounds[scenario];StudioSettings.Save();ready=EditorApplication.timeSinceStartup+1;return;}
            Finish("PASS "+checks+" contrast, native wire pixels/cache reuse, Shift+W toggle, legacy Move overlap, custom binding and unassignment checks");
        }
        catch(Exception error){Finish(error.ToString());}
    }
    static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        
        File.WriteAllText(Output("wire-contrast-result.txt"), result);
        EditorApplication.delayCall += () =>
        {
            if (ordinary) ordinary.Close();
            if (view) view.Close();
            if (host) UnityEngine.Object.DestroyImmediate(host);
            double end=EditorApplication.timeSinceStartup+1;
            void Exit() { if(EditorApplication.timeSinceStartup<end)return;EditorApplication.update-=Exit;EditorApplication.Exit(result.StartsWith("PASS")?0:1); }
            EditorApplication.update+=Exit;
        };
    }
}
#endif

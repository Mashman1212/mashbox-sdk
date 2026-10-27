#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using MashBoxSDK.AnimationStudio;

public static class StudioSceneBackgroundValidation
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
            var defaults = new StudioSettings.Preferences();
            Check(defaults.sceneBackground == (Color)new Color32(71,71,71,255), "Default changed from modeling gray");
            var legacy = new StudioSettings.Preferences();
            JsonUtility.FromJsonOverwrite("{\"theme\":\"graphite\"}", legacy);
            Check(legacy.sceneBackground == defaults.sceneBackground, "Old profiles lose the default");
            StudioSettings.Current.sceneBackground = defaults.sceneBackground;
            StudioSettings.Save();
            ordinary = ScriptableObject.CreateInstance<SceneView>();
            ordinary.Show();
            ordinaryBackground = ordinary.camera.backgroundColor;
            document = ScriptableObject.CreateInstance<StudioModelDocument>();
            Directory.CreateDirectory(StudioModelDocument.WorkspaceFolder);
            document.SetWorkspacePath(Path.Combine(StudioModelDocument.WorkspaceFolder, "background-"+Guid.NewGuid().ToString("N")+".workspace"));
            AnimationStudioWindow.Open();
            host = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Members).Invoke(host, new object[] { true });
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Members).Invoke(host, new object[] { document });
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v => v.Studio == host);
            view.modelGridVisible = false;
            view.position = new Rect(60,60,800,650);
            view.LookAtDirect(Vector3.zero, Quaternion.Euler(20,30,0), 3);
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
            File.WriteAllBytes(Output("scene-background-"+phase+".png"), image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
    }
    static bool Mostly(Color32[] pixels, Color expected)
    {
        Color32 value = expected;
        return pixels.Count(p => Math.Abs(p.r-value.r)<12 && Math.Abs(p.g-value.g)<12 && Math.Abs(p.b-value.b)<12) > pixels.Length*.65;
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < ready) return;
        try
        {
            var pixels = Pixels();
            if (pixels == null) { view.Repaint(); ready=EditorApplication.timeSinceStartup+.2; return; }
            if (phase == 0)
            {
                Check(Mostly(pixels, new Color32(71,71,71,255)), "Default not rendered as existing gray");
                baseline = pixels;
                ordinaryBackground = ordinary.camera.backgroundColor;
                string theme = StudioSettings.Current.theme;
                StudioSettings.Current.sceneBackground = Custom;
                StudioSettings.Save();
                Check(StudioSettings.Current.theme == theme, "Background edit changed UI palette");
                StudioSettings.Reload();
                Check(StudioSettings.Current.sceneBackground == Custom, "Background did not survive save/reload");
            }
            else if (phase == 1)
            {
                Check(Mostly(pixels, Custom), "Live background edit absent from cached native viewport");
                Check(pixels.Zip(baseline,(a,b)=>a.Equals(b)?0:1).Sum() > pixels.Length*.65, "Viewport did not redraw");
                Check(ordinary.camera.backgroundColor == ordinaryBackground, "Ordinary Unity camera changed");
                host.ToolbarModelEditor.EditLighting(s => s.studioLights=true);
            }
            else if (phase == 2)
            {
                Check(Mostly(pixels, Custom), "Studio Lights background differs");
                StudioSettings.Current.sceneBackground = new StudioSettings.Preferences().sceneBackground;
                StudioSettings.Save();
            }
            else
            {
                Check(Mostly(pixels, new Color32(71,71,71,255)), "Default gray not restored");
                Check(ordinary.camera.backgroundColor == ordinaryBackground, "Restoration changed Unity camera");
                Finish("PASS " + checks + " default, legacy profile, persistence, palette independence, native Camera/Studio background and Unity isolation checks");
                return;
            }
            phase++;
            ready = EditorApplication.timeSinceStartup + 1;
        }
        catch (Exception error) { Finish(error.ToString()); }
    }
    static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        File.WriteAllText(Output("scene-background-result.txt"), result);
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

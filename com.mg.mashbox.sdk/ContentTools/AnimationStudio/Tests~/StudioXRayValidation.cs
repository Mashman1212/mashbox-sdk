using EditorPrefs = MashBoxSDK.AnimationStudio.StudioPreferenceStore;
using System;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class StudioXRayValidation
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool pass, string message) { if (!pass) throw new Exception(message); checks++; Debug.Log("PASS " + message); }
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    static object Get(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    public static void Run()
    {
        var doc = ScriptableObject.CreateInstance<StudioModelDocument>();
        var mesh = StudioModelDocument.BuildPrimitive(PrimitiveType.Cube);
        var quad = StudioModelDocument.BuildPrimitive(PrimitiveType.Quad);
        var shader = Shader.Find("Hidden/MashBox/AnimationStudioPreview");
        var source = new Material(shader);
        source.SetColor("_Color", new Color(.7f,.5f,.3f,1));
        doc.nodes.Add(new StudioModelDocument.Node {name="Cube", mesh=mesh, materials=new[]{source}});
        var cameraObject = new GameObject("X-ray validation camera");
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled=false;
        var host = ScriptableObject.CreateInstance<StudioControlsValidationHost>();
        var editor = new StudioModelEditor(() => {});
        bool hadProfile = EditorPrefs.HasKey(StudioShortcuts.PreferenceKey);
        string savedProfile = EditorPrefs.GetString(StudioShortcuts.PreferenceKey);
        RenderTexture target = null; Texture2D pixels = null;
        try
        {
            host.ShowUtility(); host.Focus(); host.SendEvent(new Event {type=EventType.Layout});
            editor.Load(doc); editor.SelectObject(0);
            camera.transform.position = new Vector3(0,0,-5); camera.transform.LookAt(Vector3.zero);
            Check(!editor.XRayMode, "Normal display is the default");
            Check(editor.SelectionWireframeLines(0).Length == 24, "Cube retains all twelve polygon edges without diagonals");
            var front = editor.VisibleWireframeLines(0,camera);
            Check(front.Length == 24, "Normal wireframe retains back-facing edges for GPU depth testing");
            Check(ReferenceEquals(front,editor.VisibleWireframeLines(0,camera)), "Stationary camera reuses visible wireframe buffer");
            camera.transform.position = new Vector3(4,3,-5); camera.transform.LookAt(Vector3.zero);
            Check(editor.VisibleWireframeLines(0,camera).Length == 24, "Orbiting retains the cached polygon edge geometry");
            camera.orthographic = true;
            Check(editor.VisibleWireframeLines(0,camera).Length == 24, "Orthographic display retains all depth-tested edges");
            editor.Transforms[0].localScale = new Vector3(-2,3,.7f);
            Check(editor.VisibleWireframeLines(0,camera).Length == 24, "Mirrored nonuniform scale retains polygon edges");
            editor.Transforms[0].localScale = Vector3.one;
            camera.transform.position = new Vector3(0,0,-5); camera.transform.LookAt(Vector3.zero); camera.orthographic=false;
            foreach (var mode in new[]{StudioModelEditor.ComponentMode.Vertex,StudioModelEditor.ComponentMode.Edge,StudioModelEditor.ComponentMode.Face})
            {
                Set(editor,"mode",mode); editor.Components.Clear();
                int count = (int)Call(editor,"ComponentCount");
                editor.Components.UnionWith(Enumerable.Range(0,count));
                Call(editor,"EnsureComponentDrawing"); Call(editor,"EnsureComponentHover");
                var visibleHighlight=(Mesh)Get(editor,"componentHighlight");
                Check(visibleHighlight && visibleHighlight.GetIndexCount(0)>0, mode+" retains selected geometry for depth testing");
                Check(editor.Components.Count==count, mode+" visibility does not alter actual selection");
                Call(editor,"EnsureComponentDrawing");
                Check(ReferenceEquals(visibleHighlight,Get(editor,"componentHighlight")), mode+" stationary visibility reuses overlay mesh");
            }
            // Open, single-sided surfaces must retain edges from either side.
            doc.nodes[0].mesh=quad; editor.Load(doc); editor.SelectObject(0);
            Check(editor.VisibleWireframeLines(0,camera).Length==8,"Open quad is outlined from its front");
            camera.transform.position = new Vector3(0,0,5); camera.transform.LookAt(Vector3.zero);
            Check(editor.VisibleWireframeLines(0,camera).Length==8,"Open quad back-face outline remains available through openings");
            editor.SetXRayMode(true);
            Check(editor.VisibleWireframeLines(0,camera).Length==8,"X-ray reveals all open-quad edges");
            doc.nodes[0].mesh=mesh; editor.Load(doc); editor.SelectObject(0);
            Check(editor.XRayMode && editor.VisibleWireframeLines(0,camera).Length==24,"X-ray persists through rebuild and reveals all cube edges");
            var preview=editor.Transforms[0].GetComponent<Renderer>().sharedMaterial;
            Check(preview!=source && Mathf.Abs(preview.GetFloat("_Opacity")-.22f)<.001f && preview.GetFloat("_ZWrite")==0,"X-ray uses transparent preview material without depth writes");
            Check(preview.renderQueue==(int)RenderQueue.Transparent && preview.GetFloat("_SrcBlend")==5 && preview.GetFloat("_DstBlend")==10,"X-ray material enables transparent sorting and alpha blending");
            Check(source.GetFloat("_Opacity")==1 && source.GetFloat("_ZWrite")==1 && source.renderQueue<3000,"X-ray leaves source material unchanged");
            Check(!ShaderUtil.ShaderHasError(shader),"Preview shader imports without errors");
            // Render a white surface against black to verify actual blending, not just material flags.
            camera.scene=editor.Scene; camera.overrideSceneCullingMask=UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(editor.Scene);
            camera.transform.position=new Vector3(0,0,-3);camera.transform.LookAt(Vector3.zero);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            camera.orthographic=true;camera.orthographicSize=1;
            target=new RenderTexture(64,64,24);camera.targetTexture=target;
            pixels=new Texture2D(64,64,TextureFormat.RGBA32,false);
            float Sample()
            {
                camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,64,64),0,0);pixels.Apply();RenderTexture.active=previous;
                return pixels.GetPixel(32,32).grayscale;
            }
            editor.SetDisplayMode(false,false);
            float transparent=Sample(); editor.SetXRayMode(false);float opaque=Sample();
            Check(opaque>.1f && transparent>0 && transparent<opaque*.8f,"Rendered X-ray mesh is transparent against the background");
            Check(editor.Transforms[0].GetComponent<Renderer>().sharedMaterial.GetFloat("_Opacity")==1,"Leaving X-ray restores opaque preview");
            editor.SetDisplayMode(true,true);doc.lighting.studioLights=true;Call(editor,"ApplyDisplayMaterials");
            Check(editor.Transforms[0].GetComponent<Renderer>().sharedMaterial==source,"Normal studio lighting restores original material");
            editor.SetXRayMode(true);
            Check(editor.Transforms[0].GetComponent<Renderer>().sharedMaterial!=source,"X-ray transparency works with studio lighting enabled");
            editor.SetXRayMode(false); StudioShortcuts.Reset();
            host.Drive(() => {Event.current=new Event {type=EventType.KeyDown,keyCode=KeyCode.X,modifiers=EventModifiers.Alt};editor.HandleModelKeyboard(null);});
            Check(editor.XRayMode,"Alt+X enables X-ray through Studio shortcut dispatch");
            host.Drive(() => {Event.current=new Event {type=EventType.KeyDown,keyCode=KeyCode.X,modifiers=EventModifiers.Alt};editor.HandleModelKeyboard(null);});
            Check(!editor.XRayMode,"Alt+X toggles X-ray off");
            StudioShortcuts.Set(StudioShortcut.XRay,new[]{new StudioShortcuts.Binding(KeyCode.F6)});
            host.Drive(() => {Event.current=new Event {type=EventType.KeyDown,keyCode=KeyCode.F6};editor.HandleModelKeyboard(null);});
            Check(editor.XRayMode && StudioShortcuts.Label(StudioShortcut.XRay)=="F6","X-ray shortcut can be remapped in Studio preferences");
            StudioShortcuts.Reload();Check(StudioShortcuts.Label(StudioShortcut.XRay)=="F6","X-ray remap survives reload");
            Check(StudioShortcuts.Pressed(StudioShortcut.GridSnap,new Event {type=EventType.KeyDown,keyCode=KeyCode.X}),"Plain X remains hold grid snap");
            Debug.Log("STUDIO_XRAY_VALIDATION_SUCCESS "+checks+" checks");
        }
        finally
        {
            camera.targetTexture=null;if(target)Object.DestroyImmediate(target);if(pixels)Object.DestroyImmediate(pixels);
            host.Close();editor.Dispose();Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(doc);Object.DestroyImmediate(mesh);Object.DestroyImmediate(quad);Object.DestroyImmediate(source);
            if(hadProfile)EditorPrefs.SetString(StudioShortcuts.PreferenceKey,savedProfile);else EditorPrefs.DeleteKey(StudioShortcuts.PreferenceKey);
            StudioShortcuts.Reload();
        }
    }
}

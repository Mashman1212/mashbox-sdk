using EditorPrefs = MashBoxSDK.AnimationStudio.StudioPreferenceStore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioDisplayValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> Results = new List<string>();
        static AnimationStudioWindow controller;
        static MashBoxStudioViewport view;
        static StudioModelEditor editor;
        static string path, oldModel, errors = "";
        static bool hadModel, hdrp;
        static int step;
        static double next;
        static byte[] disk;
        static float dark, cameraBrightness, backgroundBrightness; static Material sourceMaterial, originalMaterial; static Texture2D testTexture;
        static Color ambient;
        static ShadowQuality shadows;
        static string ModelKey => "MashBox.LastModel." + Application.dataPath;
        static void Check(bool value,string label) { if(!value) throw new Exception(label); Results.Add("PASS: " + label); }
        public static void RunHDRP()
        {
            hdrp = true;
            var globalType = Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings, Unity.RenderPipelines.HighDefinition.Runtime");
            globalType.GetMethod("Ensure",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[] {true});
            var assetType = Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset, Unity.RenderPipelines.HighDefinition.Runtime");
            var pipeline = (RenderPipelineAsset)ScriptableObject.CreateInstance(assetType);
            AssetDatabase.CreateAsset(pipeline,AssetDatabase.GenerateUniqueAssetPath("Assets/Lighting Pipeline.asset"));
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            Run();
        }
        public static void Run()
        {
            hadModel=EditorPrefs.HasKey(ModelKey); oldModel=EditorPrefs.GetString(ModelKey);
            ambient=RenderSettings.ambientLight; shadows=QualitySettings.shadows;
            path=AssetDatabase.GenerateUniqueAssetPath("Assets/Lighting Test.asset");
            var doc=StudioModelDocument.CreateEmpty(path); doc.AddPrimitive(PrimitiveType.Sphere); doc.nodes[0].position=Vector3.up*.5f;
            doc.lighting.exposure=0; doc.lighting.ground=true; doc.Save(); disk=File.ReadAllBytes(path);
            AnimationStudioWindow.Open(); controller=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",Private).Invoke(controller,new object[] {true});
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",Private).Invoke(controller,new object[] {doc});
            editor=(StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor",Private).GetValue(controller); editor.SelectObject(0);
            view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First();
            view.cameraMode=SceneView.GetBuiltinCameraMode(DrawCameraMode.Textured); view.sceneLighting=true;
            view.position=new Rect(100,100,1400,800); view.pivot=Vector3.up*.5f; view.rotation=Quaternion.Euler(25,145,0); view.size=2; view.LookAtDirect(Vector3.up*.5f,Quaternion.Euler(25,145,0),2); view.Focus();
            Application.logMessageReceived+=Log; EditorApplication.update+=Tick; next=EditorApplication.timeSinceStartup+3;
        }
        static void Log(string text,string stack,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) errors+=text+"\n"+stack; }
        static StudioPreviewLighting Rig => (StudioPreviewLighting)typeof(StudioModelEditor).GetField("previewLighting",Private).GetValue(editor);
        static float Brightness()
        {
            var rt=view.camera.targetTexture; Check(rt,"Viewport produces a rendered texture");
            if (!hdrp) view.camera.Render();
            else { rt = new RenderTexture(rt.width,rt.height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB); rt.Create(); RenderPipeline.SubmitRenderRequest(view.camera,new RenderPipeline.StandardRequest { destination=rt }); }
            var previous=RenderTexture.active; RenderTexture.active=rt;
            var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply(); RenderTexture.active=previous;
            File.WriteAllBytes("display-"+step+".png",image.EncodeToPNG());
            var background=image.GetPixel(20,image.height-20);
            if(!editor.LightingSettings.studioLights)
            {
                Check(Mathf.Abs(background.r-background.g)<.01f && Mathf.Abs(background.g-background.b)<.01f,"Camera mode has a neutral gray background");
                Check(background.r>.05f && background.r<.5f,"Camera background stays medium gray: "+background.r);
                if(backgroundBrightness>0) Check(Mathf.Abs(background.r-backgroundBrightness)<.01f,"Camera background is stable across orbits and lighting changes");
                backgroundBrightness=background.r;
            }
            var p=view.camera.WorldToScreenPoint(Vector3.up*.6f);
            if(step==5) { var texel=image.GetPixel((int)p.x,(int)p.y); Check(texel.g>texel.r+.05f&&texel.g>texel.b+.05f,"Unlit preview renders the assigned green texture, not a flat fallback"); }
            float sum=0; int count=0;
            for(int y=(int)p.y-18;y<(int)p.y+18;y++) for(int x=(int)p.x-18;x<(int)p.x+18;x++) { sum+=image.GetPixel(x,y).grayscale; count++; }
            Object.DestroyImmediate(image); if(hdrp) { rt.Release(); Object.DestroyImmediate(rt); } return sum/count;
        }
        static void Tick()
        {
            view.Repaint(); if(EditorApplication.timeSinceStartup<next) return; next=EditorApplication.timeSinceStartup+5;
            try
            {
                var renderer=editor.Transforms[0].GetComponent<Renderer>();
                switch(step++)
                {
                    case 0:
                        Check(!editor.LightingSettings.studioLights,"Existing workspaces default to camera lighting");
                        editor.SetDisplayMode(true,false);
                        Check(!Rig.Key.enabled && !Rig.Fill.enabled,"World lights are disabled in camera mode");
                        Check(renderer.sharedMaterial.GetFloat("_CameraLighting")==1 && renderer.sharedMaterial.GetFloat("_Unlit")==0,"Lit modeling uses camera-relative shading");
                        break;
                    case 1:
                        cameraBrightness=Brightness(); Check(cameraBrightness>.1f && cameraBrightness<.95f,"Camera shading renders without clipping: "+cameraBrightness);
                        view.LookAtDirect(Vector3.up*.5f,Quaternion.Euler(-25,15,0),2);
                        editor.EditLighting(s=>{s.keyIntensity=8;s.fillIntensity=8;s.exposure=5;s.keyColor=Color.red;});
                        break;
                    case 2:
                        float orbited=Brightness(); Check(Mathf.Abs(orbited-cameraBrightness)<.055f,"Camera lighting stays consistent after orbit and changing world lights/exposure: "+cameraBrightness+" -> "+orbited);
                        break;
                    case 3:
                        originalMaterial=editor.Document.nodes[0].materials[0];
                        sourceMaterial=new Material(originalMaterial);
                        testTexture=new Texture2D(2,2); testTexture.SetPixels(Enumerable.Repeat(new Color(.2f,.8f,.4f),4).ToArray()); testTexture.Apply();
                        string textureProperty=sourceMaterial.HasProperty("_BaseColorMap")?"_BaseColorMap":"_MainTex";
                        sourceMaterial.SetTexture(textureProperty,testTexture); sourceMaterial.SetTextureScale(textureProperty,new Vector2(2,3));
                        sourceMaterial.SetColor(sourceMaterial.HasProperty("_BaseColor")?"_BaseColor":"_Color",Color.white);
                        editor.Document.nodes[0].materials=new[]{sourceMaterial};
                        editor.SetDisplayMode(false,true);
                        Check(renderer.sharedMaterial!=sourceMaterial&&renderer.sharedMaterial.GetFloat("_Unlit")==1,"Unlit uses a disposable preview material");
                        Check(renderer.sharedMaterial.GetTexture("_MainTex")==testTexture&&renderer.sharedMaterial.GetTextureScale("_MainTex")==new Vector2(2,3),"Unlit preserves the base texture and UV tiling");
                        break;
                    case 4:
                        dark=Brightness(); Check(dark>.03f,"Unlit textured model renders visibly");
                        editor.EditLighting(s=>{s.keyIntensity=0;s.fillIntensity=0;}); break;
                    case 5:
                        float unlit=Brightness(); Check(Mathf.Abs(unlit-dark)<.025f,"Unlit model is unaffected by switching off studio lights: "+dark+" -> "+unlit);
                        editor.SetDisplayMode(false,false); break;
                    case 6:
                        Check(renderer.sharedMaterial.GetFloat("_Unlit")==1&&renderer.sharedMaterial.GetTexture("_MainTex")!=testTexture,"Unlit neutral mode hides assigned textures");
                        Check(Brightness()>.03f,"Unlit neutral mode renders with lights off");
                        editor.SetDisplayMode(true,false); break;
                    case 7:
                        editor.EditLighting(s=>{s.studioLights=true;s.exposure=0;s.keyColor=Color.white;});
                        break;
                    case 8:
                        Check(renderer.sharedMaterial!=sourceMaterial&&renderer.sharedMaterial.shader==originalMaterial.shader,"Lit neutral mode uses pipeline shading without changing assigned materials");
                        dark=Brightness(); editor.EditLighting(s=>{s.keyIntensity=4;s.fillIntensity=1;}); break;
                    case 9:
                        float lit=Brightness(); Check(lit>dark+.03f,"Lit neutral mode responds to studio lights: "+dark+" -> "+lit);
                        editor.SetDisplayMode(true,true);
                        Check(renderer.sharedMaterial==sourceMaterial&&editor.Document.nodes[0].materials[0]==sourceMaterial,"Textured lit mode restores the exact assigned material");
                        Check(sourceMaterial.GetTexture(sourceMaterial.HasProperty("_BaseColorMap")?"_BaseColorMap":"_MainTex")==testTexture,"Display modes leave source material properties unchanged");
                        editor.SetDisplayMode(true,false);
                        editor.EditLighting(s=>s.studioLights=false);
                        break;
                    case 10:
                        float restored=Brightness(); Check(Mathf.Abs(restored-cameraBrightness)<.055f,"Returning from studio lights restores consistent camera shading");
                        Check(File.ReadAllBytes(path).SequenceEqual(disk),"Display toggles do not write workspace files");
                        Finish(null); break;
                }
                view.Repaint();
            }
            catch(Exception e) { Finish(e.ToString()); }
        }
        static void Finish(string error)
        {
            EditorApplication.update-=Tick; Application.logMessageReceived-=Log;
            if(!string.IsNullOrEmpty(errors)) error+=errors;
            if(!string.IsNullOrEmpty(error)) Results.Add("FAIL: "+error);
            if(editor!=null && originalMaterial) editor.Document.nodes[0].materials=new[]{originalMaterial};
            if(view)view.Close(); if(controller)Object.DestroyImmediate(controller);
            if(hadModel)EditorPrefs.SetString(ModelKey,oldModel);else EditorPrefs.DeleteKey(ModelKey);
            if(sourceMaterial) Object.DestroyImmediate(sourceMaterial); if(testTexture) Object.DestroyImmediate(testTexture);
            File.WriteAllLines(hdrp?"display-hdrp-result.txt":"display-result.txt",Results);
            EditorApplication.delayCall+=()=>EditorApplication.Exit(string.IsNullOrEmpty(error)?0:1);
        }
    }
}

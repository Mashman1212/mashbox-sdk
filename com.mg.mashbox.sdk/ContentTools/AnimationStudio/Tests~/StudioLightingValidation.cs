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
    public static class StudioLightingValidation
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
        static float dark;
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
            var doc=StudioModelDocument.CreateEmpty(path); doc.AddPrimitive(PrimitiveType.Cube); doc.nodes[0].position=Vector3.up*.5f;
            doc.lighting.exposure=-3; doc.lighting.ground=true; doc.Save(); disk=File.ReadAllBytes(path);
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
            else { rt = new RenderTexture(rt.width,rt.height,24,RenderTextureFormat.ARGBHalf); rt.Create(); RenderPipeline.SubmitRenderRequest(view.camera,new RenderPipeline.StandardRequest { destination=rt }); }
            var previous=RenderTexture.active; RenderTexture.active=rt;
            var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply(); RenderTexture.active=previous;
            File.WriteAllBytes("lighting-"+step+".png",image.EncodeToPNG());
            var p=view.camera.WorldToScreenPoint(Vector3.up*.6f);
            float sum=0; int count=0;
            for(int y=(int)p.y-18;y<(int)p.y+18;y++) for(int x=(int)p.x-18;x<(int)p.x+18;x++) { sum+=image.GetPixel(x,y).grayscale; count++; }
            Object.DestroyImmediate(image); if(hdrp) { rt.Release(); Object.DestroyImmediate(rt); } return sum/count;
        }
        static void Tick()
        {
            view.Repaint(); if(EditorApplication.timeSinceStartup<next) return; next=EditorApplication.timeSinceStartup+5;
            try
            {
                switch(step++)
                {
                    case 0:
                        Check(Rig.Key.shadows==LightShadows.Soft,"Preview key casts shadows");
                        var ground=editor.Scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>()).First(r=>r.name=="Preview Ground");
                        Check(ground.receiveShadows&&ground.shadowCastingMode==ShadowCastingMode.Off,"Optional preview ground receives shadows");
                        Check(editor.Document.nodes.Count==1&&editor.Transforms.Length==1,"Lighting and ground stay outside model hierarchy/export data");
                        dark=Brightness(); editor.EditLighting(s=>s.exposure=1); break;
                    case 1:
                        float bright=Brightness(); Check(bright>dark+.03f,"Exposure visibly brightens rendering: "+dark+" -> "+bright);
                        Undo.IncrementCurrentGroup(); editor.EditLighting(s=>{s.shadows=false;s.azimuth=80;s.keyIntensity=2;s.fillIntensity=.7f;}); Undo.FlushUndoRecordObjects();
                        Check(Rig.Key.shadows==LightShadows.None&&Quaternion.Angle(Rig.Key.transform.rotation,Quaternion.Euler(40,80,0))<.001f,"Shadow toggle and light direction apply live");
                        Undo.PerformUndo(); break;
                    case 2:
                        Check(editor.LightingSettings.shadows&&Rig.Key.shadows==LightShadows.Soft,"Undo rebuilds the prior lighting setup");
                        Check(RenderSettings.ambientLight==ambient&&QualitySettings.shadows==shadows,"Preview controls leave main-scene ambient and project shadow quality unchanged");
                        Check(File.ReadAllBytes(path).SequenceEqual(disk),"Lighting adjustments do not write model files automatically");
                        editor.Save(); Check(!File.ReadAllBytes(path).SequenceEqual(disk),"Explicit Save persists lighting settings with the workspace");
                        if(hdrp) {
                            var dataType=Type.GetType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData, Unity.RenderPipelines.HighDefinition.Runtime");
                            editor.ConfigureLightingCamera(view);
                            editor.SuspendLighting();
                            Check(!(bool)dataType.GetProperty("isEditorCameraPreview").GetValue(view.camera.GetComponent(dataType)),"Leaving modeling restores the camera preview mode");
                        }
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
            if(view)view.Close(); if(controller)Object.DestroyImmediate(controller);
            if(hadModel)EditorPrefs.SetString(ModelKey,oldModel);else EditorPrefs.DeleteKey(ModelKey);
            File.WriteAllLines(hdrp?"lighting-hdrp-result.txt":"lighting-result.txt",Results);
            EditorApplication.delayCall+=()=>EditorApplication.Exit(string.IsNullOrEmpty(error)?0:1);
        }
    }
}

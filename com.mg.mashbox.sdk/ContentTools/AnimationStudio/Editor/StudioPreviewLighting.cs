using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    // Owns only disposable preview objects. No source material, RenderSettings or quality edits.
    internal sealed class StudioPreviewLighting : IDisposable
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly Type CameraDataType = Type.GetType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData, Unity.RenderPipelines.HighDefinition.Runtime");
        private static readonly Type LightDataType = Type.GetType("UnityEngine.Rendering.HighDefinition.HDAdditionalLightData, Unity.RenderPipelines.HighDefinition.Runtime");
        private readonly GameObject root, ground;
        internal readonly Light Key, Fill;
        private readonly Component hdKey;
        private readonly Material groundMaterial;
        private readonly ScriptableObject profile;
        private readonly Object exposure;
        private readonly Object indirectLighting;
        private Cubemap materialEnvironment;
        private ReflectionProbe materialProbe;
        private bool materialPreview;
        private readonly List<Object> volumeComponents = new List<Object>();
        private readonly bool hdrp;
        private bool studioLights;
        private readonly Dictionary<SceneView,(Color background, CameraClearFlags clearFlags)> cameraColors = new Dictionary<SceneView,(Color,CameraClearFlags)>();
        private readonly Dictionary<SceneView,(Component data, bool preview, LayerMask mask, bool effects, Color background, object clearMode, object aa, object aaQuality)> cameras = new Dictionary<SceneView,(Component,bool,LayerMask,bool,Color,object,object,object)>();
        private readonly Dictionary<Component,(bool custom,object frames,object mask)> cameraFrameSettings = new Dictionary<Component,(bool,object,object)>();
        private readonly Dictionary<SceneView,(Component data,bool lights,bool aa,bool smaa)> configuredCameras = new Dictionary<SceneView,(Component,bool,bool,bool)>();
        internal int CameraConfigurationCount { get; private set; }

        internal StudioPreviewLighting(Scene scene)
        {
            hdrp = GraphicsSettings.currentRenderPipeline && GraphicsSettings.currentRenderPipeline.GetType().Name.Contains("HDRenderPipeline");
            root = new GameObject("MashBox Studio Preview Lighting") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root,scene);
            Key = NewLight("Key Light"); Fill = NewLight("Fill Light");
            if (hdrp && LightDataType != null)
            {
                hdKey = Key.gameObject.GetComponent(LightDataType) ?? Key.gameObject.AddComponent(LightDataType);
                if (!Fill.gameObject.GetComponent(LightDataType)) Fill.gameObject.AddComponent(LightDataType);
            }
            ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Preview Ground"; ground.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(ground,scene); ground.transform.SetParent(root.transform,false);
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            var pipeline = GraphicsSettings.currentRenderPipeline;
            var template = pipeline ? pipeline.defaultMaterial : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            groundMaterial = new Material(template) { hideFlags = HideFlags.HideAndDontSave };
            var renderer = ground.GetComponent<Renderer>(); renderer.sharedMaterial = groundMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            if (hdrp)
            {
                var volumeType = Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
                var profileType = Type.GetType("UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core.Runtime");
                var exposureType = Type.GetType("UnityEngine.Rendering.HighDefinition.Exposure, Unity.RenderPipelines.HighDefinition.Runtime");
                if (volumeType != null && profileType != null && exposureType != null)
                {
                    var volume = root.AddComponent(volumeType);
                    profile = ScriptableObject.CreateInstance(profileType); profile.hideFlags = HideFlags.HideAndDontSave;
                    exposure = AddVolumeComponent(exposureType);
                    Parameter(exposure,"mode",0); // ExposureMode.Fixed
                    // Project-default sky/fog volumes must not swamp this isolated studio rig.
                    Parameter(AddVolumeComponent(Type.GetType("UnityEngine.Rendering.HighDefinition.VisualEnvironment, Unity.RenderPipelines.HighDefinition.Runtime")),"skyType",0);
                    Parameter(AddVolumeComponent(Type.GetType("UnityEngine.Rendering.HighDefinition.Fog, Unity.RenderPipelines.HighDefinition.Runtime")),"enabled",false);
                    var indirect = AddVolumeComponent(Type.GetType("UnityEngine.Rendering.HighDefinition.IndirectLightingController, Unity.RenderPipelines.HighDefinition.Runtime"));
                    indirectLighting=indirect;
                    Parameter(indirect,"indirectDiffuseLightingMultiplier",0f);
                    Parameter(indirect,"reflectionLightingMultiplier",0f);
                    Set(volume,"isGlobal",true); Set(volume,"priority",10000f); Set(volume,"sharedProfile",profile);
                }
            }
        }
        private Object AddVolumeComponent(Type type)
        {
            var component = (Object)profile.GetType().GetMethod("Add",new[] {typeof(Type),typeof(bool)}).Invoke(profile,new object[] {type,true});
            component.hideFlags = HideFlags.HideAndDontSave; volumeComponents.Add(component); return component;
        }
        private Light NewLight(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(go,root.scene); go.transform.SetParent(root.transform,false);
            var light = go.AddComponent<Light>(); light.type = LightType.Directional; return light;
        }
        internal void Apply(StudioLightingSettings s)
        {
            studioLights = s.studioLights;
            Key.enabled = Fill.enabled = studioLights;
            float ev = Mathf.Clamp(s.exposure,-5,5);
            // HDRP uses lux: one studio intensity unit is 100 lux. Camera exposure also affects emissive materials.
            // Built-in uses lighting gain.
            float gain = hdrp && exposure ? 100 : Mathf.Pow(2,ev);
            Key.intensity = Mathf.Clamp(s.keyIntensity,0,8) * gain; Fill.intensity = Mathf.Clamp(s.fillIntensity,0,8) * gain;
            Key.color = s.keyColor; Fill.color = s.fillColor;
            Key.transform.rotation = Quaternion.Euler(s.elevation,s.azimuth,0);
            Fill.transform.rotation = Quaternion.Euler(15,s.azimuth + 160,0);
            Key.shadows = s.shadows ? LightShadows.Soft : LightShadows.None;
            Key.shadowStrength = Mathf.Clamp01(s.shadowStrength); Fill.shadows = LightShadows.None;
            if (hdKey) Set(hdKey,"shadowDimmer",Mathf.Clamp01(s.shadowStrength));
            if (exposure) Parameter(exposure,"fixedExposure",studioLights ? -ev : 0);
            ground.SetActive(studioLights && s.ground); ground.transform.localPosition = new Vector3(0,s.groundHeight,0);
            ground.transform.localScale = Vector3.one * Mathf.Clamp(s.groundSize,1,200) / 10;
            if (groundMaterial.HasProperty("_BaseColor")) groundMaterial.SetColor("_BaseColor",s.groundColor);
            if (groundMaterial.HasProperty("_Color")) groundMaterial.SetColor("_Color",s.groundColor);
            if (groundMaterial.HasProperty("_Smoothness")) groundMaterial.SetFloat("_Smoothness",0);
            if (groundMaterial.HasProperty("_Glossiness")) groundMaterial.SetFloat("_Glossiness",0);
        }
        internal void ApplyMaterialPreview()
        {
            Apply(new StudioLightingSettings {
                studioLights=true, keyIntensity=1.3f, fillIntensity=.6f,
                keyColor=Color.white, fillColor=Color.white,
                elevation=35, azimuth=150, shadows=false, ground=false
            });
            materialPreview=true;
            configuredCameras.Clear();
            // Both lights illuminate the front/side of the object. A back-facing
            // fill left ordinary cloth unnecessarily dark in the material view.
            // The SDK character faces +Z. Directional lights point along their
            // rays, so their forward direction must point back toward -Z.
            Fill.transform.rotation=Quaternion.Euler(15,240,0);
            // Native HDRP materials need exposure appropriate to this 130/60-lux
            // rig. EV 0 was intended for self-lit preview shaders and clips lit
            // albedo to white. Keep exposure fixed, independent of game volumes.
            if(exposure) Parameter(exposure,"fixedExposure",6f);
            if(indirectLighting) Parameter(indirectLighting,"reflectionLightingMultiplier",1f);
            EnsureMaterialEnvironment();
        }
        private void EnsureMaterialEnvironment()
        {
            if(materialEnvironment)return;
            // A neutral, constant environment gives metallic/reflective areas a
            // stable studio response instead of reflecting pure black. It lives
            // only in this preview scene; project sky/probe assets are untouched.
            materialEnvironment=new Cubemap(16,TextureFormat.RGBAHalf,true) {
                name="Studio neutral material environment",hideFlags=HideFlags.HideAndDontSave
            };
            float value=hdrp?16f:.35f;
            var pixels=new Color[16*16];
            for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(value,value,value,1);
            for(int face=0;face<6;face++)materialEnvironment.SetPixels(pixels,(CubemapFace)face);
            materialEnvironment.Apply(true,false);
            var go=new GameObject("Studio material reflection") {hideFlags=HideFlags.HideAndDontSave};
            SceneManager.MoveGameObjectToScene(go,root.scene);go.transform.SetParent(root.transform,false);
            var probe=go.AddComponent<ReflectionProbe>();
            materialProbe=probe;
            probe.mode=ReflectionProbeMode.Custom;probe.customBakedTexture=materialEnvironment;
            probe.size=Vector3.one*1000;probe.blendDistance=0;probe.intensity=1;probe.boxProjection=false;
            var type=hdrp?Type.GetType("UnityEngine.Rendering.HighDefinition.HDAdditionalReflectionData, Unity.RenderPipelines.HighDefinition.Runtime"):null;
            if(type!=null)
            {
                var data=go.GetComponent(type)??go.AddComponent(type);
                var mode=Enum.Parse(Get(data,"mode").GetType(),"Custom");
                Set(data,"mode",mode);
                type.GetMethod("SetTexture",Members).Invoke(data,new object[]{mode,materialEnvironment});
                Set(Get(data,"influenceVolume"),"boxSize",Vector3.one*1000);
                type.GetMethod("PrepareCulling",Members)?.Invoke(data,null);
            }
        }
        internal void SetMaterialPreviewExposure(float stops)
        {
            stops=Mathf.Clamp(stops,-4,4);
            if(hdrp && exposure) Parameter(exposure,"fixedExposure",6f-stops);
            else
            {
                float gain=Mathf.Pow(2,stops);
                Key.intensity=1.3f*gain;Fill.intensity=.6f*gain;
                if(materialProbe)materialProbe.intensity=gain;
            }
        }
        internal void ConfigureCamera(SceneView view)
        {
            if (!view.camera) return;
            if (!cameraColors.ContainsKey(view)) cameraColors.Add(view,(view.camera.backgroundColor,view.camera.clearFlags));
            // Preserve the original neutral viewport gray in display space. HDRP's
            // camera-preview path needs the explicit linear equivalent below.
            Color background = new Color32(71,71,71,255);
            view.camera.clearFlags = CameraClearFlags.SolidColor;
            view.camera.backgroundColor = background;
            if (!hdrp || !view.camera || CameraDataType == null || !profile) return;
            if (!cameras.ContainsKey(view))
            {
                var data = view.camera.GetComponent(CameraDataType) ?? view.camera.gameObject.AddComponent(CameraDataType);
                cameras.Add(view,(data,(bool)Get(data,"isEditorCameraPreview"),(LayerMask)Get(data,"volumeLayerMask"),view.sceneViewState.showImageEffects,(Color)Get(data,"backgroundColorHDR"),Get(data,"clearColorMode"),Get(data,"antialiasing"),Get(data,"SMAAQuality")));
            }
            var state = cameras[view];
            // HDRP's regular asset preview forcibly disables exposure and shadows.
            // Camera-preview mode retains preview-scene isolation while enabling them.
            bool antialiasing = !StudioSettings.Current.disableViewportAntialiasing;
            bool smaa = StudioSettings.Current.useViewportSmaa;
            view.sceneViewState.showImageEffects = studioLights || antialiasing;
            // Native layout, input and repaint passes share these camera settings.
            // Rebuild the reflected HDRP overrides only when their inputs change.
            if (configuredCameras.TryGetValue(view,out var configured) && configured.data == state.data &&
                configured.lights == studioLights && configured.aa == antialiasing && configured.smaa == smaa) return;
            // Regular asset previews disable HDRP post-processing, including AA.
            // Keep camera-preview rendering enabled for SMAA even with studio lights off.
            Set(state.data,"isEditorCameraPreview",studioLights || antialiasing); Set(state.data,"volumeLayerMask",(LayerMask)1);
            Set(state.data,"antialiasing",Enum.Parse(state.aa.GetType(),!antialiasing ? "None" : StudioSettings.Current.useViewportSmaa ? "SubpixelMorphologicalAntiAliasing" : "FastApproximateAntialiasing"));
            Set(state.data,"SMAAQuality",Enum.Parse(state.aaQuality.GetType(),"High"));
            ConfigureFrameSettings(state.data, studioLights || antialiasing, !studioLights);
            // Camera-preview mode bypasses Unity's gray asset-preview background.
            // Set HDRP's own clear color explicitly; Camera.backgroundColor alone is ignored.
            Set(state.data,"clearColorMode",Enum.Parse(state.clearMode.GetType(),"Color"));
            Set(state.data,"backgroundColorHDR",background.linear);
            configuredCameras[view] = (state.data,studioLights,antialiasing,smaa);
            CameraConfigurationCount++;
        }
        private void ConfigureFrameSettings(Component data, bool postprocessing, bool neutralPreview)
        {
            // Per-camera overrides prevent project Volume defaults from adding effects
            // when the post-processing path is enabled solely to run spatial AA.
            const string framesName = "m_RenderingPathCustomFrameSettings";
            const string maskName = "renderingPathCustomFrameSettingsOverrideMask";
            if (!cameraFrameSettings.ContainsKey(data))
                cameraFrameSettings.Add(data,((bool)Get(data,"customRenderingSettings"),Get(data,framesName),Get(data,maskName)));
            // Start from independent copies of the original settings so switching back
            // to Studio lights restores all lighting passes and preserves Undo/resume.
            object frames = RuntimeHelpers.GetObjectValue(cameraFrameSettings[data].frames);
            object mask = RuntimeHelpers.GetObjectValue(cameraFrameSettings[data].mask), bits = Get(mask,"mask");
            var setEnabled = frames.GetType().GetMethod("SetEnabled",Members);
            var fieldType = setEnabled.GetParameters()[0].ParameterType;
            var indexer = bits.GetType().GetProperty("Item");
            void Override(string name,bool enabled)
            {
                object field = Enum.Parse(fieldType,name);
                setEnabled.Invoke(frames,new[] {field,(object)enabled});
                indexer.SetValue(bits,true,new object[] {(uint)Convert.ToInt32(field)});
            }
            Override("Postprocess",postprocessing);
            Override("Antialiasing",!StudioSettings.Current.disableViewportAntialiasing);
            Override("ExposureControl",studioLights);
            if(materialPreview)
            {
                // Material inspection must not inherit gameplay occlusion,
                // shadow histories, probes or custom passes from project quality.
                foreach(string effect in new[]{"ShadowMaps","ContactShadows","ScreenSpaceShadows","SSAO","SSGI","SSR","TransparentSSR","PlanarProbe","RayTracing","AdaptiveProbeVolume","VolumetricClouds","AtmosphericScattering","Volumetrics","CustomPass","MotionVectors","ObjectMotionVectors","Decals","LightLayers"})
                    Override(effect,false);
                Override("ReflectionProbe",true);
                Override("DirectSpecularLighting",true);
            }
            foreach (string effect in new[] {"MotionBlur","DepthOfField","Bloom","PaniniProjection","LensFlareScreenSpace","LensFlareDataDriven","LensDistortion","ChromaticAberration","Vignette","ColorGrading","Tonemapping","FilmGrain","Dithering","CustomPostProcess"})
                Override(effect,false);
            // Neutral modelling materials shade themselves. They do not sample HDRP
            // lighting, reflection, fog or motion buffers. Keep geometry, transparency,
            // depth and spatial AA while omitting those unused render passes.
            if (neutralPreview)
                foreach (string effect in new[] {"LitShaderMode","DepthPrepassWithDeferredRendering","ClearGBuffers","TransparentPrepass","TransparentPostpass","Decals","Refraction","Distortion","RoughDistortion","MotionVectors","ObjectMotionVectors","ShadowMaps","ContactShadows","ScreenSpaceShadows","SSR","TransparentSSR","SSAO","SSGI","SubsurfaceScattering","VolumetricClouds","AtmosphericScattering","Volumetrics","ReflectionProbe","PlanarProbe","LightLayers","RayTracing","AdaptiveProbeVolume","FPTLForForwardOpaque","BigTilePrepass","ComputeLightVariants","ComputeMaterialVariants","CustomPass"})
                    Override(effect,false);
            Set(mask,"mask",bits); Set(data,framesName,frames); Set(data,maskName,mask);
            Set(data,"customRenderingSettings",true);
        }
        internal static void ConfigureStudioBackground(SceneView view)
        {
            if (!view || !view.camera) return;
            var background = new Color(.35f,.35f,.35f,1);
            view.sceneViewState.showSkybox = false;
            view.sceneViewState.showFog = false;
            view.camera.clearFlags = CameraClearFlags.SolidColor;
            view.camera.backgroundColor = background;
            // HDRP owns its clear mode separately from Camera.clearFlags. Apply this
            // in both Studio modes, after SceneView has set up the preview camera.
            var data = CameraDataType == null ? null : view.camera.GetComponent(CameraDataType);
            if (!data) return;
            var clearMode = Get(data,"clearColorMode");
            Set(data,"clearColorMode",Enum.Parse(clearMode.GetType(),"Color"));
            Set(data,"backgroundColorHDR",background.linear);
        }
        private static object Get(object target,string name) => target.GetType().GetField(name,Members)?.GetValue(target) ?? target.GetType().GetProperty(name,Members)?.GetValue(target);
        private static void Set(object target,string name,object value)
        {
            var field = target.GetType().GetField(name,Members);
            if (field != null) field.SetValue(target,value);
            else target.GetType().GetProperty(name,Members)?.SetValue(target,value);
        }
        private static void Parameter(Object component,string name,object value)
        {
            var parameter = Get(component,name); var property = parameter.GetType().GetProperty("value",Members);
            property.SetValue(parameter,property.PropertyType.IsEnum ? Enum.ToObject(property.PropertyType,value) : value);
            Set(parameter,"overrideState",true);
        }
        internal void RestoreCameras()
        {
            configuredCameras.Clear();
            foreach (var pair in cameraColors)
            {
                if (!pair.Key || !pair.Key.camera) continue;
                pair.Key.camera.backgroundColor = pair.Value.background;
                pair.Key.camera.clearFlags = pair.Value.clearFlags;
            }
            cameraColors.Clear();
            foreach(var pair in cameras)
            {
                if (!pair.Key || !pair.Value.data) continue;
                Set(pair.Value.data,"isEditorCameraPreview",pair.Value.preview); Set(pair.Value.data,"volumeLayerMask",pair.Value.mask);
                Set(pair.Value.data,"backgroundColorHDR",pair.Value.background); Set(pair.Value.data,"clearColorMode",pair.Value.clearMode);
                Set(pair.Value.data,"antialiasing",pair.Value.aa); Set(pair.Value.data,"SMAAQuality",pair.Value.aaQuality);
                pair.Key.sceneViewState.showImageEffects = pair.Value.effects;
            }
            cameras.Clear();
            foreach (var pair in cameraFrameSettings)
            {
                if (!pair.Key) continue;
                Set(pair.Key,"customRenderingSettings",pair.Value.custom);
                Set(pair.Key,"m_RenderingPathCustomFrameSettings",pair.Value.frames);
                Set(pair.Key,"renderingPathCustomFrameSettingsOverrideMask",pair.Value.mask);
            }
            cameraFrameSettings.Clear();
        }
        public void Dispose()
        {
            RestoreCameras();
            if (root) Object.DestroyImmediate(root);
            if (groundMaterial) Object.DestroyImmediate(groundMaterial);
            if (materialEnvironment) Object.DestroyImmediate(materialEnvironment);
            foreach (var component in volumeComponents) if (component) Object.DestroyImmediate(component);
            if (profile) Object.DestroyImmediate(profile);
        }
    }
}

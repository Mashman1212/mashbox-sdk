using System;
using System.Collections.Generic;
using System.Reflection;
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
        private readonly List<Object> volumeComponents = new List<Object>();
        private readonly bool hdrp;
        private bool studioLights;
        private readonly Dictionary<SceneView,(Color background, CameraClearFlags clearFlags)> cameraColors = new Dictionary<SceneView,(Color,CameraClearFlags)>();
        private readonly Dictionary<SceneView,(Component data, bool preview, LayerMask mask, bool effects, Color background, object clearMode)> cameras = new Dictionary<SceneView,(Component,bool,LayerMask,bool,Color,object)>();

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
            if (exposure) Parameter(exposure,"fixedExposure",-ev);
            ground.SetActive(studioLights && s.ground); ground.transform.localPosition = new Vector3(0,s.groundHeight,0);
            ground.transform.localScale = Vector3.one * Mathf.Clamp(s.groundSize,1,200) / 10;
            if (groundMaterial.HasProperty("_BaseColor")) groundMaterial.SetColor("_BaseColor",s.groundColor);
            if (groundMaterial.HasProperty("_Color")) groundMaterial.SetColor("_Color",s.groundColor);
            if (groundMaterial.HasProperty("_Smoothness")) groundMaterial.SetFloat("_Smoothness",0);
            if (groundMaterial.HasProperty("_Glossiness")) groundMaterial.SetFloat("_Glossiness",0);
        }
        internal void ConfigureCamera(SceneView view)
        {
            if (!view.camera) return;
            if (!cameraColors.ContainsKey(view)) cameraColors.Add(view,(view.camera.backgroundColor,view.camera.clearFlags));
            var background = new Color(.35f,.35f,.35f,1);
            view.camera.clearFlags = CameraClearFlags.SolidColor;
            view.camera.backgroundColor = background;
            if (!hdrp || !view.camera || CameraDataType == null || !profile) return;
            if (!cameras.ContainsKey(view))
            {
                var data = view.camera.GetComponent(CameraDataType) ?? view.camera.gameObject.AddComponent(CameraDataType);
                cameras.Add(view,(data,(bool)Get(data,"isEditorCameraPreview"),(LayerMask)Get(data,"volumeLayerMask"),view.sceneViewState.showImageEffects,(Color)Get(data,"backgroundColorHDR"),Get(data,"clearColorMode")));
            }
            var state = cameras[view];
            // HDRP's regular asset preview forcibly disables exposure and shadows.
            // Camera-preview mode retains preview-scene isolation while enabling them.
            Set(state.data,"isEditorCameraPreview",studioLights); Set(state.data,"volumeLayerMask",(LayerMask)1);
            // Camera-preview mode bypasses Unity's gray asset-preview background.
            // Set HDRP's own clear color explicitly; Camera.backgroundColor alone is ignored.
            Set(state.data,"clearColorMode",Enum.Parse(state.clearMode.GetType(),"Color"));
            Set(state.data,"backgroundColorHDR",background.linear);
            view.sceneViewState.showImageEffects = studioLights;
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
                pair.Key.sceneViewState.showImageEffects = pair.Value.effects;
            }
            cameras.Clear();
        }
        public void Dispose()
        {
            RestoreCameras();
            if (root) Object.DestroyImmediate(root);
            if (groundMaterial) Object.DestroyImmediate(groundMaterial);
            foreach (var component in volumeComponents) if (component) Object.DestroyImmediate(component);
            if (profile) Object.DestroyImmediate(profile);
        }
    }
}

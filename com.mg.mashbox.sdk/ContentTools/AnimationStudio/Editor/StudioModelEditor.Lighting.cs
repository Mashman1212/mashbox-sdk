using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private StudioPreviewLighting previewLighting;
        private readonly StudioLightingSettings emptyLighting = new StudioLightingSettings();
        internal StudioLightingSettings LightingSettings => Document ? Document.lighting ?? (Document.lighting = new StudioLightingSettings()) : emptyLighting;
        internal void ConfigureLightingCamera(SceneView view) => previewLighting?.ConfigureModelCamera(view,LightingSettings,
            displayLighting&&(displayTextures||checkerMap)&&!displayXray&&!wireframeOnly);
        internal void SuspendLighting() { SetSubdivisionActive(false);previewLighting?.RestoreCameras(); }
        internal void EditLighting(Action<StudioLightingSettings> change)
        {
            if (Document) Undo.RecordObject(Document,"Edit viewport lighting");
            change(LightingSettings); previewLighting?.Apply(LightingSettings);
            ApplyDisplayMaterials();
            if (Document) EditorUtility.SetDirty(Document);
            repaint(); toolOptions?.Repaint();
        }
        internal bool LightingOptionsVisible { get; private set; }
        internal void ShowLightingOptions(bool visible)
        {
            LightingOptionsVisible = visible;
            if (visible) { CameraOptionsVisible = false; OpenToolOptions(); }
            repaint(); toolOptions?.Repaint();
        }
        private static readonly string[] ViewportLightingModes = { "Camera Lighting", "Studio Lights" };
        private void DrawViewportLightingOptions()
        {
            var s = LightingSettings;
            EditorGUI.BeginChangeCheck();
            bool studioLights = EditorGUILayout.Popup("Lighting mode", s.studioLights ? 1 : 0, ViewportLightingModes) == 1;
            using (new EditorGUI.DisabledScope(!studioLights && !(previewLighting?.SupportsPhysicalMaterials??false)))
            {
                float exposure = EditorGUILayout.Slider("Exposure (EV)",s.exposure,-5,5);
                GUILayout.Space(8); GUILayout.Label("Key Light",StudioTheme.Styles.boldLabel);
                float key = EditorGUILayout.Slider("Intensity",s.keyIntensity,0,8);
                Color keyColor = EditorGUILayout.ColorField("Color",s.keyColor);
                float elevation = EditorGUILayout.Slider("Elevation",s.elevation,-90,90);
                float azimuth = EditorGUILayout.Slider("Azimuth",s.azimuth,-180,180);
                GUILayout.Space(8); GUILayout.Label("Fill Light",StudioTheme.Styles.boldLabel);
                float fill = EditorGUILayout.Slider("Intensity",s.fillIntensity,0,8);
                Color fillColor = EditorGUILayout.ColorField("Color",s.fillColor);
                float reflection=s.cameraReflectionIntensity;
                if(!studioLights)reflection=EditorGUILayout.Slider(new GUIContent("Reflections","Strength of the neutral reflection environment. Lower values keep dark and rough surfaces from looking washed out."),reflection,0,2);
                bool shadows=s.shadows, ground=s.ground;
                float strength=s.shadowStrength, height=s.groundHeight, size=s.groundSize;
                Color groundColor=s.groundColor;
                if(studioLights)
                {
                    GUILayout.Space(8); GUILayout.Label("Shadows",StudioTheme.Styles.boldLabel);
                    shadows=EditorGUILayout.Toggle("Draw shadows",shadows);
                    strength=EditorGUILayout.Slider("Strength",strength,0,1);
                    GUILayout.Space(8); GUILayout.Label("Ground",StudioTheme.Styles.boldLabel);
                    ground=EditorGUILayout.Toggle("Ground plane",ground);
                    height=EditorGUILayout.FloatField("Height",height);
                    size=EditorGUILayout.Slider("Size",size,1,200);
                    groundColor=EditorGUILayout.ColorField("Color",groundColor);
                }
                if (EditorGUI.EndChangeCheck()) EditLighting(v => {
                    v.studioLights=studioLights;
                    v.exposure=exposure; v.keyIntensity=key; v.keyColor=keyColor; v.elevation=elevation; v.azimuth=azimuth;
                    v.fillIntensity=fill; v.fillColor=fillColor; v.cameraReflectionIntensity=reflection; v.shadows=shadows; v.shadowStrength=strength;
                    v.ground=ground; v.groundHeight=height; v.groundSize=size; v.groundColor=groundColor;
                });
            }
            GUILayout.Space(10);
            if (GUILayout.Button("Reset Lighting")) EditLighting(v => JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new StudioLightingSettings()),v));
        }
    }
}

using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private StudioPreviewLighting previewLighting;
        private StudioLightingWindow lightingWindow;
        private readonly StudioLightingSettings emptyLighting = new StudioLightingSettings();
        internal StudioLightingSettings LightingSettings => Document ? Document.lighting ?? (Document.lighting = new StudioLightingSettings()) : emptyLighting;
        internal void ConfigureLightingCamera(SceneView view) => previewLighting?.ConfigureCamera(view);
        internal void SuspendLighting() => previewLighting?.RestoreCameras();
        internal void EditLighting(Action<StudioLightingSettings> change)
        {
            if (Document) Undo.RecordObject(Document,"Edit viewport lighting");
            change(LightingSettings); previewLighting?.Apply(LightingSettings);
            ApplyDisplayMaterials();
            if (Document) EditorUtility.SetDirty(Document);
            repaint(); lightingWindow?.Repaint();
        }
        private void OpenLighting()
        {
            if (!lightingWindow) lightingWindow = ScriptableObject.CreateInstance<StudioLightingWindow>();
            lightingWindow.Bind(this); lightingWindow.ShowUtility(); lightingWindow.Focus();
        }
    }

    internal sealed class StudioLightingWindow : EditorWindow
    {
        private StudioModelEditor owner;
        private Vector2 scroll;
        internal void Bind(StudioModelEditor editor) { owner = editor; titleContent = new GUIContent("Viewport Lighting"); minSize = new Vector2(340,490); }
        private void OnGUI()
        {
            if (owner == null) return;
            owner.HandleSaveShortcut();
            using (StudioTheme.Scope())
            {
                EditorGUI.DrawRect(new Rect(Vector2.zero,position.size),StudioTheme.Current.Panel);
                scroll = EditorGUILayout.BeginScrollView(scroll);
                using (new EditorGUILayout.VerticalScope(StudioTheme.Styles.inspectorDefaultMargins))
                {
                    var s = owner.LightingSettings;
                    EditorGUI.BeginChangeCheck();
                    bool studioLights = EditorGUILayout.Toggle("Studio lights",s.studioLights);
                    using (new EditorGUI.DisabledScope(!studioLights))
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
                        GUILayout.Space(8); GUILayout.Label("Shadows",StudioTheme.Styles.boldLabel);
                        bool shadows = EditorGUILayout.Toggle("Draw shadows",s.shadows);
                        float strength = EditorGUILayout.Slider("Strength",s.shadowStrength,0,1);
                        GUILayout.Space(8); GUILayout.Label("Ground",StudioTheme.Styles.boldLabel);
                        bool ground = EditorGUILayout.Toggle("Ground plane",s.ground);
                        float height = EditorGUILayout.FloatField("Height",s.groundHeight);
                        float size = EditorGUILayout.Slider("Size",s.groundSize,1,200);
                        Color groundColor = EditorGUILayout.ColorField("Color",s.groundColor);
                        if (EditorGUI.EndChangeCheck()) owner.EditLighting(v => {
                            v.studioLights=studioLights;
                            v.exposure=exposure; v.keyIntensity=key; v.keyColor=keyColor; v.elevation=elevation; v.azimuth=azimuth;
                            v.fillIntensity=fill; v.fillColor=fillColor; v.shadows=shadows; v.shadowStrength=strength;
                            v.ground=ground; v.groundHeight=height; v.groundSize=size; v.groundColor=groundColor;
                        });
                    }
                    GUILayout.Space(10);
                    if (GUILayout.Button("Reset Lighting")) owner.EditLighting(v => JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new StudioLightingSettings()),v));
                }
                EditorGUILayout.EndScrollView();
            }
        }
    }
}

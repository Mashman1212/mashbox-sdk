using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "MashBoxSDK.AnimationStudio", sourceAssembly: "MashBoxSDK.AnimationStudio.Editor", sourceClassName: "AnimationStudioViewport")]
    public sealed partial class MashBoxStudioViewport : SceneView
    {
        // This window owns an isolated asset-preview scene. Unity's stage handler
        // would replace that scene, culling mask and skybox settings on prefab entry/exit.
        protected override bool SupportsStageHandling() => false;

        [SerializeField] internal AnimationStudioWindow Studio;
        [SerializeField] internal bool dockableHost;
        [InitializeOnLoadMethod]
        private static void RestoreStudioViews()
        {
            EditorApplication.delayCall += () =>
            {
                foreach (var view in Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>())
#if MashBoxDev
                    if (view) AnimationStudioWindow.RestoreExistingViewport(view);
#else
                    if (view) view.Close();
#endif
            };
        }
        private IMGUIContainer timeline;
        private IMGUIContainer inspector;
        private IMGUIContainer rigControls;
        private IMGUIContainer modeSelector;
        private VisualElement studioMenuBackground;
        private IMGUIContainer modelMenuBar, viewportOptions, studioSettings;
        private IMGUIContainer meshStatistics;
        internal void BindStatistics(System.Action draw)
        {
            BindViewportControl(ref meshStatistics, "mashbox-mesh-statistics", draw);
            meshStatistics.pickingMode = PickingMode.Ignore;
            meshStatistics.style.width = 256; meshStatistics.style.height = 132;
            UpdatePanelLayout(); ApplyTheme();
        }
        private IMGUIContainer markingMenu;
        private IMGUIContainer modelTools;
        [SerializeField] internal bool modelGridVisible = true;

        internal void BindModelTools(System.Action draw)
        {
            if (modelTools == null)
            {
                modelTools = new IMGUIContainer { name = "mashbox-model-tools" };
                modelTools.style.position = Position.Absolute;
                modelTools.style.top = ModelingPanelTop; modelTools.style.bottom = 8;
                modelTools.style.left = PanelMargin; modelTools.style.width = ModelToolsWidth;
                modelTools.style.backgroundColor = StudioTheme.Current.ToolRail;
                modelTools.style.borderRightWidth = 1;
                modelTools.style.borderRightColor = StudioTheme.Current.Border;
                modelTools.style.paddingLeft = 3;
                modelTools.style.paddingTop = 5;
                rootVisualElement.Add(modelTools);
            }
            modelTools.onGUIHandler = StudioTheme.Wrap(draw);
            modelTools.style.display = draw == null ? DisplayStyle.None : DisplayStyle.Flex;
            modelTools.BringToFront();
        }

        internal void BindMarkingMenu(System.Action draw)
        {
            if (markingMenu == null)
            {
                markingMenu = new IMGUIContainer { name = "mashbox-model-marking-menu", pickingMode = PickingMode.Ignore };
                markingMenu.style.position = Position.Absolute;
                markingMenu.style.left = 0; markingMenu.style.right = 0;
                markingMenu.style.top = 0; markingMenu.style.bottom = 0;
                rootVisualElement.Add(markingMenu);
            }
            markingMenu.onGUIHandler = StudioTheme.Wrap(draw);
            markingMenu.style.display = draw == null ? DisplayStyle.None : DisplayStyle.Flex;
            markingMenu.BringToFront();
        }

        internal void BindModes(System.Action draw, bool modeling)
        {
            if (modeSelector == null)
            {
                studioMenuBackground = new VisualElement { name = "mashbox-studio-menu-background", pickingMode = PickingMode.Ignore };
                studioMenuBackground.style.position = Position.Absolute;
                studioMenuBackground.style.left = 0; studioMenuBackground.style.right = 0;
                studioMenuBackground.style.top = 0; studioMenuBackground.style.height = 24;
                studioMenuBackground.style.borderBottomWidth = 3;
                rootVisualElement.Add(studioMenuBackground);
                modeSelector = new IMGUIContainer { name = "mashbox-studio-mode-selector" };
                modeSelector.style.position = Position.Absolute;
                modeSelector.style.width = ModeSelectorWidth; modeSelector.style.height = 24;
                modeSelector.style.backgroundColor = Color.clear;
                rootVisualElement.Add(modeSelector);
            }
            modeSelector.onGUIHandler = StudioTheme.Wrap(draw); modeSelector.BringToFront();
            if (inspector != null) { inspector.style.top = ModelingPanelTop; inspector.style.bottom = modeling ? 8 : 148; inspector.style.height = StyleKeyword.Auto; }
            if (rigControls != null) { rigControls.style.top = ModelingPanelTop; rigControls.style.bottom = modeling ? new StyleLength(8) : new StyleLength(StyleKeyword.Auto); }
            if (timeline != null) timeline.style.display = modeling ? DisplayStyle.None : DisplayStyle.Flex;
            BindPanelResizers(modeling);
            ApplyTheme();
        }

        internal void BindViewportControls(System.Action menu, System.Action options, System.Action settings)
        {
            BindViewportControl(ref modelMenuBar, "mashbox-model-menu", menu);
            BindViewportControl(ref viewportOptions, "mashbox-viewport-options", options);
            BindViewportControl(ref studioSettings, "mashbox-studio-settings", settings);
            modelMenuBar.style.top = 0; modelMenuBar.style.left = PanelMargin + ModeSelectorWidth + 20; modelMenuBar.style.right = 0; modelMenuBar.style.height = 24;
            viewportOptions.style.width = 202; viewportOptions.style.height = 24;
            studioSettings.style.width = 24; studioSettings.style.height = 24;
            UpdatePanelLayout(); ApplyTheme();
        }
        private void BindViewportControl(ref IMGUIContainer control, string elementName, System.Action draw)
        {
            if (control == null)
            {
                control = new IMGUIContainer { name = elementName };
                control.style.position = Position.Absolute;
                rootVisualElement.Add(control);
            }
            control.onGUIHandler = StudioTheme.Wrap(draw);
            control.style.display = draw == null ? DisplayStyle.None : DisplayStyle.Flex;
            control.BringToFront();
        }

        internal bool IsOverViewportControls(Vector2 point) =>
            IsOverControl(modelMenuBar, point) || IsOverControl(modeSelector, point) || IsOverControl(viewportOptions, point) || IsOverControl(studioSettings, point);
        private static bool IsOverControl(IMGUIContainer control, Vector2 point) =>
            control != null && control.resolvedStyle.display != DisplayStyle.None && control.layout.Contains(point);

        internal void BindRigControls(System.Action draw)
        {
            if (rigControls == null)
            {
                rigControls = new IMGUIContainer { name = "mashbox-animation-rig-controls" };
                rigControls.style.position = Position.Absolute;
                rigControls.style.top = 8;
                rigControls.style.left = 8;
                rigControls.style.width = 260;
                rigControls.style.backgroundColor = StudioTheme.Current.Panel;
                rootVisualElement.Add(rigControls);
            }
            rigControls.onGUIHandler = StudioTheme.Wrap(draw);
            rigControls.BringToFront();
        }

        internal void BindInspector(System.Action draw)
        {
            if (inspector == null)
            {
                inspector = new IMGUIContainer();
                inspector.name = "mashbox-animation-inspector";
                inspector.style.position = Position.Absolute;
                inspector.style.top = 8;
                inspector.style.right = 8;
                inspector.style.width = 350;
                inspector.style.backgroundColor = StudioTheme.Current.Panel;
                rootVisualElement.Add(inspector);
            }
            inspector.onGUIHandler = StudioTheme.Wrap(draw);
            SetInspectorExpanded(true);
            inspector.BringToFront();
        }

        internal void SetInspectorExpanded(bool expanded)
        {
            if (inspector == null) return;
            inspector.style.bottom = expanded ? new StyleLength(148) : new StyleLength(StyleKeyword.Auto);
            inspector.style.height = expanded ? new StyleLength(StyleKeyword.Auto) : new StyleLength(28);
        }

        internal void BindTimeline(System.Action draw)
        {
            minSize = new Vector2(700, 400);
            if (timeline == null)
            {
                timeline = new IMGUIContainer();
                timeline.name = "mashbox-animation-timeline";
                timeline.style.position = Position.Absolute;
                timeline.style.left = 0;
                timeline.style.right = 0;
                timeline.style.bottom = 0;
                timeline.style.height = 140;
                timeline.style.backgroundColor = StudioTheme.Current.Header;
                rootVisualElement.Add(timeline);
            }
            timeline.onGUIHandler = StudioTheme.Wrap(draw);
            timeline.BringToFront();
        }

        internal void Bind(Scene scene)
        {
            customScene = scene;
            // Studio draws its own grid, controls and selection handles. Scene-wide
            // map/terrain gizmo callbacks must not run against this preview camera.
            drawGizmos = false;
            titleContent = new GUIContent("MashBox Studio");
            sceneLighting = true;
            showGrid = false; // Draw our own ground reference in the isolated preview scene.
            sceneViewState.showSkybox = false;
            sceneViewState.showFog = false;
            sceneViewState.showImageEffects = false;
            rotation = Quaternion.Euler(10, 180, 0);
            foreach (var overlay in overlayCanvas.overlays) overlay.displayed = false;
            // SceneView's stage handling otherwise culls ordinary preview scenes.
            typeof(SceneView).GetProperty("overrideSceneCullingMask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(this, EditorSceneManager.GetSceneCullingMask(scene));
            Repaint();
        }

        internal void ApplyTheme()
        {
            if (meshStatistics != null) meshStatistics.style.display = panelsModeling && StudioSettings.Current.showMeshStatistics ? DisplayStyle.Flex : DisplayStyle.None;
            var palette = StudioTheme.Current;
            if (modeSelector != null) modeSelector.style.backgroundColor = Color.clear;
            if (studioMenuBackground != null)
            {
                studioMenuBackground.style.backgroundColor = palette.Button;
                studioMenuBackground.style.borderBottomColor = palette.Header;
            }
            if (modelMenuBar != null) modelMenuBar.style.backgroundColor = Color.clear;
            if (viewportOptions != null) viewportOptions.style.backgroundColor = Color.clear;
            if (studioSettings != null) studioSettings.style.backgroundColor = Color.clear;
            if (inspector != null) inspector.style.backgroundColor = palette.Panel;
            if (rigControls != null) rigControls.style.backgroundColor = palette.Panel;
            if (modelTools != null)
            {
                modelTools.style.backgroundColor = palette.ToolRail;
                modelTools.style.borderRightColor = palette.Border;
            }
            if (timeline != null) timeline.style.backgroundColor = palette.Header;
        }
        internal void DrawGroundGrid()
        {
            if (Event.current.type != EventType.Repaint) return;
            var previousColor = Handles.color;
            var previousDepth = Handles.zTest;
            var previousMatrix = Handles.matrix;
            try
            {
                Handles.matrix = Matrix4x4.identity;
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                float spacing = StudioSettings.Current.gridSpacing;
                const int steps = 40;
                float extent = steps * spacing;
                // Fixed, bounded geometry: no scene objects, asset writes, or picking controls.
                for (int i = -steps; i <= steps; i++)
                {
                    if (i == 0) continue;
                    float coordinate = i * spacing;
                    float fade = 1f - 0.65f * Mathf.Abs(i) / steps;
                    Handles.color = StudioTheme.Alpha(StudioTheme.Current.Grid, (i % 4 == 0 ? 0.32f : 0.14f) * fade * StudioSettings.Current.gridOpacity);
                    Handles.DrawLine(new Vector3(-extent, 0, coordinate), new Vector3(extent, 0, coordinate));
                    Handles.DrawLine(new Vector3(coordinate, 0, -extent), new Vector3(coordinate, 0, extent));
                }
                Handles.color = new Color(0.9f, 0.3f, 0.25f, 0.65f * StudioSettings.Current.gridOpacity);
                Handles.DrawLine(new Vector3(-extent, 0, 0), new Vector3(extent, 0, 0));
                Handles.color = new Color(0.25f, 0.5f, 1f, 0.65f * StudioSettings.Current.gridOpacity);
                Handles.DrawLine(new Vector3(0, 0, -extent), new Vector3(0, 0, extent));
            }
            finally
            {
                Handles.color = previousColor;
                Handles.zTest = previousDepth;
                Handles.matrix = previousMatrix;
            }
        }
    }
}

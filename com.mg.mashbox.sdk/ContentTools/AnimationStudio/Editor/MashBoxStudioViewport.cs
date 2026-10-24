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
        private VisualElement statusBar;
        private readonly System.Collections.Generic.List<StatusToast> statusMessages = new System.Collections.Generic.List<StatusToast>();
        private readonly System.Collections.Generic.Dictionary<string, double> statusLastShown = new System.Collections.Generic.Dictionary<string, double>(System.StringComparer.Ordinal);
        private readonly System.Collections.Generic.List<string> expiredStatusKeys = new System.Collections.Generic.List<string>();
        private const int MaxStatusMessages = 6;
        private const double StatusRepeatCooldown = 2;
        private IVisualElementScheduledItem statusAnimation;
        private bool statusEnabled;
        private const double StatusFadeIn = .18, StatusHold = 2.1, StatusFadeOut = .4;
        private sealed class StatusToast
        {
            internal Label label;
            internal double started, hold;
        }
        internal void ShowStatusMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            EnsureStatusMessages();
            if (!statusEnabled || StudioSettings.Current.hideStatusBar) return;
            string text = message.Replace('\n', ' ').Trim();
            double now = EditorApplication.timeSinceStartup;
            // Suppression must not refresh a toast's lifetime or schedule redraws.
            expiredStatusKeys.Clear();
            foreach (var pair in statusLastShown)
                if (now - pair.Value >= StatusRepeatCooldown) expiredStatusKeys.Add(pair.Key);
            foreach (string key in expiredStatusKeys) statusLastShown.Remove(key);
            if (statusLastShown.ContainsKey(text)) return;
            foreach (var toast in statusMessages)
                if (toast.label.text == text && now - toast.started < StatusFadeIn + toast.hold + StatusFadeOut) return;
            statusLastShown[text] = now;
            while (statusMessages.Count >= MaxStatusMessages)
            {
                statusMessages[0].label.RemoveFromHierarchy();
                statusMessages.RemoveAt(0);
            }
            var label = new Label(text) { pickingMode = PickingMode.Ignore, tooltip = message };
            label.style.minHeight = 30;
            label.style.paddingLeft = label.style.paddingRight = 12;
            label.style.paddingTop = label.style.paddingBottom = 6;
            label.style.marginTop = 6;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.fontSize = 13;
            label.style.backgroundColor = StudioTheme.Current.Header;
            label.style.color = StudioTheme.Current.Text;
            label.style.borderTopLeftRadius = label.style.borderTopRightRadius = 4;
            label.style.borderBottomLeftRadius = label.style.borderBottomRightRadius = 4;
            label.style.opacity = 0;
            statusBar.Add(label);
            // Allow roughly 25 characters per second, with the short-message minimum.
            // Store the duration once so every message has its own stable expiry.
            statusMessages.Add(new StatusToast {
                label = label, started = now,
                hold = System.Math.Max(StatusHold, message.Trim().Length / 25d)
            });
            // Newest is appended at the bottom; existing messages move upward.
            LayoutStatusMessages(); statusBar.BringToFront();
            statusAnimation.Resume();
            RepaintUI();
            TickStatusMessages();
        }
        private void EnsureStatusMessages()
        {
            if (statusBar != null) return;
            statusBar = new VisualElement { name = "mashbox-status-notifications", pickingMode = PickingMode.Ignore };
            statusBar.style.position = Position.Absolute;
            statusBar.style.bottom = PanelMargin;
            rootVisualElement.Add(statusBar);
            statusAnimation = statusBar.schedule.Execute(TickStatusMessages).Every(16);
            statusAnimation.Pause();
        }
        private void LayoutStatusMessages()
        {
            if (statusBar == null) return;
            float width = Mathf.Min(520, Mathf.Max(0, StudioContentWidth - 32));
            statusBar.style.width = width;
            statusBar.style.left = Mathf.Max(16, (StudioContentWidth - width) * .5f);
            statusBar.style.display = statusEnabled && !StudioSettings.Current.hideStatusBar && statusMessages.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
        private void TickStatusMessages()
        {
            double now = EditorApplication.timeSinceStartup;
            bool changed = false;
            for (int i = statusMessages.Count - 1; i >= 0; i--)
            {
                var toast = statusMessages[i]; double age = now - toast.started;
                if (age >= StatusFadeIn + toast.hold + StatusFadeOut)
                { toast.label.RemoveFromHierarchy(); statusMessages.RemoveAt(i); changed = true; continue; }
                float opacity;
                if (age < StatusFadeIn) opacity = (float)(age / StatusFadeIn);
                else if (age < StatusFadeIn + toast.hold) opacity = 1;
                else opacity = 1 - (float)((age - StatusFadeIn - toast.hold) / StatusFadeOut);
                opacity = Mathf.Clamp01(opacity);
                if (toast.label.style.opacity.value != opacity)
                { toast.label.style.opacity = opacity; changed = true; }
            }
            if (changed)
            {
                LayoutStatusMessages();
                statusBar.MarkDirtyRepaint();
                RepaintUI();
            }
            // Keep the scheduler alive through the finite notification lifetime.
            // Holding fully opaque messages causes no redraws; an empty stack stops it.
            if (statusMessages.Count == 0) statusAnimation?.Pause();
        }
        private void ClearStatusMessages()
        {
            statusAnimation?.Pause();
            statusMessages.Clear(); statusBar?.Clear(); statusLastShown.Clear(); expiredStatusKeys.Clear();
            LayoutStatusMessages();
            RepaintUI();
        }
        private IMGUIContainer orientationAxes;
        private readonly Vector3[] orientationAxisLine = new Vector3[2];
        private static readonly GUIContent[] OrientationAxisLabels = { new GUIContent("X"), new GUIContent("Y"), new GUIContent("Z") };
        private GUIStyle orientationAxisLabelStyle;
        private void DrawOrientationAxes()
        {
            if (Event.current.type != EventType.Repaint || !camera) return;
            var inverse = Quaternion.Inverse(camera.transform.rotation);
            // Paint the farthest axis first so overlapping axis labels read in 3D.
            var x = inverse * Vector3.right;
            var y = inverse * Vector3.up;
            var z = inverse * Vector3.forward;
            Handles.BeginGUI();
            using (new Handles.DrawingScope())
            {
                int drawn = 0;
                for (int pass = 0; pass < 3; pass++)
                {
                    int next = -1; float depth = float.NegativeInfinity;
                    for (int axis = 0; axis < 3; axis++)
                    {
                        float candidate = axis == 0 ? x.z : axis == 1 ? y.z : z.z;
                        if ((drawn & (1 << axis)) == 0 && candidate > depth)
                        { next = axis; depth = candidate; }
                    }
                    drawn |= 1 << next;
                    DrawOrientationAxis(next == 0 ? x : next == 1 ? y : z,
                        next == 0 ? Handles.xAxisColor : next == 1 ? Handles.yAxisColor : Handles.zAxisColor, next);
                }
            }
            Handles.EndGUI();
        }
        private void DrawOrientationAxis(Vector3 direction, Color color, int axis)
        {
            if (orientationAxisLabelStyle == null)
                orientationAxisLabelStyle = new GUIStyle(EditorStyles.boldLabel) {
                    alignment = TextAnchor.MiddleCenter, fontSize = 12,
                    padding = new RectOffset(), margin = new RectOffset()
                };
            var origin = new Vector3(32, 32, 0);
            var projected = new Vector3(direction.x, -direction.y, 0);
            var tip = origin + projected * 23 + projected.normalized * 2;
            Handles.color = color;
            orientationAxisLine[0] = origin;
            orientationAxisLine[1] = origin + projected * 17;
            Handles.DrawAAPolyLine(3f, orientationAxisLine);
            orientationAxisLabelStyle.normal.textColor = color;
            GUI.Label(new Rect(tip.x-8, tip.y-8, 16, 16), OrientationAxisLabels[axis], orientationAxisLabelStyle);
        }
        internal void BindStatusBar(bool enabled)
        {
            statusEnabled = enabled;
            EnsureStatusMessages();
            if (!enabled) ClearStatusMessages();
            LayoutStatusMessages();
            BindViewportControl(ref orientationAxes, "mashbox-orientation-axes", enabled ? (System.Action)DrawOrientationAxes : null);
            orientationAxes.pickingMode = PickingMode.Ignore;
            UpdatePanelLayout(); ApplyTheme();
        }
        private IMGUIContainer modeSelector;
        private VisualElement studioMenuBackground;
        private IMGUIContainer modelMenuBar, viewportOptions, studioSettings;
        private IMGUIContainer meshStatistics;
        internal void BindStatistics(System.Action draw)
        {
            BindViewportControl(ref meshStatistics, "mashbox-mesh-statistics", draw);
            meshStatistics.pickingMode = PickingMode.Ignore;
            meshStatistics.style.width = 256; meshStatistics.style.height = 72;
            BindCameraSettingsButton();
            UpdatePanelLayout(); ApplyTheme();
        }
        private IMGUIContainer markingMenu;
        private IMGUIContainer modelToolOptions;
        internal void BindModelToolOptions(System.Action draw)
        {
            BindViewportControl(ref modelToolOptions, "mashbox-model-tool-options", draw);
            modelToolOptions.focusable = true;
            UpdatePanelLayout(); ApplyTheme();
        }
        internal void FocusModelToolOptions()
        {
            if (StudioSettings.Current.hideToolOptions)
            { StudioSettings.Current.hideToolOptions = false; StudioSettings.Save(); }
            UpdatePanelLayout(); modelToolOptions?.Focus(); modelToolOptions?.MarkDirtyRepaint(); RepaintUI();
        }
        [SerializeField] internal bool modelGridVisible = true;

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
            markingMenu.onGUIHandler = WrapStudioGUI(draw);
            markingMenu.style.display = draw == null ? DisplayStyle.None : DisplayStyle.Flex;
            markingMenu.BringToFront();
        }

        internal void BindModes(System.Action draw, bool modeling)
        {
            if (modeSelector == null)
            {
                studioMenuBackground = new VisualElement { name = "mashbox-studio-menu-background", pickingMode = PickingMode.Position };
                studioMenuBackground.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                studioMenuBackground.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
                studioMenuBackground.RegisterCallback<WheelEvent>(evt => evt.StopPropagation());
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
            modeSelector.onGUIHandler = WrapStudioGUI(draw, true); modeSelector.BringToFront();
            if (inspector != null) { inspector.style.top = ModelingPanelTop; inspector.style.bottom = modeling ? PanelMargin : 148; inspector.style.height = StyleKeyword.Auto; }
            if (rigControls != null) { rigControls.style.top = ModelingPanelTop; rigControls.style.bottom = modeling ? new StyleLength(PanelMargin) : new StyleLength(StyleKeyword.Auto); }
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
            control.onGUIHandler = WrapStudioGUI(draw, elementName == "mashbox-model-menu");
            control.style.display = draw == null ? DisplayStyle.None : DisplayStyle.Flex;
            control.BringToFront();
        }

        internal bool IsOverViewportControls(Vector2 point) =>
            IsOverNativeToolbar(point) || IsOverControl(viewportCameraButton, point) || IsOverControl(viewportLightingButton, point) || IsOverControl(modelMenuBar, point) || IsOverControl(modeSelector, point) || IsOverControl(viewportOptions, point) || IsOverControl(studioSettings, point);
        private bool IsOverControl(IMGUIContainer control, Vector2 point) =>
            control != null && control.resolvedStyle.display != DisplayStyle.None && control.worldBound.Contains(rootVisualElement.LocalToWorld(point));

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
            rigControls.onGUIHandler = WrapStudioGUI(draw);
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
            inspector.onGUIHandler = WrapStudioGUI(draw);
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
            timeline.onGUIHandler = WrapStudioGUI(draw);
            timeline.BringToFront();
        }

        internal void Bind(Scene scene)
        {
            MarkDirty(DirtyState.Scene);
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
            sceneViewState.alwaysRefresh = false;
            // Binding a scene must not change an existing camera view.
            InitializeStudioToolbars();
            // SceneView's stage handling otherwise culls ordinary preview scenes.
            typeof(SceneView).GetProperty("overrideSceneCullingMask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(this, EditorSceneManager.GetSceneCullingMask(scene));
            Repaint();
        }

        internal void ApplyTheme()
        {
            InitializeStudioToolbars();
            UpdateWorkspaceStatisticsState();
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
            if (modelToolOptions != null) modelToolOptions.style.backgroundColor = palette.Panel;
            if (rigControls != null) rigControls.style.backgroundColor = palette.Panel;
            foreach (var toast in statusMessages) { toast.label.style.backgroundColor = palette.Header; toast.label.style.color = palette.Text; }
            UpdatePanelLayout();
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


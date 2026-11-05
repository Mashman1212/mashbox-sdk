using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class MashBoxStudioViewport
    {
        // Embedded native SceneViews share the editor/document, never their cameras or GUI controls.
        [SerializeField] private bool quadLayout;
        internal MashBoxStudioViewport QuadOwner { get; private set; }
        internal bool IsEmbeddedView => QuadOwner;
        internal int ViewKind { get; private set; } = 1;
        internal bool IsOrthographicView => IsEmbeddedView && ViewKind != 1;
        internal StudioLightingSettings ViewLighting;
        private MashBoxStudioViewport[] quadViews;
        private VisualElement quadRoot;
        private IMGUIContainer quadHeader;
        private int maximizedQuad = -1, activeQuad = 1, hoveredQuad = -1, queuedQuad = -2;
        private bool layingOutQuad;
        private static readonly string[] QuadNames = { "Front", "Perspective", "Side", "Top" };
        internal MashBoxStudioViewport LayoutOwner => QuadOwner ? QuadOwner : this;
        internal bool FourViews => LayoutOwner.quadLayout;
        internal MashBoxStudioViewport EditingView => quadLayout && quadViews != null ? quadViews[activeQuad] : this;
        internal static EditorWindow HostWindow(SceneView view) => view is MashBoxStudioViewport tile ? tile.LayoutOwner : view;
        internal static bool PointerOver(SceneView view) => view is MashBoxStudioViewport tile && tile.IsEmbeddedView
            ? mouseOverWindow == tile.QuadOwner && tile.QuadOwner.hoveredQuad == tile.ViewKind : mouseOverWindow == view;
        internal bool OwnsModelView(SceneView view) => view == this ||
            (view is MashBoxStudioViewport embedded && embedded.QuadOwner == this);

        internal void SetFourViews(bool enabled)
        {
            if (IsEmbeddedView) { QuadOwner.SetFourViews(enabled); return; }
            if (quadLayout == enabled && (enabled == (quadViews != null))) return;
            quadLayout = enabled;
            if (enabled) CreateQuadViews();
            else ReleaseQuadViews(true);
            UpdatePanelLayout(); MarkDirty(DirtyState.All);
        }
        private void CreateQuadViews()
        {
            if (quadViews != null || !Studio || !Studio.IsModelingMode || Studio.ToolbarModelEditor == null || !customScene.IsValid()) return;
            quadRoot = new VisualElement { name = "mashbox-four-views" };
            quadRoot.style.position = Position.Absolute;
            quadRoot.style.overflow = Overflow.Hidden;
            quadRoot.style.backgroundColor = StudioTheme.Current.Header;
            rootVisualElement.Add(quadRoot);
            quadViews = new MashBoxStudioViewport[4];
            for (int i = 0; i < 4; i++)
            {
                var view = CreateInstance<MashBoxStudioViewport>();
                view.hideFlags = HideFlags.HideAndDontSave;
                view.QuadOwner = this; view.ViewKind = i; view.Studio = Studio;
                quadViews[i] = view;
                view.ViewLighting = JsonUtility.FromJson<StudioLightingSettings>(JsonUtility.ToJson(Studio.ToolbarModelEditor.LightingSettings));
                view.modelGridVisible = modelGridVisible;
                view.panelsModeling = true;
                view.customScene = customScene;
                typeof(SceneView).GetProperty("overrideSceneCullingMask", WindowMembers)?.SetValue(view, EditorSceneManager.GetSceneCullingMask(customScene));
                view.drawGizmos = false; view.showGrid = false; view.sceneLighting = true;
                view.cameraMode = SceneView.GetBuiltinCameraMode(DrawCameraMode.Textured);
                view.sceneViewState.showSkybox = view.sceneViewState.showFog = view.sceneViewState.showImageEffects = view.sceneViewState.alwaysRefresh = false;
                view.HideSceneOverlays();
                view.cameraSettings.fieldOfView = cameraSettings.fieldOfView;
                view.orthographic = i != 1; view.camera.orthographic = i != 1; view.LookAtDirect(pivot, i == 1 ? rotation : QuadRotation(i), size);
                var root = view.rootVisualElement;
                root.style.position = Position.Absolute;
                root.style.overflow = Overflow.Hidden;
                quadRoot.Add(root);
                view.BindCanvasFocus();
                view.BindModelInput(() => Studio && Studio.IsModelingMode,
                    () => HandleQuadShortcut() || Studio.ToolbarModelEditor.HandleModelKeyboard(view),
                    () => Studio?.ToolbarModelEditor?.ReleaseShortcutHolds());
                view.quadHeader = new IMGUIContainer(view.DrawQuadHeader) { name = "mashbox-view-header" };
                view.quadHeader.style.position = Position.Absolute;
                view.quadHeader.style.left = 4; view.quadHeader.style.top = 4;
                view.quadHeader.style.width = 200; view.quadHeader.style.height = 24;
                root.Add(view.quadHeader);
                int index = i;
                root.RegisterCallback<PointerMoveEvent>(_ => { hoveredQuad = index; });
                root.RegisterCallback<PointerLeaveEvent>(e => {
                    if (!root.worldBound.Contains(e.position) && hoveredQuad == index) hoveredQuad = -1;
                });
                root.RegisterCallback<PointerDownEvent>(_ => { if (activeQuad != index) { activeQuad = index; SyncStudioToolbarState(true); } }, TrickleDown.TrickleDown);
            }
            quadRoot.RegisterCallback<KeyDownEvent>(e => {
                if (StudioShortcuts.Pressed(StudioShortcut.MaximizeViewport, new Event { type=EventType.KeyDown, keyCode=e.keyCode, modifiers=e.modifiers }) && !EditorGUIUtility.editingTextField && GUIUtility.hotControl == 0)
                { QueueQuadMaximize(); e.imguiEvent?.Use(); e.StopImmediatePropagation(); }
            }, TrickleDown.TrickleDown);
            LayoutQuadViews();
        }
        internal static Quaternion QuadRotation(int kind) => kind == 0 ? Quaternion.Euler(0, 180, 0) :
            kind == 2 ? Quaternion.Euler(0, -90, 0) : Quaternion.Euler(90, 0, 0);
        private void DrawQuadHeader()
        {
            var editor = Studio?.ToolbarModelEditor;
            if (editor == null) return;
            for (int i = 0; i < 3; i++)
            {
                var rect = new Rect(i * 28, 0, 24, 24);
                var content = new GUIContent(StudioToolbarIcons.Get(i == 0 ? "Camera Icon" : i == 1 ? "Light Icon" : "GridLayoutGroup Icon"),
                    i == 0 ? "Camera settings" : i == 1 ? "Viewport lighting" : "Toggle this view's grid");
                if (GUI.Button(rect, content, StudioTheme.Styles.overlayButton))
                {
                    QuadOwner.activeQuad = ViewKind;
                    if (i == 0) editor.ShowCameraOptions(this, true);
                    else if (i == 1) editor.ShowLightingOptions(this, true);
                    else SetModelGridVisible(!modelGridVisible);
                }
            }
            GUI.Label(new Rect(88, 2, 112, 22), QuadNames[ViewKind], EditorStyles.boldLabel);
        }
        private bool HandleQuadShortcut()
        {
            var e = Event.current;
            if (!FourViews || e == null || !StudioShortcuts.Pressed(StudioShortcut.MaximizeViewport,e) ||
                EditorGUIUtility.editingTextField || GUIUtility.hotControl != 0) return false;
            LayoutOwner.QueueQuadMaximize(); e.Use(); return true;
        }
        private void QueueQuadMaximize()
        {
            if (quadViews == null || (maximizedQuad < 0 && hoveredQuad < 0)) return;
            queuedQuad = maximizedQuad >= 0 ? -1 : hoveredQuad;
            EditorApplication.update -= ApplyQueuedQuad;
            EditorApplication.update += ApplyQueuedQuad;
        }
        private void ApplyQueuedQuad()
        {
            EditorApplication.update -= ApplyQueuedQuad;
            if (!this || quadViews == null || queuedQuad == -2) return;
            maximizedQuad = queuedQuad; queuedQuad = -2;
            if (maximizedQuad >= 0) activeQuad = maximizedQuad;
            var editor = Studio?.ToolbarModelEditor;
            if (editor?.CameraOptionsVisible == true) editor.ShowCameraOptions(EditingView, true);
            else if (editor?.LightingOptionsVisible == true) editor.ShowLightingOptions(EditingView, true);
            LayoutQuadViews(); MarkDirty(DirtyState.All);
        }
        private void LayoutQuadViews()
        {
            if (IsEmbeddedView || layingOutQuad) return;
            layingOutQuad = true;
            try
            {
                bool visible = quadLayout && Studio && Studio.IsModelingMode;
                if (!visible && quadViews != null) ReleaseQuadViews(true);
                if (visible && quadViews == null) CreateQuadViews();
                if (quadRoot == null) return;
                quadRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (cameraInputCanvas != null) cameraInputCanvas.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
                foreach (var button in new[] { viewportCameraButton, viewportLightingButton })
                    if (button != null && visible) button.style.display = DisplayStyle.None;
                if (orientationAxes != null && visible) orientationAxes.style.display = DisplayStyle.None;
                var area = ModelCanvasRect;
                // The shared menu occupies the top 24 points even with native toolbars hidden.
                float inset = Mathf.Max(0, 28 - area.y);
                area.y += inset; area.height = Mathf.Max(0, area.height - inset);
                quadRoot.style.left = area.x; quadRoot.style.top = area.y;
                quadRoot.style.width = area.width; quadRoot.style.height = area.height;
                for (int i = 0; i < 4; i++)
                {
                    var root = quadViews[i].rootVisualElement;
                    bool show = maximizedQuad < 0 || maximizedQuad == i;
                    root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                    float w = maximizedQuad < 0 ? (area.width - 2) / 2 : area.width;
                    float h = maximizedQuad < 0 ? (area.height - 2) / 2 : area.height;
                    root.style.left = maximizedQuad < 0 && (i == 1 || i == 2) ? w + 2 : 0;
                    root.style.top = maximizedQuad < 0 && i >= 2 ? h + 2 : 0;
                    root.style.width = w; root.style.height = h;
                }
                quadRoot.BringToFront();
                // Global editing actions stay above the tiles; camera/light controls live in each tile.
                if (viewportIsolateButton != null) { viewportIsolateButton.style.left=area.xMax-56; viewportIsolateButton.BringToFront(); }
                if (viewportShowAllButton != null) { viewportShowAllButton.style.left=area.xMax-28; viewportShowAllButton.BringToFront(); }
                markingMenu?.BringToFront();
            }
            finally { layingOutQuad = false; }
        }
        private void ReleaseQuadViews(bool retainPerspective)
        {
            EditorApplication.update -= ApplyQueuedQuad; queuedQuad = -2;
            if (quadViews == null) return;
            if (retainPerspective && quadViews[1])
            {
                var v = quadViews[1]; LookAtDirect(v.pivot, v.rotation, v.size);
                cameraSettings.fieldOfView = v.cameraSettings.fieldOfView;
            }
            Studio?.ToolbarModelEditor?.ReleaseViewportLighting();
            var released = quadViews; quadViews = null;
            quadRoot?.RemoveFromHierarchy(); quadRoot = null;
            foreach (var view in released) if (view) { view.rootVisualElement.RemoveFromHierarchy(); DestroyImmediate(view); }
            maximizedQuad = -1; hoveredQuad = -1;
            if (cameraInputCanvas != null) cameraInputCanvas.style.display = DisplayStyle.Flex;
        }
        private void RepaintQuadViews(DirtyState state)
        {
            if (quadViews == null) return;
            foreach (var view in quadViews) if (view)
            {
                view.viewportDirty |= state;
                view.cameraInputCanvas?.MarkDirtyRepaint(); view.quadHeader?.MarkDirtyRepaint();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class MashBoxStudioViewport
    {
        [Serializable] private sealed class StoredToolbars
        {
            public bool enabled = true;
            public List<StoredToolbar> items = new List<StoredToolbar>();
        }
        [Serializable] private sealed class StoredToolbar
        {
            public string id;
            public int index;
            public SaveData state;
        }
        private bool restoringStoredToolbars, restoredStoredToolbars;
        private void SaveStoredToolbars()
        {
            if (restoringStoredToolbars || !restoredStoredToolbars || studioToolbarOverlays.Count == 0) return;
            var saved = new StoredToolbars { enabled = overlayCanvas.overlaysEnabled };
            foreach (var overlay in studioToolbarOverlays)
            {
                if (overlay.GetType().Assembly != typeof(MashBoxStudioViewport).Assembly) continue;
                var state = new SaveData(overlay);
                // SaveData owns Unity's native container index; a tiny reader avoids private-field reflection.
                var order = JsonUtility.FromJson<StoredToolbar>(JsonUtility.ToJson(state));
                saved.items.Add(new StoredToolbar { id = overlay.id, index = order.index, state = state });
            }
            StudioPreferenceStore.SetString("Studio.Toolbars", JsonUtility.ToJson(saved));
        }
        private void RestoreStoredToolbars()
        {
            restoringStoredToolbars = true;
            try
            {
                string raw = StudioPreferenceStore.GetString("Studio.Toolbars");
                if (string.IsNullOrEmpty(raw)) return;
                var saved = JsonUtility.FromJson<StoredToolbars>(raw);
                if (saved?.items == null) return;
                saved.items.Sort((a, b) => a.index.CompareTo(b.index));
                foreach (var item in saved.items)
                    foreach (var overlay in studioToolbarOverlays)
                        if (overlay.id == item.id && item.state != null)
                        {
                            overlayCanvas.RestoreOverlay(overlay, item.state);
                        }
                overlayCanvas.overlaysEnabled = saved.enabled;
            }
            catch (ArgumentException ex) { Debug.LogWarning("MashBox Studio could not restore toolbar layout: " + ex.Message); }
            finally { restoringStoredToolbars = false; restoredStoredToolbars = true; }
        }
        private const string DisplayToolbarId = StudioViewportToolbar.Id;
        private static readonly PropertyInfo NativeCanvasRoot = typeof(OverlayCanvas).GetProperty("rootVisualElement", WindowMembers);
        [SerializeField] private int studioToolbarVersion;
        private int studioToolbarState = -1;
        private Mesh studioToolbarMesh;
        private int studioToolbarActive = -1;
        internal event Action StudioToolbarStateChanged;
        private VisualElement studioOverlayRoot;
        private readonly List<Overlay> studioToolbarOverlays = new List<Overlay>();
        private VisualElement StudioChromeRoot => studioOverlayRoot ?? rootVisualElement;
        internal bool NativeToolbarsEnabled => studioOverlayRoot != null;
        private float StudioContentWidth => rootVisualElement.contentRect.width > 0 ? rootVisualElement.contentRect.width : position.width;
        private float StudioContentHeight => rootVisualElement.contentRect.height > 0 ? rootVisualElement.contentRect.height : position.height;

        private void InitializeStudioToolbars()
        {
            if (studioOverlayRoot == null)
            {
                studioOverlayRoot = NativeCanvasRoot?.GetValue(overlayCanvas) as VisualElement;
                if (studioOverlayRoot == null) return;
                // The native overlay canvas lays out its window root around docked
                // toolbars. Keep the menu above that entire layout, not inside it.
                var top = studioOverlayRoot.Q("overlay-toolbar__top");
                if (top != null) top.style.marginTop = 24;
                // Keep the floating container's native zero origin. Changing its
                // CSS top makes Unity subtract that inset again during snapping.
                // The menu is layered above floating overlays separately.
            }
            RestrictStudioDockTargets();
            studioToolbarOverlays.Clear();
            foreach (var overlay in overlayCanvas.overlays)
            {
                if (studioToolbarVersion < 2)
                {
                    if (overlay.GetType().Assembly != typeof(MashBoxStudioViewport).Assembly) overlay.displayed = false;
                    else if (overlay.id == DisplayToolbarId) overlay.displayed = true;
                }
                if (!(overlay is ToolbarOverlay)) continue;
                studioToolbarOverlays.Add(overlay);
                overlay.rootVisualElement.UnregisterCallback<GeometryChangedEvent>(OnStudioToolbarGeometry);
                overlay.rootVisualElement.RegisterCallback<GeometryChangedEvent>(OnStudioToolbarGeometry);
                StyleStudioToolbar(overlay.rootVisualElement);
            }
            if (studioToolbarVersion < 2)
            {
                overlayCanvas.overlaysEnabled = true;
                studioToolbarVersion = 2;
                cameraMode = SceneView.GetBuiltinCameraMode(DrawCameraMode.Textured);
            }
            if (!restoredStoredToolbars) RestoreStoredToolbars();
            PlaceStudioMenuAboveToolbars();
            SyncStudioToolbarState(true);
        }
        private void RestrictStudioDockTargets()
        {
            // Keep Unity's outer toolbar docking and reordering. Its inner panel
            // and anchored-column targets sit over Studio's own panes.
            foreach (var name in new[] { "LeftAnchoredDockArea", "RightAnchoredDockArea" })
            {
                var area = studioOverlayRoot.Q(name);
                if (area == null) continue;
                // Hide the parent: Unity reactivates each child target during a drag.
                area.style.display = DisplayStyle.None;

            }
            var sides = studioOverlayRoot.Q("SceneViewContainer");
            if (sides != null) sides.style.justifyContent = Justify.SpaceBetween;
        }
        private void PlaceStudioMenuAboveToolbars()
        {
            if (studioOverlayRoot == null) return;
            if (studioMenuBackground != null) StudioChromeRoot.Add(studioMenuBackground);
            if (modelMenuBar != null)
            {
                StudioChromeRoot.Add(modelMenuBar);
                BindStudioHoverRedraw(modelMenuBar);
            }
            if (modeSelector != null)
            {
                StudioChromeRoot.Add(modeSelector);
                BindStudioHoverRedraw(modeSelector);
            }
            if (MenuInputBlocked) KeepMenuShieldOnTop();
        }
        private bool HasLeftDockedToolbar => HasDockedToolbar("overlay-toolbar__left");
        private bool HasBottomDockedToolbar => HasDockedToolbar("overlay-toolbar__bottom");
        private bool HasRightDockedToolbar => HasDockedToolbar("overlay-toolbar__right");
        private bool HasDockedToolbar(string dockName)
        {
            if (studioOverlayRoot == null || !overlayCanvas.overlaysEnabled) return false;
            var dock = studioOverlayRoot.Q(dockName);
            if (dock == null) return false;
            foreach (var overlay in studioToolbarOverlays)
                if (overlay.displayed && !overlay.floating && dock.Contains(overlay.rootVisualElement)) return true;
            return false;
        }
        private void OnStudioToolbarGeometry(GeometryChangedEvent evt)
        {
            SaveStoredToolbars();
            if (evt.currentTarget is VisualElement root) StyleStudioToolbar(root);
            UpdatePanelLayout();
        }
        private static void StyleStudioToolbar(VisualElement root)
        {
            int size = StudioSettings.Current.toolbarIconSize;
            float height = size + 4;
            bool vertical = root.ClassListContains("overlay-layout--toolbar-vertical");
            // Unity stretches the overlay root to the dock strip's minimum width,
            // but leaves its narrower content box aligned to the left.
            root.style.alignItems = vertical
                ? new StyleEnum<Align>(Align.Center)
                : new StyleEnum<Align>(StyleKeyword.Null);
            // The native vertical overlay has a fixed preferred width. Let its
            // content determine the strip width, matching horizontal padding.
            root.style.width = vertical ? new StyleLength(StyleKeyword.Auto) : new StyleLength(StyleKeyword.Null);
            root.style.minWidth = vertical ? new StyleLength(0f) : new StyleLength(StyleKeyword.Null);
            root.Query<VisualElement>(className: "unity-editor-toolbar-element__icon").ForEach(icon => {
                if (icon.style.width.value.value != size) icon.style.width = size;
                if (icon.style.height.value.value != size) icon.style.height = size;
                icon.style.marginLeft = icon.style.marginRight = 0;
                icon.style.marginTop = icon.style.marginBottom = 0;
                icon.style.flexShrink = 0;
            });
            root.Query<VisualElement>(className: "unity-editor-toolbar-element").ForEach(element => {
                if (element.ClassListContains("unity-editor-toolbar__button-strip"))
                {
                    if (element.style.height.keyword != StyleKeyword.Auto) element.style.height = StyleKeyword.Auto;
                    var contents = element.Q(className: "toolbar-contents");
                    if (contents != null)
                    {
                        var direction = vertical ? FlexDirection.Column : FlexDirection.Row;
                        if (contents.style.flexDirection.value != direction) contents.style.flexDirection = direction;
                        if (contents.style.height.keyword != StyleKeyword.Auto) contents.style.height = StyleKeyword.Auto;
                    }
                    return;
                }
                if (element.style.height.value.value != height) element.style.height = height;
                if (element.ClassListContains("unity-editor-toolbar-toggle") || element.ClassListContains("unity-editor-toolbar-button"))
                {
                    if (element.style.width.value.value != height) element.style.width = height;
                    element.style.minWidth = element.style.maxWidth = height;
                    element.style.minHeight = element.style.maxHeight = height;
                    element.style.flexShrink = 0;
                    element.style.paddingLeft = element.style.paddingRight = 0;
                    element.style.paddingTop = element.style.paddingBottom = 0;
                    element.style.alignSelf = Align.Center;
                    element.style.alignItems = Align.Center;
                    element.style.justifyContent = Justify.Center;
                    var input = element.Q(className: "unity-toggle__input");
                    if (input != null)
                    {
                        input.style.flexGrow = 1;
                        input.style.alignItems = Align.Center;
                        input.style.justifyContent = Justify.Center;
                        input.style.paddingLeft = input.style.paddingRight = 0;
                        input.style.marginLeft = input.style.marginRight = 0;
                    }
                }
                else if (element.ClassListContains("unity-dropdown-toggle"))
                {
                    if (element.style.width.value.value != size + 21) element.style.width = size + 21;
                    var toggle = element.Q(className: "unity-dropdown-toggle__toggle");
                    if (toggle != null && toggle.style.width.value.value != size + 6) toggle.style.width = size + 6;
                }
            });
            root.Query<VisualElement>(className: "unity-toolbar-overlay").ForEach(toolbar => {
                if (toolbar.style.minHeight.value.value != height) toolbar.style.minHeight = height;
            });
        }
        private bool IsOverNativeToolbar(Vector2 point)
        {
            if (studioOverlayRoot == null || !overlayCanvas.overlaysEnabled) return false;
            var world = rootVisualElement.LocalToWorld(point);
            foreach (var overlay in studioToolbarOverlays)
                if (overlay.displayed && overlay.rootVisualElement.worldBound.Contains(world)) return true;
            return false;
        }
        internal void AddStudioToolbarMenu(StudioMenu menu)
        {
            // Unity's canvas is the registry. Query it when opening the menu so
            // restored, hidden and newly registered toolbars are included too.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var overlay in overlayCanvas.overlays)
            {
                if (!(overlay is ToolbarOverlay) || overlay.GetType().Assembly != typeof(MashBoxStudioViewport).Assembly || !seen.Add(overlay.id)) continue;
                var item = overlay;
                bool visible = overlayCanvas.overlaysEnabled && item.displayed;
                // Saved Unity layouts may retain an obsolete displayName. The
                // toolbar declaration owns its current user-facing name; its ID
                // stays unchanged so visibility and docking still restore.
                string label = item.GetType().GetCustomAttribute<OverlayAttribute>(false)?.displayName;
                if (string.IsNullOrWhiteSpace(label)) label = string.IsNullOrWhiteSpace(item.displayName) ? item.id : item.displayName;
                label = label.Replace('/', '∕'); // A toolbar title is one menu leaf.
                menu.AddItem(new GUIContent("Toolbars/" + label), visible, () => {
                    overlayCanvas.overlaysEnabled = true;
                    item.displayed = !visible; SaveStoredToolbars();
                    UpdatePanelLayout();
                    MarkDirty(DirtyState.UI);
                }, commandKey: "Toolbars/" + item.id, shortcutEligible: false);
            }
        }
        internal void SyncStudioToolbarState(bool force = false)
        {
            var editor = Studio?.ToolbarModelEditor;
            int state = editor == null ? 0 : 128 | (editor.ToolbarLocalAxes ? 1 : 0) | (modelGridVisible ? 2 : 0) |
                (editor.ToolbarLighting ? 4 : 0) | (editor.ToolbarTextures ? 8 : 0) |
                (editor.ModelDrawMode == DrawCameraMode.Wireframe ? 16 : 0) |
                (editor.ModelDrawMode == DrawCameraMode.TexturedWire ? 32 : 0) | (!StudioSettings.Current.hideObjectWireframe ? 64 : 0) | (editor.ToolbarTransformState << 8) | ((int)editor.ToolbarComponentMode << 12) | (editor.HasBlendShape ? 16384 : 0) | (editor.CameraOptionsVisible ? 32768 : 0) | (editor.LightingOptionsVisible ? 65536 : 0) | (editor.BackfaceCulling ? 131072 : 0);
            // Track selection identity without reading skin bind-pose arrays on
            // every dirty redraw. Controls validate component editing on refresh.
            var mesh = editor?.ActiveMesh;
            int active = editor?.Active ?? -1;
            if (!force && state == studioToolbarState && mesh == studioToolbarMesh && active == studioToolbarActive) return;
            if (storedConfigurationInitialized) StudioPreferenceStore.SetFloat("Studio.GridVisible", modelGridVisible ? 1 : 0);
            studioToolbarState = state;
            studioToolbarMesh = mesh; studioToolbarActive = active;
            StudioToolbarStateChanged?.Invoke();
        }
        private void ReleaseStudioToolbars()
        {
            foreach (var overlay in studioToolbarOverlays)
            {
                overlay.rootVisualElement.UnregisterCallback<GeometryChangedEvent>(OnStudioToolbarGeometry);
            }
            studioToolbarOverlays.Clear();
            if (modelMenuBar != null) UnbindStudioHoverRedraw(modelMenuBar);
            if (modeSelector != null) UnbindStudioHoverRedraw(modeSelector);
            studioOverlayRoot = null;
        }
    }
}

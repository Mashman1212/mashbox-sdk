using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    // Default indices apply only when Unity has no saved placement for an
    // overlay. Stable IDs let Unity restore each toolbar independently.
    [Overlay(typeof(MashBoxStudioViewport), Id, "Grid", true,
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top,
        defaultDockIndex = 0, defaultLayout = Layout.HorizontalToolbar)]
    internal sealed class StudioGridToolbar : ToolbarOverlay
    {
        internal const string Id = "MashBox/Grid";
        public StudioGridToolbar() : base(StudioToolbarGrid.Id) { }
    }

    [Overlay(typeof(MashBoxStudioViewport), Id, "Viewport", true,
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top,
        defaultDockIndex = 1, defaultLayout = Layout.HorizontalToolbar)]
    internal sealed class StudioViewportToolbar : ToolbarOverlay
    {
        internal const string Id = "MashBox/Viewport";
        public StudioViewportToolbar() : base(StudioToolbarSettings.Id,
            StudioToolbarOrientation.Id, StudioToolbarObjectWire.Id) { }
    }

    [Overlay(typeof(MashBoxStudioViewport), Id, "Display", true,
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top,
        defaultDockIndex = 2, defaultLayout = Layout.HorizontalToolbar)]
    internal sealed class StudioDisplayToolbar : ToolbarOverlay
    {
        internal const string Id = "MashBox/Display";
        public StudioDisplayToolbar() : base(StudioToolbarWireOnly.Id,
            StudioToolbarShadedWire.Id, StudioToolbarLighting.Id, StudioToolbarTextures.Id) { }
    }

    [Overlay(typeof(MashBoxStudioViewport), Id, "Transform Tools", true,
        defaultDockZone = DockZone.LeftToolbar, defaultDockPosition = DockPosition.Top,
        defaultDockIndex = 0, defaultLayout = Layout.VerticalToolbar)]
    internal sealed class StudioTransformToolbar : ToolbarOverlay
    {
        internal const string Id = "MashBox/TransformTools";
        public StudioTransformToolbar() : base(StudioToolbarSelect.Id, StudioToolbarMove.Id,
            StudioToolbarRotate.Id, StudioToolbarScale.Id) { }
    }

    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarSettings : EditorToolbarButton
    {
        internal const string Id = "MashBox/Viewport/Settings";
        public StudioToolbarSettings()
        {
            name = "studio-toolbar-settings";
            icon = EditorGUIUtility.IconContent("SettingsIcon").image as Texture2D;
            tooltip = "Studio settings and appearance";
            clicked += StudioSettingsWindow.Open;
        }
    }

    // Native overlay controls bind to their own containing Studio window. They
    // refresh on display-state changes, not on an editor tick or polling timer.
    internal abstract class StudioToolbarToggle : EditorToolbarToggle, IAccessContainerWindow
    {
        protected MashBoxStudioViewport view;
        public EditorWindow containerWindow
        {
            get => view;
            set { Unbind(); view = value as MashBoxStudioViewport; if (panel != null) Bind(); RefreshState(); }
        }
        protected StudioToolbarToggle(string elementName, string iconName, string hint)
        {
            name = elementName; tooltip = hint;
            if (!string.IsNullOrEmpty(iconName)) icon = EditorGUIUtility.IconContent(iconName).image as Texture2D;
            RegisterCallback<AttachToPanelEvent>(_ => Bind());
            RegisterCallback<DetachFromPanelEvent>(_ => Unbind());
            this.RegisterValueChangedCallback(evt => {
                var editor = view ? view.Studio?.ToolbarModelEditor : null;
                if (editor == null) return;
                Apply(editor, evt.newValue);
                view.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
            });
        }
        private void Bind()
        {
            if (view) { view.StudioToolbarStateChanged -= RefreshState; view.StudioToolbarStateChanged += RefreshState; }
            RefreshState();
        }
        private void Unbind() { if (view) view.StudioToolbarStateChanged -= RefreshState; }
        private void RefreshState()
        {
            var editor = view ? view.Studio?.ToolbarModelEditor : null;
            SetEnabled(editor != null);
            SetValueWithoutNotify(editor != null && Read(editor));
        }
        protected abstract bool Read(StudioModelEditor editor);
        protected abstract void Apply(StudioModelEditor editor, bool enabled);
    }

    internal abstract class StudioTransformToolbarToggle : StudioToolbarToggle
    {
        private readonly Tool target;
        private readonly StudioShortcut shortcut;
        private readonly string title;
        protected StudioTransformToolbarToggle(Tool target, StudioShortcut shortcut, string title, string iconName)
            : base("studio-toolbar-" + title.ToLowerInvariant(), iconName, title)
        {
            this.target = target; this.shortcut = shortcut; this.title = title;
            RegisterCallback<AttachToPanelEvent>(_ => { StudioShortcuts.changed -= RefreshTooltip; StudioShortcuts.changed += RefreshTooltip; RefreshTooltip(); });
            RegisterCallback<DetachFromPanelEvent>(_ => StudioShortcuts.changed -= RefreshTooltip);
            RegisterCallback<PointerDownEvent>(evt => {
                if (evt.button != 0 || evt.clickCount != 2 || !view) return;
                var editor = view.Studio?.ToolbarModelEditor;
                if (editor == null) return;
                editor.ActivateTool(target); editor.OpenToolOptions();
                SetValueWithoutNotify(true); evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
        }
        private void RefreshTooltip() => tooltip = title + " (" + StudioShortcuts.Label(shortcut) + ") - Double-click for options";
        protected override bool Read(StudioModelEditor editor) => editor.ToolbarToolSelected(target);
        protected override void Apply(StudioModelEditor editor, bool enabled)
        {
            // An active tool is a radio selection; clicking it again must not
            // leave the model state and its native toggle visually out of sync.
            if (enabled) editor.ActivateTool(target);
            else SetValueWithoutNotify(Read(editor));
        }
    }
    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarSelect : StudioTransformToolbarToggle
    {
        internal const string Id = "MashBox/Tools/Select";
        private static Texture2D selectIcon;
        public StudioToolbarSelect() : base(Tool.None, StudioShortcut.Select, "Select", null)
        {
            if (!selectIcon) selectIcon = CreateSelectIcon();
            icon = selectIcon;
        }
        private static Texture2D CreateSelectIcon()
        {
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) {
                name = "Studio select cursor", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear
            };
            var points = new[] { new Vector2(5,27), new Vector2(5,7), new Vector2(11,13),
                new Vector2(16,4), new Vector2(20,6), new Vector2(15,15), new Vector2(23,15) };
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                int coverage = 0;
                for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++)
                {
                    var point = new Vector2(x + (sx + .5f) / 4, y + (sy + .5f) / 4);
                    bool inside = false;
                    for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                        if ((points[i].y > point.y) != (points[j].y > point.y) &&
                            point.x < (points[j].x - points[i].x) * (point.y - points[i].y) / (points[j].y - points[i].y) + points[i].x) inside = !inside;
                    if (inside) coverage++;
                }
                pixels[y * 32 + x] = new Color(.85f,.85f,.85f,coverage / 16f);
            }
            texture.SetPixels(pixels); texture.Apply(false, true); return texture;
        }
    }
    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarMove : StudioTransformToolbarToggle
    {
        internal const string Id = "MashBox/Tools/Move";
        public StudioToolbarMove() : base(Tool.Move, StudioShortcut.Move, "Move", "MoveTool") { }
    }
    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarRotate : StudioTransformToolbarToggle
    {
        internal const string Id = "MashBox/Tools/Rotate";
        public StudioToolbarRotate() : base(Tool.Rotate, StudioShortcut.Rotate, "Rotate", "RotateTool") { }
    }
    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarScale : StudioTransformToolbarToggle
    {
        internal const string Id = "MashBox/Tools/Scale";
        public StudioToolbarScale() : base(Tool.Scale, StudioShortcut.Scale, "Scale", "ScaleTool") { }
    }

    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarOrientation : EditorToolbarDropdown, IAccessContainerWindow
    {
        internal const string Id = "MashBox/Viewport/Orientation";
        private MashBoxStudioViewport view;
        public EditorWindow containerWindow
        {
            get => view;
            set { Unbind(); view = value as MashBoxStudioViewport; if (panel != null) Bind(); RefreshState(); }
        }
        public StudioToolbarOrientation()
        {
            name = "studio-toolbar-orientation"; tooltip = "Local or World transform axes";
            icon = EditorGUIUtility.IconContent("ToolHandleLocal").image as Texture2D;
            clicked += OpenMenu;
            RegisterCallback<AttachToPanelEvent>(_ => Bind());
            RegisterCallback<DetachFromPanelEvent>(_ => Unbind());
        }
        private void Bind()
        {
            if (view) { view.StudioToolbarStateChanged -= RefreshState; view.StudioToolbarStateChanged += RefreshState; }
            RefreshState();
        }
        private void Unbind() { if (view) view.StudioToolbarStateChanged -= RefreshState; }
        private void RefreshState()
        {
            var editor = view ? view.Studio?.ToolbarModelEditor : null;
            SetEnabled(editor != null);
            text = editor == null || editor.ToolbarLocalAxes ? "Local" : "World";
        }
        private void OpenMenu()
        {
            var editor = view ? view.Studio?.ToolbarModelEditor : null;
            if (editor == null) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Local"), editor.ToolbarLocalAxes, () => view.Studio?.ToolbarModelEditor?.SetToolbarLocalAxes(true));
            menu.AddItem(new GUIContent("World"), !editor.ToolbarLocalAxes, () => view.Studio?.ToolbarModelEditor?.SetToolbarLocalAxes(false));
            menu.DropDown(worldBound);
        }
    }

    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarGrid : StudioToolbarToggle
    {
        internal const string Id = "MashBox/Viewport/Grid";
        public StudioToolbarGrid() : base("studio-toolbar-grid", "GridLayoutGroup Icon", "Show modeling grid") { }
        protected override bool Read(StudioModelEditor editor) => view.modelGridVisible;
        protected override void Apply(StudioModelEditor editor, bool enabled) => view.modelGridVisible = enabled;
    }
    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarWireOnly : StudioToolbarToggle
    {
        internal const string Id = "MashBox/Viewport/Wireframe";
        public StudioToolbarWireOnly() : base("studio-toolbar-wireframe", "Toolbars/wireframe", "Wireframe only") { }
        protected override bool Read(StudioModelEditor editor) => editor.ModelDrawMode == DrawCameraMode.Wireframe;
        protected override void Apply(StudioModelEditor editor, bool enabled) => editor.SetModelDrawMode(view, enabled ? DrawCameraMode.Wireframe : DrawCameraMode.TexturedWire);
    }
    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarShadedWire : StudioToolbarToggle
    {
        internal const string Id = "MashBox/Viewport/ShadedWireframe";
        public StudioToolbarShadedWire() : base("studio-toolbar-shaded-wireframe", "Toolbars/ShadedWireframe", "Shaded wireframe") { }
        protected override bool Read(StudioModelEditor editor) => editor.ModelDrawMode == DrawCameraMode.TexturedWire;
        protected override void Apply(StudioModelEditor editor, bool enabled) => editor.SetModelDrawMode(view, enabled ? DrawCameraMode.TexturedWire : DrawCameraMode.Textured);
    }
    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarLighting : StudioToolbarToggle
    {
        internal const string Id = "MashBox/Viewport/Lighting";
        public StudioToolbarLighting() : base("studio-toolbar-lighting", "SceneViewLighting", "Viewport lighting") { }
        protected override bool Read(StudioModelEditor editor) => editor.ToolbarLighting;
        protected override void Apply(StudioModelEditor editor, bool enabled) => editor.SetDisplayMode(enabled, editor.ToolbarTextures);
    }
    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarTextures : StudioToolbarToggle
    {
        internal const string Id = "MashBox/Viewport/Textures";
        public StudioToolbarTextures() : base("studio-toolbar-textures", "Material Icon", "Material textures (off shows neutral shading)") { }
        protected override bool Read(StudioModelEditor editor) => editor.ToolbarTextures;
        protected override void Apply(StudioModelEditor editor, bool enabled) => editor.SetDisplayMode(editor.ToolbarLighting, enabled);
    }
    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarObjectWire : StudioToolbarToggle
    {
        internal const string Id = "MashBox/Viewport/ObjectWireframe";
        public StudioToolbarObjectWire() : base("studio-toolbar-object-wireframe", "Mesh Icon", "Object wireframe overlay") { }
        protected override bool Read(StudioModelEditor editor) => !StudioSettings.Current.hideObjectWireframe;
        protected override void Apply(StudioModelEditor editor, bool enabled) => editor.SetModelDrawMode(view, enabled ? DrawCameraMode.TexturedWire : DrawCameraMode.Textured);
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    // Default indices apply only when Unity has no saved placement for an
    // overlay. Stable IDs let Unity restore each toolbar independently.
    [Overlay(typeof(MashBoxStudioViewport), Id, "Selection Modes", true,
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top,
        defaultDockIndex = 0, defaultLayout = Layout.HorizontalToolbar)]
    internal sealed class StudioSelectionToolbar : ToolbarOverlay
    {
        internal const string Id = "MashBox/SelectionModes";
        public StudioSelectionToolbar() : base(StudioToolbarObjectMode.Id, StudioToolbarVertexMode.Id,
            StudioToolbarEdgeMode.Id, StudioToolbarFaceMode.Id) { }
    }

    [Overlay(typeof(MashBoxStudioViewport), Id, "Grid", true,
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top,
        defaultDockIndex = 1, defaultLayout = Layout.HorizontalToolbar)]
    internal sealed class StudioGridToolbar : ToolbarOverlay
    {
        internal const string Id = "MashBox/Grid";
        public StudioGridToolbar() : base(StudioToolbarGrid.Id) { }
    }

    [Overlay(typeof(MashBoxStudioViewport), Id, DisplayName, true,
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top,
        defaultDockIndex = 2, defaultLayout = Layout.HorizontalToolbar)]
    internal sealed class StudioViewportToolbar : ToolbarOverlay
    {
        internal const string Id = "MashBox/Viewport";
        internal const string DisplayName = "World & Local";
        public StudioViewportToolbar() : base(StudioToolbarOrientation.Id) { }
    }

    [Overlay(typeof(MashBoxStudioViewport), Id, "Viewport Shading", true,
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top,
        defaultDockIndex = 3, defaultLayout = Layout.HorizontalToolbar)]
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
            if (!string.IsNullOrEmpty(iconName)) icon = StudioToolbarIcons.Get(iconName);
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
            SetEnabled(editor != null && CanApply(editor));
            SetValueWithoutNotify(editor != null && Read(editor));
        }
        protected virtual bool CanApply(StudioModelEditor editor) => true;
        protected abstract bool Read(StudioModelEditor editor);
        protected abstract void Apply(StudioModelEditor editor, bool enabled);
    }

    internal abstract class StudioComponentToolbarToggle : StudioToolbarToggle
    {
        private readonly StudioModelEditor.ComponentMode target;
        private readonly StudioShortcut shortcut;
        protected StudioComponentToolbarToggle(StudioModelEditor.ComponentMode target, StudioShortcut shortcut)
            : base("studio-toolbar-mode-" + target.ToString().ToLowerInvariant(), null, target + " selection mode")
        {
            this.target = target; this.shortcut = shortcut;
            icon = StudioToolbarIcons.Get("Studio " + target + " selection", () => CreateIcon(target));
            RegisterCallback<AttachToPanelEvent>(_ => { StudioShortcuts.changed -= RefreshTooltip; StudioShortcuts.changed += RefreshTooltip; RefreshTooltip(); });
            RegisterCallback<DetachFromPanelEvent>(_ => StudioShortcuts.changed -= RefreshTooltip);
            RefreshTooltip();
        }
        private void RefreshTooltip()
        {
            string keys = StudioShortcuts.Label(shortcut);
            tooltip = target + " Selection Mode" + (string.IsNullOrEmpty(keys) ? "" : " (" + keys + ")");
        }
        protected override bool CanApply(StudioModelEditor editor) => editor.CanSetToolbarComponentMode(target);
        protected override bool Read(StudioModelEditor editor) => editor.ToolbarComponentMode == target;
        protected override void Apply(StudioModelEditor editor, bool enabled)
        {
            if (enabled) editor.SetToolbarComponentMode(target);
            SetValueWithoutNotify(Read(editor));
        }
        private static Texture2D CreateIcon(StudioModelEditor.ComponentMode mode)
        {
            // A shared 20px-wide isometric silhouette fills the 24px master.
            // Omit hidden edges and use the same neutral strokes as Unity's tools.
            var points = new[] { new Vector2(12,1.5f), new Vector2(22,7), new Vector2(22,17),
                new Vector2(12,22.5f), new Vector2(2,17), new Vector2(2,7), new Vector2(12,12) };
            var edges = new[] { (0,1), (1,2), (2,3), (3,4), (4,5), (5,0), (6,0), (6,2), (6,4) };
            var face = new[] { 6, 2, 1, 0 };
            var pixels = new Color[24*24];
            var stroke = new Color(.82f,.82f,.82f,1);
            var highlight = new Color(.35f,.85f,1,1);
            float Distance(Vector2 p, Vector2 a, Vector2 b) => Vector2.Distance(p, a + (b-a)*Mathf.Clamp01(Vector2.Dot(p-a,b-a)/(b-a).sqrMagnitude));
            bool InFace(Vector2 p)
            {
                for(int i=0;i<face.Length;i++)
                {
                    var a=points[face[i]];var b=points[face[(i+1)%face.Length]];
                    if((b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x)>0) return false;
                }
                return true;
            }
            for (int y=0;y<24;y++) for (int x=0;x<24;x++)
            {
                var color=Color.clear;
                for(int sy=0;sy<4;sy++) for(int sx=0;sx<4;sx++)
                {
                    var p=new Vector2(x+(sx+.5f)/4,y+(sy+.5f)/4); var sample=Color.clear;
                    if(mode==StudioModelEditor.ComponentMode.Face && InFace(p)) sample=new Color(.35f,.75f,.95f,1);
                    foreach(var e in edges) if(Distance(p,points[e.Item1],points[e.Item2])<.75f) sample=mode==StudioModelEditor.ComponentMode.Object?new Color(.35f,.75f,.95f,1):stroke;
                    if(mode==StudioModelEditor.ComponentMode.Vertex && Vector2.Distance(p,points[6])<2.25f) sample=highlight;
                    if(mode==StudioModelEditor.ComponentMode.Edge && Distance(p,points[6],points[2])<1.2f) sample=highlight;
                    if(mode==StudioModelEditor.ComponentMode.Face)
                        for(int i=0;i<face.Length;i++)
                            if(Distance(p,points[face[i]],points[face[(i+1)%face.Length]])<.8f) sample=highlight;
                    color+=sample/16;
                }
                pixels[y*24+x]=color.a>0?new Color(color.r/color.a,color.g/color.a,color.b/color.a,color.a):Color.clear;
            }
            var texture=new Texture2D(24,24,TextureFormat.RGBA32,false);texture.SetPixels(pixels);texture.Apply(false,true);return texture;
        }
    }
    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarObjectMode : StudioComponentToolbarToggle
    {
        internal const string Id="MashBox/Selection/Object";
        public StudioToolbarObjectMode() : base(StudioModelEditor.ComponentMode.Object, StudioShortcut.Object) { }
    }
    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarVertexMode : StudioComponentToolbarToggle
    {
        internal const string Id="MashBox/Selection/Vertex";
        public StudioToolbarVertexMode() : base(StudioModelEditor.ComponentMode.Vertex, StudioShortcut.Vertex) { }
    }
    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarEdgeMode : StudioComponentToolbarToggle
    {
        internal const string Id="MashBox/Selection/Edge";
        public StudioToolbarEdgeMode() : base(StudioModelEditor.ComponentMode.Edge, StudioShortcut.Edge) { }
    }
    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarFaceMode : StudioComponentToolbarToggle
    {
        internal const string Id="MashBox/Selection/Face";
        public StudioToolbarFaceMode() : base(StudioModelEditor.ComponentMode.Face, StudioShortcut.Face) { }
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
        public StudioToolbarSelect() : base(Tool.None, StudioShortcut.Select, "Select", null)
        {
            icon = StudioToolbarIcons.Get("Studio Select", CreateSelectIcon);
        }
        private static Texture2D CreateSelectIcon()
        {
            var texture = new Texture2D(24, 24, TextureFormat.RGBA32, false) {
                name = "Studio select cursor", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear
            };
            var points = new[] { new Vector2(5,27), new Vector2(5,7), new Vector2(11,13),
                new Vector2(16,4), new Vector2(20,6), new Vector2(15,15), new Vector2(23,15) };
            var pixels = new Color[24 * 24];
            for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++)
            {
                int coverage = 0;
                for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++)
                {
                    var point = new Vector2(x + (sx + .5f) / 4, y + (sy + .5f) / 4) * (32f / 24f);
                    bool inside = false;
                    for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                        if ((points[i].y > point.y) != (points[j].y > point.y) &&
                            point.x < (points[j].x - points[i].x) * (point.y - points[i].y) / (points[j].y - points[i].y) + points[i].x) inside = !inside;
                    if (inside) coverage++;
                }
                pixels[y * 24 + x] = new Color(.85f,.85f,.85f,coverage / 16f);
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
            icon = StudioToolbarIcons.Get("ToolHandleLocal");
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
        protected override void Apply(StudioModelEditor editor, bool enabled) => editor.SetModelDrawModeEnabled(view, DrawCameraMode.Wireframe, enabled);
    }
    [EditorToolbarElement(Id, typeof(MashBoxStudioViewport))]
    internal sealed class StudioToolbarShadedWire : StudioToolbarToggle
    {
        internal const string Id = "MashBox/Viewport/ShadedWireframe";
        public StudioToolbarShadedWire() : base("studio-toolbar-shaded-wireframe", "Toolbars/ShadedWireframe", "Shaded wireframe") { }
        protected override bool Read(StudioModelEditor editor) => editor.ModelDrawMode == DrawCameraMode.TexturedWire;
        protected override void Apply(StudioModelEditor editor, bool enabled) => editor.SetModelDrawModeEnabled(view, DrawCameraMode.TexturedWire, enabled);
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
    // One 24px master per icon and skin. Native 32/64px artwork is reduced once;
    // UI Toolkit scales the same cached texture for the 16px preference.
    internal static class StudioToolbarIcons
    {
        private const int Size = 24;
        private static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        private static readonly MethodInfo LoadIcon = typeof(EditorGUIUtility).GetMethod("LoadIcon", BindingFlags.Static | BindingFlags.NonPublic);
        static StudioToolbarIcons()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
        }
        internal static Texture2D Get(string name, Func<Texture2D> create = null)
        {
            string key = (EditorGUIUtility.isProSkin ? "dark:" : "light:") + name;
            if (icons.TryGetValue(key, out var icon) && icon) return icon;
            if (create != null) icon = create();
            else if (name == "GridLayoutGroup Icon") icon = CreateGrid();
            else
            {
                var source = LoadIcon?.Invoke(null, new object[] { name + "@2x" }) as Texture2D;
                if (!source) source = EditorGUIUtility.IconContent(name).image as Texture2D;
                if (!source) return null;
                icon = Downsample(source);
            }
            icon.name = "Studio toolbar 24px " + name;
            icon.hideFlags = HideFlags.HideAndDontSave;
            icon.filterMode = FilterMode.Bilinear;
            icon.wrapMode = TextureWrapMode.Clamp;
            icons[key] = icon;
            return icon;
        }
        private static Texture2D Downsample(Texture2D source)
        {
            Color[] pixels;
            if (source.isReadable) pixels = source.GetPixels();
            else
            {
                var active = RenderTexture.active;
                bool srgb = GL.sRGBWrite;
                var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                try
                {
                    GL.sRGBWrite = target.sRGB;
                    Graphics.Blit(source, target);
                    RenderTexture.active = target;
                    readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                    readable.Apply(); pixels = readable.GetPixels();
                }
                finally
                {
                    RenderTexture.active = active; GL.sRGBWrite = srgb;
                    RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(readable);
                }
            }
            var reduced = new Color[Size * Size];
            float dx = source.width / (float)Size, dy = source.height / (float)Size;
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            {
                float left = x * dx, right = (x + 1) * dx, bottom = y * dy, top = (y + 1) * dy;
                float alpha = 0; var rgb = Vector3.zero;
                for (int sy = Mathf.FloorToInt(bottom); sy < Mathf.CeilToInt(top); sy++)
                    for (int sx = Mathf.FloorToInt(left); sx < Mathf.CeilToInt(right); sx++)
                    {
                        float weight = (Mathf.Min(right, sx + 1) - Mathf.Max(left, sx)) *
                            (Mathf.Min(top, sy + 1) - Mathf.Max(bottom, sy));
                        Color color = pixels[Mathf.Min(sy, source.height - 1) * source.width + Mathf.Min(sx, source.width - 1)];
                        float coverage = color.a * weight;
                        alpha += coverage; rgb += new Vector3(color.r, color.g, color.b) * coverage;
                    }
                reduced[y * Size + x] = alpha > 0 ? new Color(rgb.x / alpha, rgb.y / alpha, rgb.z / alpha, alpha / (dx * dy)) : Color.clear;
            }
            return MakeTexture(reduced);
        }
        private static Texture2D CreateGrid()
        {
            // Unity only supplies a 16px version of this icon. Draw a crisp 24px
            // equivalent rather than enlarging that small bitmap.
            var pixels = new Color[Size * Size];
            var color = new Color(.35f, .72f, .86f, 1);
            foreach (int line in new[] { 2, 8, 15, 21 })
                for (int p = 2; p <= 21; p++) { pixels[line * Size + p] = color; pixels[p * Size + line] = color; }
            return MakeTexture(pixels);
        }
        private static Texture2D MakeTexture(Color[] pixels)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels); texture.Apply(false, true); return texture;
        }
        private static void Clear()
        {
            foreach (var icon in icons.Values) if (icon) UnityEngine.Object.DestroyImmediate(icon);
            icons.Clear();
        }
    }

}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    [InitializeOnLoad]
    internal static class StudioTheme
    {
        internal sealed class Palette
        {
            internal readonly string Id, Name;
            internal readonly Color Canvas, Panel, Header, Button, Hover, Accent, Selection, Text, Muted, Border, Grid, UVWire, UVSelected, ToolRail, ToolSelection;
            internal Palette(string id, string name, string background, string accent)
            {
                Id = id; Name = name;
                ColorUtility.TryParseHtmlString("#" + background, out Canvas);
                ColorUtility.TryParseHtmlString("#" + accent, out Accent);
                Panel = Color.Lerp(Canvas, Color.white, .055f);
                Header = Color.Lerp(Canvas, Color.black, .25f);
                Button = Color.Lerp(Panel, Color.white, .10f);
                Hover = Color.Lerp(Button, Color.white, .10f);
                Selection = Color.Lerp(Panel, Accent, .32f);
                ToolRail = Color.Lerp(Canvas, Color.black, .30f);
                ToolSelection = Color.Lerp(ToolRail, Accent, .55f);
                Text = new Color(.92f, .93f, .94f);
                Muted = Color.Lerp(Panel, Text, .63f);
                Border = Color.Lerp(Panel, Text, .2f);
                Grid = Color.Lerp(Panel, Text, .52f);
                UVWire = Color.Lerp(Accent, Text, .35f);
                UVSelected = new Color(1, .72f, .28f);
            }
        }
        internal static readonly Palette[] Palettes =
        {
            new Palette("graphite", "Graphite", "292929", "8DABC1"),
            new Palette("charcoal", "Charcoal", "1C1C1E", "B9BCC6"),
            new Palette("slate", "Slate", "252D36", "91A9C5"),
            new Palette("midnight", "Midnight", "161E30", "7AA2F7"),
            new Palette("nord", "Nord", "2E3440", "88C0D0"),
            new Palette("ocean", "Ocean", "172C38", "58BED1"),
            new Palette("petrol", "Petrol", "193232", "6DC9B7"),
            new Palette("forest", "Forest", "243129", "8CC59B"),
            new Palette("moss", "Moss", "2B3023", "B3C578"),
            new Palette("olive", "Olive", "343327", "CEC38B"),
            new Palette("sand", "Sand", "383128", "D8BB91"),
            new Palette("copper", "Copper", "352923", "DAA07A"),
            new Palette("ember", "Ember", "342525", "ED9774"),
            new Palette("rose", "Rose", "352831", "DC9AB5"),
            new Palette("plum", "Plum", "2F2437", "C39AD9"),
            new Palette("lavender", "Lavender", "2D2B3C", "B7ACDF"),
            new Palette("indigo", "Indigo", "24283F", "9BAAF2"),
            new Palette("cobalt", "Cobalt", "1C2A40", "70AFE8"),
            new Palette("mint", "Mint", "21332F", "97D7C2"),
            new Palette("contrast", "High Contrast", "101112", "E9CC76")
        };
        internal static Palette Current => Array.Find(Palettes, p => p.Id == StudioSettings.Current.theme) ?? Palettes[0];
        internal static Color Alpha(Color color, float alpha) { color.a = alpha; return color; }
        internal static void DrawPopupBorder(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            var color = Current.Border;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
        }
        private sealed class Resources
        {
            internal GUISkin Skin;
            internal readonly Dictionary<string, GUIStyle> Styles = new Dictionary<string, GUIStyle>();
            internal readonly Dictionary<Color, Texture2D> Textures = new Dictionary<Color, Texture2D>();
            internal readonly Dictionary<Color, Texture2D> DropdownTextures = new Dictionary<Color, Texture2D>();
            internal Texture2D Texture(Color color)
            {
                if (Textures.TryGetValue(color, out var texture)) return texture;
                texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave, name = "MashBox Studio Theme" };
                texture.SetPixel(0, 0, color); texture.Apply(); Textures.Add(color, texture); return texture;
            }
            internal Texture2D Dropdown(Color color)
            {
                if (DropdownTextures.TryGetValue(color, out var texture)) return texture;
                texture = new Texture2D(64, 16) { hideFlags = HideFlags.HideAndDontSave, name = "MashBox Studio Dropdown", filterMode = FilterMode.Point };
                texture.SetPixels(Enumerable.Repeat(color, 64 * 16).ToArray());
                for (int y = 0; y < 4; y++) for (int x = -y; x <= y; x++) texture.SetPixel(55 + x, 5 + y, Current.Text);
                texture.Apply(); DropdownTextures.Add(color, texture); return texture;
            }
        }
        internal static bool HoverSuppressed { get; private set; }
        internal static void WithoutHover(Action draw)
        {
            bool previous = HoverSuppressed; HoverSuppressed = true;
            try { draw?.Invoke(); } finally { HoverSuppressed = previous; }
        }
        private static readonly Dictionary<(Palette palette, bool dark, bool suppressHover), Resources> Cache = new Dictionary<(Palette, bool, bool), Resources>();
        static StudioTheme() { AssemblyReloadEvents.beforeAssemblyReload += Dispose; EditorApplication.quitting += Dispose; }
        private static Resources GetResources()
        {
            var key = (Current, EditorGUIUtility.isProSkin, HoverSuppressed);
            if (!Cache.TryGetValue(key, out var resource)) Cache[key] = resource = new Resources();
            return resource;
        }
        private static GUIStyleState[] States(GUIStyle style) => new[] { style.normal, style.hover, style.active, style.focused, style.onNormal, style.onHover, style.onActive, style.onFocused };
        private static GUIStyle Themed(GUIStyle original, string name, Resources resource)
        {
            var style = new GUIStyle(original); var palette = Current;
            foreach (var state in States(style)) state.textColor = name.Contains("MiniLabel") || name == "miniLabel" ? palette.Muted : palette.Text;
            bool button = name.IndexOf("button", StringComparison.OrdinalIgnoreCase) >= 0 || name == "popup" || name == "toolbarDropDown";
            bool field = name.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0 && name != "textLabel" || name == "toolbarSearchField";
            bool panel = name == "box" || name == "window" || name == "helpBox" || name == "toolbar";
            if (button || field || panel)
            {
                var normal = resource.Texture(button ? palette.Button : field ? palette.Header : palette.Panel);
                var hover = resource.Texture(button ? palette.Hover : field ? palette.Header : palette.Panel);
                var selected = resource.Texture(palette.Selection);
                style.normal.background = normal; style.hover.background = hover; style.active.background = selected; style.focused.background = hover;
                style.onNormal.background = selected; style.onHover.background = selected; style.onActive.background = selected; style.onFocused.background = selected;
                foreach (var state in States(style)) state.scaledBackgrounds = Array.Empty<Texture2D>();
                // Solid color textures have no nine-slice border like Unity's built-in art.
                style.border = new RectOffset();
                if (name == "popup" || name == "toolbarDropDown")
                {
                    style.normal.background = resource.Dropdown(palette.Button); style.hover.background = resource.Dropdown(palette.Hover);
                    style.active.background = style.focused.background = resource.Dropdown(palette.Selection);
                    style.onNormal.background = style.onHover.background = style.onActive.background = style.onFocused.background = resource.Dropdown(palette.Selection);
                    style.border = new RectOffset(2, 14, 2, 2);
                }
            }
            if (HoverSuppressed)
            {
                style.hover = style.normal; style.onHover = style.onNormal;
            }
            return style;
        }
        private static GUISkin Skin(Resources resource)
        {
            return resource.Skin ? resource.Skin : CreateSkin(resource);
        }
        // Keep the captured initialization lambda out of the per-panel draw path.
        private static GUISkin CreateSkin(Resources resource)
        {
            resource.Skin = UnityEngine.Object.Instantiate(GUI.skin); resource.Skin.hideFlags = HideFlags.HideAndDontSave;
            foreach (var property in typeof(GUISkin).GetProperties(BindingFlags.Instance | BindingFlags.Public))
                if (property.PropertyType == typeof(GUIStyle) && property.CanRead && property.CanWrite)
                    property.SetValue(resource.Skin, Themed((GUIStyle)property.GetValue(resource.Skin), property.Name, resource));
            resource.Skin.customStyles = resource.Skin.customStyles.Select(s => Themed(s, s.name, resource)).ToArray();
            return resource.Skin;
        }
        // EditorGUILayout uses shared EditorStyles for some implicit labels. Only their
        // text colors are scoped; every value and the GUI skin are restored before returning.
        private static GUIStyleState[] implicitStates;
        private static readonly List<Color[]> savedText = new List<Color[]>();
        private static int depth;
        internal readonly struct ThemeScope : IDisposable
        {
            private readonly GUISkin skin;
            private readonly Color color, content, background;
            private readonly int level;
            internal ThemeScope(bool apply)
            {
                skin = GUI.skin; color = GUI.color; content = GUI.contentColor; background = GUI.backgroundColor; level = depth++;
                if (implicitStates == null)
                    implicitStates = new[] { EditorStyles.label, EditorStyles.foldout, EditorStyles.toggle, EditorStyles.numberField, EditorStyles.textField, EditorStyles.objectField, EditorStyles.popup, EditorStyles.helpBox, EditorStyles.boldLabel }.SelectMany(States).ToArray();
                if (savedText.Count <= level) savedText.Add(new Color[implicitStates.Length]);
                var text = Current.Text;
                for (int i = 0; i < implicitStates.Length; i++) { savedText[level][i] = implicitStates[i].textColor; implicitStates[i].textColor = text; }
                GUI.skin = Skin(GetResources()); GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white;
            }
            public void Dispose()
            {
                for (int i = 0; i < implicitStates.Length; i++) implicitStates[i].textColor = savedText[level][i];
                GUI.skin = skin; GUI.color = color; GUI.contentColor = content; GUI.backgroundColor = background; depth--;
            }
        }
        internal static ThemeScope Scope() => new ThemeScope(true);
        internal static Action Wrap(Action draw) => draw == null ? null : (Action)(() => { using (Scope()) draw(); });
        private static void Dispose()
        {
            foreach (var resource in Cache.Values)
            {
                if (resource.Skin) UnityEngine.Object.DestroyImmediate(resource.Skin);
                foreach (var texture in resource.Textures.Values) if (texture) UnityEngine.Object.DestroyImmediate(texture);
                foreach (var texture in resource.DropdownTextures.Values) if (texture) UnityEngine.Object.DestroyImmediate(texture);
            }
            Cache.Clear(); implicitStates = null; savedText.Clear(); depth = 0;
        }
        internal static class Styles
        {
            internal static GUIStyle hierarchyRow
            {
                get
                {
                    var resources = GetResources();
                    if (resources.Styles.TryGetValue("hierarchyRow", out var style)) return style;
                    var button = miniButton;
                    style = new GUIStyle(GUIStyle.none)
                    {
                        alignment = TextAnchor.MiddleLeft, font = button.font,
                        fontSize = button.fontSize, fontStyle = button.fontStyle,
                        padding = new RectOffset(button.padding.left, button.padding.right, button.padding.top, button.padding.bottom),
                        margin = new RectOffset(button.margin.left, button.margin.right, button.margin.top, button.margin.bottom),
                        fixedHeight = button.fixedHeight
                    };
                    style.normal.textColor = Current.Text;
                    resources.Styles.Add("hierarchyRow", style);
                    return style;
                }
            }
            private static GUIStyle Get(string name)
            {
                var resources = GetResources();
                if (!resources.Styles.TryGetValue(name, out var style))
                {
                    var original = (GUIStyle)typeof(EditorStyles).GetProperty(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);
                    resources.Styles[name] = style = Themed(original, name, resources);
                }
                return style;
            }
            private static GUIStyle Overlay(bool dropdown)
            {
                var resources = GetResources();
                string key = dropdown ? "overlayDropdown" : "overlayButton";
                if (resources.Styles.TryGetValue(key, out var cached)) return cached;
                var palette = Current;
                var style = new GUIStyle(dropdown ? EditorStyles.toolbarDropDown : EditorStyles.miniButton)
                {
                    margin = new RectOffset(), overflow = new RectOffset(),
                    border = dropdown ? new RectOffset(0, 14, 0, 0) : new RectOffset(),
                    padding = dropdown ? new RectOffset(6, 17, 0, 0) : new RectOffset(5, 5, 0, 0),
                    fixedHeight = 24, contentOffset = Vector2.zero,
                    alignment = dropdown ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter
                };
                foreach (var state in States(style))
                {
                    state.textColor = palette.Text;
                    state.scaledBackgrounds = Array.Empty<Texture2D>();
                    state.background = dropdown ? resources.Dropdown(palette.Header) : resources.Texture(palette.Header);
                }
                style.hover.background = style.focused.background = dropdown ? resources.Dropdown(palette.Button) : resources.Texture(palette.Button);
                style.active.background = style.onNormal.background = style.onHover.background = style.onActive.background = style.onFocused.background =
                    dropdown ? resources.Dropdown(palette.Selection) : resources.Texture(palette.Selection);
                if (HoverSuppressed) { style.hover = style.normal; style.onHover = style.onNormal; }
                resources.Styles.Add(key, style); return style;
            }
            internal static GUIStyle menuDropdown
            {
                get
                {
                    var resources = GetResources();
                    if (resources.Styles.TryGetValue("menuDropdown", out var style)) return style;
                    style = new GUIStyle(Overlay(true)) { fixedHeight = 21 };
                    style.normal.background = resources.Dropdown(Current.Button);
                    style.hover.background = style.focused.background = resources.Dropdown(Current.Hover);
                    resources.Styles.Add("menuDropdown", style);
                    return style;
                }
            }
            internal static GUIStyle menuBarButton
            {
                get
                {
                    var resources = GetResources();
                    if (resources.Styles.TryGetValue("menuBarButton", out var style)) return style;
                    style = new GUIStyle(menuDropdown)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        padding = new RectOffset(6, 6, 0, 0),
                        border = new RectOffset()
                    };
                    foreach (var state in States(style)) state.background = resources.Texture(Current.Selection);
                    style.normal.background = resources.Texture(Current.Button);
                    style.hover.background = style.focused.background = resources.Texture(Current.Hover);
                    resources.Styles.Add("menuBarButton", style);
                    return style;
                }
            }
            internal static GUIStyle overlayButton => Overlay(false);
            internal static GUIStyle modelPanel
            {
                get
                {
                    var resources = GetResources();
                    if (resources.Styles.TryGetValue("modelPanel", out var style)) return style;
                    style = new GUIStyle(GUIStyle.none) { padding = new RectOffset(8, 8, 8, 8), margin = new RectOffset() };
                    resources.Styles.Add("modelPanel", style); return style;
                }
            }
            internal static GUIStyle hierarchyInset
            {
                get
                {
                    var resources = GetResources();
                    if (resources.Styles.TryGetValue("hierarchyInset", out var style)) return style;
                    style = new GUIStyle(GUIStyle.none) { padding = new RectOffset(4, 4, 4, 4), margin = new RectOffset(0, 0, 4, 0) };
                    style.normal.background = resources.Texture(Color.Lerp(Current.Panel, Color.black, .06f));
                    resources.Styles.Add("hierarchyInset", style); return style;
                }
            }
            internal static GUIStyle hierarchyVisibilityButton
            {
                get
                {
                    var resources = GetResources();
                    if (resources.Styles.TryGetValue("hierarchyVisibilityButton", out var style)) return style;
                    style = new GUIStyle(GUIStyle.none) { alignment = TextAnchor.MiddleCenter, imagePosition = ImagePosition.ImageOnly };
                    resources.Styles.Add("hierarchyVisibilityButton", style); return style;
                }
            }
            internal static GUIStyle panelHeader
            {
                get
                {
                    var resources = GetResources();
                    if (resources.Styles.TryGetValue("panelHeader", out var style)) return style;
                    style = new GUIStyle(boldLabel)
                    {
                        alignment = TextAnchor.MiddleLeft, fixedHeight = 20,
                        margin = new RectOffset(0, 0, 0, 4), padding = new RectOffset()
                    };
                    resources.Styles.Add("panelHeader", style); return style;
                }
            }
            internal static GUIStyle overlayDropdown => Overlay(true);
            internal static GUIStyle label => Get("label");
            internal static GUIStyle boldLabel => Get("boldLabel");
            internal static GUIStyle miniLabel => Get("miniLabel");
            internal static GUIStyle centeredGreyMiniLabel => Get("centeredGreyMiniLabel");
            internal static GUIStyle wordWrappedMiniLabel => Get("wordWrappedMiniLabel");
            internal static GUIStyle helpBox => Get("helpBox");
            internal static GUIStyle inspectorDefaultMargins => Get("inspectorDefaultMargins");
            internal static GUIStyle miniButton => Get("miniButton");
            internal static GUIStyle miniButtonLeft => Get("miniButtonLeft");
            internal static GUIStyle miniButtonMid => Get("miniButtonMid");
            internal static GUIStyle miniButtonRight => Get("miniButtonRight");
            internal static GUIStyle popup => Get("popup");
            internal static GUIStyle textArea => Get("textArea");
            internal static GUIStyle toolbar => Get("toolbar");
            internal static GUIStyle toolbarButton => Get("toolbarButton");
            internal static GUIStyle toolbarDropDown => Get("toolbarDropDown");
            internal static GUIStyle toolbarSearchField => Get("toolbarSearchField");
        }
    }
}

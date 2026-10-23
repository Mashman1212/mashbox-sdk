using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    [InitializeOnLoad]
    internal static partial class StudioTheme
    {
        internal enum PaletteTone { Dark, Light, SoftDark, Contrast }
        internal sealed partial class Palette
        {
            internal readonly string Id, Name;
            internal readonly Color Canvas, Panel, Header, Button, Hover, Accent, Selection, Text, Muted, Border, Grid, UVWire, UVSelected, ToolRail, ToolSelection;
            internal Palette(string id, string name, string background, string accent, PaletteTone tone = PaletteTone.Dark)
            {
                Id = id; Name = name;
                ColorUtility.TryParseHtmlString("#" + background, out Canvas);
                ColorUtility.TryParseHtmlString("#" + accent, out Accent);
                bool light = tone == PaletteTone.Light, contrast = tone == PaletteTone.Contrast;
                bool soft = tone == PaletteTone.SoftDark;
                Panel = Color.Lerp(Canvas, Color.white, light ? .35f : contrast ? .10f : .055f);
                Header = Color.Lerp(Canvas, Color.black, light ? .08f : contrast ? .08f : soft ? .12f : .25f);
                Button = light ? Color.Lerp(Canvas, Color.black, .04f) : Color.Lerp(Panel, Color.white, contrast ? .12f : .10f);
                Hover = light ? Color.Lerp(Button, Accent, .12f) : Color.Lerp(Button, Color.white, contrast ? .07f : soft ? .07f : .10f);
                Selection = Color.Lerp(Panel, Accent, light ? .20f : contrast || soft ? .20f : .32f);
                ToolRail = Color.Lerp(Canvas, Color.black, light ? .12f : contrast ? .08f : soft ? .15f : .30f);
                ToolSelection = Color.Lerp(ToolRail, Accent, light ? .18f : contrast || soft ? .25f : .55f);
                Text = light ? new Color(.14f, .16f, .20f) : contrast ? Color.white : soft ? new Color(.97f, .97f, .98f) : new Color(.92f, .93f, .94f);
                Muted = Color.Lerp(Panel, Text, light ? .76f : contrast ? .82f : soft ? .76f : .63f);
                Border = Color.Lerp(Panel, Text, light ? .38f : contrast ? .48f : soft ? .30f : .2f);
                Grid = Color.Lerp(Panel, Text, light ? .36f : contrast ? .45f : .52f);
                UVWire = Color.Lerp(Accent, Text, light ? .15f : .35f);
                UVSelected = light ? new Color(.57f, .33f, 0) : new Color(1, .72f, .28f);
            }
        }
        internal static readonly Palette[] Palettes =
        {
            new Palette("graphite", "Graphite", "292929", "8DABC1"),
            new Palette("charcoal", "Charcoal", "1C1C1E", "B9BCC6"),
            new Palette("contrast", "High Contrast", "101112", "E9CC76"),
            new Palette("ocean", "Ocean", "172C38", "58BED1"),
            new Palette("porcelain", "Porcelain", "EEECE6", "44627D", PaletteTone.Light),
            new Palette("silver", "Silver", "9A9C9E", "C7C8CA", "8B8E91", "E2E3E5", "F2F3F4", "254D6B", "151A20", "424A52", "60676F", "B2C6D6", "8B8E91", "B2C6D6"),
            new Palette("concrete", "Concrete", "82898E", "A2A8AC", "858B8F", "C1C6C9", "D5DADD", "235F67", "10161B", "384750", "5D6A72", "AEC7CB", "858B8F", "AEC7CB"),
            new Palette("gunmetal", "Gunmetal", "1F262A", "293438", "131B1E", "3D4B50", "4C5D63", "9CBDB8", "F4F5F3", "BBC8C9", "687B80", "355A5A", "131B1E", "355A5A"),
            new Palette("blue-steel", "Blue Steel", "2E465A", "26343F", "162530", "405563", "4E6776", "A7CEDC", "F4F7F8", "BDCCD2", "718B99", "3D6275", "192A35", "3D6275"),
            new Palette("violet-smoke", "Violet Smoke", "343047", "474352", "242039", "565260", "625D6E", "C6B9DD", "F6F2FB", "C6BED2", "8B819C", "514667", "262236", "514667"),
            new Palette("ink-crimson", "Ink and Crimson", "24272B", "34393E", "171A1E", "454D54", "505B63", "E84E55", "F7F7F6", "C5CACD", "747D85", "6A353D", "171A1E", "6A353D"),
            new Palette("chalk-vermilion", "Chalk and Vermilion", "E1E3E4", "F3F4F4", "C8CDD0", "D5DBDE", "EAECED", "B52930", "202429", "535B63", "8A949C", "EDC5C7", "BEC5C8", "EDC5C7"),
            new Palette("pearl-teal", "Pearl and Teal", "D5E2E4", "F0F6F5", "B4CCD0", "C5D9DB", "DCEAE9", "165F66", "142D32", "3F6268", "7E9FA5", "A8CCCE", "ADC7CD", "A8CCCE"),
            new Palette("warm-ivory", "Warm Ivory", "E7E5D3", "F8F6E7", "CBCBB7", "DEDECA", "EFEDD9", "4E5D49", "292F25", "5D6656", "969D88", "C4D0B7", "C5C6B0", "C4D0B7"),
            new Palette("linen-ink", "Linen and Ink", "D4C5B2", "EFE5D7", "BEAD96", "DDD0BC", "E9DFD0", "303B45", "27231E", "5E5145", "8D7962", "BCC6C8", "B5A28B", "BCC6C8"),
            new Palette("graphite-amber", "Graphite Amber", "383834", "4C4C46", "262722", "5B5C51", "666858", "E5C46D", "FAF8ED", "D0CDBA", "969681", "615939", "262722", "615939"),
            new Palette("neutral-contrast", "Neutral Contrast", "3C4146", "242A2F", "1B2024", "48545E", "596874", "D8E4ED", "FFFFFF", "DCE4E9", "93A4B1", "435F72", "1B2024", "435F72"),
            new Palette("slate-contrast", "Slate Contrast", "3F5960", "283B42", "1D2E35", "3A5963", "486C77", "BCE5DB", "FFFFFF", "D1E6E7", "82AEB3", "376568", "1D2E35", "376568"),
            new Palette("silver-contrast", "Silver Contrast", "B4B7BA", "E2E5E7", "8E9499", "CAD1D6", "EEF1F3", "1B4568", "0E1720", "3C4E5C", "556C7D", "A8C4D7", "8E9499", "A8C4D7"),
            new Palette("violet-contrast", "Violet Contrast", "3E3B50", "2D293D", "211D30", "49425F", "5E5378", "C5DFF0", "FFFFFF", "D8D2E7", "A49BBD", "3D596E", "211D30", "3D596E")
        };
        internal static Palette Current => StudioSettings.Current.theme == "custom" ? Custom : Array.Find(Palettes, p => p.Id == StudioSettings.Current.theme) ?? Palettes[0];
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
            bool field = name.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0 && name != "textLabel" || name == "toolbarSearchField" || name == "numberField" || name == "objectField";
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
        // Unity's implicit fields use shared EditorStyles rather than GUI.skin.
        // Scope their foreground AND background together, restoring every state
        // on exit so Studio styling never leaks into other Unity inspectors.
        private static readonly string[] implicitFieldNames = { "numberField", "textField", "objectField", "popup", "helpBox" };
        private static GUIStyle[] implicitFields;
        private sealed class FieldSnapshot
        {
            internal readonly Color[] Text = new Color[40];
            internal readonly Texture2D[] Background = new Texture2D[40];
            internal readonly Texture2D[][] Scaled = new Texture2D[40][];
            internal readonly RectOffset[] Borders = new RectOffset[5];
        }
        private static readonly List<FieldSnapshot> savedFields = new List<FieldSnapshot>();
        private static void ApplyImplicitFields(int level, Resources resources)
        {
            if (implicitFields == null)
                implicitFields = new[] { EditorStyles.numberField, EditorStyles.textField, EditorStyles.objectField, EditorStyles.popup, EditorStyles.helpBox };
            if (savedFields.Count <= level) savedFields.Add(new FieldSnapshot());
            var saved = savedFields[level];
            for (int field = 0; field < implicitFields.Length; field++)
            {
                string key = "implicit:" + implicitFieldNames[field];
                if (!resources.Styles.TryGetValue(key, out var themed))
                    resources.Styles[key] = themed = Themed(implicitFields[field], implicitFieldNames[field], resources);
                var target = States(implicitFields[field]); var source = States(themed);
                saved.Borders[field] = implicitFields[field].border;
                implicitFields[field].border = themed.border;
                for (int state = 0; state < target.Length; state++)
                {
                    int index = field * 8 + state;
                    saved.Text[index] = target[state].textColor;
                    saved.Background[index] = target[state].background;
                    saved.Scaled[index] = target[state].scaledBackgrounds;
                    target[state].textColor = source[state].textColor;
                    target[state].background = source[state].background;
                    target[state].scaledBackgrounds = source[state].scaledBackgrounds;
                }
            }
        }
        private static void RestoreImplicitFields(int level)
        {
            var saved = savedFields[level];
            for (int field = 0; field < implicitFields.Length; field++)
            {
                implicitFields[field].border = saved.Borders[field];
                var target = States(implicitFields[field]);
                for (int state = 0; state < target.Length; state++)
                {
                    int index = field * 8 + state;
                    target[state].textColor = saved.Text[index];
                    target[state].background = saved.Background[index];
                    target[state].scaledBackgrounds = saved.Scaled[index];
                }
            }
        }
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
                    implicitStates = new[] { EditorStyles.label, EditorStyles.foldout, EditorStyles.toggle, EditorStyles.boldLabel }.SelectMany(States).ToArray();
                if (savedText.Count <= level) savedText.Add(new Color[implicitStates.Length]);
                var text = Current.Text;
                for (int i = 0; i < implicitStates.Length; i++) { savedText[level][i] = implicitStates[i].textColor; implicitStates[i].textColor = text; }
                var resources = GetResources();
                GUI.skin = Skin(resources); ApplyImplicitFields(level, resources); GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white;
            }
            public void Dispose()
            {
                for (int i = 0; i < implicitStates.Length; i++) implicitStates[i].textColor = savedText[level][i];
                RestoreImplicitFields(level);
                GUI.skin = skin; GUI.color = color; GUI.contentColor = content; GUI.backgroundColor = background; depth--;
            }
        }
        internal static ThemeScope Scope() => new ThemeScope(true);
        internal static Action Wrap(Action draw) => draw == null ? null : (Action)(() => { using (Scope()) draw(); });
        private static void Dispose()
        {
            EditorApplication.delayCall -= ReleaseRetiredCustom; retiredCustom.Clear();
            foreach (var resource in Cache.Values) DestroyResources(resource);
            Cache.Clear(); implicitStates = null; implicitFields = null; savedFields.Clear(); savedText.Clear(); depth = 0;
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
                    style.normal.background = resources.Texture(Color.Lerp(Current.Panel, Color.black, .16f));
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

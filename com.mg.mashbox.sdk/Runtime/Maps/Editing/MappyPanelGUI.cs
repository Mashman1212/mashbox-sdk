using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.Editing
{
    /// <summary>Runtime IMGUI presentation matching the Mappy Scene View overlay.</summary>
    public sealed class MappyPanelGUI : IDisposable
    {
        public GUISkin Skin { get; }
        public GUIStyle Heading { get; }
        public GUIStyle Wrapped { get; }
        public GUIStyle Small { get; }
        public GUIStyle Title { get; }
        readonly GUIStyle row, foldout, iconButton, shortcut, toolLabel, checkbox;
        readonly Texture2D category, tool, action, editing;
        readonly List<Texture2D> textures = new List<Texture2D>();
        readonly Dictionary<(string icon, string tooltip), GUIContent> content = new Dictionary<(string, string), GUIContent>();

        public MappyPanelGUI(GUISkin source)
        {
            Skin = UnityEngine.Object.Instantiate(source);
            Skin.name = "MashBox Mappy runtime skin";
            Skin.hideFlags = HideFlags.HideAndDontSave;
            var background = Texture(new Color(.16f, .16f, .16f));
            var surface = Texture(new Color(.20f, .20f, .20f));
            var button = Texture(new Color(.265f, .265f, .265f));
            var hover = Texture(new Color(.32f, .32f, .32f));
            var field = Texture(new Color(.12f, .12f, .12f));
            category = Texture(MappyPanelVisuals.CategorySelection);
            tool = Texture(MappyPanelVisuals.ToolSelection);
            action = Texture(MappyPanelVisuals.ActionSelection);
            editing = Texture(MappyPanelVisuals.EditingSelection);
            var text = new Color(.76f, .76f, .76f);

            Surface(Skin.box, background, background, background, text);
            Skin.box.padding = new RectOffset(4, 4, 4, 4);
            Skin.box.margin = new RectOffset();
            Skin.label.fontSize = 12;
            Skin.label.fontStyle = FontStyle.Normal;
            Skin.label.padding = new RectOffset(3, 3, 2, 2);
            Skin.label.margin = new RectOffset();
            Skin.label.normal.textColor = text;
            Surface(Skin.button, button, hover, category, text);
            Skin.button.fontSize = 12;
            Skin.button.fixedHeight = 22;
            Skin.button.padding = new RectOffset(5, 5, 2, 2);
            Skin.button.margin = new RectOffset(1, 1, 1, 1);
            Skin.button.clipping = TextClipping.Clip;
            Skin.button.onNormal.textColor = Color.white;
            Surface(Skin.textField, field, field, field, new Color(.85f, .85f, .85f));
            Skin.textField.fontSize = 12;
            Skin.textField.fixedHeight = 20;
            Skin.textField.padding = new RectOffset(4, 4, 2, 2);
            Skin.textField.margin = new RectOffset(2, 2, 1, 1);
            Skin.settings.selectionColor = MappyPanelVisuals.CategorySelection;
            Skin.toggle = new GUIStyle(Skin.label);
            Skin.toggle.padding = new RectOffset(20, 3, 2, 2);
            Skin.toggle.fixedHeight = 22;
            Skin.toggle.normal.background = ToggleTexture(false);
            Skin.toggle.onNormal.background = ToggleTexture(true);
            Skin.toggle.hover.background = Skin.toggle.normal.background;
            Skin.toggle.onHover.background = Skin.toggle.onNormal.background;
            Skin.toggle.border = new RectOffset(18, 0, 0, 0);
            Skin.toggle.overflow = new RectOffset();
            Surface(Skin.verticalScrollbar, background, background, background, text);
            Skin.verticalScrollbar.fixedWidth = 10;
            Surface(Skin.verticalScrollbarThumb, hover, hover, hover, text);
            Skin.verticalScrollbarThumb.fixedWidth = 10;
            Skin.verticalScrollbarThumb.fixedHeight = 0;
            Skin.verticalScrollbarUpButton = new GUIStyle { fixedHeight = 0 };
            Skin.verticalScrollbarDownButton = new GUIStyle { fixedHeight = 0 };

            Heading = new GUIStyle(Skin.label) { fontStyle = FontStyle.Bold };
            Wrapped = new GUIStyle(Skin.label) { wordWrap = true };
            Small = new GUIStyle(Wrapped) { fontSize = 11 };
            Title = new GUIStyle(Skin.label) { fontSize = 14, fixedHeight = 25 };
            row = new GUIStyle(Skin.box) { padding = new RectOffset(4, 0, 0, 0), margin = new RectOffset(0, 0, 0, 1) };
            Surface(row, surface, surface, surface, text);
            foldout = new GUIStyle(Skin.label) { fixedHeight = 22, alignment = TextAnchor.MiddleLeft };
            foldout.hover.textColor = MappyPanelVisuals.ShortcutColor;
            iconButton = new GUIStyle(Skin.button)
            {
                fixedHeight = MappyPanelVisuals.IconSize, fixedWidth = MappyPanelVisuals.IconSize,
                padding = new RectOffset(5, 5, 5, 5), margin = new RectOffset()
            };
            shortcut = new GUIStyle(Wrapped);
            shortcut.normal.textColor = MappyPanelVisuals.ShortcutColor;
            toolLabel = new GUIStyle(Skin.label) { alignment = TextAnchor.MiddleRight };
            toolLabel.normal.textColor = MappyPanelVisuals.CurrentToolColor;
            checkbox = new GUIStyle(Skin.toggle) { alignment = TextAnchor.MiddleRight, fixedWidth = 42 };
        }

        public bool Foldout(string title, bool open)
        {
            if (GUILayout.Button((open ? "▾ " : "▸ ") + title, foldout)) open = !open;
            return open;
        }

        public void BeginRow(string title)
        {
            GUILayout.BeginHorizontal(row, GUILayout.MinHeight(MappyPanelVisuals.RowHeight));
            GUILayout.Label(title, Heading, GUILayout.Width(MappyPanelVisuals.HeadingWidth));
            GUILayout.FlexibleSpace();
        }
        public void EndRow() => GUILayout.EndHorizontal();

        public bool IconButton(string icon, string tooltip, bool selected = false, string color = "tool")
        {
            var key = (icon, tooltip);
            if (!content.TryGetValue(key, out var label))
                content[key] = label = new GUIContent(MappyPanelVisuals.Icon(icon), tooltip);
            var selectedTexture = color == "action" ? action : color == "editing" ? editing : color == "category" ? category : tool;
            var previous = iconButton.normal.background;
            iconButton.normal.background = selected ? selectedTexture : previous;
            iconButton.active.background = selectedTexture;
            bool clicked = GUILayout.Button(label, iconButton);
            iconButton.normal.background = previous;
            return clicked;
        }

        public bool ToggleRow(string title, bool value)
        {
            BeginRow(title);
            value = GUILayout.Toggle(value, value ? "On" : "Off", checkbox);
            EndRow();
            return value;
        }

        public void Controls(string current)
        {
            BeginRow("Controls");
            GUILayout.Label(current, toolLabel);
            EndRow();
        }

        public void Shortcut(string icon, string label, string keys)
        {
            GUILayout.BeginHorizontal(row);
            GUILayout.Label(MappyPanelVisuals.Icon(icon), GUILayout.Width(19), GUILayout.Height(22));
            GUILayout.Label(label, Wrapped, GUILayout.Width(100));
            GUILayout.Label(keys, shortcut);
            GUILayout.EndHorizontal();
        }

        public void DrawTooltip(Rect panel)
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(GUI.tooltip)) return;
            var tooltip = new GUIContent(GUI.tooltip);
            float height = Wrapped.CalcHeight(tooltip, panel.width - 16) + 12;
            var rect = new Rect(panel.x + 4, Mathf.Clamp(Event.current.mousePosition.y + 20, panel.y, panel.yMax - height), panel.width - 8, height);
            GUI.Box(rect, GUIContent.none, Skin.box);
            rect.x += 4; rect.y += 4; rect.width -= 8; rect.height -= 8;
            GUI.Label(rect, tooltip, Wrapped);
        }

        Texture2D Texture(Color color)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color); texture.Apply(false, true); textures.Add(texture);
            return texture;
        }

        Texture2D ToggleTexture(bool selected)
        {
            var texture = new Texture2D(18, 22, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            var pixels = new Color32[18 * 22];
            for (int y = 4; y < 18; y++)
                for (int x = 1; x < 15; x++) pixels[y * 18 + x] = new Color32(38, 38, 38, 255);
            if (selected)
                for (int x = 4; x <= 12; x++)
                {
                    int y = x < 7 ? 12 - x : x - 2;
                    pixels[y * 18 + x] = pixels[(y + 1) * 18 + x] = new Color32(194, 194, 194, 255);
                }
            texture.SetPixels32(pixels); texture.Apply(false, true); textures.Add(texture);
            return texture;
        }

        static void Surface(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D selected, Color text)
        {
            style.border = new RectOffset();
            foreach (var state in new[] { style.normal, style.hover, style.active, style.focused, style.onNormal, style.onHover, style.onActive, style.onFocused })
            {
                state.background = normal; state.textColor = text;
#if UNITY_EDITOR
                state.scaledBackgrounds = null;
#endif
            }
            style.hover.background = hover;
            style.active.background = style.onNormal.background = style.onHover.background = style.onFocused.background = selected;
        }

        public void Dispose()
        {
            Destroy(Skin);
            foreach (var texture in textures) Destroy(texture);
            textures.Clear(); content.Clear();
        }
        static void Destroy(UnityEngine.Object obj)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}

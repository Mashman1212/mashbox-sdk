using MashBoxSDK.Maps;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.Splines;
using UnityEditor.Toolbars;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.UIElements;

namespace MashBoxSDK.MapTools
{
    internal static class MBEditorToolVisuals
    {
        static readonly Color ModeToolSelectionColor = new Color(0.68f, 0.38f, 0.08f, 0.95f);
        static readonly Color ToolActionSelectionColor = new Color(0.42f, 0.27f, 0.68f, 0.95f);
        static readonly System.Collections.Generic.Dictionary<string, Texture2D> CustomIcons =
            new System.Collections.Generic.Dictionary<string, Texture2D>();

        internal static void ConfigureIconOnly(
            EditorToolbarToggle element,
            string iconName,
            string fallbackIconName,
            string label,
            string description)
        {
            Texture2D toolbarIcon = GetToolbarIcon(iconName);
            if (toolbarIcon == null)
                toolbarIcon = GetToolbarIcon(fallbackIconName);

            element.text = string.Empty;
            element.icon = toolbarIcon;
            element.tooltip = label + ": " + description;
            element.style.width = 28f;
            element.style.minWidth = 28f;
            element.style.maxWidth = 28f;
            element.style.height = 28f;
            element.style.minHeight = 28f;
            element.style.maxHeight = 28f;
            element.style.flexShrink = 0f;
        }

        internal static Texture2D GetToolbarIcon(string iconName)
        {
            if (!iconName.StartsWith("MashBox."))
                return EditorGUIUtility.FindTexture(iconName);

            if (CustomIcons.TryGetValue(iconName, out Texture2D existing))
                return existing;

            Texture2D created = CreateMashBoxIcon(iconName);
            CustomIcons[iconName] = created;
            return created;
        }

        static Texture2D CreateMashBoxIcon(string iconName)
        {
            const int size = 32;
            var icon = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = iconName + " Icon",
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            var bright = new Color32(240, 244, 250, 255);
            var mid = new Color32(195, 203, 215, 255);

            switch (iconName)
            {
                case "MashBox.ActiveEditing":
                    DrawIconLine(pixels, size, new Vector2(6f, 6f), new Vector2(20f, 20f), 4.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(5f, 9f), new Vector2(9f, 5f), 2.2f, mid);
                    DrawIconLine(pixels, size, new Vector2(24f, 20f), new Vector2(24f, 30f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(19f, 25f), new Vector2(29f, 25f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(20.5f, 21.5f), new Vector2(27.5f, 28.5f), 1.6f, mid);
                    DrawIconLine(pixels, size, new Vector2(20.5f, 28.5f), new Vector2(27.5f, 21.5f), 1.6f, mid);
                    DrawIconDot(pixels, size, new Vector2(24f, 25f), 2f, bright);
                    break;
                case "MashBox.GameplayGizmos":
                    DrawIconLine(pixels, size, new Vector2(3f, 16f), new Vector2(10f, 22f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(10f, 22f), new Vector2(22f, 22f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(22f, 22f), new Vector2(29f, 16f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(3f, 16f), new Vector2(10f, 10f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(10f, 10f), new Vector2(22f, 10f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(22f, 10f), new Vector2(29f, 16f), 2f, mid);
                    DrawIconDot(pixels, size, new Vector2(16f, 16f), 5f, bright);
                    DrawIconDot(pixels, size, new Vector2(16f, 16f), 2f, new Color32(120, 160, 210, 255));
                    break;
                case "MashBox.Brush":
                    DrawIconLine(pixels, size, new Vector2(25f, 26f), new Vector2(14f, 15f), 4.5f, mid);
                    DrawIconLine(pixels, size, new Vector2(16f, 17f), new Vector2(11f, 12f), 6f, bright);
                    DrawIconLine(pixels, size, new Vector2(10f, 12f), new Vector2(5f, 6f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(12f, 10f), new Vector2(7f, 5f), 2f, bright);
                    break;
                case "MashBox.Spline":
                    DrawIconLine(pixels, size, new Vector2(4f, 22f), new Vector2(10f, 12f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(10f, 12f), new Vector2(18f, 20f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(18f, 20f), new Vector2(28f, 8f), 2.2f, bright);
                    DrawIconDot(pixels, size, new Vector2(4f, 22f), 2.3f, mid);
                    DrawIconDot(pixels, size, new Vector2(18f, 20f), 2.3f, mid);
                    DrawIconDot(pixels, size, new Vector2(28f, 8f), 2.3f, mid);
                    break;
                case "MashBox.SplineEdit":
                    DrawIconLine(pixels, size, new Vector2(4f, 22f), new Vector2(11f, 12f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(11f, 12f), new Vector2(20f, 19f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(20f, 19f), new Vector2(28f, 9f), 2.2f, bright);
                    DrawIconDot(pixels, size, new Vector2(11f, 12f), 3.2f, mid);
                    DrawIconLine(pixels, size, new Vector2(11f, 6f), new Vector2(11f, 18f), 1.8f, bright);
                    DrawIconLine(pixels, size, new Vector2(5f, 12f), new Vector2(17f, 12f), 1.8f, bright);
                    break;
                case "MashBox.NewSpline":
                    DrawIconLine(pixels, size, new Vector2(4f, 22f), new Vector2(10f, 13f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(10f, 13f), new Vector2(18f, 19f), 2.2f, bright);
                    DrawIconDot(pixels, size, new Vector2(4f, 22f), 2.2f, mid);
                    DrawIconDot(pixels, size, new Vector2(10f, 13f), 2.2f, mid);
                    DrawIconDot(pixels, size, new Vector2(18f, 19f), 2.2f, mid);
                    DrawIconLine(pixels, size, new Vector2(25f, 8f), new Vector2(25f, 20f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(19f, 14f), new Vector2(31f, 14f), 2.4f, bright);
                    break;
                case "MashBox.Terrain":
                    DrawIconLine(pixels, size, new Vector2(3f, 7f), new Vector2(11f, 21f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(11f, 21f), new Vector2(16f, 14f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(16f, 14f), new Vector2(22f, 25f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(22f, 25f), new Vector2(29f, 7f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(3f, 7f), new Vector2(29f, 7f), 2f, mid);
                    break;
                case "MashBox.Decor":
                    DrawIconLine(pixels, size, new Vector2(16f, 5f), new Vector2(16f, 24f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(16f, 17f), new Vector2(8f, 22f), 3f, mid);
                    DrawIconLine(pixels, size, new Vector2(16f, 14f), new Vector2(24f, 19f), 3f, mid);
                    DrawIconLine(pixels, size, new Vector2(7f, 5f), new Vector2(25f, 5f), 2f, bright);
                    break;
                case "MashBox.VertexPaint":
                    DrawIconLine(pixels, size, new Vector2(25f, 26f), new Vector2(14f, 15f), 4f, mid);
                    DrawIconLine(pixels, size, new Vector2(14f, 15f), new Vector2(8f, 9f), 4.8f, bright);
                    DrawIconDot(pixels, size, new Vector2(7f, 24f), 2f, bright);
                    DrawIconDot(pixels, size, new Vector2(14f, 26f), 2f, bright);
                    DrawIconDot(pixels, size, new Vector2(6f, 16f), 2f, bright);
                    break;
                case "MashBox.Splat":
                    DrawIconLine(pixels, size, new Vector2(16f, 27f), new Vector2(8f, 13f), 2.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(16f, 27f), new Vector2(24f, 13f), 2.5f, bright);
                    DrawIconDot(pixels, size, new Vector2(16f, 12f), 8f, mid);
                    DrawIconDot(pixels, size, new Vector2(16f, 13f), 5.5f, bright);
                    break;
                case "MashBox.Loft":
                    DrawIconLine(pixels, size, new Vector2(4f, 24f), new Vector2(12f, 15f), 2f, bright);
                    DrawIconLine(pixels, size, new Vector2(12f, 15f), new Vector2(28f, 20f), 2f, bright);
                    DrawIconLine(pixels, size, new Vector2(4f, 17f), new Vector2(12f, 8f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(12f, 8f), new Vector2(28f, 13f), 2f, mid);
                    break;
                case "MashBox.UV":
                    DrawIconLine(pixels, size, new Vector2(5f, 5f), new Vector2(27f, 5f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(27f, 5f), new Vector2(27f, 27f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(27f, 27f), new Vector2(5f, 27f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(5f, 27f), new Vector2(5f, 5f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(5f, 20f), new Vector2(14f, 11f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(14f, 11f), new Vector2(27f, 18f), 2.2f, bright);
                    break;
                case "MashBox.Sculpt":
                    DrawIconLine(pixels, size, new Vector2(24f, 25f), new Vector2(18f, 19f), 6f, mid);
                    DrawIconLine(pixels, size, new Vector2(18f, 19f), new Vector2(8f, 9f), 3.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(5f, 10f), new Vector2(10f, 5f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(20f, 28f), new Vector2(28f, 20f), 2f, mid);
                    break;
                case "MashBox.Displace":
                    DrawIconLine(pixels, size, new Vector2(16f, 5f), new Vector2(16f, 27f), 2.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(16f, 27f), new Vector2(11f, 21f), 2.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(16f, 27f), new Vector2(21f, 21f), 2.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(5f, 8f), new Vector2(27f, 8f), 2.5f, mid);
                    break;
                case "MashBox.Smooth":
                    DrawIconLine(pixels, size, new Vector2(3f, 12f), new Vector2(9f, 20f), 2.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(9f, 20f), new Vector2(16f, 12f), 2.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(16f, 12f), new Vector2(23f, 20f), 2.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(23f, 20f), new Vector2(29f, 12f), 2.5f, bright);
                    break;
                case "MashBox.Flatten":
                    DrawIconLine(pixels, size, new Vector2(4f, 9f), new Vector2(28f, 9f), 3f, bright);
                    DrawIconLine(pixels, size, new Vector2(10f, 27f), new Vector2(10f, 15f), 2.3f, mid);
                    DrawIconLine(pixels, size, new Vector2(22f, 27f), new Vector2(22f, 15f), 2.3f, mid);
                    DrawIconLine(pixels, size, new Vector2(10f, 15f), new Vector2(7f, 19f), 2.3f, mid);
                    DrawIconLine(pixels, size, new Vector2(22f, 15f), new Vector2(25f, 19f), 2.3f, mid);
                    break;
            }

            icon.SetPixels32(pixels);
            icon.Apply(false, true);
            return icon;
        }

        static void DrawIconLine(
            Color32[] pixels,
            int textureSize,
            Vector2 start,
            Vector2 end,
            float width,
            Color32 color)
        {
            Vector2 segment = end - start;
            float segmentLengthSquared = segment.sqrMagnitude;
            float radius = width * 0.5f;
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                    float amount = segmentLengthSquared > 0f
                        ? Mathf.Clamp01(Vector2.Dot(point - start, segment) / segmentLengthSquared)
                        : 0f;
                    if (Vector2.Distance(point, start + segment * amount) <= radius)
                        pixels[y * textureSize + x] = color;
                }
            }
        }

        static void DrawIconDot(
            Color32[] pixels,
            int textureSize,
            Vector2 center,
            float radius,
            Color32 color)
        {
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                    if (Vector2.Distance(point, center) <= radius)
                        pixels[y * textureSize + x] = color;
                }
            }
        }

        internal static void ApplyModeToolSelection(VisualElement element, bool selected)
        {
            element.style.backgroundColor = selected
                ? new StyleColor(ModeToolSelectionColor)
                : new StyleColor(StyleKeyword.Null);
            element.style.color = selected
                ? new StyleColor(Color.white)
                : new StyleColor(StyleKeyword.Null);
        }

        internal static void ApplyToolActionSelection(VisualElement element, bool selected)
        {
            element.style.backgroundColor = selected
                ? new StyleColor(ToolActionSelectionColor)
                : new StyleColor(StyleKeyword.Null);
            element.style.color = selected
                ? new StyleColor(Color.white)
                : new StyleColor(StyleKeyword.Null);
        }
    }

    [Overlay(typeof(SceneView), "MashBox Mappy", true)]
    public sealed class MBGameplayGizmoOverlay : ToolbarOverlay
    {
        internal const float PanelWidth = 171f;
        internal const float RowHeadingWidth = 54f;

        static Texture2D s_CollapsedIcon;

        public MBGameplayGizmoOverlay() : base(
            MBActiveEditingToggle.Id,
            MBGameplayGizmoToggle.Id,
            MBBrushModeToggle.Id,
            MBSplineCategoryToggle.Id,
            MBSplineModeToggle.Id,
            MBSplineLoftModeToggle.Id,
            MBMeshSculptModeToggle.Id,
            MBUvSplineModeToggle.Id,
            MBDecorBrushToggle.Id,
            MBPainterBrushToggle.Id,
            MBSplatBrushToggle.Id,
            MBSplineEditModeToggle.Id,
            MBNewSplineButton.Id,
            MBDisplaceSculptToggle.Id,
            MBSmoothSculptToggle.Id,
            MBFlattenSculptToggle.Id,
            MBMoveUvToggle.Id,
            MBSideOffsetUvToggle.Id,
            MBUvScaleToggle.Id)
        {
            collapsedIcon = GetCollapsedIcon();
        }

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.style.width = Length.Percent(100f);
            root.style.minWidth = PanelWidth;
            root.style.alignSelf = Align.Stretch;

            var displayRow = CreateRow("Display", out VisualElement displayContent);
            displayContent.Add(new MBGameplayGizmoToggle());
            root.Add(displayRow);

            var editingRow = CreateRow("Editing", out VisualElement editingContent);
            editingContent.Add(new MBActiveEditingToggle());
            root.Add(editingRow);

            var modeRow = CreateRow("Mode", out VisualElement modeContent);
            modeContent.Add(new MBBrushModeToggle());
            modeContent.Add(new MBSplineCategoryToggle());
            root.Add(modeRow);

            var toolsRow = CreateRow("Tools", out VisualElement toolsContent);
            toolsContent.Add(new MBDecorBrushToggle());
            toolsContent.Add(new MBPainterBrushToggle());
            toolsContent.Add(new MBSplatBrushToggle());
            toolsContent.Add(new MBMeshSculptModeToggle());
            toolsContent.Add(new MBSplineModeToggle());
            toolsContent.Add(new MBSplineLoftModeToggle());
            toolsContent.Add(new MBUvSplineModeToggle());
            root.Add(toolsRow);

            var actionsRow = CreateRow("Actions", out VisualElement actionsContent);
            actionsContent.Add(new MBSplineEditModeToggle());
            actionsContent.Add(new MBNewSplineButton());
            actionsContent.Add(new MBDisplaceSculptToggle());
            actionsContent.Add(new MBSmoothSculptToggle());
            actionsContent.Add(new MBFlattenSculptToggle());
            actionsContent.Add(new MBMoveUvToggle());
            actionsContent.Add(new MBSideOffsetUvToggle());
            actionsContent.Add(new MBUvScaleToggle());
            root.Add(actionsRow);

            System.Action syncActionsVisibility = () =>
            {
                MBEditorAuthoringMode mode = MBEditorToolState.Mode;
                bool hasActions = mode == MBEditorAuthoringMode.SplineLoft
                    || mode == MBEditorAuthoringMode.Spline
                    || mode == MBEditorAuthoringMode.MeshSculpt
                    || mode == MBEditorAuthoringMode.UVSpline;
                actionsRow.style.display = MBEditorToolState.ActiveEditing && hasActions
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            };

            VisualElement controlsSection = CreateControlsSection();
            root.Add(controlsSection);

            System.Action syncEditingVisibility = () =>
            {
                DisplayStyle editingDisplay = MBEditorToolState.ActiveEditing
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                modeRow.style.display = editingDisplay;
                toolsRow.style.display = editingDisplay;
                controlsSection.style.display = MBEditorToolState.ActiveEditing
                    && MBEditorToolState.Mode != MBEditorAuthoringMode.Spline
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                syncActionsVisibility();
            };
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                MBEditorToolState.ModeChanged += syncEditingVisibility;
                MBEditorToolState.ActiveEditingChanged += syncEditingVisibility;
                syncEditingVisibility();
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                MBEditorToolState.ModeChanged -= syncEditingVisibility;
                MBEditorToolState.ActiveEditingChanged -= syncEditingVisibility;
            });
            syncEditingVisibility();

            return root;
        }

        static Toolbar CreateRow(string label, out VisualElement content)
        {
            var row = new Toolbar();
            row.style.width = Length.Percent(100f);
            row.style.alignSelf = Align.Stretch;
            row.style.height = new StyleLength(StyleKeyword.Auto);
            row.style.minHeight = 22f;
            row.style.flexShrink = 0f;
            row.style.alignItems = Align.FlexStart;
            var heading = new Label(label);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.minWidth = RowHeadingWidth;
            heading.style.width = RowHeadingWidth;
            heading.style.marginLeft = 4f;
            heading.style.marginTop = 3f;
            row.Add(heading);

            content = new VisualElement();
            content.style.flexDirection = FlexDirection.Row;
            content.style.flexWrap = Wrap.Wrap;
            content.style.justifyContent = Justify.FlexEnd;
            content.style.flexGrow = 1f;
            content.style.flexShrink = 1f;
            content.style.minWidth = 0f;
            row.Add(content);
            return row;
        }

        static VisualElement CreateControlsSection()
        {
            var section = new Toolbar();
            section.style.width = Length.Percent(100f);
            section.style.alignSelf = Align.Stretch;
            section.style.height = new StyleLength(StyleKeyword.Auto);
            section.style.flexDirection = FlexDirection.Column;
            section.style.flexShrink = 0f;
            section.style.paddingLeft = 6f;
            section.style.paddingRight = 6f;
            section.style.paddingTop = 4f;
            section.style.paddingBottom = 5f;

            var headingRow = new VisualElement();
            headingRow.style.flexDirection = FlexDirection.Row;
            headingRow.style.alignItems = Align.Center;
            headingRow.style.marginBottom = 3f;

            var heading = new Label("Controls");
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.flexGrow = 1f;
            headingRow.Add(heading);
            headingRow.Add(new MBCurrentToolLabel());
            section.Add(headingRow);

            var divider = new VisualElement();
            divider.style.height = 1f;
            divider.style.marginBottom = 4f;
            divider.style.backgroundColor = new StyleColor(new Color(0.5f, 0.5f, 0.5f, 0.28f));
            section.Add(divider);

            section.Add(new MBModeControlsLabel());
            return section;
        }

        static Texture2D GetCollapsedIcon()
        {
            if (s_CollapsedIcon != null)
                return s_CollapsedIcon;

            const int size = 32;
            s_CollapsedIcon = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MashBox Mappy MB Icon",
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            DrawGlyph(pixels, size, 4, 9, new[]
            {
                "10001",
                "11011",
                "10101",
                "10101",
                "10001",
                "10001",
                "10001"
            });
            DrawGlyph(pixels, size, 18, 9, new[]
            {
                "11110",
                "10001",
                "10001",
                "11110",
                "10001",
                "10001",
                "11110"
            });

            s_CollapsedIcon.SetPixels32(pixels);
            s_CollapsedIcon.Apply(false, true);
            return s_CollapsedIcon;
        }

        static void DrawGlyph(Color32[] pixels, int textureSize, int startX, int startY, string[] rows)
        {
            var color = new Color32(235, 241, 250, 255);
            const int scale = 2;
            for (int row = 0; row < rows.Length; row++)
            {
                for (int column = 0; column < rows[row].Length; column++)
                {
                    if (rows[row][column] != '1')
                        continue;

                    for (int offsetY = 0; offsetY < scale; offsetY++)
                    {
                        for (int offsetX = 0; offsetX < scale; offsetX++)
                        {
                            int x = startX + column * scale + offsetX;
                            int y = textureSize - 1 - (startY + row * scale + offsetY);
                            pixels[y * textureSize + x] = color;
                        }
                    }
                }
            }
        }
    }

    public sealed class MBCurrentToolLabel : Label
    {
        static readonly Color CurrentToolColor = new Color(0.9f, 0.58f, 0.24f, 1f);

        public MBCurrentToolLabel()
        {
            style.color = new StyleColor(CurrentToolColor);
            style.unityFontStyleAndWeight = FontStyle.Bold;
            style.unityTextAlign = TextAnchor.MiddleRight;
            this.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                MBEditorToolState.ModeChanged += Sync;
                MBEditorToolState.BrushModeChanged += Sync;
                MBEditorToolState.SculptModeChanged += Sync;
                MBEditorToolState.UvModeChanged += Sync;
                Sync();
            });
            this.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                MBEditorToolState.ModeChanged -= Sync;
                MBEditorToolState.BrushModeChanged -= Sync;
                MBEditorToolState.SculptModeChanged -= Sync;
                MBEditorToolState.UvModeChanged -= Sync;
            });
            Sync();
        }

        void Sync()
        {
            text = MBEditorToolState.Mode switch
            {
                MBEditorAuthoringMode.Brush => MBEditorToolState.BrushMode switch
                {
                    MBBrushMode.Decor => "Decor",
                    MBBrushMode.Painter => "Vertex Painter",
                    MBBrushMode.SplatMap => "Splat",
                    _ => "Brush"
                },
                MBEditorAuthoringMode.SplineLoft => "Spline Loft",
                MBEditorAuthoringMode.Spline => "Spline",
                MBEditorAuthoringMode.MeshSculpt => "Sculpt - " + MBEditorToolState.SculptMode,
                MBEditorAuthoringMode.UVSpline => "UV Spline - " + (MBEditorToolState.UvMode switch
                {
                    MBUvHandleMode.MoveAndUv => "Move + UV",
                    MBUvHandleMode.SideOffset => "Side Offset",
                    MBUvHandleMode.UvScale => "UV Scale",
                    _ => "Edit"
                }),
                MBEditorAuthoringMode.Terrain => "Terrain",
                _ => string.Empty
            };
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBActiveEditingToggle : EditorToolbarToggle
    {
        public const string Id = "MashBox/Active Editing";

        public MBActiveEditingToggle()
        {
            text = string.Empty;
            icon = MBEditorToolVisuals.GetToolbarIcon("MashBox.ActiveEditing");
            tooltip = "Enable automatic MashBox Scene editing and selection behavior for the current mode.";
            style.width = 28f;
            style.minWidth = 28f;
            style.maxWidth = 28f;
            style.height = 28f;
            style.minHeight = 28f;
            style.maxHeight = 28f;
            style.flexShrink = 0f;
            value = MBEditorToolState.ActiveEditing;
            this.RegisterValueChangedCallback(changeEvent => MBEditorToolState.ActiveEditing = changeEvent.newValue);
            this.RegisterCallback<AttachToPanelEvent>(_ => MBEditorToolState.ActiveEditingChanged += Sync);
            this.RegisterCallback<DetachFromPanelEvent>(_ => MBEditorToolState.ActiveEditingChanged -= Sync);
            UpdateColor();
        }

        void Sync()
        {
            SetValueWithoutNotify(MBEditorToolState.ActiveEditing);
            UpdateColor();
            SceneView.RepaintAll();
        }

        void UpdateColor()
        {
            if (MBEditorToolState.ActiveEditing)
            {
                style.backgroundColor = new StyleColor(new Color(0.12f, 0.48f, 0.2f, 0.95f));
                style.color = new StyleColor(Color.white);
            }
            else
            {
                style.backgroundColor = new StyleColor(StyleKeyword.Null);
                style.color = new StyleColor(StyleKeyword.Null);
            }
        }
    }

    public abstract class MBEditorCategoryToggle : EditorToolbarToggle
    {
        readonly MBEditorAuthoringCategory m_Category;

        protected MBEditorCategoryToggle(
            MBEditorAuthoringCategory category,
            string label,
            string description,
            string iconName,
            string fallbackIconName)
        {
            m_Category = category;
            MBEditorToolVisuals.ConfigureIconOnly(this, iconName, fallbackIconName, label, description);
            this.RegisterValueChangedCallback(OnValueChanged);
            this.RegisterCallback<AttachToPanelEvent>(_ => MBEditorToolState.ModeChanged += Sync);
            this.RegisterCallback<DetachFromPanelEvent>(_ => MBEditorToolState.ModeChanged -= Sync);
            Sync();
        }

        void OnValueChanged(ChangeEvent<bool> changeEvent)
        {
            if (changeEvent.newValue)
                MBEditorToolState.RequestCategory(m_Category);
            Sync();
        }

        void Sync()
        {
            SetValueWithoutNotify(MBEditorToolState.Category == m_Category);
            SceneView.RepaintAll();
        }
    }

    public abstract class MBEditorModeToggle : EditorToolbarToggle
    {
        readonly MBEditorAuthoringMode m_Mode;
        readonly MBEditorAuthoringCategory m_Category;

        protected MBEditorModeToggle(
            MBEditorAuthoringMode mode,
            MBEditorAuthoringCategory category,
            string label,
            string description,
            string iconName,
            string fallbackIconName)
        {
            m_Mode = mode;
            m_Category = category;
            MBEditorToolVisuals.ConfigureIconOnly(this, iconName, fallbackIconName, label, description);
            SetValueWithoutNotify(MBEditorToolState.Mode == m_Mode);
            this.RegisterValueChangedCallback(OnValueChanged);
            this.RegisterCallback<AttachToPanelEvent>(_ => MBEditorToolState.ModeChanged += Sync);
            this.RegisterCallback<DetachFromPanelEvent>(_ => MBEditorToolState.ModeChanged -= Sync);
            Sync();
        }

        void OnValueChanged(ChangeEvent<bool> changeEvent)
        {
            if (changeEvent.newValue)
                MBEditorToolState.RequestMode(m_Mode);
            else if (MBEditorToolState.Mode == m_Mode)
                MBEditorToolState.RequestMode(m_Mode);
            Sync();
        }

        void Sync()
        {
            style.display = MBEditorToolState.Category == m_Category
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            bool selected = MBEditorToolState.Mode == m_Mode;
            SetValueWithoutNotify(selected);
            MBEditorToolVisuals.ApplyModeToolSelection(this, selected);
            SceneView.RepaintAll();
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBBrushModeToggle : MBEditorCategoryToggle
    {
        public const string Id = "MashBox/Editor Mode/Brush";
        public MBBrushModeToggle() : base(
            MBEditorAuthoringCategory.Brush,
            "Brush",
            "Show painting, scattering, and sculpting tools.",
            "MashBox.Brush",
            "d_MoveTool") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBSplineCategoryToggle : MBEditorCategoryToggle
    {
        public const string Id = "MashBox/Editor Mode/Spline Category";
        public MBSplineCategoryToggle() : base(
            MBEditorAuthoringCategory.Spline,
            "Spline",
            "Show Spline Loft, individual Spline, and UV Spline tools.",
            "MashBox.Spline",
            "d_RectTool") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBSplineLoftModeToggle : MBEditorModeToggle
    {
        public const string Id = "MashBox/Editor Mode/Spline Loft";
        public MBSplineLoftModeToggle() : base(
            MBEditorAuthoringMode.SplineLoft,
            MBEditorAuthoringCategory.Spline,
            "Loft",
            "Switch to the Spline Loft authoring mode.",
            "MashBox.Loft",
            "d_RectTool") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBSplineModeToggle : MBEditorModeToggle
    {
        public const string Id = "MashBox/Editor Mode/Spline";
        public MBSplineModeToggle() : base(
            MBEditorAuthoringMode.Spline,
            MBEditorAuthoringCategory.Spline,
            "Single",
            "Switch to the individual Spline authoring mode.",
            "MashBox.Spline",
            "d_MoveTool") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBMeshSculptModeToggle : MBEditorModeToggle
    {
        public const string Id = "MashBox/Editor Mode/Mesh Sculpt";
        public MBMeshSculptModeToggle() : base(
            MBEditorAuthoringMode.MeshSculpt,
            MBEditorAuthoringCategory.Brush,
            "Sculpt",
            "Switch to the Mesh Sculpt authoring mode.",
            "MashBox.Sculpt",
            "d_RotateTool") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBUvSplineModeToggle : MBEditorModeToggle
    {
        public const string Id = "MashBox/Editor Mode/UV Spline";
        public MBUvSplineModeToggle() : base(
            MBEditorAuthoringMode.UVSpline,
            MBEditorAuthoringCategory.Spline,
            "UV",
            "Switch to the UV Spline authoring mode.",
            "MashBox.UV",
            "d_ScaleTool") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBTerrainModeToggle : MBEditorCategoryToggle
    {
        public const string Id = "MashBox/Editor Mode/Terrain";
        public MBTerrainModeToggle() : base(
            MBEditorAuthoringCategory.Terrain,
            "Terrain",
            "Open terrain conversion and surface authoring tools.",
            "MashBox.Terrain",
            "d_ScaleTool") { }
    }

    public abstract class MBBrushSubmodeToggle : EditorToolbarToggle
    {
        readonly MBBrushMode m_Mode;

        protected MBBrushSubmodeToggle(
            MBBrushMode mode,
            string label,
            string tooltipText,
            string iconName,
            string fallbackIconName)
        {
            m_Mode = mode;
            MBEditorToolVisuals.ConfigureIconOnly(this, iconName, fallbackIconName, label, tooltipText);
            this.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    MBEditorToolState.RequestMode(MBEditorAuthoringMode.Brush);
                    MBEditorToolState.BrushMode = m_Mode;
                }
                Sync();
            });
            this.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                MBEditorToolState.ModeChanged += Sync;
                MBEditorToolState.BrushModeChanged += Sync;
                MBEditorToolState.ActiveEditingChanged += Sync;
                Sync();
            });
            this.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                MBEditorToolState.ModeChanged -= Sync;
                MBEditorToolState.BrushModeChanged -= Sync;
                MBEditorToolState.ActiveEditingChanged -= Sync;
            });
            Sync();
        }

        void Sync()
        {
            style.display = MBEditorToolState.Category == MBEditorAuthoringCategory.Brush
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            SetEnabled(MBEditorToolState.ActiveEditing);
            bool selected = MBEditorToolState.Mode == MBEditorAuthoringMode.Brush
                && MBEditorToolState.BrushMode == m_Mode;
            SetValueWithoutNotify(selected);
            MBEditorToolVisuals.ApplyModeToolSelection(this, selected);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBDecorBrushToggle : MBBrushSubmodeToggle
    {
        public const string Id = "MashBox/Brush/Decor";
        public MBDecorBrushToggle() : base(
            MBBrushMode.Decor,
            "Decor",
            "Scatter prefab decorations.",
            "MashBox.Decor",
            "d_MoveTool") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBPainterBrushToggle : MBBrushSubmodeToggle
    {
        public const string Id = "MashBox/Brush/Painter";
        public MBPainterBrushToggle() : base(
            MBBrushMode.Painter,
            "Vertex Painter",
            "Paint vertex colors.",
            "MashBox.VertexPaint",
            "d_MoveTool") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBSplatBrushToggle : MBBrushSubmodeToggle
    {
        public const string Id = "MashBox/Brush/Splat";
        public MBSplatBrushToggle() : base(
            MBBrushMode.SplatMap,
            "Splat",
            "Paint the selected splat-map channel; hold Shift to erase.",
            "MashBox.Splat",
            "d_ScaleTool") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBSplineEditModeToggle : EditorToolbarToggle
    {
        public const string Id = "MashBox/Spline/Toggle Edit Mode";

        int m_ActivationAttempts;
        bool m_ActivatedByThisToggle;

        public MBSplineEditModeToggle()
        {
            MBEditorToolVisuals.ConfigureIconOnly(
                this,
                "MashBox.SplineEdit",
                "d_MoveTool",
                "Edit Spline",
                "Toggle Unity's spline knot editing mode for the selected spline.");
            this.RegisterValueChangedCallback(OnValueChanged);
            this.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                MBEditorToolState.ModeChanged += Sync;
                MBEditorToolState.ActiveEditingChanged += Sync;
                Selection.selectionChanged += Sync;
                ToolManager.activeToolChanged += Sync;
                ToolManager.activeContextChanged += Sync;
                Sync();
            });
            this.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                MBEditorToolState.ModeChanged -= Sync;
                MBEditorToolState.ActiveEditingChanged -= Sync;
                Selection.selectionChanged -= Sync;
                ToolManager.activeToolChanged -= Sync;
                ToolManager.activeContextChanged -= Sync;
                EditorApplication.delayCall -= ActivateSplineEditMode;
            });
            Sync();
        }

        void OnValueChanged(ChangeEvent<bool> changeEvent)
        {
            if (changeEvent.newValue)
                QueueSplineEditMode();
            else
                DisableSplineEditMode();
            Sync();
        }

        void QueueSplineEditMode()
        {
            if (!IsAvailable())
                return;

            m_ActivationAttempts = 0;
            EditorApplication.delayCall -= ActivateSplineEditMode;
            EditorApplication.delayCall += ActivateSplineEditMode;
        }

        void ActivateSplineEditMode()
        {
            EditorApplication.delayCall -= ActivateSplineEditMode;
            if (!IsAvailable())
            {
                Sync();
                return;
            }

            SplineContainer selectedSpline = FindSelectedSpline();
            Selection.activeGameObject = selectedSpline.gameObject;
            m_ActivatedByThisToggle = true;
            ToolManager.SetActiveContext<SplineToolContext>();
            ToolManager.SetActiveTool<SplineMoveTool>();

            if (!IsSplineEditModeActive() && ++m_ActivationAttempts < 3)
                EditorApplication.delayCall += ActivateSplineEditMode;

            Sync();
            SceneView.RepaintAll();
        }

        void DisableSplineEditMode()
        {
            EditorApplication.delayCall -= ActivateSplineEditMode;
            m_ActivationAttempts = 0;
            m_ActivatedByThisToggle = false;
            if (ToolManager.activeContextType == typeof(SplineToolContext))
                ToolManager.SetActiveContext<GameObjectToolContext>();
            SceneView.RepaintAll();
        }

        void Sync()
        {
            bool available = IsAvailable();
            if (!available && m_ActivatedByThisToggle)
                DisableSplineEditMode();

            style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
            SetEnabled(MBEditorToolState.ActiveEditing && available);
            bool selected = available && IsSplineEditModeActive();
            SetValueWithoutNotify(selected);
            MBEditorToolVisuals.ApplyToolActionSelection(this, selected);
        }

        static bool IsAvailable()
        {
            bool splineMode = MBEditorToolState.Mode == MBEditorAuthoringMode.Spline
                || MBEditorToolState.Mode == MBEditorAuthoringMode.SplineLoft;
            return MBEditorToolState.ActiveEditing && splineMode && FindSelectedSpline() != null;
        }

        static bool IsSplineEditModeActive()
        {
            return ToolManager.activeContextType == typeof(SplineToolContext)
                && ToolManager.activeToolType == typeof(SplineMoveTool);
        }

        static SplineContainer FindSelectedSpline()
        {
            GameObject selected = Selection.activeGameObject;
            return selected != null
                ? selected.GetComponent<SplineContainer>() ?? selected.GetComponentInParent<SplineContainer>()
                : null;
        }
    }

    public abstract class MBSplineActionButton : EditorToolbarButton
    {
        readonly MBEditorToolAction m_Action;

        protected MBSplineActionButton(MBEditorToolAction action, string label, string tooltipText)
        {
            m_Action = action;
            text = label;
            tooltip = tooltipText;
            clicked += Invoke;
            this.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                MBEditorToolState.ModeChanged += Sync;
                MBEditorToolState.ActiveEditingChanged += Sync;
                Sync();
            });
            this.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                MBEditorToolState.ModeChanged -= Sync;
                MBEditorToolState.ActiveEditingChanged -= Sync;
            });
            Sync();
        }

        void Invoke()
        {
            MBEditorToolAction action = m_Action;
            if (m_Action == MBEditorToolAction.CreateSpline
                && MBEditorToolState.Mode == MBEditorAuthoringMode.SplineLoft)
                action = MBEditorToolAction.CreateLoftSpline;
            MBEditorToolState.RequestAction(action);
        }

        void Sync()
        {
            bool visible = MBEditorToolState.Mode == MBEditorAuthoringMode.Spline
                || MBEditorToolState.Mode == MBEditorAuthoringMode.SplineLoft;
            style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            SetEnabled(MBEditorToolState.ActiveEditing);
            if (m_Action == MBEditorToolAction.CreateSpline)
                tooltip = MBEditorToolState.Mode == MBEditorAuthoringMode.SplineLoft
                    ? "Create a new spline and add it to the current loft."
                    : "Create a new spline.";
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBNewSplineButton : MBSplineActionButton
    {
        public const string Id = "MashBox/Spline/New";
        public MBNewSplineButton() : base(MBEditorToolAction.CreateSpline, "New Spline", "Create a spline for the current spline mode.")
        {
            text = string.Empty;
            icon = MBEditorToolVisuals.GetToolbarIcon("MashBox.NewSpline");
            style.width = 28f;
            style.minWidth = 28f;
            style.maxWidth = 28f;
            style.height = 28f;
            style.minHeight = 28f;
            style.maxHeight = 28f;
            style.flexShrink = 0f;
        }
    }

    public abstract class MBSculptSubmodeToggle : EditorToolbarToggle
    {
        readonly MBSculptMode m_Mode;

        protected MBSculptSubmodeToggle(
            MBSculptMode mode,
            string label,
            string tooltipText,
            string iconName,
            string fallbackIconName)
        {
            m_Mode = mode;
            MBEditorToolVisuals.ConfigureIconOnly(this, iconName, fallbackIconName, label, tooltipText);
            this.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                    MBEditorToolState.SculptMode = m_Mode;
                Sync();
            });
            this.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                MBEditorToolState.ModeChanged += Sync;
                MBEditorToolState.SculptModeChanged += Sync;
                MBEditorToolState.ActiveEditingChanged += Sync;
                Sync();
            });
            this.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                MBEditorToolState.ModeChanged -= Sync;
                MBEditorToolState.SculptModeChanged -= Sync;
                MBEditorToolState.ActiveEditingChanged -= Sync;
            });
            Sync();
        }

        void Sync()
        {
            style.display = MBEditorToolState.Mode == MBEditorAuthoringMode.MeshSculpt ? DisplayStyle.Flex : DisplayStyle.None;
            SetEnabled(MBEditorToolState.ActiveEditing);
            bool selected = MBEditorToolState.SculptMode == m_Mode;
            SetValueWithoutNotify(selected);
            MBEditorToolVisuals.ApplyToolActionSelection(this, selected);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBDisplaceSculptToggle : MBSculptSubmodeToggle
    {
        public const string Id = "MashBox/Sculpt/Displace";
        public MBDisplaceSculptToggle() : base(
            MBSculptMode.Displace,
            "Displace",
            "Push or pull the mesh; hold Ctrl to invert.",
            "MashBox.Displace",
            "d_MoveTool") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBSmoothSculptToggle : MBSculptSubmodeToggle
    {
        public const string Id = "MashBox/Sculpt/Smooth";
        public MBSmoothSculptToggle() : base(
            MBSculptMode.Smooth,
            "Smooth",
            "Smooth vertices beneath the brush.",
            "MashBox.Smooth",
            "d_RotateTool") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBFlattenSculptToggle : MBSculptSubmodeToggle
    {
        public const string Id = "MashBox/Sculpt/Flatten";
        public MBFlattenSculptToggle() : base(
            MBSculptMode.Flatten,
            "Flatten",
            "Flatten vertices beneath the brush.",
            "MashBox.Flatten",
            "d_RectTool") { }
    }

    public abstract class MBUvSubmodeToggle : EditorToolbarToggle
    {
        readonly MBUvHandleMode m_Mode;

        protected MBUvSubmodeToggle(
            MBUvHandleMode mode,
            string label,
            string tooltipText,
            string iconName,
            string fallbackIconName)
        {
            m_Mode = mode;
            MBEditorToolVisuals.ConfigureIconOnly(this, iconName, fallbackIconName, label, tooltipText);
            this.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                    MBEditorToolState.UvMode = m_Mode;
                Sync();
            });
            this.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                MBEditorToolState.ModeChanged += Sync;
                MBEditorToolState.UvModeChanged += Sync;
                MBEditorToolState.ActiveEditingChanged += Sync;
                Sync();
            });
            this.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                MBEditorToolState.ModeChanged -= Sync;
                MBEditorToolState.UvModeChanged -= Sync;
                MBEditorToolState.ActiveEditingChanged -= Sync;
            });
            Sync();
        }

        void Sync()
        {
            style.display = MBEditorToolState.Mode == MBEditorAuthoringMode.UVSpline ? DisplayStyle.Flex : DisplayStyle.None;
            SetEnabled(MBEditorToolState.ActiveEditing);
            bool selected = MBEditorToolState.UvMode == m_Mode;
            SetValueWithoutNotify(selected);
            MBEditorToolVisuals.ApplyToolActionSelection(this, selected);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBMoveUvToggle : MBUvSubmodeToggle
    {
        public const string Id = "MashBox/UV/Move";
        public MBMoveUvToggle() : base(
            MBUvHandleMode.MoveAndUv,
            "Move + UV",
            "Move a UV spline knot and its UV section (W).",
            "d_MoveTool",
            "d_RectTool") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBSideOffsetUvToggle : MBUvSubmodeToggle
    {
        public const string Id = "MashBox/UV/Side Offset";
        public MBSideOffsetUvToggle() : base(
            MBUvHandleMode.SideOffset,
            "Side Offset",
            "Adjust the selected UV section sideways (E).",
            "d_RectTool",
            "d_MoveTool") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBUvScaleToggle : MBUvSubmodeToggle
    {
        public const string Id = "MashBox/UV/Scale";
        public MBUvScaleToggle() : base(
            MBUvHandleMode.UvScale,
            "UV Scale",
            "Scale UVs across and along the selected section (R).",
            "d_ScaleTool",
            "d_RectTool") { }
    }

    public sealed class MBModeControlsLabel : VisualElement
    {
        static readonly Color ShortcutColor = new Color(0.35f, 0.67f, 0.96f, 1f);

        public MBModeControlsLabel()
        {
            style.flexDirection = FlexDirection.Column;
            style.width = Length.Percent(100f);
            style.alignSelf = Align.Stretch;
            style.flexShrink = 1f;
            this.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                MBEditorToolState.ModeChanged += Sync;
                Sync();
            });
            this.RegisterCallback<DetachFromPanelEvent>(_ => MBEditorToolState.ModeChanged -= Sync);
            Sync();
        }

        void Sync()
        {
            Clear();

            switch (MBEditorToolState.Mode)
            {
                case MBEditorAuthoringMode.Brush:
                    AddShortcut("MashBox.Brush", "Paint", "LMB Drag");
                    AddShortcut("d_ViewToolZoom", "Radius", "Ctrl + MMB Left / Right");
                    AddShortcut("d_ScaleTool", "Strength", "Ctrl + MMB Up / Down");
                    AddShortcut("d_SceneViewFx", "Focus Surface", "F");
                    break;
                case MBEditorAuthoringMode.SplineLoft:
                case MBEditorAuthoringMode.Spline:
                    AddShortcut("TreeEditor.Trash", "Remove Knots", "Delete / Backspace");
                    break;
                case MBEditorAuthoringMode.MeshSculpt:
                    AddShortcut("MashBox.Sculpt", "Sculpt", "LMB Drag");
                    AddShortcut("d_RotateTool", "Invert", "Ctrl");
                    AddShortcut("MashBox.Smooth", "Smooth", "Shift");
                    AddShortcut("d_PreMatCube", "Noise", "Ctrl + Shift");
                    AddShortcut("d_ViewToolZoom", "Radius / Strength", "Ctrl + MMB Drag");
                    break;
                case MBEditorAuthoringMode.UVSpline:
                    AddShortcut("d_MoveTool", "Move + UV", "W");
                    AddShortcut("d_RectTool", "Side Offset", "E");
                    AddShortcut("d_ScaleTool", "UV Scale", "R");
                    AddShortcut("d_SceneViewFx", "Focus Knot", "F");
                    break;
                case MBEditorAuthoringMode.Terrain:
                    AddShortcut("d_TerrainInspector.TerrainToolSettings", "Open Terrain Tools", "MashBox SDK");
                    break;
            }

            style.display = DisplayStyle.Flex;
        }

        void AddShortcut(string iconName, string action, string shortcut)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 20f;

            Texture iconTexture = MBEditorToolVisuals.GetToolbarIcon(iconName);
            if (iconTexture != null)
            {
                var icon = new Image { image = iconTexture };
                icon.style.width = 16f;
                icon.style.height = 16f;
                icon.style.marginLeft = 6f;
                icon.style.marginRight = 7f;
                icon.scaleMode = ScaleMode.ScaleToFit;
                row.Add(icon);
            }
            else
            {
                var spacer = new VisualElement();
                spacer.style.width = 29f;
                row.Add(spacer);
            }

            var actionLabel = new Label(action);
            actionLabel.style.flexGrow = 1f;
            actionLabel.style.flexShrink = 1f;
            actionLabel.style.whiteSpace = WhiteSpace.Normal;
            row.Add(actionLabel);

            var shortcutLabel = new Label(shortcut);
            shortcutLabel.style.color = new StyleColor(ShortcutColor);
            shortcutLabel.style.width = 75f;
            shortcutLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            shortcutLabel.style.whiteSpace = WhiteSpace.Normal;
            row.Add(shortcutLabel);

            Add(row);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBGameplayGizmoToggle : EditorToolbarToggle
    {
        public const string Id = "MashBox/Gameplay Gizmos";

        public MBGameplayGizmoToggle()
        {
            text = string.Empty;
            icon = MBEditorToolVisuals.GetToolbarIcon("MashBox.GameplayGizmos");
            tooltip = "Show or hide MashBox race and gameplay gizmos in the Scene view.";
            style.width = 28f;
            style.minWidth = 28f;
            style.maxWidth = 28f;
            style.height = 28f;
            style.minHeight = 28f;
            style.maxHeight = 28f;
            style.flexShrink = 0f;
            value = MBGameplayGizmoVisibility.Visible;

            this.RegisterValueChangedCallback(changeEvent =>
            {
                MBGameplayGizmoVisibility.Visible = changeEvent.newValue;
                SceneView.RepaintAll();
            });
            this.RegisterCallback<AttachToPanelEvent>(_ => MBGameplayGizmoVisibility.Changed += SyncFromSharedSetting);
            this.RegisterCallback<DetachFromPanelEvent>(_ => MBGameplayGizmoVisibility.Changed -= SyncFromSharedSetting);
        }

        private void SyncFromSharedSetting()
        {
            SetValueWithoutNotify(MBGameplayGizmoVisibility.Visible);
        }
    }
}

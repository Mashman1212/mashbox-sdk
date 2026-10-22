using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class MashBoxStudioViewport
    {
        internal const string LeftWidthKey = "MashBox.Studio.HierarchyWidth";
        internal const string RightWidthKey = "MashBox.Studio.ChannelsWidth";
        internal const string ToolOptionsHeightKey = "MashBox.Studio.ToolOptionsHeight";
        private float preferredToolHeight = 220;
        private IMGUIContainer toolOptionsDivider;
        private int toolResizeControl;
        private bool resizingTools;
        private float toolResizeStartY, toolResizeStartHeight, toolResizeOriginalHeight;
        internal float ToolOptionsHeight => Mathf.Clamp(
            float.IsNaN(preferredToolHeight) || float.IsInfinity(preferredToolHeight) ? 220 : preferredToolHeight,
            80, Mathf.Max(80, StudioContentHeight - ModelingPanelTop - PanelMargin - DividerWidth - 120));
        private const float MinLeft = 180, MinRight = 220, MaxPanel = 600, MinCanvas = 320, DividerWidth = 8;
        private const float PanelMargin = 8;
        private const float PanelCornerRadius = 8;
        private const float ModelingPanelTop = 8;
        internal const float ModeSelectorWidth = 128;
        private float HierarchyLeft => PanelMargin;
        private float PanelLayoutWidth => PanelMargin * 2 + DividerWidth * 2;
        private float preferredLeft = 260, preferredRight = 350;
        private bool panelsInitialized, panelsModeling;
        private IMGUIContainer leftDivider, rightDivider;
        private int resizingSide, resizeControl;
        private float resizeStartX, resizeStartWidth;
        private Vector2 resizeOriginalWidths;

        internal Rect ModelCanvasRect
        {
            get
            {
                var widths = PanelWidths;
                float left = HierarchyLeft + widths.x + DividerWidth;
                float right = StudioContentWidth - PanelMargin - widths.y - DividerWidth;
                return new Rect(left, ModelingPanelTop, Mathf.Max(0, right - left), Mathf.Max(0, StudioContentHeight - ModelingPanelTop - PanelMargin));
            }
        }
        internal Vector2 PanelWidths
        {
            get
            {
                float left = ValidWidth(preferredLeft, 260, MinLeft), right = ValidWidth(preferredRight, 350, MinRight);
                float budget = Mathf.Max(MinLeft + MinRight, StudioContentWidth - PanelLayoutWidth - MinCanvas);
                if (left + right > budget)
                {
                    float extra = left + right - MinLeft - MinRight;
                    float factor = extra > 0 ? Mathf.Clamp01((budget - MinLeft - MinRight) / extra) : 0;
                    left = MinLeft + (left - MinLeft) * factor; right = MinRight + (right - MinRight) * factor;
                }
                return new Vector2(left, right);
            }
        }
        private static float ValidWidth(float value, float fallback, float minimum) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, MaxPanel);
        private void BindPanelResizers(bool modeling)
        {
            panelsModeling = modeling;
            if (!panelsInitialized)
            {
                // Keep existing layouts when upgrading from the former preference keys.
                preferredLeft = ValidWidth(EditorPrefs.GetFloat(LeftWidthKey, EditorPrefs.GetFloat("MashBox.MGStudio.HierarchyWidth", 260)), 260, MinLeft);
                preferredRight = ValidWidth(EditorPrefs.GetFloat(RightWidthKey, EditorPrefs.GetFloat("MashBox.MGStudio.ChannelsWidth", 350)), 350, MinRight);
                preferredToolHeight = EditorPrefs.GetFloat(ToolOptionsHeightKey, 220);
                panelsInitialized = true;
                rootVisualElement.RegisterCallback<GeometryChangedEvent>(_ => UpdatePanelLayout());
            }
            if (leftDivider == null)
            {
                leftDivider = CreatePanelDivider(1, "mashbox-hierarchy-divider");
                rightDivider = CreatePanelDivider(2, "mashbox-channels-divider");
                toolOptionsDivider = new IMGUIContainer(WrapStudioGUI(DrawToolOptionsDivider)) { name = "mashbox-tool-options-divider", tooltip = "Drag to resize Tool Options and Channels" };
                toolOptionsDivider.style.position = Position.Absolute;
                toolOptionsDivider.style.height = DividerWidth;
                rootVisualElement.Add(toolOptionsDivider);
            }
            UpdatePanelLayout(); leftDivider.BringToFront(); rightDivider.BringToFront();
        }
        private IMGUIContainer CreatePanelDivider(int side, string elementName)
        {
            var divider = new IMGUIContainer(WrapStudioGUI(() => DrawPanelDivider(side))) { name = elementName, tooltip = "Drag to resize panel" };
            divider.style.position = Position.Absolute; divider.style.width = DividerWidth;
            rootVisualElement.Add(divider); return divider;
        }
        private void UpdatePanelLayout()
        {
            if (!panelsInitialized) return;
            var widths = PanelWidths;
            bool showTools = panelsModeling && modelToolOptions != null && modelToolOptions.onGUIHandler != null && !StudioSettings.Current.hideToolOptions;
            bool showChannels = panelsModeling && inspector != null && inspector.onGUIHandler != null && inspector.style.display.value != DisplayStyle.None;
            float rightRadius = panelsModeling && !HasRightDockedToolbar ? PanelCornerRadius : 0;
            if (rigControls != null)
            {
                rigControls.style.left = HierarchyLeft; rigControls.style.width = widths.x;
                if (panelsModeling) rigControls.style.bottom = PanelMargin;
                float leftRadius = panelsModeling && !HasLeftDockedToolbar ? PanelCornerRadius : 0;
                rigControls.style.borderTopLeftRadius = rigControls.style.borderBottomLeftRadius = leftRadius;
                rigControls.style.overflow = panelsModeling ? Overflow.Hidden : Overflow.Visible;
            }
            LayoutStatusMessages();
            if (orientationAxes != null)
            {
                orientationAxes.style.left = HierarchyLeft + widths.x + 8;
                orientationAxes.style.bottom = 0;
                orientationAxes.style.width = 64; orientationAxes.style.height = 64;
                orientationAxes.style.display = panelsModeling && !StudioSettings.Current.hideOrientationAxes ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (inspector != null)
            {
                inspector.style.right = PanelMargin; inspector.style.width = widths.y;
                inspector.style.top = showTools ? ModelingPanelTop + ToolOptionsHeight + DividerWidth : ModelingPanelTop;
                // Only the outer right corners of the visible panel stack are rounded.
                inspector.style.borderTopRightRadius = 0;
                inspector.style.borderBottomRightRadius = rightRadius;
                inspector.style.overflow = panelsModeling ? Overflow.Hidden : Overflow.Visible;
            }
            if (modelToolOptions != null)
            {
                modelToolOptions.style.right = PanelMargin; modelToolOptions.style.width = widths.y;
                modelToolOptions.style.top = ModelingPanelTop; modelToolOptions.style.height = ToolOptionsHeight;
                modelToolOptions.style.display = showTools ? DisplayStyle.Flex : DisplayStyle.None;
                modelToolOptions.style.borderTopRightRadius = 0;
                modelToolOptions.style.borderBottomRightRadius = !showChannels ? rightRadius : 0;
                modelToolOptions.style.overflow = Overflow.Hidden;
            }
            if (toolOptionsDivider != null)
            {
                toolOptionsDivider.style.right = PanelMargin; toolOptionsDivider.style.width = widths.y;
                toolOptionsDivider.style.top = ModelingPanelTop + ToolOptionsHeight;
                toolOptionsDivider.style.display = showTools ? DisplayStyle.Flex : DisplayStyle.None;
            }
            float canvasLeft = HierarchyLeft + widths.x + DividerWidth;
            float overlayTop = ModelingPanelTop;
            if (modeSelector != null) { modeSelector.style.left = PanelMargin; modeSelector.style.top = 0; }
            float optionsTop = overlayTop;
            if (viewportOptions != null) { viewportOptions.style.left = canvasLeft + 40; viewportOptions.style.top = optionsTop; }
            if (studioSettings != null) { studioSettings.style.left = canvasLeft + 8; studioSettings.style.top = optionsTop; }
            if (meshStatistics != null) { meshStatistics.style.left = canvasLeft + 8; meshStatistics.style.top = optionsTop; }
            LayoutWorkspaceStatistics(widths.y);
            if (leftDivider == null || rightDivider == null) return;
            leftDivider.style.left = HierarchyLeft + widths.x;
            rightDivider.style.right = PanelMargin + widths.y;
            leftDivider.style.top = rightDivider.style.top = ModelingPanelTop;
            leftDivider.style.bottom = rightDivider.style.bottom = panelsModeling ? 8 : 148;
        }
        private void DrawPanelDivider(int side)
        {
            var divider = side == 1 ? leftDivider : rightDivider;
            var rect = new Rect(0, 0, DividerWidth, divider.contentRect.height);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            var evt = Event.current;
            int id = GUIUtility.GetControlID(side == 1 ? "MashBoxStudio.LeftDivider".GetHashCode() : "MashBoxStudio.RightDivider".GetHashCode(), FocusType.Passive);
            if (evt.type == EventType.Repaint && (rect.Contains(evt.mousePosition) || resizingSide == side))
                EditorGUI.DrawRect(new Rect(3, 0, 2, rect.height), StudioTheme.Current.Border);
            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition) && GUIUtility.hotControl == 0)
            {
                resizingSide = side; resizeControl = id; GUIUtility.hotControl = id; GUIUtility.keyboardControl = 0;
                resizeStartX = GUIUtility.GUIToScreenPoint(evt.mousePosition).x;
                resizeOriginalWidths = new Vector2(preferredLeft, preferredRight);
                resizeStartWidth = side == 1 ? PanelWidths.x : PanelWidths.y;
                evt.Use(); Repaint();
            }
            if (resizingSide != side) return;
            if (GUIUtility.hotControl != resizeControl) { resizingSide = 0; return; }
            if (evt.type == EventType.MouseDrag)
            {
                float delta = GUIUtility.GUIToScreenPoint(evt.mousePosition).x - resizeStartX;
                ResizePanel(side, resizeStartWidth + (side == 1 ? delta : -delta)); evt.Use();
            }
            else if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                SavePanelWidths(); resizingSide = 0; GUIUtility.hotControl = 0; evt.Use(); Repaint();
            }
            else if (StudioShortcuts.Pressed(StudioShortcut.Cancel,evt))
            {
                preferredLeft = resizeOriginalWidths.x; preferredRight = resizeOriginalWidths.y;
                resizingSide = 0; GUIUtility.hotControl = 0; UpdatePanelLayout(); evt.Use(); Repaint();
            }
        }
        internal void ResizePanel(int side, float requestedWidth)
        {
            var widths = PanelWidths;
            float budget = Mathf.Max(MinLeft + MinRight, StudioContentWidth - PanelLayoutWidth - MinCanvas);
            if (side == 1) { preferredRight = widths.y; preferredLeft = Mathf.Clamp(ValidWidth(requestedWidth, 260, MinLeft), MinLeft, Mathf.Min(MaxPanel, budget - widths.y)); }
            else { preferredLeft = widths.x; preferredRight = Mathf.Clamp(ValidWidth(requestedWidth, 350, MinRight), MinRight, Mathf.Min(MaxPanel, budget - widths.x)); }
            UpdatePanelLayout(); Repaint();
        }
        internal void SavePanelWidths()
        {
            EditorPrefs.SetFloat(LeftWidthKey, preferredLeft); EditorPrefs.SetFloat(RightWidthKey, preferredRight);
        }
        internal void ResizeToolOptions(float height)
        {
            preferredToolHeight = height;
            preferredToolHeight = ToolOptionsHeight;
            UpdatePanelLayout(); Repaint();
        }
        private void DrawToolOptionsDivider()
        {
            var rect = new Rect(0, 0, PanelWidths.y, DividerWidth);
            var grip = new Rect(rect.center.x - 24, 0, 48, DividerWidth);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);
            var evt = Event.current;
            int id = GUIUtility.GetControlID("MashBoxStudio.ToolOptionsDivider".GetHashCode(), FocusType.Passive);
            if (evt.type == EventType.Repaint)
            {
                var palette = StudioTheme.Current;
                bool highlighted = resizingTools || rect.Contains(evt.mousePosition);
                EditorGUI.DrawRect(grip, highlighted ? palette.Hover : palette.Button);
                EditorGUI.DrawRect(new Rect(grip.x + 8, grip.center.y - 1, grip.width - 16, 2), highlighted ? palette.Accent : palette.Muted);
            }
            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition) && GUIUtility.hotControl == 0)
            {
                resizingTools = true; toolResizeControl = id; GUIUtility.hotControl = id; GUIUtility.keyboardControl = 0;
                toolResizeStartY = GUIUtility.GUIToScreenPoint(evt.mousePosition).y;
                toolResizeStartHeight = ToolOptionsHeight; toolResizeOriginalHeight = preferredToolHeight;
                evt.Use();
            }
            if (!resizingTools) return;
            if (GUIUtility.hotControl != toolResizeControl) { resizingTools = false; return; }
            if (evt.type == EventType.MouseDrag)
            {
                ResizeToolOptions(toolResizeStartHeight + GUIUtility.GUIToScreenPoint(evt.mousePosition).y - toolResizeStartY);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                EditorPrefs.SetFloat(ToolOptionsHeightKey, preferredToolHeight);
                resizingTools = false; GUIUtility.hotControl = 0; evt.Use(); Repaint();
            }
            else if (StudioShortcuts.Pressed(StudioShortcut.Cancel, evt))
            {
                preferredToolHeight = toolResizeOriginalHeight;
                resizingTools = false; GUIUtility.hotControl = 0; UpdatePanelLayout(); evt.Use(); Repaint();
            }
        }
    }
}

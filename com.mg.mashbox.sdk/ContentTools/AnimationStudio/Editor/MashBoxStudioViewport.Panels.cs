using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class MashBoxStudioViewport
    {
        internal const string LeftWidthKey = "MashBox.Studio.HierarchyWidth";
        internal const string RightWidthKey = "MashBox.Studio.ChannelsWidth";
        private const float MinLeft = 180, MinRight = 220, MaxPanel = 600, MinCanvas = 320, DividerWidth = 8;
        private const float PanelMargin = 8, ModelToolsWidth = 40;
        private const float ModelingPanelTop = 32;
        internal const float ModeSelectorWidth = 128;
        private float HierarchyLeft => PanelMargin + (panelsModeling ? ModelToolsWidth : 0);
        private float PanelLayoutWidth => PanelMargin * 2 + DividerWidth * 2 + (panelsModeling ? ModelToolsWidth : 0);
        private float preferredLeft = 260, preferredRight = 350;
        private bool panelsInitialized, panelsModeling;
        private bool animationSettingsExpanded;
        private IMGUIContainer leftDivider, rightDivider;
        private int resizingSide, resizeControl;
        private float resizeStartX, resizeStartWidth;
        private Vector2 resizeOriginalWidths;

        internal Rect ModelCanvasRect
        {
            get
            {
                var widths = PanelWidths;
                float left = PanelMargin + ModelToolsWidth + widths.x + DividerWidth;
                float right = position.width - PanelMargin - widths.y - DividerWidth;
                return new Rect(left, ModelingPanelTop, Mathf.Max(0, right - left), Mathf.Max(0, position.height - ModelingPanelTop - PanelMargin));
            }
        }
        internal Vector2 PanelWidths
        {
            get
            {
                float left = ValidWidth(preferredLeft, 260, MinLeft), right = ValidWidth(preferredRight, 350, MinRight);
                float budget = Mathf.Max(MinLeft + MinRight, position.width - PanelLayoutWidth - MinCanvas);
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
                panelsInitialized = true;
                rootVisualElement.RegisterCallback<GeometryChangedEvent>(_ => UpdatePanelLayout());
            }
            if (leftDivider == null)
            {
                leftDivider = CreatePanelDivider(1, "mashbox-hierarchy-divider");
                rightDivider = CreatePanelDivider(2, "mashbox-channels-divider");
            }
            UpdatePanelLayout(); leftDivider.BringToFront(); rightDivider.BringToFront();
        }
        private IMGUIContainer CreatePanelDivider(int side, string elementName)
        {
            var divider = new IMGUIContainer(() => DrawPanelDivider(side)) { name = elementName, tooltip = "Drag to resize panel" };
            divider.style.position = Position.Absolute; divider.style.width = DividerWidth;
            rootVisualElement.Add(divider); return divider;
        }
        private void UpdatePanelLayout()
        {
            if (!panelsInitialized) return;
            var widths = PanelWidths;
            if (rigControls != null) { rigControls.style.left = HierarchyLeft; rigControls.style.width = panelsModeling ? widths.x : 398; }
            if (inspector != null)
            {
                inspector.style.right = PanelMargin;
                inspector.style.width = panelsModeling || animationSettingsExpanded ? widths.y : 206;
                inspector.style.bottom = panelsModeling ? new StyleLength(8) : new StyleLength(StyleKeyword.Auto);
                inspector.style.height = panelsModeling ? new StyleLength(StyleKeyword.Auto) : new StyleLength(animationSettingsExpanded ? Mathf.Clamp(position.height - 180, 140, 440) : 38);
                inspector.style.backgroundColor = panelsModeling || animationSettingsExpanded ? StudioTheme.Current.Panel : Color.clear;
            }
            float canvasLeft = HierarchyLeft + (panelsModeling ? widths.x : 398) + DividerWidth;
            float overlayTop = ModelingPanelTop;
            if (attachments != null) { attachments.style.right = PanelMargin + (animationSettingsExpanded ? widths.y + 12 : 0); attachments.style.top = overlayTop + (animationSettingsExpanded ? 0 : 42); }
            if (modeSelector != null) { modeSelector.style.left = PanelMargin; modeSelector.style.top = 0; }
            float optionsTop = overlayTop;
            if (viewportOptions != null) { viewportOptions.style.left = canvasLeft + 40; viewportOptions.style.top = optionsTop; }
            if (studioSettings != null) { studioSettings.style.left = canvasLeft + 8; studioSettings.style.top = optionsTop; }
            if (meshStatistics != null) { meshStatistics.style.left = canvasLeft + 8; meshStatistics.style.top = optionsTop + 32; }
            if (leftDivider == null || rightDivider == null) return;
            leftDivider.style.display = panelsModeling ? DisplayStyle.Flex : DisplayStyle.None;
            rightDivider.style.display = panelsModeling ? DisplayStyle.Flex : DisplayStyle.None;
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
            else if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                preferredLeft = resizeOriginalWidths.x; preferredRight = resizeOriginalWidths.y;
                resizingSide = 0; GUIUtility.hotControl = 0; UpdatePanelLayout(); evt.Use(); Repaint();
            }
        }
        internal void ResizePanel(int side, float requestedWidth)
        {
            var widths = PanelWidths;
            float budget = Mathf.Max(MinLeft + MinRight, position.width - PanelLayoutWidth - MinCanvas);
            if (side == 1) { preferredRight = widths.y; preferredLeft = Mathf.Clamp(ValidWidth(requestedWidth, 260, MinLeft), MinLeft, Mathf.Min(MaxPanel, budget - widths.y)); }
            else { preferredLeft = widths.x; preferredRight = Mathf.Clamp(ValidWidth(requestedWidth, 350, MinRight), MinRight, Mathf.Min(MaxPanel, budget - widths.x)); }
            UpdatePanelLayout(); Repaint();
        }
        internal void SavePanelWidths()
        {
            EditorPrefs.SetFloat(LeftWidthKey, preferredLeft); EditorPrefs.SetFloat(RightWidthKey, preferredRight);
        }
    }
}

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
        private float VisibleToolRailWidth => panelsModeling && modelTools != null && modelTools.onGUIHandler != null ? ModelToolsWidth : 0;
        private float HierarchyLeft => PanelMargin + VisibleToolRailWidth;
        private float PanelLayoutWidth => PanelMargin * 2 + DividerWidth * 2 + VisibleToolRailWidth;
        private float preferredLeft = 260, preferredRight = 350;
        private bool panelsInitialized, panelsModeling;
        private bool animationSettingsExpanded;
        [SerializeField] private bool skinLeftTucked, skinRightTucked;
        [SerializeField] private bool skinInfluencesTucked = true;
        private IMGUIContainer skinInfluencesPanel, skinInfluencesTab;
        [SerializeField] private bool skinFoldDefaultsInitialized;
        private const float SkinPanelTabWidth = 148;
        internal float SkinInspectorWidth => inspector != null && inspector.contentRect.width > 0 ? inspector.contentRect.width : PanelWidths.y;
        internal float SkinToolbarLeft => Mathf.Max(ModelCanvasRect.x, HierarchyLeft + 224 + DividerWidth);
        private IMGUIContainer skinLeftTab, skinRightTab, skinPracticeTab;
        internal void BindSkinPractice(System.Action draw)
        {
            BindViewportControl(ref skinPracticeTab,"mashbox-skin-practice",draw);
            skinPracticeTab.style.width=104;skinPracticeTab.style.height=21;
            UpdatePanelLayout();
        }
        private Vector2 VisiblePanelWidths => new Vector2(FoldablePanels && skinLeftTucked && skinInfluencesTucked ? 24 : PanelWidths.x, FoldablePanels && skinRightTucked ? 24 : PanelWidths.y);
        private IMGUIContainer leftDivider, rightDivider;
        private int resizingSide, resizeControl;
        private float resizeStartX, resizeStartWidth;
        private Vector2 resizeOriginalWidths;

        internal Rect ModelCanvasRect
        {
            get
            {
                var widths = VisiblePanelWidths;
                float left = HierarchyLeft + widths.x + DividerWidth;
                float right = position.width - PanelMargin - widths.y - DividerWidth;
                return new Rect(left, ModelingPanelTop, Mathf.Max(0, right - left), Mathf.Max(0, position.height - ModelingPanelTop - (skinPreviewTimeline ? 56 : PanelMargin)));
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
            if (FoldablePanels && !skinFoldDefaultsInitialized)
            {
                skinLeftTucked = skinRightTucked = true;
                skinFoldDefaultsInitialized = true;
            }
            var widths = VisiblePanelWidths;
            if (rigControls != null) rigControls.style.display = FoldablePanels && skinLeftTucked ? DisplayStyle.None : DisplayStyle.Flex;
            if (inspector != null) inspector.style.display = FoldablePanels && skinRightTucked ? DisplayStyle.None : DisplayStyle.Flex;
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
            float optionsLeft = FoldablePanels ? SkinToolbarLeft : canvasLeft;
            if (viewportOptions != null) { viewportOptions.style.left = optionsLeft + 40; viewportOptions.style.top = optionsTop; }
            if (studioSettings != null) { studioSettings.style.left = optionsLeft + 8; studioSettings.style.top = optionsTop; }
            if (meshStatistics != null) { meshStatistics.style.left = canvasLeft + 8; meshStatistics.style.top = optionsTop + (retopologyPanels ? 62 : 32); }
            if (leftDivider == null || rightDivider == null) return;
            if (FoldablePanels)
            {
                if (rigControls != null) rigControls.style.top = ModelingPanelTop + 28;
                if (inspector != null) inspector.style.top = ModelingPanelTop + 28;
                if (rigControls != null) rigControls.style.bottom = PanelMargin;
                LayoutSkinInfluences(widths);
                if (inspector != null) inspector.style.bottom = PanelMargin;
                if (timeline != null && skinPreviewTimeline)
                {
                    timeline.style.left = canvasLeft + 8;
                    timeline.style.right = PanelMargin + widths.y + DividerWidth + 8;
                }
            }
            if (!FoldablePanels)
            {
                if (skinInfluencesPanel != null) skinInfluencesPanel.style.display = DisplayStyle.None;
                if (rigControls != null) rigControls.style.height = new StyleLength(StyleKeyword.Auto);
            }
            leftDivider.style.display = panelsModeling && !(FoldablePanels && skinLeftTucked && skinInfluencesTucked) ? DisplayStyle.Flex : DisplayStyle.None;
            rightDivider.style.display = panelsModeling && !(FoldablePanels && skinRightTucked) ? DisplayStyle.Flex : DisplayStyle.None;
            leftDivider.style.left = HierarchyLeft + widths.x;
            rightDivider.style.right = PanelMargin + widths.y;
            leftDivider.style.top = rightDivider.style.top = ModelingPanelTop;
            leftDivider.style.bottom = rightDivider.style.bottom = panelsModeling ? 8 : 148;
            UpdateSkinPanelTabs(widths);
        }
        private void UpdateSkinPanelTabs(Vector2 widths)
        {
            if (FoldablePanels && skinLeftTab == null)
            {
                skinLeftTab = CreateSkinPanelTab(true);
                skinRightTab = CreateSkinPanelTab(false);
            }
            if(skinPracticeTab!=null)
            {
                skinPracticeTab.style.display=skinPreviewTimeline||retopologyPanels?DisplayStyle.Flex:DisplayStyle.None;
                skinPracticeTab.style.top=0;
                skinPracticeTab.style.right=PanelMargin;
                skinPracticeTab.BringToFront();
            }
            if (skinLeftTab == null) return;
            if (skinInfluencesTab != null)
            {
                skinInfluencesTab.style.display = skinPreviewTimeline ? DisplayStyle.Flex : DisplayStyle.None;
                skinInfluencesTab.style.left = HierarchyLeft + 112;
                skinInfluencesTab.BringToFront();
            }
            skinLeftTab.style.display = skinRightTab.style.display = FoldablePanels ? DisplayStyle.Flex : DisplayStyle.None;
            skinLeftTab.style.left = HierarchyLeft;
            skinRightTab.style.right = PanelMargin;
            skinLeftTab.BringToFront(); skinRightTab.BringToFront();
        }
        internal void BindSkinInfluences(System.Action draw)
        {
            if (skinInfluencesPanel == null)
            {
                skinInfluencesPanel = new IMGUIContainer { name = "mashbox-skin-influences" };
                skinInfluencesPanel.style.position = Position.Absolute;
                rootVisualElement.Add(skinInfluencesPanel);
                skinInfluencesTab = new IMGUIContainer(StudioTheme.Wrap(() =>
                {
                    var rect = new Rect(0,0,112,24);
                    EditorGUI.DrawRect(rect,new Color(.035f,.04f,.04f));
                    if (!skinInfluencesTucked) EditorGUI.DrawRect(new Rect(5,22,102,2),new Color(.5f,1,.87f));
                    GUI.Label(rect,(skinInfluencesTucked ? "›  " : "‹  ") + "Influences",new GUIStyle(StudioTheme.Styles.label){alignment=TextAnchor.MiddleCenter});
                    if (GUI.Button(rect,new GUIContent("","Show / tuck Influences"),GUIStyle.none))
                    { skinInfluencesTucked=!skinInfluencesTucked; UpdatePanelLayout(); Repaint(); }
                }));
                skinInfluencesTab.style.position=Position.Absolute;
                skinInfluencesTab.style.top=ModelingPanelTop;
                skinInfluencesTab.style.width=112; skinInfluencesTab.style.height=24;
                rootVisualElement.Add(skinInfluencesTab);
            }
            skinInfluencesPanel.onGUIHandler=StudioTheme.Wrap(draw);
            UpdatePanelLayout();
        }
        private void LayoutSkinInfluences(Vector2 widths)
        {
            float top=ModelingPanelTop+28;
            float available=Mathf.Max(80,position.height-top-PanelMargin);
            bool both=!skinLeftTucked && !skinInfluencesTucked;
            float meshHeight=available*.42f;
            if (rigControls != null)
            {
                rigControls.style.height=both ? new StyleLength(meshHeight) : new StyleLength(StyleKeyword.Auto);
                rigControls.style.bottom=both ? new StyleLength(StyleKeyword.Auto) : new StyleLength(PanelMargin);
            }
            if(skinInfluencesPanel == null) return;
            skinInfluencesPanel.style.display=skinInfluencesTucked || retopologyPanels ? DisplayStyle.None : DisplayStyle.Flex;
            skinInfluencesPanel.style.left=HierarchyLeft;
            skinInfluencesPanel.style.width=widths.x;
            skinInfluencesPanel.style.top=both ? top+meshHeight+8 : top;
            skinInfluencesPanel.style.bottom=PanelMargin;
            skinInfluencesPanel.style.backgroundColor=StudioTheme.Current.Panel;
        }
        private IMGUIContainer CreateSkinPanelTab(bool left)
        {
            var tab = new IMGUIContainer(StudioTheme.Wrap(() =>
            {
                bool tucked = left ? skinLeftTucked : skinRightTucked;
                string arrow = left == tucked ? "›" : "‹";
                string title = left ? "Meshes" : retopologyPanels ? (retopologyTutorialGuide ? "Tutorial Guide" : "Topology Tools") : (skinTutorialGuide?"Tutorial Guide":"Brush Settings");
                var rect = new Rect(0,0,left ? 106 : SkinPanelTabWidth,24);
                var content = new GUIContent(left ? arrow + "  " + title : title + "  " + arrow,(tucked ? "Show " : "Tuck ") + title);
                EditorGUI.DrawRect(rect, rect.Contains(Event.current.mousePosition) ? new Color(.10f,.12f,.12f) : new Color(.035f,.04f,.04f));
                if (!tucked) EditorGUI.DrawRect(new Rect(5,22,rect.width-10,2),new Color(.5f,1,.87f));
                GUI.Label(rect,content,new GUIStyle(StudioTheme.Styles.label) { alignment = TextAnchor.MiddleCenter });
                if (GUI.Button(rect,new GUIContent("",content.tooltip),GUIStyle.none))
                {
                    if (left) skinLeftTucked = !skinLeftTucked; else skinRightTucked = !skinRightTucked;
                    UpdatePanelLayout(); Repaint();
                }
            }));
            tab.style.position = Position.Absolute;
            tab.style.top = ModelingPanelTop; tab.style.width = left ? 106 : SkinPanelTabWidth; tab.style.height = 24;
            tab.style.backgroundColor = new Color(.04f,.045f,.045f);
            rootVisualElement.Add(tab); return tab;
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

using ContentTools.PhotoBooth.Editor;
using MashBoxSDK.ContentTools.Editor;
using MashBoxSDK.EditorResources;
using MashBoxSDK.Maching;
using MashBoxSDK.MapTools;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.SDKMain
{
    public class MashBoxSDKWindow : EditorWindow
    {
        private enum MainTab
        {
            Setup,
            ContentTools,
            MapTools
        }

        private enum ContentToolTab
        {
            ContentBuilder,
            TextureReducer,
            PhotoBooth,
            MachEquipManager
        }

        private int _tab;
        private int _contentToolTab;
        private ContentPackBuilderWindow contentBuilderTool;
        private MashBoxMapToolsWindow mapExporterTool;
        private MachEquipManagerWindow machEquipManager;
        private MashBoxSetupWindow setupTool;
        private PhotoBoothWindow photoBoothTool;
        private TextureSizeReducerTool textureSizeReducerTool;
        
        private const string PREF_KEY_TAB = "MashBoxSDK.SelectedTab";
        private const string PREF_KEY_CONTENT_TOOL_TAB = "MashBoxSDK.SelectedContentToolTab";

        // Header
        private Texture2D _headerTex;

        private const float HEADER_MIN = 56f;
        private const float HEADER_MAX = 80f;

        [MenuItem("MashBox/SDK")]
        public static void Open()
        {
            GetWindow<MashBoxSDKWindow>("MashBox SDK");
        }

        private void OnEnable()
        {
            _tab = EditorPrefs.GetInt(PREF_KEY_TAB, 0);
            _contentToolTab = EditorPrefs.GetInt(PREF_KEY_CONTENT_TOOL_TAB, 0);
            _headerTex = AssetDatabase.LoadAssetAtPath<Texture2D>(MashBoxEditorResources.HEADER);
            textureSizeReducerTool?.Initialize();
        }

        private void OnGUI()
        {
#if UNITY_EDITOR
            MashBoxInputSystemSetup.UpdateRequests();
#endif
            DrawHeaderBanner();

            GUILayout.Space(8);

            DrawTabs();

            GUILayout.Space(12);

            switch (_tab)
            {
                case 0: DrawSetupTab(); break;
                case 1: DrawContentToolsTab(); break;
                case 2: DrawMapTab(); break;
            }
        }

        private void OnDisable()
        {
            DestroyTool(ref setupTool);
            DestroyTool(ref contentBuilderTool);
            DestroyTool(ref mapExporterTool);
            DestroyTool(ref machEquipManager);
            DestroyTool(ref photoBoothTool);
            textureSizeReducerTool = null;
        }

        private static T CreateHiddenToolWindow<T>() where T : ScriptableObject
        {
            var instance = CreateInstance<T>();
            instance.hideFlags = HideFlags.HideAndDontSave;
            return instance;
        }

        private static T EnsureHiddenToolWindow<T>(ref T tool) where T : ScriptableObject
        {
            if (tool == null)
                tool = CreateHiddenToolWindow<T>();

            return tool;
        }

        private static void DestroyTool<T>(ref T tool) where T : ScriptableObject
        {
            if (tool != null)
                DestroyImmediate(tool);

            tool = null;
        }

        private TextureSizeReducerTool EnsureTextureSizeReducerTool()
        {
            return textureSizeReducerTool ?? (textureSizeReducerTool = new TextureSizeReducerTool());
        }

        // --------------------------------------------------
        // HEADER
        // --------------------------------------------------

        private void DrawHeaderBanner()
        {
            if (_headerTex == null)
                _headerTex = AssetDatabase.LoadAssetAtPath<Texture2D>(MashBoxEditorResources.HEADER);
            
            
            if (_headerTex == null)
            {
                Debug.LogError($"FAILED TO LOAD: {MashBoxEditorResources.HEADER}");
            }
            
            if (_headerTex == null) return;

            float vw = EditorGUIUtility.currentViewWidth;
            float aspect = (float)_headerTex.height / Mathf.Max(1, _headerTex.width);
            float desiredH = Mathf.Clamp(vw * aspect, HEADER_MIN, HEADER_MAX);

            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Height(desiredH), GUILayout.ExpandWidth(true));

            var card = new Rect(r.x, r.y, r.width, r.height);

            var bg = MashBoxEditorTheme.SubtleBackground(EditorGUIUtility.isProSkin ? 0.14f : 0.08f);

            EditorGUI.DrawRect(card, bg);
            GUI.DrawTexture(card, _headerTex, ScaleMode.ScaleAndCrop, true);

            // Divider
            EditorGUI.DrawRect(
                new Rect(card.x, card.yMax, card.width, 1f),
                MashBoxEditorTheme.SelectedBorder()
            );
        }

        // --------------------------------------------------
        // TABS
        // --------------------------------------------------

        private void DrawTabs()
        {
            int newTab = MashBoxTabDrawer.DrawTabs(_tab, new[]
            {
                "Setup",
                "Content Tools",
                "Map Tools"
            }, MashBoxTabDrawer.TabVisualStyle.Primary, new[]
            {
#if UNITY_EDITOR
                MashBoxSDKState.UpdateAvailable || MashBoxInputSystemSetup.ShouldShowSetupAlert,
#else
                MashBoxSDKState.UpdateAvailable,
#endif
                false,
                false
            });

            if (newTab != _tab)
            {
                if (_tab == (int)MainTab.MapTools && newTab != (int)MainTab.MapTools)
                    mapExporterTool?.DeactivateEmbeddedSceneTools();

                _tab = newTab;
                EditorPrefs.SetInt(PREF_KEY_TAB, _tab);
            }
        }

        private void DrawTabButton(string label, int index)
        {
            bool isActive = _tab == index;

            var style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(12, 12, 6, 6),
                normal =
                {
                    textColor = isActive
                        ? Color.white
                        : (EditorGUIUtility.isProSkin ? new Color(0.7f, 0.7f, 0.7f) : Color.black)
                }
            };

            var bg = isActive
                ? (EditorGUIUtility.isProSkin
                    ? new Color(0.25f, 0.45f, 0.85f) 
                    : new Color(0.3f, 0.5f, 0.9f))
                : Color.clear;

            Rect r = GUILayoutUtility.GetRect(new GUIContent(label), style, GUILayout.ExpandWidth(true));

            if (bg.a > 0)
                EditorGUI.DrawRect(r, bg);

            if (GUI.Button(r, label, style))
            {
                if (_tab != index)
                {
                    _tab = index;
                    EditorPrefs.SetInt(PREF_KEY_TAB, _tab);
                }
            }
        }
        
        // --------------------------------------------------
        // STUP TAB
        // --------------------------------------------------

        
        private void DrawSetupTab()
        {
            EnsureHiddenToolWindow(ref setupTool).Draw();
        }
        
        // --------------------------------------------------
        // CONTENT TAB
        // --------------------------------------------------

        private void DrawContentTab()
        {
            EnsureHiddenToolWindow(ref contentBuilderTool).Draw();
        }

        private void DrawContentToolsTab()
        {
            int newTab = MashBoxTabDrawer.DrawTabs(_contentToolTab, new[]
            {
                "Content Builder",
                "Texture Reducer",
                "Photo Booth",
                "Mach Equip Manager"
            }, MashBoxTabDrawer.TabVisualStyle.Secondary);

            if (newTab != _contentToolTab)
            {
                _contentToolTab = newTab;
                EditorPrefs.SetInt(PREF_KEY_CONTENT_TOOL_TAB, _contentToolTab);
            }

            GUILayout.Space(8f);

            switch ((ContentToolTab)_contentToolTab)
            {
                case ContentToolTab.ContentBuilder:
                    DrawContentTab();
                    break;
                case ContentToolTab.TextureReducer:
                    EnsureTextureSizeReducerTool().Draw();
                    break;
                case ContentToolTab.PhotoBooth:
                    DrawPhotoBoothTab();
                    break;
                case ContentToolTab.MachEquipManager:
                    DrawMachEquipManager();
                    break;
            }
        }
        
        // --------------------------------------------------
        // PHOTO BOOTH TAB
        // --------------------------------------------------
        
        void DrawPhotoBoothTab()
        {
            EnsureHiddenToolWindow(ref photoBoothTool).Draw();
        }
        
        // --------------------------------------------------
        // Mach Equip Manager TAB
        // --------------------------------------------------

        private void DrawMachEquipManager()
        {
            EnsureHiddenToolWindow(ref machEquipManager).Draw();
        }

        // --------------------------------------------------
        // MAP TAB
        // --------------------------------------------------

        private void DrawMapTab()
        {
            EnsureHiddenToolWindow(ref mapExporterTool).Draw();
        }
    }

    internal static class MashBoxTabDrawer
    {
        internal enum TabVisualStyle
        {
            Primary,
            Secondary
        }

        public static int DrawTabs(int selectedIndex, string[] labels, TabVisualStyle style, bool[] alertFlags = null)
        {
            var containerRect = GUILayoutUtility.GetRect(0f, GetHeight(style), GUILayout.ExpandWidth(true));
            containerRect = new Rect(containerRect.x, containerRect.y, containerRect.width, GetHeight(style));

            DrawBackground(containerRect, style);

            float spacing = style == TabVisualStyle.Primary ? 8f : 6f;
            float totalSpacing = spacing * (labels.Length - 1);
            float tabWidth = (containerRect.width - totalSpacing) / labels.Length;

            for (int i = 0; i < labels.Length; i++)
            {
                var tabRect = new Rect(
                    containerRect.x + i * (tabWidth + spacing),
                    containerRect.y,
                    tabWidth,
                    containerRect.height);

                var showAlert = alertFlags != null && i < alertFlags.Length && alertFlags[i];
                if (DrawTabButton(tabRect, labels[i], i == selectedIndex, style, showAlert))
                    selectedIndex = i;
            }

            return selectedIndex;
        }

        private static bool DrawTabButton(Rect rect, string label, bool isActive, TabVisualStyle style, bool showAlert)
        {
            var isHovered = rect.Contains(Event.current.mousePosition);
            var bg = GetButtonBackground(style, isActive, isHovered);
            var border = GetButtonBorder(style, isActive, isHovered);
            var textColor = GetTextColor(style, isActive, isHovered);
            var shadow = EditorGUIUtility.isProSkin
                ? new Color(0f, 0f, 0f, isActive ? 0.3f : 0.18f)
                : new Color(0f, 0f, 0f, isActive ? 0.12f : 0.08f);

            var shadowRect = new Rect(rect.x, rect.y + 1f, rect.width, rect.height);
            EditorGUI.DrawRect(shadowRect, shadow);
            EditorGUI.DrawRect(rect, bg);
            DrawBorder(rect, border);

            if (isActive && style == TabVisualStyle.Primary)
            {
                    EditorGUI.DrawRect(
                    new Rect(rect.x + 10f, rect.yMax - 3f, rect.width - 20f, 2f),
                    MashBoxEditorTheme.WithAlpha(MashBoxEditorTheme.Current.Accent, 0.95f));
            }

            var textStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal,
                fontSize = style == TabVisualStyle.Primary ? 12 : 11
            };
            textStyle.normal.textColor = textColor;

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                return true;

            GUI.Label(rect, label, textStyle);

            if (showAlert)
                DrawAlertIndicator(rect, style);

            return false;
        }

        private static void DrawAlertIndicator(Rect rect, TabVisualStyle style)
        {
            var size = style == TabVisualStyle.Primary ? 10f : 8f;
            var padding = style == TabVisualStyle.Primary ? 10f : 8f;
            var dotRect = new Rect(
                rect.xMax - size - padding,
                rect.y + (rect.height - size) * 0.5f,
                size,
                size);

            EditorGUI.DrawRect(dotRect, new Color(0.88f, 0.2f, 0.2f, 1f));
            DrawBorder(dotRect, EditorGUIUtility.isProSkin ? new Color(1f, 0.8f, 0.8f, 0.7f) : new Color(0.45f, 0.05f, 0.05f, 0.6f));
        }

        private static void DrawBackground(Rect rect, TabVisualStyle style)
        {
            var bg = style == TabVisualStyle.Primary
                ? MashBoxEditorTheme.SubtleBackground(EditorGUIUtility.isProSkin ? 0.16f : 0.08f)
                : MashBoxEditorTheme.SubtleBackground(EditorGUIUtility.isProSkin ? 0.10f : 0.05f);

            EditorGUI.DrawRect(rect, bg);
            DrawBorder(rect, MashBoxEditorTheme.Border(false));
        }

        private static void DrawBorder(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        private static float GetHeight(TabVisualStyle style)
        {
            return style == TabVisualStyle.Primary ? 34f : 26f;
        }

        private static Color GetButtonBackground(TabVisualStyle style, bool isActive, bool isHovered)
        {
            if (style == TabVisualStyle.Primary)
            {
                if (isActive)
                    return MashBoxEditorTheme.Primary();

                if (isHovered)
                    return MashBoxEditorTheme.Primary(true);

                return MashBoxEditorTheme.SubtleBackground(EditorGUIUtility.isProSkin ? 0.36f : 0.18f);
            }

            if (isActive)
                return MashBoxEditorTheme.Secondary();

            if (isHovered)
                return MashBoxEditorTheme.Secondary(true);

            return MashBoxEditorTheme.SubtleBackground(EditorGUIUtility.isProSkin ? 0.28f : 0.14f);
        }

        private static Color GetButtonBorder(TabVisualStyle style, bool isActive, bool isHovered)
        {
            if (isActive)
                return MashBoxEditorTheme.Border(true);

            if (isHovered)
                return MashBoxEditorTheme.Border(false);

            return EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.10f);
        }

        private static Color GetTextColor(TabVisualStyle style, bool isActive, bool isHovered)
        {
            return MashBoxEditorTheme.Text(isActive, isHovered);
        }
    }
}

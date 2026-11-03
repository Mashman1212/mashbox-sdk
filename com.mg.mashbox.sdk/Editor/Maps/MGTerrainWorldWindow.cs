#if UNITY_EDITOR
using System;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    /// <summary>Persistent scene-world workspace; selecting paint sources does not lose the target.</summary>
    public sealed class MGTerrainWorldWindow : EditorWindow
    {
        [SerializeField] MGTerrainWorld m_World;
        [SerializeField] bool m_Locked;
        [SerializeField] string m_WorldScenePath;
        [SerializeField] string m_WorldHierarchy;
        double m_NextWorldRefresh;
        bool m_PointerOverWindow;
        [SerializeField] int m_Page;
        [SerializeField] Vector2 m_Scroll;
        MGTerrainWorldEditor m_Editor;
        MGTerrainWorld[] m_Worlds = Array.Empty<MGTerrainWorld>();
        static readonly string[] Pages = { "Tiles", "Details", "Rendering", "Baking", "Collision", "Diagnostics" };
        static readonly string[] Descriptions = {
            "Build your world, extend its edges, and manage terrain tiles.",
            "Paint detail density, scale, and detail variation across your world.",
            "Balance surface quality, foliage distance, and streaming cost.",
            "Capture world maps and prepare distant terrain meshes.",
            "Build and manage the physics surfaces used by your terrain.",
            "Inspect Scene camera LOD and estimate world data costs." };

        [MenuItem("Tools/MashBox/MG Terrain/Terrain World", false, 0)]
        public static void Open() => Open(null);

        public static void Open(MGTerrainWorld world)
        {
            bool alreadyOpen = HasOpenInstances<MGTerrainWorldWindow>();
            var window = GetWindow<MGTerrainWorldWindow>();
            window.titleContent = new GUIContent("Terrain World", EditorGUIUtility.IconContent("Terrain Icon").image);
            window.minSize = new Vector2(440, 360);
            if (!alreadyOpen)
            {
                Rect main = EditorGUIUtility.GetMainWindowPosition();
                window.position = new Rect(main.center.x - 480, main.center.y - 350, 960, 700);
            }
            if (world != null) window.SetWorld(world);
            window.Show();
            window.Focus();
        }

        void OnEnable()
        {
#if UNITY_6000_0_OR_NEWER
            wantsMouseEnterLeaveWindow = true;
            titleContent = new GUIContent("Terrain World", EditorGUIUtility.IconContent("Terrain Icon").image);
            minSize = new Vector2(440, 360);
            m_Page = Mathf.Clamp(m_Page, 0, Pages.Length - 1);
            Selection.selectionChanged += FollowSelection;
            EditorApplication.hierarchyChanged += RefreshWorlds;
            Undo.undoRedoPerformed += Repaint;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += RefreshWorlds;
            RefreshWorlds();
            FollowSelection();
#endif
        }

        void OnDisable()
        {
            Selection.selectionChanged -= FollowSelection;
            EditorApplication.hierarchyChanged -= RefreshWorlds;
            Undo.undoRedoPerformed -= Repaint;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.delayCall -= RefreshWorlds;
            ReleaseEditor();
        }

        void OnInspectorUpdate()
        {
            RefreshBrushHover();
            // OnEnable can run before scene objects finish loading after a domain/scene reload.
            if (!IsSceneWorld(m_World) && EditorApplication.timeSinceStartup >= m_NextWorldRefresh)
            {
                m_NextWorldRefresh = EditorApplication.timeSinceStartup + 1;
                RefreshWorlds();
            }
            Repaint();
        }

        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
            {
                RememberWorld();
                ReleaseEditor();
                m_World = null;
                return;
            }
            RefreshWorlds();
            FollowSelection();
        }

        static string HierarchyKey(Transform transform)
        {
            string key = transform.GetSiblingIndex().ToString();
            while (transform.parent != null)
            {
                transform = transform.parent;
                key = transform.GetSiblingIndex() + "/" + key;
            }
            return key;
        }

        void RememberWorld()
        {
            if (!IsSceneWorld(m_World)) return;
            m_WorldScenePath = m_World.gameObject.scene.path;
            m_WorldHierarchy = HierarchyKey(m_World.transform);
        }

        void RefreshWorlds()
        {
            m_Worlds = Resources.FindObjectsOfTypeAll<MGTerrainWorld>()
                .Where(IsSceneWorld).OrderBy(w => w.gameObject.scene.name).ThenBy(w => w.name).ToArray();
            if (!IsSceneWorld(m_World))
            {
                var remembered = string.IsNullOrEmpty(m_WorldHierarchy) ? null : m_Worlds.FirstOrDefault(world =>
                    world.gameObject.scene.path == m_WorldScenePath && HierarchyKey(world.transform) == m_WorldHierarchy);
                var selected = !m_Locked && Selection.activeGameObject != null
                    ? Selection.activeGameObject.GetComponentInParent<MGTerrainWorld>(true) : null;
                if (remembered != null) SetWorld(remembered);
                else if (IsSceneWorld(selected)) SetWorld(selected);
                else if (m_Worlds.Length == 1) SetWorld(m_Worlds[0]);
            }
            Repaint();
        }

        static bool IsSceneWorld(MGTerrainWorld world) => world != null && !EditorUtility.IsPersistent(world)
            && world.gameObject.scene.IsValid() && world.gameObject.scene.isLoaded
            && !UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(world.gameObject.scene);

        void FollowSelection()
        {
            if (EditorApplication.isPlaying != EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!m_Locked && Selection.activeGameObject != null)
            {
                var world = Selection.activeGameObject.GetComponentInParent<MGTerrainWorld>(true);
                if (IsSceneWorld(world)) SetWorld(world);
            }
            Repaint();
        }

        void ReleaseEditor()
        {
            if (m_Editor != null) DestroyImmediate(m_Editor);
            m_Editor = null;
            SceneView.RepaintAll();
        }

        void SetWorld(MGTerrainWorld world)
        {
            if (world == m_World) { RememberWorld(); return; }
            ReleaseEditor();
            m_World = world;
            RememberWorld();
            m_Scroll = Vector2.zero;
            Repaint();
        }

        void RefreshBrushHover()
        {
            bool over = mouseOverWindow == this;
            if (over == m_PointerOverWindow) return;
            m_PointerOverWindow = over;
            SceneView.RepaintAll();
        }

        void OnGUI()
        {
#if UNITY_6000_0_OR_NEWER
            if (Event.current.type == EventType.MouseEnterWindow || Event.current.type == EventType.MouseLeaveWindow)
            {
                RefreshBrushHover();
                SceneView.RepaintAll();
            }
            DrawTargetBar();
            if (!IsSceneWorld(m_World)) { ReleaseEditor(); DrawEmptyState(); return; }
            if (m_Editor != null && m_Editor.target != m_World) ReleaseEditor();
            if (m_Editor == null)
            {
                m_Editor = (MGTerrainWorldEditor)UnityEditor.Editor.CreateEditor(m_World, typeof(MGTerrainWorldEditor));
                m_Editor.SetWorkspacePage(m_Page);
            }
            DrawNavigation();
            GUILayout.Space(4);
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            try
            {
                if (!m_World.isActiveAndEnabled)
                    EditorGUILayout.HelpBox("This world is disabled. Enable it to use shared rendering and world painting.", MessageType.Info);
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    EditorGUILayout.HelpBox("Play Mode: settings changed here may be reset when you leave Play Mode.", MessageType.Info);
                m_Editor.DrawWorkspace();
            }
            finally { EditorGUILayout.EndScrollView(); }
            GUILayout.Space(4);
            EditorGUILayout.LabelField("Scene: " + m_World.gameObject.scene.name + "   •   " + (m_Locked ? "Target locked" : "Follows terrain selection"), EditorStyles.miniLabel);
#else
            EditorGUILayout.HelpBox("MG Terrain requires Unity 6 or newer.", MessageType.Info);
#endif
        }

        void DrawTargetBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(new GUIContent(m_World != null ? m_World.name : "Choose terrain world", "Choose a terrain world from the loaded scenes."), EditorStyles.toolbarDropDown))
                {
                    RefreshWorlds();
                    var menu = new GenericMenu();
                    foreach (var world in m_Worlds)
                    {
                        var choice = world;
                        menu.AddItem(new GUIContent(world.gameObject.scene.name + "/" + world.name + " [" + (Array.IndexOf(m_Worlds, world) + 1) + "]"), world == m_World, () => SetWorld(choice));
                    }
                    if (m_Worlds.Length == 0) menu.AddDisabledItem(new GUIContent("No terrain worlds in loaded scenes"));
                    menu.AddSeparator("");
                    if (EditorApplication.isPlayingOrWillChangePlaymode) menu.AddDisabledItem(new GUIContent("Create Terrain World"));
                    else menu.AddItem(new GUIContent("Create Terrain World"), false, CreateWorld);
                    menu.ShowAsContext();
                }
                m_Locked = MGTerrainToolbarIcons.Toggle(m_Locked, m_Locked ? MGTerrainToolbarIcons.Icon.Locked : MGTerrainToolbarIcons.Icon.Unlocked, "Lock world selection");
                using (new EditorGUI.DisabledScope(m_World == null))
                {
                    if (MGTerrainToolbarIcons.Button(MGTerrainToolbarIcons.Icon.Select, "Select this world in the Hierarchy")) Selection.activeGameObject = m_World.gameObject;
                    if (MGTerrainToolbarIcons.Button(MGTerrainToolbarIcons.Icon.Frame, "Frame this world in the Scene view")) FrameWorld();
                }
                GUILayout.Space(5);
                MGTerrainGizmos.DrawToolbar();
            }
        }

        GUIStyle m_TabStyle;

        void DrawNavigation()
        {
            m_TabStyle ??= new GUIStyle(EditorStyles.miniButton) { fixedHeight = 0, stretchHeight = true };
            // Icon-only navigation fits in one row at every supported dock width.
            int columns = Pages.Length;
            for (int row = 0; row < Pages.Length / columns; row++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int i = row * columns; i < (row + 1) * columns; i++)
                    {
                        Rect rect = GUILayoutUtility.GetRect(0, 38, GUILayout.Height(38), GUILayout.ExpandWidth(true));
                        bool selected = m_Page == i;
                        if (GUI.Toggle(rect, selected, new GUIContent("", Pages[i] + ": " + Descriptions[i]), m_TabStyle) && !selected)
                        {
                            m_Page = i;
                            m_Scroll = Vector2.zero;
                            m_Editor.SetWorkspacePage(i);
                            GUIUtility.ExitGUI();
                        }
                        if (Event.current.type == EventType.Repaint)
                        {
                            DrawNavigationIcon(new Rect(rect.center.x - 10, rect.center.y - 10, 20, 20), i, selected);
                            if (selected) EditorGUI.DrawRect(new Rect(rect.x + 1, rect.yMax - 3, rect.width - 2, 2), new Color(.35f, .75f, 1f));
                        }
                    }
                }
            }
        }

        // Vector glyphs retain sharp edges at editor DPI and use the active editor theme.
        static void DrawNavigationIcon(Rect rect, int page, bool selected)
        {
            Color previous = Handles.color;
            Handles.BeginGUI();
            try
            {
                Handles.color = selected ? new Color(.35f, .8f, 1f)
                    : EditorGUIUtility.isProSkin ? new Color(.82f, .86f, .9f) : new Color(.22f, .28f, .34f);
                void Line(params Vector2[] points) => Handles.DrawAAPolyLine(1.8f,
                    points.Select(p => new Vector3(rect.x + p.x, rect.y + p.y)).ToArray());
                Vector2 P(float x, float y) => new Vector2(x, y);
                switch (page)
                {
                    case 0: // Neighbouring terrain tiles.
                        for (int n = 0; n < 3; n++)
                        {
                            float t = 2 + n * 8;
                            Line(P(2, t), P(18, t));
                            Line(P(t, 2), P(t, 18));
                        }
                        break;
                    case 1: // Brush and its painted tip.
                        Line(P(7, 12), P(15, 2), P(18, 5), P(10, 15), P(7, 12));
                        Line(P(7, 12), P(3, 13), P(2, 18), P(7, 18), P(10, 15));
                        Line(P(13, 5), P(16, 8));
                        break;
                    case 2: // Terrain surface and sun.
                        Line(P(1, 17), P(7, 6), P(12, 13), P(15, 9), P(19, 17), P(1, 17));
                        Line(P(5, 10), P(7, 12), P(9, 9));
                        Handles.DrawWireDisc(new Vector3(rect.x + 15, rect.y + 4), Vector3.forward, 2);
                        break;
                    case 3: // Capture tile with rising heat.
                        Line(P(2, 13), P(10, 9), P(18, 13), P(10, 18), P(2, 13));
                        for (int n = 0; n < 3; n++)
                        {
                            float x = 5 + n * 5;
                            Line(P(x, 7), P(x - 1, 5), P(x, 2));
                        }
                        break;
                    case 4: // Collision shield.
                        Line(P(10, 1), P(18, 4), P(17, 12), P(14, 16), P(10, 19), P(6, 16), P(3, 12), P(2, 4), P(10, 1));
                        Line(P(6, 10), P(9, 13), P(14, 7));
                        break;
                    case 5: // Performance graph.
                        Line(P(2, 2), P(2, 18), P(19, 18));
                        Line(P(5, 14), P(9, 9), P(12, 12), P(18, 4));
                        Line(P(14, 4), P(18, 4), P(18, 8));
                        break;
                }
            }
            finally { Handles.color = previous; Handles.EndGUI(); }
        }


        void DrawEmptyState()
        {
            GUILayout.Space(30);
            EditorGUILayout.LabelField("Build a terrain world", new GUIStyle(EditorStyles.boldLabel) { fontSize = 22 }, GUILayout.Height(32));
            EditorGUILayout.LabelField("Choose a world above, select one in the Hierarchy, or create a world to begin. Existing converted MG Terrain tiles can be adopted without copying their terrain data.", EditorStyles.wordWrappedLabel);
            GUILayout.Space(20);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Create Terrain World", GUILayout.Height(36))) CreateWorld();
                var chunks = Selection.gameObjects.SelectMany(go => go.GetComponentsInChildren<MGTerrain>(true)).Distinct().ToArray();
                using (new EditorGUI.DisabledScope(chunks.Length == 0))
                    if (GUILayout.Button("Create From Selected MG Terrain Tiles", GUILayout.Height(30)))
                    {
                        try { SetWorld(MGTerrainWorldEditor.Adopt(chunks)); }
                        catch (Exception error) { EditorUtility.DisplayDialog("Cannot adopt terrain tiles", error.Message, "OK"); }
                    }
            }
        }

        void CreateWorld()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var go = new GameObject("MG Terrain World");
            Undo.RegisterCreatedObjectUndo(go, "Create Terrain World");
            SetWorld(Undo.AddComponent<MGTerrainWorld>(go));
            Selection.activeGameObject = go;
            RefreshWorlds();
        }

        void FrameWorld()
        {
            var renderers = m_World.GetComponentsInChildren<MeshRenderer>(true);
            var bounds = new Bounds(m_World.transform.position, Vector3.one);
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            }
            SceneView.lastActiveSceneView?.Frame(bounds, false);
        }
    }
}
#endif

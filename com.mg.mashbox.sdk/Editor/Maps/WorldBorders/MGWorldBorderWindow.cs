using System;
using System.Linq;
using MashBoxSDK.Maps;
using MashBoxSDK.SDKMain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools.WorldBorders
{
    public sealed class MGWorldBorderWindow : EditorWindow
    {
        Vector2 scroll;
        MBWorldBorderWall[] walls = Array.Empty<MBWorldBorderWall>();
        UnityEditor.Editor inspector;
        string message;
        [MenuItem("MashBox/Map Tools/World Border Tool")]
        [MenuItem("Tools/MashBox/World Border Tool")]
        public static void Open() => MashBoxSDKWindow.OpenWorldBorders();

        void OnEnable()
        {
            Selection.selectionChanged += Refresh;
            EditorApplication.hierarchyChanged += Refresh;
            EditorSceneManager.activeSceneChangedInEditMode += SceneChanged;
            Undo.undoRedoPerformed += Refresh;
            Refresh();
        }
        void OnDisable()
        {
            Selection.selectionChanged -= Refresh;
            EditorApplication.hierarchyChanged -= Refresh;
            EditorSceneManager.activeSceneChangedInEditMode -= SceneChanged;
            Undo.undoRedoPerformed -= Refresh;
            if (inspector) DestroyImmediate(inspector);
        }
        void SceneChanged(Scene before, Scene after) => Refresh();
        void Refresh()
        {
            var scene = SceneManager.GetActiveScene();
            walls = scene.IsValid() && scene.isLoaded ? scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MBWorldBorderWall>(true)).ToArray() : Array.Empty<MBWorldBorderWall>();
            Repaint();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
        internal void Load(MGWorldBorderProfile profile)
        {
            var root = MGWorldBorderBuilder.ResolveRoot(profile);
            if (root) Selection.activeGameObject = root;
        }
        public void SetEmbeddedActive(bool active) { }
        public void DrawEmbedded() => DrawGUI();
        void OnGUI() => DrawGUI();

        void DrawGUI()
        {
            var hero = GUILayoutUtility.GetRect(0, 76, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(hero, MashBoxEditorTheme.SelectedFill());
            EditorGUI.DrawRect(new Rect(hero.x, hero.y, 4, hero.height), MashBoxEditorTheme.Current.Accent);
            GUI.Label(new Rect(hero.x+18, hero.y+12, hero.width-30, 25), "WORLD BORDER PROXIES", new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 });
            GUI.Label(new Rect(hero.x+18, hero.y+43, hero.width-30, 22), "Place the walls. The map builds them at runtime.", EditorStyles.miniLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            GUILayout.Space(10);
            EditorGUILayout.HelpBox("Use Move, Rotate and Scale on each proxy, just like challenge gates. X = width, Y = height, Z = thickness. Duplicate walls with Ctrl+D. There are no baked visual tiles in the authored scene.", MessageType.Info);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("ADD WALL PROXY", GUILayout.Height(34))) Run(() =>
                    {
                        var centre = SceneView.lastActiveSceneView ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
                        var wall = MBWorldBorderAuthoring.CreateWall(MBWorldBorderAuthoring.Group(), centre + Vector3.up * 64, Quaternion.identity, new Vector3(128,128,.5f));
                        Selection.activeGameObject = wall.gameObject;
                        EditorSceneManager.MarkSceneDirty(wall.gameObject.scene);
                    });
                    if (GUILayout.Button("CREATE FOUR WALLS", GUILayout.Height(34))) Run(() =>
                    {
                        var centre = SceneView.lastActiveSceneView ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
                        MBWorldBorderAuthoring.CreateRectangle(centre);
                    });
                }
                EditorGUILayout.LabelField("Four-wall starter: 1,024 m square · 128 m high · 32 runtime tiles", EditorStyles.wordWrappedMiniLabel);
                if (MBWorldBorderAuthoring.CanConvert(Selection.activeGameObject) && GUILayout.Button("Convert selected baked border to proxies", GUILayout.Height(28)))
                    Run(() => MBWorldBorderAuthoring.ConvertBakedBorder(Selection.activeGameObject));
            }
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            GUILayout.Space(12);
            EditorGUILayout.LabelField("WALLS IN THIS SCENE", EditorStyles.boldLabel);
            if (walls.Length == 0) EditorGUILayout.LabelField("Add a wall, then place and scale its proxy.");
            foreach (var wall in walls)
            {
                if (!wall) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(wall, typeof(MBWorldBorderWall), true);
                    GUILayout.Label(wall.TileCount + " tiles", GUILayout.Width(68));
                    if (GUILayout.Button("Select", GUILayout.Width(56))) Selection.activeGameObject = wall.gameObject;
                }
            }
            var selected = Selection.gameObjects.Select(g => g.GetComponent<MBWorldBorderWall>()).Where(w => w).Cast<UnityEngine.Object>().ToArray();
            if (selected.Length > 0)
            {
                GUILayout.Space(14);
                EditorGUILayout.LabelField("SELECTED WALL SETTINGS", EditorStyles.boldLabel);
                UnityEditor.Editor.CreateCachedEditor(selected, typeof(MBWorldBorderWallEditor), ref inspector);
                inspector.OnInspectorGUI();
            }
            GUILayout.Space(14);
            EditorGUILayout.HelpBox("At map load each enabled proxy creates one collision wall and distance-culled visual tiles using the SDK hologram artwork. Culling does not disable collision. Start with 128 m tiles and profile in-game. The game must include this SDK version.", MessageType.None);
            EditorGUILayout.EndScrollView();
        }
        void Run(Action action)
        {
            try { action(); message = null; Refresh(); }
            catch (Exception e) { message = e.Message; Debug.LogException(e); }
        }
    }
}

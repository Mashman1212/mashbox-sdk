#if UNITY_EDITOR
using System;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    [CustomEditor(typeof(MGTerrainWorld))]
    public sealed partial class MGTerrainWorldEditor : Editor
    {
        MGTerrain m_EditChunk;
        Editor m_ChunkEditor;
        bool m_ShowChunk;

        [MenuItem("GameObject/MashBox/MG Terrain World From Selected Chunks", false, 20)]
        static void CreateFromSelection()
        {
            var chunks = SelectedChunks();
            if (chunks.Length == 0)
            {
                EditorUtility.DisplayDialog("MG Terrain World", "Select existing converted MG Terrain chunks, or their parent objects.", "OK");
                return;
            }
            try { Selection.activeGameObject = Adopt(chunks).gameObject; }
            catch (Exception error) { Debug.LogException(error); }
        }

        static MGTerrain[] SelectedChunks() => Selection.gameObjects
            .SelectMany(go => go.GetComponentsInChildren<MGTerrain>(true)).Distinct().ToArray();

        // Does not serialize/copy terrain data: reparenting preserves the exact existing component and assets.
        internal static MGTerrainWorld Adopt(MGTerrain[] chunks, MGTerrainWorld destination = null)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Adopt chunks outside Play Mode.");
            chunks = chunks.Where(chunk => chunk != null).Distinct().ToArray();
            if (chunks.Length == 0) throw new ArgumentException("No MG Terrain chunks were selected.");
            Scene scene = chunks[0].gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded || chunks.Any(chunk => chunk.gameObject.scene != scene)
                || destination != null && destination.gameObject.scene != scene)
                throw new InvalidOperationException("Adopt chunks within one loaded scene. Separate scene chunks can register under their own world after loading.");
            foreach (var chunk in chunks)
            {
                if (EditorUtility.IsPersistent(chunk) || PrefabUtility.IsPartOfPrefabInstance(chunk))
                    throw new InvalidOperationException("Unpack the selected terrain prefab instance before reparenting it.");
                if (chunks.Any(other => other != chunk && chunk.transform.IsChildOf(other.transform)))
                    throw new InvalidOperationException("Terrain chunks must not be nested inside one another.");
                if (destination != null && destination.transform.IsChildOf(chunk.transform))
                    throw new InvalidOperationException("The destination cannot be inside an adopted chunk.");
                Vector3 scale = chunk.transform.lossyScale;
                // An identity root preserves translation/rotation/scale, but cannot represent inherited shear.
                var matrix = chunk.transform.localToWorldMatrix;
                Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
                if (scale.x <= 0 || scale.y <= 0 || scale.z <= 0
                    || Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > .0001f
                    || Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) > .0001f
                    || Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) > .0001f)
                    throw new InvalidOperationException("Remove mirrored or sheared parent transforms before adopting terrain chunks.");
            }
            if (destination != null && (destination.transform.rotation != Quaternion.identity
                || destination.transform.lossyScale != Vector3.one))
                throw new InvalidOperationException("Use a terrain world with identity world rotation and unit scale for adoption.");

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Adopt MG Terrain Chunks");
            try
            {
                if (destination == null)
                {
                    var root = new GameObject("MG Terrain World");
                    SceneManager.MoveGameObjectToScene(root, scene);
                    Undo.RegisterCreatedObjectUndo(root, "Create Terrain World");
                    destination = Undo.AddComponent<MGTerrainWorld>(root);
                    // Start with the first selected terrain's density-distance and instance settings.
                    using var source = new SerializedObject(chunks[0]);
                    using var settings = new SerializedObject(destination);
                    int budget = source.FindProperty("m_MaxVisibleDenseDetailInstances").intValue;
                    float distance = source.FindProperty("m_MaxDensityDetailDistance").floatValue;
                    if (budget > 0) settings.FindProperty("m_VisibleDetailBudget").intValue = budget;
                    if (distance > 0) settings.FindProperty("m_DetailDistance").floatValue = distance;
                    settings.ApplyModifiedProperties();
                }
                foreach (var chunk in chunks)
                    Undo.SetTransformParent(chunk.transform, destination.transform, "Adopt Terrain Chunk");
                destination.RefreshChunks();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo);
                return destination;
            }
            catch { Undo.RevertAllDownToGroup(undo); throw; }
        }

        enum WorldTab { Tiles, Details, Settings }
        WorldTab m_Tab;
        const string TabPreference = "MashBox.MGTerrainWorld.InspectorTab";
        void OnEnable()
        {
            m_Tab = (WorldTab)Mathf.Clamp(SessionState.GetInt(TabPreference, 0), 0, 2);
            SceneView.duringSceneGui += DrawScene;
        }
        void OnDisable()
        {
            SceneView.duringSceneGui -= DrawScene;
            if (m_ChunkEditor != null) DestroyImmediate(m_ChunkEditor);
        }
        void DrawScene(SceneView view)
        {
            if (m_Tab == WorldTab.Tiles && DrawTilePlacement(view)) return;
            if ((m_Tab == WorldTab.Details || m_Tab == WorldTab.Tiles && m_ShowChunk) && m_ChunkEditor is MGTerrainEditor editor) editor.DrawWorldSceneGUI();
        }
        public override void OnInspectorGUI()
        {
            var world = (MGTerrainWorld)target;
            serializedObject.Update();
            MashBoxSDK.EditorResources.MashBoxInspectorHeaderUtility.DrawScriptHeader();
            EditorGUILayout.LabelField("MG Terrain World", EditorStyles.boldLabel);
            DrawTabBar();
            EditorGUILayout.Space(6);
            switch (m_Tab)
            {
                case WorldTab.Tiles: DrawTilesTab(world); break;
                case WorldTab.Details:
                    DrawWorldTools(world);
                    if (world.Chunks.Count == 0)
                        EditorGUILayout.HelpBox("Create or adopt a terrain tile in the Tiles tab to begin painting.", MessageType.Info);
                    break;
                case WorldTab.Settings: DrawSettingsTab(world); break;
            }
        }

        void DrawSettingsTab(MGTerrainWorld world)
        {
            EditorGUILayout.HelpBox("One renderer and overall detail budget. Child MG Terrain components retain their existing painted data and editing tools. Distant chunk detail resources are released automatically; surface meshes and colliders stay loaded.", MessageType.Info);
            DrawPropertiesExcluding(serializedObject, "m_Script", "m_Quality");
            if (serializedObject.ApplyModifiedProperties()) world.ApplySharedQuality();
            DrawWorldQuality(world);
            EditorGUILayout.LabelField("Registered Chunks", world.Chunks.Count.ToString());
#if UNITY_6000_0_OR_NEWER
            EditorGUILayout.LabelField("Shared Renderers", world.SharedRendererCount.ToString());
#endif
            EditorGUILayout.LabelField("Submitted Details", world.LastSubmittedDetailInstances.ToString("N0"));
            DrawWorldBakes(world);
        }

        void DrawTilesTab(MGTerrainWorld world)
        {
            DrawTileCreation(world);
            DrawWorldDataEstimator(world);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Import Terrain", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Adopt Selected Converted Chunks"))
                {
                    try { Adopt(SelectedChunks(), world); }
                    catch (Exception error) { Debug.LogException(error); }
                }
                if (GUILayout.Button("Convert Selected Unity Terrains Into This World")) ConvertSelected(world);
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("World Tiles", EditorStyles.boldLabel);
            foreach (var chunk in world.GetComponentsInChildren<MGTerrain>(true))
            {
                if (chunk.World != null && chunk.World != world) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(chunk, typeof(MGTerrain), true);
                    if (GUILayout.Button("Edit", GUILayout.Width(45)))
                    {
                        if (m_EditChunk != chunk && m_ChunkEditor != null) DestroyImmediate(m_ChunkEditor);
                        m_EditChunk = chunk; m_ShowChunk = true; m_PaintWorld = false;
                    }
                    using (new EditorGUI.DisabledScope(Application.isPlaying))
                        if (GUILayout.Button("Detach", GUILayout.Width(55)))
                        {
                            Undo.SetTransformParent(chunk.transform, world.transform.parent, "Detach Terrain Chunk");
                            world.RefreshChunks();
                            GUIUtility.ExitGUI();
                        }
                }
            }
            if (m_ShowChunk && m_EditChunk != null && m_EditChunk.World == world)
            {
                m_ShowChunk = EditorGUILayout.Foldout(m_ShowChunk, "Edit " + m_EditChunk.name, true);
                if (m_ShowChunk)
                {
                    CreateCachedEditor(m_EditChunk, typeof(MGTerrainEditor), ref m_ChunkEditor);
                    ((MGTerrainEditor)m_ChunkEditor).DrawWorldTileInspector();
                }
            }
        }

        GUIStyle m_WorldTabButtonStyle;
        void DrawTabBar()
        {
            // miniButton's built-in fixed height otherwise paints a short background
            // inside the taller layout rect, leaving the centered glyph hanging below it.
            m_WorldTabButtonStyle ??= new GUIStyle(EditorStyles.miniButton)
            { fixedHeight = 0, stretchHeight = true, alignment = TextAnchor.MiddleCenter };
            string[] labels = { "Tiles", "Details", "Settings" };
            string[] tips = { "Create tiles, add neighbours and manage this world's terrain.",
                "Paint detail density, size and grass sub-IDs across the world.",
                "World quality, rendering budgets, diagnostics and appearance baking." };
            using (new EditorGUILayout.HorizontalScope())
                for (int i = 0; i < labels.Length; i++)
                {
                    Rect rect = GUILayoutUtility.GetRect(36, 36, GUILayout.Height(36), GUILayout.ExpandWidth(true));
                    bool selected = (int)m_Tab == i;
                    if (GUI.Toggle(rect, selected, new GUIContent("", labels[i] + ": " + tips[i]), m_WorldTabButtonStyle) && !selected)
                    {
                        m_AddTiles = false;
                        m_ShowChunk = false;
                        if (m_ChunkEditor != null) { DestroyImmediate(m_ChunkEditor); m_ChunkEditor = null; }
                        m_Tab = (WorldTab)i;
                        SessionState.SetInt(TabPreference, i);
                        SceneView.RepaintAll();
                        Repaint();
                        GUIUtility.ExitGUI();
                    }
                    if (Event.current.type == EventType.Repaint)
                        DrawTabIcon(new Rect(rect.center.x - 8, rect.center.y - 8, 16, 16), i, selected);
                }
        }

        // Small vector glyphs stay crisp at editor DPI and adapt to both Unity themes.
        static void DrawTabIcon(Rect r, int icon, bool selected)
        {
            Color previous = Handles.color;
            Handles.BeginGUI();
            Handles.color = selected ? new Color(.35f, .75f, 1f)
                : EditorGUIUtility.isProSkin ? new Color(.85f, .85f, .85f) : new Color(.22f, .22f, .22f);
            void Line(float x1, float y1, float x2, float y2) => Handles.DrawAAPolyLine(2f,
                new Vector3(r.x + x1, r.y + y1), new Vector3(r.x + x2, r.y + y2));
            if (icon == 0)
            {
                for (int n = 0; n < 3; n++) { Line(1, 1 + n * 7, 15, 1 + n * 7); Line(1 + n * 7, 1, 1 + n * 7, 15); }
            }
            else if (icon == 1)
            {
                Line(6, 10, 13, 1); Line(13, 1, 15, 3); Line(15, 3, 8, 12);
                Line(6, 10, 8, 12); Line(6, 10, 3, 11); Line(3, 11, 1, 15);
                Line(1, 15, 6, 15); Line(6, 15, 8, 12);
            }
            else
            {
                for (int n = 0; n < 3; n++)
                {
                    float y = 3 + n * 5, x = n == 1 ? 11 : 5;
                    Line(1, y, 15, y); Line(x, y - 2, x, y + 2);
                    Line(x + 1, y - 2, x + 1, y + 2);
                }
            }
            Handles.color = previous;
            Handles.EndGUI();
        }

        void DrawWorldQuality(MGTerrainWorld world)
        {
            var quality = serializedObject.FindProperty("m_Quality");
            quality.isExpanded = EditorGUILayout.Foldout(quality.isExpanded, "Quality", true);
            if (!quality.isExpanded) return;
            using (new EditorGUI.IndentLevelScope())
            {
                DrawWorldQualityPresets(world);
                // Presets can update the serialized object, so acquire a fresh iterator.
                quality = serializedObject.FindProperty("m_Quality");
                var child = quality.Copy();
                var end = quality.GetEndProperty();
                bool enterChildren = true;
                while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
                {
                    EditorGUILayout.PropertyField(child, true);
                    enterChildren = false;
                }
            }
            if (serializedObject.ApplyModifiedProperties()) world.ApplySharedQuality();
        }

        static void ConvertSelected(MGTerrainWorld world)
        {
            var terrains = Selection.gameObjects.SelectMany(go => go.GetComponentsInChildren<Terrain>(true))
                .Where(terrain => terrain.enabled && terrain.terrainData != null).Distinct().ToArray();
            if (terrains.Length == 0) { Debug.LogWarning("Select enabled Unity Terrain objects to convert.", world); return; }
            if (terrains.Any(terrain => terrain.gameObject.scene != world.gameObject.scene))
            { Debug.LogError("Source terrains and the destination world must be in the same scene.", world); return; }
            string folder = TerrainToMeshConverter.ToProjectAssetPath(EditorUtility.OpenFolderPanel("Terrain Output Assets", Application.dataPath, ""));
            if (string.IsNullOrEmpty(folder)) return;
            var options = new TerrainConversionOptions { DestinationWorld = world, ConvertMesh = true,
                AddMeshCollider = true, ConvertTrees = true, ConvertDetails = true, DisableSourceTerrain = true };
            // Use the established converter's defaults for mesh resolution and splat maps.
            options.MaximumMeshResolution = 513;
            options.ExportSplatMaps = true;
            if (!TerrainToMeshConverter.ConfirmLargeGameObjectConversions(TerrainToMeshConverter.Analyze(terrains), options)) return;
            try { foreach (var terrain in terrains) TerrainToMeshConverter.Convert(terrain, folder, options); }
            catch (Exception error) { Debug.LogException(error, world); }
            world.RefreshChunks();
        }
    }

    public sealed partial class MGTerrainEditor
    {
        internal void DrawWorldSceneGUI() => OnSceneGUI();
    }
}
#endif


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
        double m_NextSurfaceLodRepaint;

        [MenuItem("GameObject/MashBox/MG Terrain World From Selected Chunks", false, 20)]
        static void CreateFromSelection()
        {
#if !UNITY_6000_0_OR_NEWER
            EditorUtility.DisplayDialog("MG Terrain World", "MG Terrain World requires Unity 6 or newer.", "OK");
#else
            var chunks = SelectedChunks();
            if (chunks.Length == 0)
            {
                EditorUtility.DisplayDialog("MG Terrain World", "Select existing converted MG Terrain chunks, or their parent objects.", "OK");
                return;
            }
            try { Selection.activeGameObject = Adopt(chunks).gameObject; }
            catch (Exception error) { Debug.LogException(error); }
#endif
        }

        static MGTerrain[] SelectedChunks() => Selection.gameObjects
            .SelectMany(go => go.GetComponentsInChildren<MGTerrain>(true)).Distinct().ToArray();

        // Does not serialize/copy terrain data: reparenting preserves the exact existing component and assets.
        internal static MGTerrainWorld Adopt(MGTerrain[] chunks, MGTerrainWorld destination = null)
        {
#if !UNITY_6000_0_OR_NEWER
            throw new NotSupportedException("MG Terrain World requires Unity 6 or newer.");
#else
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
                    float distance = source.FindProperty("m_MaxDensityDetailDistance").floatValue;
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
#endif
        }

        enum WorldTab { Tiles, Details, Settings, Baking, Collision, Diagnostics }
        WorldTab m_Tab;
        void OnEnable()
        {
#if UNITY_6000_0_OR_NEWER
            SceneView.duringSceneGui += DrawScene;
            Undo.undoRedoPerformed += ClearPlacementPreviews;
#endif
        }
        void OnDisable()
        {
            SceneView.duringSceneGui -= DrawScene;
            Undo.undoRedoPerformed -= ClearPlacementPreviews;
            DisposePlacementPreviews();
            if (m_ChunkEditor != null) DestroyImmediate(m_ChunkEditor);
        }
        void DrawScene(SceneView view)
        {
            if (!m_Workspace) return;
            if ((m_Tab == WorldTab.Settings || m_Tab == WorldTab.Diagnostics) && EditorApplication.timeSinceStartup >= m_NextSurfaceLodRepaint)
            {
                m_NextSurfaceLodRepaint = EditorApplication.timeSinceStartup + .25;
                Repaint();
            }
            if (m_Tab == WorldTab.Tiles && DrawTilePlacement(view)) return;
            if ((m_Tab == WorldTab.Details || m_Tab == WorldTab.Tiles && m_ShowChunk) && m_ChunkEditor is MGTerrainEditor editor) editor.DrawWorldSceneGUI();
        }
        public override void OnInspectorGUI()
        {
#if UNITY_6000_0_OR_NEWER
            var world = (MGTerrainWorld)target;
            MashBoxSDK.EditorResources.MashBoxInspectorHeaderUtility.DrawScriptHeader();
            EditorGUILayout.LabelField("MG Terrain World", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Manage terrain tiles, paint details, tune rendering and bake your world in the Terrain World window.", MessageType.None);
            if (GUILayout.Button("Open Terrain World", GUILayout.Height(32))) MGTerrainWorldWindow.Open(world);
            EditorGUILayout.LabelField("Registered tiles", world.Chunks.Count.ToString("N0"));
#else
            EditorGUILayout.HelpBox("MG Terrain requires Unity 6 or newer.", MessageType.Info);
#endif
        }

        void DrawSurfaceLodPreview(MGTerrainWorld world)
        {
            EditorGUILayout.LabelField("Surface LOD - Scene Camera", EditorStyles.boldLabel);
            var view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null)
            {
                EditorGUILayout.HelpBox("Open a Scene view to inspect terrain LOD selection.", MessageType.Info);
                return;
            }
            float[] columns = { .22f, .10f, .17f, .17f, .34f };
            MGTerrainDiagnosticTable.Row(0, columns, new[] { "Tile", "LOD", "Triangles", "Original", "Selection reason" }, heading: true);
            long original = 0, selected = 0;
            int row = 0;
            foreach (var tile in world.Chunks)
            {
                if (tile == null || !tile.isActiveAndEnabled) continue;
                string reason = tile.GetSurfaceLodPreview(view.camera, out int lod, out long source, out long triangles);
                original += source; selected += triangles;
                MGTerrainDiagnosticTable.Row(++row, columns, new[] { tile.name, lod == 0 ? "Original" : lod.ToString(), triangles.ToString("N0"), source.ToString("N0"), reason });
            }
            MGTerrainDiagnosticTable.Row(++row, columns, new[] { "Total", "", selected.ToString("N0"), original.ToString("N0"), "Before camera culling" }, total: true);
            EditorGUILayout.LabelField("Scene camera selection only. Game cameras choose LOD independently.", EditorStyles.wordWrappedMiniLabel);
        }


        void DrawTilesTab(MGTerrainWorld world)
        {

            DrawTileCreation(world);
            m_ShowEstimates = EditorGUILayout.Foldout(m_ShowEstimates, "Tile measurements", true);
            if (m_ShowEstimates)
            {
                m_CurrentMeasurements.Draw(OwnedTiles(world));
            }
            EditorGUILayout.Space();
            m_ShowImport = EditorGUILayout.Foldout(m_ShowImport, "Import existing terrain", true);
            if (m_ShowImport)
            {
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
            }
            EditorGUILayout.LabelField("World tiles", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                m_TileSearch = EditorGUILayout.TextField(new GUIContent("Find tile"), m_TileSearch, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(45))) { m_TileSearch = ""; GUI.FocusControl(null); }
            }
            var visibleTiles = OwnedTiles(world).Where(tile => string.IsNullOrEmpty(m_TileSearch) || tile.name.IndexOf(m_TileSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (visibleTiles.Length == 0) EditorGUILayout.HelpBox(string.IsNullOrEmpty(m_TileSearch) ? "Generate your first tile above, or expand Import existing terrain." : "No tiles match your search.", MessageType.Info);
            foreach (var chunk in visibleTiles)
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
                    if (GUILayout.Button("Frame", GUILayout.Width(50))) SceneView.lastActiveSceneView?.Frame(MGTerrainTileAuthoring.BoundsOf(chunk), false);
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
                    if (child.name != "m_DistantDetailBudgetReserve") EditorGUILayout.PropertyField(child, true);
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

            var options = new TerrainConversionOptions { DestinationWorld = world, ConvertMesh = true,
                AddMeshCollider = true, ConvertTrees = true, ConvertDetails = true, DisableSourceTerrain = true };
            // Use the established converter's defaults for mesh resolution and splat maps.
            options.MaximumMeshResolution = 513;
            options.ExportSplatMaps = true;
            if (!TerrainToMeshConverter.ConfirmLargeGameObjectConversions(TerrainToMeshConverter.Analyze(terrains), options)) return;
            try
            {
                string folder = MGTerrainSceneAssets.Folder(world.gameObject.scene);
                foreach (var terrain in terrains) TerrainToMeshConverter.Convert(terrain, folder, options);
            }
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


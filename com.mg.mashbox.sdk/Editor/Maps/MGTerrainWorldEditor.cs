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

        void OnEnable() => SceneView.duringSceneGui += DrawScene;
        void OnDisable()
        {
            SceneView.duringSceneGui -= DrawScene;
            if (m_ChunkEditor != null) DestroyImmediate(m_ChunkEditor);
        }
        void DrawScene(SceneView view)
        {
            if ((m_PaintWorld || m_ShowChunk) && m_ChunkEditor is MGTerrainEditor editor) editor.DrawWorldSceneGUI();
        }
        public override void OnInspectorGUI()
        {
            var world = (MGTerrainWorld)target;
            serializedObject.Update();
            EditorGUILayout.LabelField("MG Terrain World", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("One renderer and overall detail budget. Child MG Terrain components retain their existing painted data and editing tools. Distant chunk detail resources are released automatically; surface meshes and colliders stay loaded.", MessageType.Info);
            DrawPropertiesExcluding(serializedObject, "m_Script");
            if (serializedObject.ApplyModifiedProperties()) world.ApplySharedQuality();
            EditorGUILayout.LabelField("Registered Chunks", world.Chunks.Count.ToString());
#if UNITY_6000_0_OR_NEWER
            EditorGUILayout.LabelField("Shared Renderers", world.SharedRendererCount.ToString());
#endif
            EditorGUILayout.LabelField("Submitted Details", world.LastSubmittedDetailInstances.ToString("N0"));
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Adopt Selected Converted Chunks"))
                {
                    try { Adopt(SelectedChunks(), world); }
                    catch (Exception error) { Debug.LogException(error); }
                }
                if (GUILayout.Button("Convert Selected Unity Terrains Into This World")) ConvertSelected(world);
            }
            DrawWorldTools(world);
            EditorGUILayout.Space();
            foreach (var chunk in world.GetComponentsInChildren<MGTerrain>(true))
            {
                if (chunk.World != null && chunk.World != world) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(chunk, typeof(MGTerrain), true);
                    if (GUILayout.Button("Edit", GUILayout.Width(45)))
                    {
                        if (m_EditChunk != chunk && m_ChunkEditor != null) DestroyImmediate(m_ChunkEditor);
                        m_EditChunk = chunk; m_ShowChunk = true;
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
            if (!m_PaintWorld && m_EditChunk != null && m_EditChunk.World == world)
            {
                m_ShowChunk = EditorGUILayout.Foldout(m_ShowChunk, "Edit " + m_EditChunk.name, true);
                if (m_ShowChunk)
                {
                    CreateCachedEditor(m_EditChunk, typeof(MGTerrainEditor), ref m_ChunkEditor);
                    m_ChunkEditor.OnInspectorGUI();
                }
            }
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


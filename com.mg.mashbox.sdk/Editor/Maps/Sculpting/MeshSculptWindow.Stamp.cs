using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MeshSculptWindow
    {
        static readonly Unity.Profiling.ProfilerMarker StampWireframeMarker = new Unity.Profiling.ProfilerMarker("MG.Stamp.TerrainWireframe");
        [SerializeField] Mesh m_StampMesh;
        [SerializeField] float m_StampHeight = 1f;
        [SerializeField] float m_StampRotation;
        MeshStampBrush m_MeshStampBrush;
        [SerializeField] MashBoxSDK.Maps.TerrainSystem.MGTerrainWorld m_StampWorld;
        Mesh m_FailedStampMesh;
        string m_StampReadError;
        bool IsMeshStamp => m_Mode == MashBoxSDK.Maps.Sculpting.MeshSculptModifier.SculptMode.MeshStamp;

        internal static void OpenMeshStamp()
        {
            MBEditorToolState.SculptMode = MBSculptMode.MeshStamp;
            MBEditorToolState.Mode = MBEditorAuthoringMode.MeshSculpt;
            MBEditorToolState.ActiveEditing = true;
            var window = GetWindow<MeshSculptWindow>("Mesh Sculpt");
            window.ActivateSceneTool();
            window.Show();
        }

        void DrawMeshStampSettings(bool compact = false)
        {
            DrawStampSource();
            m_StampHeight = Mathf.Max(0.001f, EditorGUILayout.FloatField(new GUIContent(compact ? "Height (m)" : "Stamp Height (m)", "Maximum height added per stamp. Falloff reduces it towards the edges; Ctrl carves. Repeated stamps accumulate."), m_StampHeight));
            m_StampRotation = EditorGUILayout.Slider(new GUIContent(compact ? "Rotation" : "Stamp Rotation", "Rotate with Ctrl + scroll wheel in the Scene view. Add Shift for finer rotation. This does not zoom the Scene view."), m_StampRotation, 0f, 360f);
            if (!compact) EditorGUILayout.HelpBox("Choose a mesh shape, then click or drag on a sculptable surface. Its top silhouette adds height; Ctrl carves it inward. Height sets the maximum rise per stamp, radius scales the footprint, and falloff softens the edges. The mesh's local Y is up. Stamps remain editable with Undo and Remove Last.", MessageType.Info);
            if (StampMesh == null)
                EditorGUILayout.HelpBox(m_StampSourceKind == StampSourceKind.Prefab ? "Assign a Stamp Prefab to see the brush preview." : "Assign a Stamp Mesh to see the brush preview.", MessageType.None);
            else if (StampMesh.bounds.size.y < 0.00001f)
                EditorGUILayout.HelpBox("Choose a shape with height along its local Y axis.", MessageType.Warning);
            if (!string.IsNullOrEmpty(m_StampReadError))
            {
                EditorGUILayout.HelpBox(m_StampReadError, MessageType.Warning);
                if (GUILayout.Button("Retry Reading Stamp")) { m_FailedStampMesh = null; m_StampReadError = null; ReleasePrefabStamp(); SceneView.RepaintAll(); }
            }
        }

        internal static void DrawMeshStampOverlay()
        {
            var owner = s_ActiveSceneToolOwner;
            if (owner == null)
            {
                if (GUILayout.Button("Open Mesh Stamp Settings")) OpenMeshStamp();
                return;
            }
            float previousWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 85f;
            try
            {
                EditorGUI.BeginChangeCheck();
                var selectedWorld = SelectedTerrainWorld;
                owner.m_StampWorld = (MashBoxSDK.Maps.TerrainSystem.MGTerrainWorld)EditorGUILayout.ObjectField("Target World", selectedWorld,
                    typeof(MashBoxSDK.Maps.TerrainSystem.MGTerrainWorld), true);
                owner.DrawMeshStampSettings(true);
                owner.DrawRadiusSettings();
                using (new EditorGUI.DisabledScope(owner.StampMesh == null))
                    if (GUILayout.Button(new GUIContent("Use Mesh Proportions", "Set stamp height to preserve the source mesh's height-to-width ratio at this radius.")))
                    {
                        var bounds = owner.StampMesh.bounds;
                        owner.m_StampHeight = bounds.size.y * owner.m_Radius / Mathf.Max(.00001f, new Vector2(bounds.extents.x, bounds.extents.z).magnitude);
                    }
                owner.m_Falloff = EditorGUILayout.Slider("Falloff", owner.m_Falloff, 0f, 8f);
                owner.m_Spacing = EditorGUILayout.Slider(new GUIContent("Spacing", "Distance between stamps as a fraction of radius. Higher values reduce repeated sampling during a drag."), owner.m_Spacing, .05f, 1f);
                if (EditorGUI.EndChangeCheck()) { owner.Repaint(); SceneView.RepaintAll(); }
                var world = SelectedTerrainWorld;
                EditorGUILayout.HelpBox(world != null
                    ? "Target: " + world.name + ". All active tiles are sculptable. A stamp edits only tiles inside its radius, across shared borders."
                    : "Select an MG Terrain World to stamp across its tiles without Shift-clicking each mesh.", MessageType.None);
            }
            finally { EditorGUIUtility.labelWidth = previousWidth; }
        }

        bool PrepareMeshStamp()
        {
            if (StampMesh == null || StampMesh == m_FailedStampMesh || StampMesh.bounds.size.y < 0.00001f)
                return false;
            if (m_MeshStampBrush == null || m_MeshStampBrush.Source != StampMesh)
            {
                try { m_MeshStampBrush = new MeshStampBrush(StampMesh); m_StampReadError = null; }
                catch (System.Exception error)
                {
                    m_FailedStampMesh = StampMesh;
                    m_StampReadError = "Cannot read stamp geometry: " + error.Message;
                    m_MeshStampBrush = null;
                    Repaint();
                    return false;
                }
            }
            return true;
        }

        [MenuItem("MashBox/Validation/Validate World Mesh Stamp")]
        public static void ValidateWorldMeshStamp()
        {
            var selection = Selection.activeObject;
            var previousOwner = s_ActiveSceneToolOwner;
            var previousWorld = previousOwner != null ? previousOwner.m_StampWorld : null;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var resources = new System.Collections.Generic.HashSet<Object>();
            MeshSculptWindow tool = null;
            void Check(bool pass, string message) { if (!pass) throw new System.InvalidOperationException(message); }
            try
            {
                var root = new GameObject("World Stamp Validation");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var world = root.AddComponent<MashBoxSDK.Maps.TerrainSystem.MGTerrainWorld>();
                world.TileSize = 10; world.TileResolution = 11;
                var first = MGTerrainWorldEditor.GenerateTile(world);
                var second = MGTerrainTileAuthoring.AddTile(first, Vector2Int.right);
                var distant = MGTerrainTileAuthoring.AddTile(second, Vector2Int.right);
                foreach (var tile in world.Chunks)
                {
                    resources.Add(tile.MeshFilter.sharedMesh);
                    foreach (var material in tile.MeshRenderer.sharedMaterials) resources.Add(material);
                }
                Selection.activeGameObject = root;
                Check(IsSculptableSurface(first.MeshFilter) && IsSculptableSurface(second.MeshFilter), "World selection must enroll all tiles without Shift-click.");
                var stamp = new Mesh { vertices = new[] { new Vector3(-1,0,-1), new Vector3(1,0,-1), new Vector3(1,0,1), new Vector3(-1,0,1), new Vector3(0,1,0) },
                    triangles = new[] { 0,4,1,1,4,2,2,4,3,3,4,0 } };
                stamp.RecalculateBounds(); resources.Add(stamp);
                tool = CreateInstance<MeshSculptWindow>();
                tool.m_Modifier = MGTerrainTileAuthoring.Modifier(first);
                tool.m_Mode = MashBoxSDK.Maps.Sculpting.MeshSculptModifier.SculptMode.MeshStamp;
                tool.m_StampMesh = stamp; tool.m_StampHeight = 2; tool.m_Strength = .1f; tool.m_Radius = 4; tool.m_Falloff = 0;
                s_ActiveSceneToolOwner = tool;
                Selection.activeGameObject = first.gameObject;
                Check(SelectedTerrainWorld == world, "Selecting a child tile must target its world.");
                Selection.activeObject = stamp;
                Check(SelectedTerrainWorld == world, "Selecting the stamp asset must retain the terrain world.");
                tool.RecordStroke(new RaycastHit { point = new Vector3(10,0,5), normal = Vector3.up }, false, false);
                Check(MGTerrainTileAuthoring.Modifier(first).StrokeCount > 0 && MGTerrainTileAuthoring.Modifier(second).StrokeCount > 0, "Stamp must edit both sides of a tile border.");
                Check(MGTerrainTileAuthoring.Modifier(distant).StrokeCount == 0, "Tiles outside the brush must remain untouched.");
                var a = first.MeshFilter.sharedMesh.vertices; var b = second.MeshFilter.sharedMesh.vertices;
                Check(Mathf.Abs(a[5 * 11 + 10].y - 2f) < .0001f, "Stamp Height must directly set peak rise regardless of sculpt strength.");
                for (int z = 0; z < 11; z++) Check(Mathf.Abs(a[z * 11 + 10].y - b[z * 11].y) < .0001f, "Shared border must remain joined.");
                foreach (var tile in world.Chunks) resources.Add(tile.MeshFilter.sharedMesh);
                Debug.Log("World mesh stamp PASS: world selection, cross-tile stamping, joined border, untouched distant tile.");
            }
            finally
            {
                Selection.activeObject = selection;
                if (tool != null) DestroyImmediate(tool);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                foreach (var resource in resources) if (resource != null && !EditorUtility.IsPersistent(resource)) DestroyImmediate(resource);
                if (previousOwner != null) { previousOwner.m_StampWorld = previousWorld; previousOwner.ActivateSceneTool(); }
            }
        }

        void DrawMeshStampPreview(Vector3 center, bool invert)
        {
            if (Event.current.type != EventType.Repaint || !PrepareMeshStamp()) return;
            var tiles = new System.Collections.Generic.List<MashBoxSDK.Maps.TerrainSystem.MGTerrain>();
            var world = SelectedTerrainWorld;
            if (world == null && m_Modifier != null && m_Modifier.Target != null)
                world = m_Modifier.Target.GetComponentInParent<MashBoxSDK.Maps.TerrainSystem.MGTerrain>()?.World;
            if (world != null)
                foreach (var tile in world.Chunks)
                {
                    if (tile == null || !tile.isActiveAndEnabled || tile.MeshFilter == null || tile.MeshFilter.sharedMesh == null) continue;
                    var b = MGTerrainTileAuthoring.BoundsOf(tile);
                    if (b.min.x <= center.x + m_Radius && b.max.x >= center.x - m_Radius
                        && b.min.z <= center.z + m_Radius && b.max.z >= center.z - m_Radius) tiles.Add(tile);
                }
            var targets = new System.Collections.Generic.List<MeshFilter>();
            foreach (var tile in tiles) targets.Add(tile.MeshFilter);
            if (targets.Count == 0 && m_Modifier != null && m_Modifier.Target != null
                && m_Modifier.Target.sharedMesh != null && m_Modifier.Target.sharedMesh.isReadable)
                targets.Add(m_Modifier.Target);
            DrawPrefabStampPreview(center, invert, tiles);
            if (m_StampSourceKind != StampSourceKind.Prefab || !m_PrefabMaterialPreview || m_StampTerrainWireframe)
                using (StampWireframeMarker.Auto())
                    m_MeshStampBrush.DrawPreview(targets, center, m_Radius, m_StampHeight, m_StampRotation,
                        invert ? -1f : 1f, m_Falloff);
        }
    }
}

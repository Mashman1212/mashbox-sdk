using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MeshSculptWindow
    {
        [SerializeField] Mesh m_StampMesh;
        [SerializeField] float m_StampHeight = 1f;
        [SerializeField] float m_StampRotation;
        MeshStampBrush m_MeshStampBrush;
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
            var mesh = (Mesh)EditorGUILayout.ObjectField("Stamp Mesh", m_StampMesh, typeof(Mesh), false);
            if (mesh != m_StampMesh) { m_StampMesh = mesh; m_MeshStampBrush = null; }
            m_StampHeight = Mathf.Max(0.001f, EditorGUILayout.FloatField("Stamp Height (m)", m_StampHeight));
            m_StampRotation = EditorGUILayout.Slider("Stamp Rotation", m_StampRotation, 0f, 360f);
            if (!compact) EditorGUILayout.HelpBox("Choose a mesh shape, then click or drag on a sculptable surface. Its top silhouette adds height; Ctrl carves it inward. Radius scales the footprint, strength scales each stamp, and falloff softens the edges. The mesh's local Y is up. Stamps remain editable with Undo and Remove Last.", MessageType.Info);
            if (m_StampMesh == null)
                EditorGUILayout.HelpBox("Assign a Stamp Mesh to see the brush preview.", MessageType.None);
            else if (!m_StampMesh.isReadable)
                EditorGUILayout.HelpBox("Enable Read/Write on the stamp mesh's import settings.", MessageType.Warning);
            else if (m_StampMesh.bounds.size.y < 0.00001f)
                EditorGUILayout.HelpBox("Choose a shape with height along its local Y axis.", MessageType.Warning);
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
            EditorGUIUtility.labelWidth = 115f;
            try
            {
                EditorGUI.BeginChangeCheck();
                owner.DrawMeshStampSettings(true);
                owner.m_Radius = Mathf.Max(.01f, EditorGUILayout.FloatField("Radius (m)", owner.m_Radius));
                owner.m_Strength = EditorGUILayout.Slider("Strength", owner.m_Strength, 0f, 1f);
                owner.m_Falloff = EditorGUILayout.Slider("Falloff", owner.m_Falloff, 0f, 8f);
                owner.m_Spacing = EditorGUILayout.Slider(new GUIContent("Stroke Spacing", "Distance between stamps as a fraction of radius. Higher values reduce repeated sampling during a drag."), owner.m_Spacing, .05f, 1f);
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
            if (m_StampMesh == null || !m_StampMesh.isReadable || m_StampMesh.bounds.size.y < 0.00001f)
                return false;
            if (m_MeshStampBrush == null || m_MeshStampBrush.Source != m_StampMesh)
                m_MeshStampBrush = new MeshStampBrush(m_StampMesh);
            return true;
        }

        [MenuItem("MashBox/Validation/Validate World Mesh Stamp")]
        public static void ValidateWorldMeshStamp()
        {
            var selection = Selection.activeObject;
            var previousOwner = s_ActiveSceneToolOwner;
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
                tool.m_StampMesh = stamp; tool.m_StampHeight = 2; tool.m_Strength = 1; tool.m_Radius = 4; tool.m_Falloff = 0;
                tool.RecordStroke(new RaycastHit { point = new Vector3(10,0,5), normal = Vector3.up }, false, false);
                Check(MGTerrainTileAuthoring.Modifier(first).StrokeCount > 0 && MGTerrainTileAuthoring.Modifier(second).StrokeCount > 0, "Stamp must edit both sides of a tile border.");
                Check(MGTerrainTileAuthoring.Modifier(distant).StrokeCount == 0, "Tiles outside the brush must remain untouched.");
                var a = first.MeshFilter.sharedMesh.vertices; var b = second.MeshFilter.sharedMesh.vertices;
                Check(a[5 * 11 + 10].y > 0, "Stamp must raise the shared border.");
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
                if (previousOwner != null) previousOwner.ActivateSceneTool();
            }
        }

        void DrawMeshStampPreview(Vector3 center, bool invert)
        {
            if (Event.current.type != EventType.Repaint || !PrepareMeshStamp()) return;
            m_MeshStampBrush.DrawPreview(center, m_Radius, m_StampHeight, m_StampRotation,
                invert ? -m_Strength : m_Strength, m_Falloff);
        }
    }
}

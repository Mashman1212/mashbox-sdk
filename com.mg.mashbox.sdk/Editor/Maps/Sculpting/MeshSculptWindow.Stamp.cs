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

        void DrawMeshStampSettings()
        {
            var mesh = (Mesh)EditorGUILayout.ObjectField("Stamp Mesh", m_StampMesh, typeof(Mesh), false);
            if (mesh != m_StampMesh) { m_StampMesh = mesh; m_MeshStampBrush = null; }
            m_StampHeight = Mathf.Max(0.001f, EditorGUILayout.FloatField("Stamp Height (m)", m_StampHeight));
            m_StampRotation = EditorGUILayout.Slider("Stamp Rotation", m_StampRotation, 0f, 360f);
            EditorGUILayout.HelpBox("Choose a mesh shape, then click or drag on a sculptable surface. Its top silhouette adds height; Ctrl carves it inward. Radius scales the footprint, strength scales each stamp, and falloff softens the edges. The mesh's local Y is up. Stamps remain editable with Undo and Remove Last.", MessageType.Info);
            if (m_StampMesh == null)
                EditorGUILayout.HelpBox("Assign a Stamp Mesh to see the brush preview.", MessageType.None);
            else if (!m_StampMesh.isReadable)
                EditorGUILayout.HelpBox("Enable Read/Write on the stamp mesh's import settings.", MessageType.Warning);
            else if (m_StampMesh.bounds.size.y < 0.00001f)
                EditorGUILayout.HelpBox("Choose a shape with height along its local Y axis.", MessageType.Warning);
        }

        bool PrepareMeshStamp()
        {
            if (m_StampMesh == null || !m_StampMesh.isReadable || m_StampMesh.bounds.size.y < 0.00001f)
                return false;
            if (m_MeshStampBrush == null || m_MeshStampBrush.Source != m_StampMesh)
                m_MeshStampBrush = new MeshStampBrush(m_StampMesh);
            return true;
        }

        void DrawMeshStampPreview(Vector3 center, bool invert)
        {
            if (Event.current.type != EventType.Repaint || !PrepareMeshStamp()) return;
            m_MeshStampBrush.DrawPreview(center, m_Radius, m_StampHeight, m_StampRotation,
                invert ? -m_Strength : m_Strength, m_Falloff);
        }
    }
}

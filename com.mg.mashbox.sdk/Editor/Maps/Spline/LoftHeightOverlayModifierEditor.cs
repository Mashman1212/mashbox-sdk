using MashBoxSDK.EditorResources;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.Maps.Spline
{
    [CustomEditor(typeof(LoftHeightOverlayModifier))]
    public sealed class LoftHeightOverlayModifierEditor : Editor
    {
        SerializedProperty m_Loft;
        SerializedProperty m_BakeResolution;
        SerializedProperty m_SubdivisionLevels;
        SerializedProperty m_DisplacementScale;
        SerializedProperty m_HeightCenter;
        SerializedProperty m_SurfaceOffset;
        SerializedProperty m_ChunkLength;
        SerializedProperty m_FadeBoundaryEdges;

        void OnEnable()
        {
            m_Loft = serializedObject.FindProperty("m_Loft");
            m_BakeResolution = serializedObject.FindProperty("m_BakeResolution");
            m_SubdivisionLevels = serializedObject.FindProperty("m_SubdivisionLevels");
            m_DisplacementScale = serializedObject.FindProperty("m_DisplacementScale");
            m_HeightCenter = serializedObject.FindProperty("m_HeightCenter");
            m_SurfaceOffset = serializedObject.FindProperty("m_SurfaceOffset");
            m_ChunkLength = serializedObject.FindProperty("m_ChunkLength");
            m_FadeBoundaryEdges = serializedObject.FindProperty("m_FadeBoundaryEdges");
        }

        public override void OnInspectorGUI()
        {
            MashBoxInspectorHeaderUtility.DrawScriptHeader();
            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Loft MicroBump Layer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Bakes MG_Lit_Trail's eight blended mask-map heights and displaces a separate visual mesh. Splat weights come from UV2; layer mask heights repeat across the original tiled UV0 using each layer's Tiling value; UV3/TEXCOORD3 is reserved only for the non-overlapping bake atlas.",
                MessageType.Info);
            EditorGUILayout.PropertyField(m_Loft);
            EditorGUILayout.PropertyField(m_BakeResolution);
            EditorGUILayout.PropertyField(m_SubdivisionLevels);
            EditorGUILayout.PropertyField(m_DisplacementScale);
            EditorGUILayout.PropertyField(m_HeightCenter);
            EditorGUILayout.PropertyField(m_SurfaceOffset);
            EditorGUILayout.PropertyField(m_ChunkLength);
            EditorGUILayout.PropertyField(m_FadeBoundaryEdges);
            EditorGUILayout.HelpBox(
                "Offline bake: disabled generic renderers and active non-convex MeshCollider chunks are rebuilt only when you press Rebuild MicroBump Layer. Scene loading, spline edits, and automatic loft regeneration do not run the bake.",
                MessageType.None);
            serializedObject.ApplyModifiedProperties();

            var modifier = (LoftHeightOverlayModifier)target;
            if (modifier.Loft != null && modifier.transform == modifier.Loft.transform)
            {
                EditorGUILayout.HelpBox(
                    "This is the original root-mounted version. Rebuild will migrate the behavior onto a MicroBump Layer child container.",
                    MessageType.Warning);
                if (GUILayout.Button("Move To MicroBump Layer And Rebuild", GUILayout.Height(26f)))
                {
                    MultiSplineLoftEditor.CreateOrRebuildHeightOverlay(modifier.Loft);
                    GUIUtility.ExitGUI();
                }
                return;
            }

            if (!string.IsNullOrEmpty(modifier.LastError))
                EditorGUILayout.HelpBox(modifier.LastError, MessageType.Warning);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Baked Debug Texture", modifier.BakedMicroBumpTexture, typeof(Texture2D), false);
                EditorGUILayout.ObjectField("Debug HDRP Lit Material", modifier.DebugMaterial, typeof(Material), false);
            }
            if (modifier.BakedMicroBumpTexture != null)
            {
                Rect previewRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.MaxHeight(256f));
                EditorGUI.DrawPreviewTexture(previewRect, modifier.BakedMicroBumpTexture, null, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild MicroBump Layer", GUILayout.Height(26f)))
                {
                    Undo.RecordObject(modifier, "Rebuild Loft MicroBump Layer");
                    modifier.Rebuild();
                    EditorUtility.SetDirty(modifier);
                    SceneView.RepaintAll();
                }

                using (new EditorGUI.DisabledScope(modifier.OverlayMesh == null || modifier.transform.childCount == 0))
                {
                    if (GUILayout.Button("Select First Chunk", GUILayout.Height(26f)))
                        Selection.activeGameObject = modifier.transform.GetChild(0).gameObject;
                }
            }

            using (new EditorGUI.DisabledScope(modifier.OverlayMesh == null))
            {
                if (GUILayout.Button("Clear MicroBump Chunks"))
                {
                    Undo.RecordObject(modifier, "Clear Loft MicroBump Layer");
                    modifier.ClearGenerated();
                    EditorUtility.SetDirty(modifier);
                    SceneView.RepaintAll();
                }
            }
        }
    }
}

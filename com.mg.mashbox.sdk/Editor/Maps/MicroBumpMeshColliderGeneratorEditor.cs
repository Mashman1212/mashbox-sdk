using MashBoxSDK.EditorResources;
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    [CustomEditor(typeof(MicroBumpMeshColliderGenerator))]
    public sealed class MicroBumpMeshColliderGeneratorEditor : Editor
    {
        SerializedProperty m_BakeResolution;
        SerializedProperty m_ChunkSize;
        SerializedProperty m_GridSpacing;
        SerializedProperty m_DisplacementScale;
        SerializedProperty m_HeightCenter;
        SerializedProperty m_SurfaceOffset;
        SerializedProperty m_DisableSourceCollider;

        void OnEnable()
        {
            m_BakeResolution = serializedObject.FindProperty("m_BakeResolution");
            m_ChunkSize = serializedObject.FindProperty("m_ChunkSize");
            m_GridSpacing = serializedObject.FindProperty("m_GridSpacing");
            m_DisplacementScale = serializedObject.FindProperty("m_DisplacementScale");
            m_HeightCenter = serializedObject.FindProperty("m_HeightCenter");
            m_SurfaceOffset = serializedObject.FindProperty("m_SurfaceOffset");
            m_DisableSourceCollider = serializedObject.FindProperty("m_DisableSourceCollider");
        }

        public override void OnInspectorGUI()
        {
            MashBoxInspectorHeaderUtility.DrawScriptHeader();
            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("MicroBump Mesh Colliders", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Builds square, high-resolution MeshCollider chunks over this terrain-like mesh. It uses the same MG_Lit_Trail eight-layer height bake as the Loft MicroBump system. The source mesh needs normalized, non-overlapping splat UVs in UV2/TEXCOORD2; converted terrains already have them.",
                MessageType.Info);
            EditorGUILayout.PropertyField(m_BakeResolution);
            EditorGUILayout.PropertyField(m_ChunkSize);
            EditorGUILayout.PropertyField(m_GridSpacing);
            EditorGUILayout.PropertyField(m_DisplacementScale);
            EditorGUILayout.PropertyField(m_HeightCenter);
            EditorGUILayout.PropertyField(m_SurfaceOffset);
            EditorGUILayout.PropertyField(m_DisableSourceCollider);
            EditorGUILayout.HelpBox(
                "Generation is offline and explicit. Grid Spacing controls collision detail and cost; halving it creates roughly four times as many vertices. The generated children are independent chunks so streaming can be added later.",
                MessageType.None);
            serializedObject.ApplyModifiedProperties();

            var generator = (MicroBumpMeshColliderGenerator)target;
            long estimatedVertices = generator.EstimatedVertexCount;
            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Estimated Chunks", generator.EstimatedChunkCount.ToString("N0"));
            EditorGUILayout.LabelField("Estimated Grid Vertices", estimatedVertices.ToString("N0"));
            EditorGUILayout.LabelField("Estimated Triangles", (estimatedVertices * 2L).ToString("N0"));
            if (estimatedVertices > 2000000)
            {
                EditorGUILayout.HelpBox(
                    "This is a very dense collider bake. It will use substantial scene memory and physics cooking time. Increasing Grid Spacing from 0.25 to 0.5 reduces the grid to roughly one quarter of the vertices.",
                    MessageType.Warning);
            }
            if (!string.IsNullOrEmpty(generator.LastError))
                EditorGUILayout.HelpBox(generator.LastError, MessageType.Warning);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Generated Chunks", generator.GeneratedChunkCount);
                EditorGUILayout.IntField("Generated Vertices", generator.GeneratedVertexCount);
                EditorGUILayout.ObjectField("Baked Height Preview", generator.BakedHeightPreview, typeof(Texture2D), false);
            }
            if (generator.BakedHeightPreview != null)
            {
                Rect previewRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.MaxHeight(256f));
                EditorGUI.DrawPreviewTexture(previewRect, generator.BakedHeightPreview, null, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild Collider Chunks", GUILayout.Height(26f)))
                {
                    if (estimatedVertices > 2000000 &&
                        !EditorUtility.DisplayDialog(
                            "Build Dense MicroBump Colliders?",
                            $"These settings generate approximately {estimatedVertices:N0} grid vertices and {estimatedVertices * 2L:N0} triangles before terrain holes are removed.\n\nThis can still take time and creates expensive physics data. Increase Grid Spacing for a lighter bake.",
                            "Build Anyway",
                            "Cancel"))
                    {
                        return;
                    }
                    Undo.RecordObject(generator, "Rebuild MicroBump Collider Chunks");
                    generator.Rebuild();
                    EditorUtility.SetDirty(generator);
                    SceneView.RepaintAll();
                }

                using (new EditorGUI.DisabledScope(generator.GeneratedRoot == null || generator.GeneratedRoot.transform.childCount == 0))
                {
                    if (GUILayout.Button("Select First Chunk", GUILayout.Height(26f)))
                        Selection.activeGameObject = generator.GeneratedRoot.transform.GetChild(0).gameObject;
                }
            }

            using (new EditorGUI.DisabledScope(generator.GeneratedRoot == null && generator.BakedHeightPreview == null))
            {
                if (GUILayout.Button("Clear Generated Colliders"))
                {
                    Undo.RecordObject(generator, "Clear MicroBump Collider Chunks");
                    generator.ClearGenerated();
                    EditorUtility.SetDirty(generator);
                    SceneView.RepaintAll();
                }
            }
        }
    }
}

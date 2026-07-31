using MashBoxSDK.EditorResources;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.Maps.Spline
{
    [CustomEditor(typeof(LoftShoulderModifier))]
    public sealed class LoftShoulderModifierEditor : Editor
    {
        SerializedProperty m_Loft;
        SerializedProperty m_LayeredBankMaterial;
        SerializedProperty m_UseSharedSideProfile;
        SerializedProperty m_MatchOuterNormalsToTerrain;
        SerializedProperty m_SharedSideProfile;
        SerializedProperty m_Left;
        SerializedProperty m_Right;
        SerializedProperty m_Start;
        SerializedProperty m_Finish;

        void OnEnable()
        {
            m_Loft = serializedObject.FindProperty("m_Loft");
            m_LayeredBankMaterial = serializedObject.FindProperty("m_LayeredBankMaterial");
            m_UseSharedSideProfile = serializedObject.FindProperty("m_UseSharedSideProfile");
            m_MatchOuterNormalsToTerrain = serializedObject.FindProperty("m_MatchOuterNormalsToTerrain");
            m_SharedSideProfile = serializedObject.FindProperty("m_SharedSideProfile");
            m_Left = serializedObject.FindProperty("m_Left");
            m_Right = serializedObject.FindProperty("m_Right");
            m_Start = serializedObject.FindProperty("m_Start");
            m_Finish = serializedObject.FindProperty("m_Finish");
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        public override void OnInspectorGUI()
        {
            MashBoxInspectorHeaderUtility.DrawScriptHeader();
            serializedObject.Update();
            var modifier = (LoftShoulderModifier)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Loft Shoulder Profiles", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Each Animation Curve describes vertical offset in meters across that edge's normalized shoulder width: curve time 0 is the loft edge and time 1 is the outside edge. Use negative values for ditches and positive values for banks or berms.",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "Trail masks match MG_Lit_Blend: Layer 1/base = soil, red = exposed rock (Layer 2), green = grass (Layer 3), and blue is reserved for puddles. Assign the layered material below or override it per edge.",
                MessageType.None);
            EditorGUILayout.PropertyField(m_Loft, new GUIContent("Loft"));
            EditorGUILayout.PropertyField(
                m_LayeredBankMaterial,
                new GUIContent("Layered Bank Material", "Shared soil/rock/grass material used by every enabled edge unless that edge supplies its own Material Override."));
            EditorGUILayout.PropertyField(
                m_MatchOuterNormalsToTerrain,
                new GUIContent(
                    "Match Outer Normals To Terrain",
                    "Optional shoulder-only override. The parent loft's Match Terrain Contact Normals setting automatically includes these shoulders."));
            if (modifier.Loft != null && modifier.Loft.MatchSideNormalsToTerrain)
                EditorGUILayout.HelpBox(
                    "Terrain-normal matching is inherited from the parent loft. Its Intersecting Faces and Contact Distance settings also apply here.",
                    MessageType.None);

            using (new EditorGUI.DisabledScope(modifier.Loft == null))
            {
                if (GUILayout.Button("Apply Eroded Trail Banks Preset", GUILayout.Height(28f)))
                {
                    Undo.RecordObject(modifier, "Apply Eroded Trail Banks");
                    modifier.ApplyErodedTrailPreset();
                    modifier.Loft.Regenerate();
                    EditorUtility.SetDirty(modifier);
                    EditorUtility.SetDirty(modifier.Loft);
                    serializedObject.Update();
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.Space(5f);
            EditorGUILayout.PropertyField(
                m_UseSharedSideProfile,
                new GUIContent("Shared Left + Right Tuning", "Uses one shoulder profile for both trail sides while keeping their erosion noise patterns different."));
            if (m_UseSharedSideProfile.boolValue)
                DrawProfile(m_SharedSideProfile, "Global Left + Right Profile");
            else
            {
                DrawProfile(m_Left, "Left Edge");
                DrawProfile(m_Right, "Right Edge");
            }
            DrawProfile(m_Start, "Start Edge");
            DrawProfile(m_Finish, "Finish Edge");

            if (serializedObject.ApplyModifiedProperties())
            {
                modifier.Loft?.QueueRegenerate();
                EditorUtility.SetDirty(modifier);
                SceneView.RepaintAll();
            }

            LoftShoulderEdgeSpline[] bendSplines = modifier.GetComponentsInChildren<LoftShoulderEdgeSpline>(true);
            if (bendSplines.Length > 0)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Outer Bend Splines", EditorStyles.boldLabel);
                foreach (LoftShoulderEdgeSpline bendSpline in bendSplines)
                {
                    if (GUILayout.Button($"Edit {bendSpline.Edge} Outer Bend"))
                        Selection.activeGameObject = bendSpline.gameObject;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(modifier.Loft == null))
                {
                    if (GUILayout.Button("Rebuild Shoulders"))
                    {
                        modifier.Loft.Regenerate();
                        EditorUtility.SetDirty(modifier.Loft);
                        SceneView.RepaintAll();
                    }
                }

                if (GUILayout.Button("Clear Generated"))
                {
                    modifier.ClearGenerated();
                    SceneView.RepaintAll();
                }
            }
        }

        static void DrawProfile(SerializedProperty profile, string label)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.PropertyField(profile, new GUIContent(label), true);
        }

        void OnUndoRedo()
        {
            var modifier = target as LoftShoulderModifier;
            modifier?.Loft?.QueueRegenerate();
            Repaint();
            SceneView.RepaintAll();
        }
    }
}

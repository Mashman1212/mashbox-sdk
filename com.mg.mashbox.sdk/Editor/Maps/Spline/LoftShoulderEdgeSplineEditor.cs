using MashBoxSDK.EditorResources;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using UnitySpline = UnityEngine.Splines.Spline;

namespace MashBoxSDK.Maps.Spline
{
    [CustomEditor(typeof(LoftShoulderEdgeSpline))]
    public sealed class LoftShoulderEdgeSplineEditor : Editor
    {
        SerializedProperty m_Modifier;
        SerializedProperty m_Edge;
        SerializedProperty m_PositionInfluence;
        SerializedProperty m_AcrossInfluence;
        SerializedProperty m_GeneratedPointCount;
        int m_SelectedIndex = -1;

        void OnEnable()
        {
            m_Modifier = serializedObject.FindProperty("m_Modifier");
            m_Edge = serializedObject.FindProperty("m_Edge");
            m_PositionInfluence = serializedObject.FindProperty("m_PositionInfluence");
            m_AcrossInfluence = serializedObject.FindProperty("m_AcrossInfluence");
            m_GeneratedPointCount = serializedObject.FindProperty("m_GeneratedPointCount");
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
            var edgeSpline = (LoftShoulderEdgeSpline)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Shoulder Outer Bend Spline", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Move the cyan points in the Scene view to bend the outside edge of this shoulder. Position Influence controls the total offset, and Across Influence controls how that offset blends from the fixed loft edge to the editable outer edge.",
                MessageType.Info);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(m_Modifier, new GUIContent("Shoulder Modifier"));
                EditorGUILayout.PropertyField(m_Edge, new GUIContent("Edge"));
            }
            EditorGUILayout.PropertyField(m_PositionInfluence, new GUIContent("Position Influence"));
            EditorGUILayout.PropertyField(m_AcrossInfluence, new GUIContent("Across Influence"));
            int sourceKnotCount = 0;
            bool matchesSource = edgeSpline.Modifier != null
                && edgeSpline.Modifier.Loft != null
                && edgeSpline.Modifier.Loft.TryGetShoulderSourceKnotCount(edgeSpline.Edge, out sourceKnotCount);
            using (new EditorGUI.DisabledScope(matchesSource))
                EditorGUILayout.PropertyField(m_GeneratedPointCount, new GUIContent("Control Points"));
            if (matchesSource)
                EditorGUILayout.HelpBox($"Control points match the {edgeSpline.Edge.ToString().ToLowerInvariant()} source spline ({sourceKnotCount} knots).", MessageType.None);

            if (serializedObject.ApplyModifiedProperties())
            {
                edgeSpline.Modifier?.Loft?.QueueRegenerate();
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField("Selected Knot Profile", EditorStyles.boldLabel);
            if (m_SelectedIndex >= 0 && m_SelectedIndex < edgeSpline.Container.Spline.Count)
            {
                EditorGUILayout.HelpBox(
                    "Enable a custom vertical profile for this knot. The shoulder blends between this curve and the effective profiles on its neighboring knots.",
                    MessageType.None);
                DrawKnotProfileControls(edgeSpline, m_SelectedIndex, false);
            }
            else
            {
                EditorGUILayout.HelpBox("Select a cyan shoulder knot in the Scene view to edit its profile.", MessageType.Info);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset Bend"))
                {
                    Undo.RecordObject(edgeSpline.Container, "Reset Shoulder Bend Spline");
                    edgeSpline.ResetToGeneratedEdge();
                    EditorUtility.SetDirty(edgeSpline.Container);
                    SceneView.RepaintAll();
                }

                using (new EditorGUI.DisabledScope(edgeSpline.Modifier?.Loft == null))
                {
                    if (GUILayout.Button("Rebuild Shoulders"))
                        edgeSpline.Modifier.Loft.Regenerate();
                }
            }
        }

        void OnSceneGUI()
        {
            var edgeSpline = (LoftShoulderEdgeSpline)target;
            UnitySpline spline = edgeSpline.Container.Spline;
            if (spline == null || spline.Count == 0)
                return;

            var positions = new Vector3[spline.Count];
            for (int index = 0; index < spline.Count; index++)
                positions[index] = edgeSpline.transform.TransformPoint((Vector3)spline[index].Position);

            if (positions.Length > 1)
            {
                Handles.color = new Color(1f, 0.55f, 0.05f, 0.95f);
                Handles.DrawAAPolyLine(4f, positions);
            }

            for (int index = 0; index < positions.Length; index++)
            {
                float size = HandleUtility.GetHandleSize(positions[index]) * 0.12f;
                Handles.color = index == m_SelectedIndex ? Color.yellow : Color.cyan;
                if (Handles.Button(positions[index], Quaternion.identity, size, size * 1.25f, Handles.SphereHandleCap))
                {
                    m_SelectedIndex = index;
                    Repaint();
                }
            }

            if (m_SelectedIndex < 0 || m_SelectedIndex >= spline.Count)
                return;

            DrawSceneProfilePanel(edgeSpline, positions[m_SelectedIndex]);

            EditorGUI.BeginChangeCheck();
            Vector3 newWorldPosition = Handles.PositionHandle(positions[m_SelectedIndex], edgeSpline.transform.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(edgeSpline.Container, "Move Shoulder Bend Point");
                BezierKnot knot = spline[m_SelectedIndex];
                knot.Position = edgeSpline.transform.InverseTransformPoint(newWorldPosition);
                spline[m_SelectedIndex] = knot;
                EditorUtility.SetDirty(edgeSpline.Container);
                edgeSpline.Modifier?.Loft?.QueueRegenerate();
                SceneView.RepaintAll();
            }

            Handles.Label(
                newWorldPosition + Vector3.up * HandleUtility.GetHandleSize(newWorldPosition) * 0.2f,
                $"{edgeSpline.Edge} shoulder bend {m_SelectedIndex + 1}/{spline.Count}",
                EditorStyles.whiteMiniLabel);
        }

        void DrawSceneProfilePanel(LoftShoulderEdgeSpline edgeSpline, Vector3 worldPosition)
        {
            SceneView sceneView = SceneView.currentDrawingSceneView;
            if (sceneView == null || sceneView.camera.WorldToScreenPoint(worldPosition).z <= 0f)
                return;

            bool enabled = edgeSpline.IsKnotProfileEnabled(m_SelectedIndex);
            const float width = 250f;
            float height = enabled ? 126f : 76f;
            const float horizontalMargin = 12f;
            const float topMargin = 42f;
            float x = Mathf.Max(horizontalMargin, sceneView.position.width - width - horizontalMargin);

            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(x, topMargin, width, height), "Knot Shoulder Profile", GUI.skin.window);
            DrawKnotProfileControls(edgeSpline, m_SelectedIndex, true);
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        void DrawKnotProfileControls(LoftShoulderEdgeSpline edgeSpline, int knotIndex, bool compact)
        {
            LoftShoulderModifier.ShoulderProfile edgeProfile = edgeSpline.Modifier?.GetEffectiveProfile(edgeSpline.Edge);
            AnimationCurve baseCurve = edgeProfile?.height;
            bool enabled = edgeSpline.IsKnotProfileEnabled(knotIndex);
            bool newEnabled = EditorGUILayout.ToggleLeft(
                compact ? $"Custom profile (knot {knotIndex + 1})" : "Enable Custom Profile",
                enabled);
            if (newEnabled != enabled)
            {
                Undo.RecordObject(edgeSpline, newEnabled ? "Enable Shoulder Knot Profile" : "Disable Shoulder Knot Profile");
                edgeSpline.SetKnotProfileEnabled(knotIndex, newEnabled, baseCurve);
                EditorUtility.SetDirty(edgeSpline);
                enabled = newEnabled;
                RepaintAfterProfileChange();
            }

            if (!enabled)
            {
                if (compact)
                    EditorGUILayout.LabelField("Uses the edge profile", EditorStyles.miniLabel);
                return;
            }

            AnimationCurve currentCurve = edgeSpline.GetKnotProfileCurve(knotIndex);
            EditorGUI.BeginChangeCheck();
            AnimationCurve newCurve = compact
                ? EditorGUILayout.CurveField(GUIContent.none, currentCurve, GUILayout.Height(38f))
                : EditorGUILayout.CurveField(new GUIContent("Height Curve", "Time 0 is the loft edge and time 1 is the outside shoulder edge."), currentCurve);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(edgeSpline, "Edit Shoulder Knot Profile");
                edgeSpline.SetKnotProfileCurve(knotIndex, newCurve);
                EditorUtility.SetDirty(edgeSpline);
                RepaintAfterProfileChange();
            }

            if (GUILayout.Button("Copy Edge Profile", EditorStyles.miniButton))
            {
                Undo.RecordObject(edgeSpline, "Reset Shoulder Knot Profile");
                edgeSpline.SetKnotProfileCurve(knotIndex, baseCurve);
                EditorUtility.SetDirty(edgeSpline);
                RepaintAfterProfileChange();
            }
        }

        void RepaintAfterProfileChange()
        {
            Repaint();
            SceneView.RepaintAll();
        }

        void OnUndoRedo()
        {
            var edgeSpline = target as LoftShoulderEdgeSpline;
            edgeSpline?.Modifier?.Loft?.QueueRegenerate();
            Repaint();
            SceneView.RepaintAll();
        }
    }
}

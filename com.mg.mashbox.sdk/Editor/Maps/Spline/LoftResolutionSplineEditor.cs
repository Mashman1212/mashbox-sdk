using MashBoxSDK.EditorResources;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using UnitySpline = UnityEngine.Splines.Spline;

namespace MashBoxSDK.Maps.Spline
{
    [CustomEditor(typeof(LoftResolutionSpline))]
    public sealed class LoftResolutionSplineEditor : Editor
    {
        SerializedProperty m_Loft;
        SerializedProperty m_SmoothInterpolation;
        SerializedProperty m_ControlPoints;
        int m_SelectedIndex = -1;
        bool m_ShowAllPoints;
        Mesh m_PreviewMesh;
        int m_PreviewGenerationVersion = -1;
        Vector3[] m_PreviewLines;

        void OnEnable()
        {
            m_Loft = serializedObject.FindProperty("m_Loft");
            m_SmoothInterpolation = serializedObject.FindProperty("m_SmoothInterpolation");
            m_ControlPoints = serializedObject.FindProperty("m_ControlPoints");
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
            var profile = (LoftResolutionSpline)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Loft Resolution Spline", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Select a cyan point in the Scene view, then use its scale handle to increase or decrease mesh density around that section. The global loft resolution remains the 1x baseline.",
                MessageType.Info);

            EditorGUILayout.PropertyField(m_Loft, new GUIContent("Loft"));
            EditorGUILayout.PropertyField(m_SmoothInterpolation, new GUIContent("Smooth Density"));
            if (profile.Loft != null)
            {
                EditorGUI.BeginChangeCheck();
                int pointCount = EditorGUILayout.IntSlider("Generated Points", profile.Loft.ResolutionSplinePointCount, 2, 200);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(profile.Loft, "Change Resolution Spline Point Count");
                    profile.Loft.ResolutionSplinePointCount = pointCount;
                    EditorUtility.SetDirty(profile.Loft);
                }
            }

            DrawSelectedPoint(profile);
            m_ShowAllPoints = EditorGUILayout.Foldout(m_ShowAllPoints, "All Resolution Points", true);
            if (m_ShowAllPoints)
            {
                EditorGUI.indentLevel++;
                for (int index = 0; index < m_ControlPoints.arraySize; index++)
                {
                    SerializedProperty point = m_ControlPoints.GetArrayElementAtIndex(index);
                    SerializedProperty pathT = point.FindPropertyRelative("pathT");
                    SerializedProperty scale = point.FindPropertyRelative("resolutionScale");
                    DrawSnappedScaleField(scale, new GUIContent($"Point {index + 1} ({pathT.floatValue:0.000})"));
                }
                EditorGUI.indentLevel--;
            }
            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed)
                QueueLoftRegeneration(profile);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(profile.Loft == null))
                {
                    if (GUILayout.Button("Refresh From Loft"))
                        RefreshFromLoft(profile);
                }

                if (GUILayout.Button("Reset To 1x"))
                {
                    Undo.RecordObject(profile, "Reset Loft Resolution Scales");
                    profile.ResetScales();
                    EditorUtility.SetDirty(profile);
                    QueueLoftRegeneration(profile);
                }
            }
        }

        void DrawSelectedPoint(LoftResolutionSpline profile)
        {
            int count = Mathf.Min(m_ControlPoints.arraySize, profile.Container.Spline.Count);
            if (m_SelectedIndex < 0 || m_SelectedIndex >= count)
            {
                EditorGUILayout.HelpBox("Select a cyan resolution point in the Scene view.", MessageType.None);
                return;
            }

            SerializedProperty point = m_ControlPoints.GetArrayElementAtIndex(m_SelectedIndex);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"Selected Point {m_SelectedIndex + 1} / {count}", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(point.FindPropertyRelative("pathT"), new GUIContent("Path Position"));
            DrawSnappedScaleField(point.FindPropertyRelative("resolutionScale"), new GUIContent("Resolution Scale"));
        }

        void OnSceneGUI()
        {
            var profile = (LoftResolutionSpline)target;
            if (profile == null || profile.Container == null)
                return;

            DrawLoftMeshPreview(profile);

            UnitySpline spline = profile.Container.Spline;
            int count = Mathf.Min(spline.Count, profile.ControlPoints.Count);
            if (count == 0)
                return;

            var positions = new Vector3[count];
            for (int index = 0; index < count; index++)
                positions[index] = profile.transform.TransformPoint((Vector3)spline[index].Position);

            if (count > 1)
            {
                Handles.color = new Color(1f, 0.65f, 0.1f, 0.95f);
                Handles.DrawAAPolyLine(4f, positions);
            }

            for (int index = 0; index < count; index++)
            {
                float handleSize = HandleUtility.GetHandleSize(positions[index]) * 0.14f;
                Handles.color = index == m_SelectedIndex ? Color.yellow : Color.cyan;
                if (Handles.Button(positions[index], Quaternion.identity, handleSize, handleSize * 1.3f, Handles.SphereHandleCap))
                {
                    m_SelectedIndex = index;
                    Repaint();
                }

                float scale = profile.ControlPoints[index].resolutionScale;
                Handles.Label(
                    positions[index] + Vector3.up * handleSize * 1.5f,
                    index == m_SelectedIndex ? $"{index + 1}  {scale:0.00}x" : (index + 1).ToString(),
                    EditorStyles.whiteMiniLabel);
            }

            if (m_SelectedIndex < 0 || m_SelectedIndex >= count)
                return;

            DrawScaleHandle(profile, positions, m_SelectedIndex);
        }

        void DrawScaleHandle(LoftResolutionSpline profile, Vector3[] positions, int index)
        {
            Vector3 position = positions[index];
            Vector3 tangent = GetPointTangent(positions, index);
            Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
            if (side.sqrMagnitude <= Mathf.Epsilon)
                side = Vector3.right;
            Vector3 normal = Vector3.Cross(tangent, side).normalized;
            Quaternion rotation = Quaternion.LookRotation(tangent, normal);
            LoftResolutionSpline.ControlPoint point = profile.ControlPoints[index];
            float originalScale = point.resolutionScale;

            Handles.color = Color.yellow;
            EditorGUI.BeginChangeCheck();
            Vector3 scaled = Handles.ScaleHandle(
                Vector3.one * originalScale,
                position,
                rotation,
                HandleUtility.GetHandleSize(position));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(profile, "Scale Loft Resolution Section");
                point.resolutionScale = LoftResolutionSpline.SnapResolutionScale(GetChangedScale(originalScale, scaled));
                EditorUtility.SetDirty(profile);
                QueueLoftRegeneration(profile);
            }

            Handles.Label(
                position + normal * HandleUtility.GetHandleSize(position) * 0.3f,
                $"Resolution {point.resolutionScale:0.00}x\nMesh grid: {profile.Loft.CurrentSamplesAlong:N0} samples along",
                EditorStyles.whiteMiniLabel);
        }

        void DrawLoftMeshPreview(LoftResolutionSpline profile)
        {
            MultiSplineLoft loft = profile.Loft;
            if (loft == null || !loft.TryGetComponent(out MeshFilter meshFilter) || meshFilter.sharedMesh == null)
                return;

            Mesh mesh = meshFilter.sharedMesh;
            if (mesh != m_PreviewMesh || m_PreviewGenerationVersion != loft.GenerationVersion)
                RebuildPreviewLines(loft, mesh);
            if (m_PreviewLines == null || m_PreviewLines.Length == 0)
                return;

            Color previousColor = Handles.color;
            Matrix4x4 previousMatrix = Handles.matrix;
            Handles.matrix = meshFilter.transform.localToWorldMatrix;
            Handles.color = new Color(0f, 0.95f, 1f, 0.9f);
            Handles.DrawLines(m_PreviewLines);
            Handles.matrix = previousMatrix;
            Handles.color = previousColor;
        }

        void RebuildPreviewLines(MultiSplineLoft loft, Mesh mesh)
        {
            m_PreviewMesh = mesh;
            m_PreviewGenerationVersion = loft.GenerationVersion;
            Vector3[] vertices = mesh.vertices;
            int alongCount = loft.CurrentSamplesAlong;
            int acrossCount = loft.CurrentSamplesAcross;
            int gridVertexCount = alongCount * acrossCount;
            if (alongCount < 2 || acrossCount < 2 || vertices.Length < gridVertexCount || loft.SurfaceNormalMode == MultiSplineLoft.NormalMode.Face)
            {
                RebuildTrianglePreviewLines(mesh, vertices);
                return;
            }

            int alongEdges = acrossCount * (alongCount - 1);
            int acrossEdges = alongCount * (acrossCount - 1);
            m_PreviewLines = new Vector3[(alongEdges + acrossEdges) * 2];
            int lineIndex = 0;
            for (int across = 0; across < acrossCount; across++)
            {
                int rowStart = across * alongCount;
                for (int along = 0; along < alongCount - 1; along++)
                {
                    m_PreviewLines[lineIndex++] = vertices[rowStart + along];
                    m_PreviewLines[lineIndex++] = vertices[rowStart + along + 1];
                }
            }

            for (int across = 0; across < acrossCount - 1; across++)
            {
                int rowStart = across * alongCount;
                int nextRowStart = rowStart + alongCount;
                for (int along = 0; along < alongCount; along++)
                {
                    m_PreviewLines[lineIndex++] = vertices[rowStart + along];
                    m_PreviewLines[lineIndex++] = vertices[nextRowStart + along];
                }
            }
        }

        void RebuildTrianglePreviewLines(Mesh mesh, Vector3[] vertices)
        {
            int[] triangles = mesh.triangles;
            m_PreviewLines = new Vector3[triangles.Length * 2];
            int lineIndex = 0;
            for (int index = 0; index + 2 < triangles.Length; index += 3)
            {
                int a = triangles[index];
                int b = triangles[index + 1];
                int c = triangles[index + 2];
                m_PreviewLines[lineIndex++] = vertices[a];
                m_PreviewLines[lineIndex++] = vertices[b];
                m_PreviewLines[lineIndex++] = vertices[b];
                m_PreviewLines[lineIndex++] = vertices[c];
                m_PreviewLines[lineIndex++] = vertices[c];
                m_PreviewLines[lineIndex++] = vertices[a];
            }
        }

        static void DrawSnappedScaleField(SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.FloatField(label, property.floatValue);
            if (EditorGUI.EndChangeCheck())
                property.floatValue = LoftResolutionSpline.SnapResolutionScale(value);
        }

        static float GetChangedScale(float originalScale, Vector3 scaled)
        {
            float xDelta = Mathf.Abs(scaled.x - originalScale);
            float yDelta = Mathf.Abs(scaled.y - originalScale);
            float zDelta = Mathf.Abs(scaled.z - originalScale);
            if (xDelta >= yDelta && xDelta >= zDelta)
                return scaled.x;
            return yDelta >= zDelta ? scaled.y : scaled.z;
        }

        static Vector3 GetPointTangent(Vector3[] positions, int index)
        {
            Vector3 tangent;
            if (index <= 0)
                tangent = positions[Mathf.Min(1, positions.Length - 1)] - positions[0];
            else if (index >= positions.Length - 1)
                tangent = positions[index] - positions[index - 1];
            else
                tangent = positions[index + 1] - positions[index - 1];
            return tangent.sqrMagnitude > Mathf.Epsilon ? tangent.normalized : Vector3.forward;
        }

        static void RefreshFromLoft(LoftResolutionSpline profile)
        {
            Undo.RecordObject(profile, "Refresh Loft Resolution Spline");
            Undo.RecordObject(profile.Container, "Refresh Loft Resolution Spline");
            if (!profile.Loft.GenerateResolutionSpline(out string error))
                EditorUtility.DisplayDialog("Generate Resolution Spline", error, "OK");
            else
            {
                EditorUtility.SetDirty(profile);
                EditorUtility.SetDirty(profile.Container);
                QueueLoftRegeneration(profile);
            }
        }

        static void QueueLoftRegeneration(LoftResolutionSpline profile)
        {
            if (profile?.Loft != null)
            {
                profile.Loft.QueueRegenerate();
                EditorUtility.SetDirty(profile.Loft);
            }
            SceneView.RepaintAll();
        }

        void OnUndoRedo()
        {
            QueueLoftRegeneration(target as LoftResolutionSpline);
            Repaint();
        }
    }
}

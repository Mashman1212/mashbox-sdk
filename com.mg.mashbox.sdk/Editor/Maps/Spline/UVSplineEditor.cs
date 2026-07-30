using System.IO;
using System.Collections.Generic;
using MashBoxSDK.EditorResources;
using MashBoxSDK.MapTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using UnitySpline = UnityEngine.Splines.Spline;

namespace MashBoxSDK.Maps.Spline
{
    [CustomEditor(typeof(UVSpline))]
    public sealed class UVSplineEditor : Editor
    {
        public static bool SceneEditingEnabled { get; set; } = true;

        enum SceneHandleMode { MoveAndUv, SideOffset, UvScale }

        SerializedProperty m_Target;
        SerializedProperty m_UvChannel;
        SerializedProperty m_LongitudinalAxis;
        SerializedProperty m_GeneratedPointCount;
        SerializedProperty m_SmoothInterpolation;
        SerializedProperty m_LivePreview;
        SerializedProperty m_AutoCrossPivot;
        SerializedProperty m_CrossPivot;
        SerializedProperty m_MovingKnotsOffsetsUv;
        SerializedProperty m_MoveFalloffPoints;
        SerializedProperty m_AutomaticMoveSensitivity;
        SerializedProperty m_AlongUvPerWorldUnit;
        SerializedProperty m_SideUvPerWorldUnit;
        SerializedProperty m_ControlPoints;

        Mesh m_PreviewMesh;
        SceneHandleMode m_SceneHandleMode;
        int m_SelectedIndex = -1;
        bool m_PreviewQueued;

        void OnEnable()
        {
            m_SceneHandleMode = (SceneHandleMode)MBEditorToolState.UvMode;
            m_Target = serializedObject.FindProperty("m_Target");
            m_UvChannel = serializedObject.FindProperty("m_UvChannel");
            m_LongitudinalAxis = serializedObject.FindProperty("m_LongitudinalAxis");
            m_GeneratedPointCount = serializedObject.FindProperty("m_GeneratedPointCount");
            m_SmoothInterpolation = serializedObject.FindProperty("m_SmoothInterpolation");
            m_LivePreview = serializedObject.FindProperty("m_LivePreview");
            m_AutoCrossPivot = serializedObject.FindProperty("m_AutoCrossPivot");
            m_CrossPivot = serializedObject.FindProperty("m_CrossPivot");
            m_MovingKnotsOffsetsUv = serializedObject.FindProperty("m_MovingKnotsOffsetsUv");
            m_MoveFalloffPoints = serializedObject.FindProperty("m_MoveFalloffPoints");
            m_AutomaticMoveSensitivity = serializedObject.FindProperty("m_AutomaticMoveSensitivity");
            m_AlongUvPerWorldUnit = serializedObject.FindProperty("m_AlongUvPerWorldUnit");
            m_SideUvPerWorldUnit = serializedObject.FindProperty("m_SideUvPerWorldUnit");
            m_ControlPoints = serializedObject.FindProperty("m_ControlPoints");
            UnitySpline.Changed += OnSplineChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            MBEditorToolState.UvModeChanged += OnSharedUvModeChanged;
        }

        void OnDisable()
        {
            UnitySpline.Changed -= OnSplineChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            MBEditorToolState.UvModeChanged -= OnSharedUvModeChanged;
            EditorApplication.delayCall -= ProcessQueuedPreview;
        }

        public override void OnInspectorGUI()
        {
            MashBoxInspectorHeaderUtility.DrawScriptHeader();
            serializedObject.Update();
            var uvSpline = (UVSpline)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("UV Spline", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select cyan knots in the Scene view. W moves a knot and its UV section, E edits side offset, and R scales UVs across/along. Use the midpoint cube to mirror-split the segment leading to the next knot.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_Target, new GUIContent("Target Mesh"));
            bool targetChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_UvChannel, new GUIContent("UV Channel"));
            EditorGUILayout.PropertyField(m_LongitudinalAxis, new GUIContent("UV Direction"));
            EditorGUILayout.PropertyField(m_GeneratedPointCount, new GUIContent("Generated Points"));
            bool generationSettingsChanged = EditorGUI.EndChangeCheck();

            serializedObject.ApplyModifiedProperties();
            if (generationSettingsChanged)
            {
                var owningLoft = uvSpline.GetComponentInParent<MultiSplineLoft>();
                if (owningLoft != null && owningLoft.SynchronizeUvSplineSettings(uvSpline))
                    EditorUtility.SetDirty(owningLoft);
            }
            if (targetChanged)
            {
                m_PreviewMesh = null;
            }

            using (new EditorGUI.DisabledScope(uvSpline.Target == null || uvSpline.Target.sharedMesh == null))
            {
                if (GUILayout.Button("Generate Spline From Mesh", GUILayout.Height(28f)))
                {
                    var owningLoft = uvSpline.GetComponentInParent<MultiSplineLoft>();
                    if (owningLoft != null && owningLoft.SynchronizeUvSplineSettings(uvSpline))
                        EditorUtility.SetDirty(owningLoft);

                    RestorePreview();
                    Undo.RecordObject(uvSpline, "Generate UV Spline");
                    Undo.RecordObject(uvSpline.Container, "Generate UV Spline");
                    if (!uvSpline.GenerateFromTarget(out string error))
                        EditorUtility.DisplayDialog("Generate UV Spline", error, "OK");
                    else
                    {
                        EditorUtility.SetDirty(uvSpline);
                        EditorUtility.SetDirty(uvSpline.Container);
                        m_SelectedIndex = uvSpline.Container.Spline.Count > 0 ? 0 : -1;
                        RequestPreview();
                        SceneView.RepaintAll();
                    }
                }
            }

            serializedObject.Update();
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("UV Controls", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_LivePreview, new GUIContent("Live Preview"));
            bool livePreviewChanged = EditorGUI.EndChangeCheck();
            var requestedHandleMode = (SceneHandleMode)GUILayout.Toolbar((int)m_SceneHandleMode, new[] { "Move + UV", "Side Offset", "UV Scale" });
            if (requestedHandleMode != m_SceneHandleMode)
            {
                m_SceneHandleMode = requestedHandleMode;
                MBEditorToolState.UvMode = (MBUvHandleMode)m_SceneHandleMode;
            }

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_SmoothInterpolation);
            EditorGUILayout.PropertyField(m_AutoCrossPivot, new GUIContent("Automatic Width Pivot"));
            if (!m_AutoCrossPivot.boolValue)
                EditorGUILayout.PropertyField(m_CrossPivot, new GUIContent("Width Pivot"));
            EditorGUILayout.PropertyField(m_MovingKnotsOffsetsUv, new GUIContent("Moving Knots Offsets UV"));
            if (m_MovingKnotsOffsetsUv.boolValue)
            {
                EditorGUILayout.PropertyField(m_MoveFalloffPoints, new GUIContent("Move Falloff (Points)", "Spreads a moved knot's UV offset across neighboring spline points. Higher values produce gentler stretching."));
                EditorGUILayout.PropertyField(m_AutomaticMoveSensitivity, new GUIContent("Automatic Move Sensitivity"));
                using (new EditorGUI.DisabledScope(m_AutomaticMoveSensitivity.boolValue))
                {
                    EditorGUILayout.PropertyField(m_AlongUvPerWorldUnit, new GUIContent("Along UV / World Unit"));
                    EditorGUILayout.PropertyField(m_SideUvPerWorldUnit, new GUIContent("Side UV / World Unit"));
                }
            }
            DrawSelectedPointInspector(uvSpline);
            EditorGUILayout.PropertyField(m_ControlPoints, new GUIContent("All Spline Points"), true);
            bool controlsChanged = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            if (controlsChanged || (livePreviewChanged && uvSpline.LivePreview))
                RequestPreview();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset Controls"))
                {
                    Undo.RecordObject(uvSpline, "Reset UV Spline Controls");
                    uvSpline.ResetControls();
                    EditorUtility.SetDirty(uvSpline);
                    RequestPreview();
                }

                using (new EditorGUI.DisabledScope(!CanBuild(uvSpline)))
                {
                    if (GUILayout.Button("Refresh Preview"))
                        RefreshPreview(uvSpline);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(uvSpline.OutputMesh == null))
                {
                    if (GUILayout.Button("Cancel Preview"))
                        RestorePreview();
                }

                using (new EditorGUI.DisabledScope(!CanBuild(uvSpline)))
                {
                    if (GUILayout.Button("Save New Mesh Asset"))
                        SaveMeshAsset(uvSpline);
                }
            }
        }

        void DrawSelectedPointInspector(UVSpline uvSpline)
        {
            int count = Mathf.Min(m_ControlPoints.arraySize, uvSpline.Container.Spline.Count);
            if (m_SelectedIndex < 0 || m_SelectedIndex >= count)
            {
                EditorGUILayout.HelpBox("Select a cyan UV knot in the Scene view to edit its section.", MessageType.None);
                return;
            }

            SerializedProperty point = m_ControlPoints.GetArrayElementAtIndex(m_SelectedIndex);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"Selected Point {m_SelectedIndex + 1} / {count}", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(point.FindPropertyRelative("pathT"), new GUIContent("Path Position"));
            EditorGUILayout.PropertyField(point.FindPropertyRelative("widthScale"), new GUIContent("Width Scale"));
            EditorGUILayout.PropertyField(point.FindPropertyRelative("lengthScale"), new GUIContent("Length Scale"));
            EditorGUILayout.PropertyField(point.FindPropertyRelative("sideOffset"), new GUIContent("Side Offset"));
            EditorGUILayout.PropertyField(point.FindPropertyRelative("alongOffset"), new GUIContent("Along Offset"));
            using (new EditorGUI.DisabledScope(m_SelectedIndex >= count - 1))
            {
                EditorGUILayout.PropertyField(
                    point.FindPropertyRelative("mirrorSplitToNext"),
                    new GUIContent("Mirror Split To Next", "Maps the full crosswise UV range onto both sides of the moving UV centerline for the segment from this point to the next point."));
            }
            if (m_SelectedIndex >= count - 1)
                EditorGUILayout.HelpBox("The final point has no following segment to split.", MessageType.None);

            bool splitToNext = m_SelectedIndex < count - 1 && point.FindPropertyRelative("mirrorSplitToNext").boolValue;
            bool splitFromPrevious = m_SelectedIndex > 0
                && m_ControlPoints.GetArrayElementAtIndex(m_SelectedIndex - 1).FindPropertyRelative("mirrorSplitToNext").boolValue;
            if (splitToNext)
            {
                EditorGUILayout.PropertyField(
                    point.FindPropertyRelative("mirrorBlendLength"),
                    new GUIContent("Split Blend Length", "Fraction of this segment used to blend between one centered path and the two mirrored paths. A value of 1 lets the transition extend all the way to the next point."));
                bool usesTopologySeam = uvSpline.Target != null && uvSpline.Target.GetComponent<MultiSplineLoft>() != null;
                using (new EditorGUI.DisabledScope(usesTopologySeam))
                {
                    EditorGUILayout.PropertyField(
                        point.FindPropertyRelative("mirrorBlendWidth"),
                        new GUIContent("Split Blend Width", "Crosswise fallback blending for meshes where a topology seam cannot be generated."));
                }
                if (usesTopologySeam)
                    EditorGUILayout.HelpBox("This loft uses a hard duplicated UV seam to preserve texture density. Crosswise blending is disabled; use Split Blend Length for the transition into and out of the split.", MessageType.None);
                EditorGUILayout.PropertyField(
                    point.FindPropertyRelative("mirrorBranchUvWidth"),
                    new GUIContent("Branch Horizontal Span", "0.5 preserves the source texture's total horizontal span across both fork branches. 1.0 maps the full source span onto each branch and may repeat the texture horizontally."));
                EditorGUILayout.LabelField("Individual Branch Scale", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(
                    point.FindPropertyRelative("mirrorLeftScale"),
                    new GUIContent("Left UV Scale", "Crosswise texture scale for only the left fork branch. 1 keeps the current scale; larger values compress the texture and smaller values stretch it wider."));
                EditorGUILayout.PropertyField(
                    point.FindPropertyRelative("mirrorRightScale"),
                    new GUIContent("Right UV Scale", "Crosswise texture scale for only the right fork branch. 1 keeps the current scale; larger values compress the texture and smaller values stretch it wider."));
                EditorGUILayout.PropertyField(point.FindPropertyRelative("flipMirrorLeft"), new GUIContent("Flip Left Branch", "Reverses the crosswise UV direction on the left fork branch."));
                EditorGUILayout.PropertyField(point.FindPropertyRelative("flipMirrorRight"), new GUIContent("Flip Right Branch", "Reverses the crosswise UV direction on the right fork branch."));
            }

            if (splitToNext || splitFromPrevious)
            {
                EditorGUILayout.LabelField("Split Branch Position", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(point.FindPropertyRelative("mirrorLeftOffset"), new GUIContent("Left Branch UV Offset"));
                EditorGUILayout.PropertyField(point.FindPropertyRelative("mirrorRightOffset"), new GUIContent("Right Branch UV Offset"));
            }
        }

        void OnSceneGUI()
        {
            if (!SceneEditingEnabled)
                return;

            var uvSpline = (UVSpline)target;
            if (uvSpline == null || uvSpline.Container == null || uvSpline.Container.Spline.Count == 0)
                return;

            UnitySpline spline = uvSpline.Container.Spline;
            int count = Mathf.Min(spline.Count, uvSpline.ControlPoints.Count);
            if (count == 0)
                return;

            Event current = Event.current;

            var positions = new Vector3[count];
            for (int i = 0; i < count; i++)
                positions[i] = uvSpline.transform.TransformPoint((Vector3)spline[i].Position);

            if (count > 1)
            {
                Handles.color = new Color(0.1f, 0.9f, 1f, 0.9f);
                Handles.DrawAAPolyLine(4f, positions);
            }

            for (int i = 0; i < count; i++)
            {
                float size = HandleUtility.GetHandleSize(positions[i]) * 0.15f;
                Handles.color = i == m_SelectedIndex ? Color.yellow : Color.cyan;
                if (Handles.Button(positions[i], Quaternion.identity, size, size * 1.3f, Handles.SphereHandleCap))
                {
                    m_SelectedIndex = i;
                    Repaint();
                }

                UVSpline.ControlPoint point = uvSpline.ControlPoints[i];
                Handles.Label(positions[i] + Vector3.up * size * 1.5f,
                    i == m_SelectedIndex
                        ? $"{i + 1}  Width {point.widthScale:0.00}  Length {point.lengthScale:0.00}  Side {point.sideOffset:0.00}"
                        : (i + 1).ToString(),
                    EditorStyles.whiteMiniLabel);
            }

            DrawMirroredSegments(uvSpline, positions, count);

            HandleSceneHotkeys(current);
            if (m_SelectedIndex < 0 || m_SelectedIndex >= count)
                return;

            DrawSelectedSegmentMirrorHandle(uvSpline, positions, m_SelectedIndex);
            DrawSelectedSplitBranchHandles(uvSpline, positions, m_SelectedIndex);
            DrawSelectedPointHandle(uvSpline, spline, positions, m_SelectedIndex);
        }

        static void DrawMirroredSegments(UVSpline uvSpline, Vector3[] positions, int count)
        {
            Handles.color = new Color(1f, 0.15f, 0.8f, 0.95f);
            for (int index = 0; index < count - 1; index++)
            {
                if (uvSpline.ControlPoints[index].mirrorSplitToNext)
                    Handles.DrawAAPolyLine(6f, positions[index], positions[index + 1]);
            }
        }

        void DrawSelectedSegmentMirrorHandle(UVSpline uvSpline, Vector3[] positions, int index)
        {
            if (index < 0 || index >= positions.Length - 1)
                return;

            UVSpline.ControlPoint point = uvSpline.ControlPoints[index];
            Vector3 midpoint = Vector3.Lerp(positions[index], positions[index + 1], 0.5f);
            Vector3 tangent = (positions[index + 1] - positions[index]).normalized;
            Vector3 side = GetTrailSide(tangent);
            Vector3 normal = Vector3.Cross(tangent, side).normalized;
            if (normal.sqrMagnitude <= Mathf.Epsilon)
                normal = Vector3.up;

            float size = HandleUtility.GetHandleSize(midpoint) * 0.12f;
            Vector3 handlePosition = midpoint + normal * size * 1.25f;
            Handles.color = point.mirrorSplitToNext
                ? new Color(1f, 0.15f, 0.8f, 1f)
                : new Color(0.55f, 0.55f, 0.55f, 0.9f);
            Handles.DrawDottedLine(midpoint, handlePosition, 3f);
            if (Handles.Button(handlePosition, Quaternion.LookRotation(normal, tangent), size, size * 1.25f, Handles.CubeHandleCap))
            {
                Undo.RecordObject(uvSpline, "Toggle Mirrored UV Split");
                point.mirrorSplitToNext = !point.mirrorSplitToNext;
                EditorUtility.SetDirty(uvSpline);
                RefreshPreviewImmediately(uvSpline);
                Repaint();
            }

            Handles.Label(
                handlePosition + normal * size,
                point.mirrorSplitToNext ? "Mirrored UV Split: ON" : "Mirrored UV Split: OFF",
                EditorStyles.whiteMiniLabel);

            if (!point.mirrorSplitToNext)
                return;

            float segmentLength = Vector3.Distance(positions[index], positions[index + 1]);
            if (segmentLength <= Mathf.Epsilon)
                return;

            float blendLength = Mathf.Clamp01(point.mirrorBlendLength);
            Vector3 startBlendPosition = Vector3.Lerp(positions[index], positions[index + 1], blendLength);
            Vector3 endBlendPosition = Vector3.Lerp(positions[index + 1], positions[index], blendLength);
            Handles.color = new Color(1f, 0.65f, 0.1f, 1f);
            EditorGUI.BeginChangeCheck();
            Vector3 movedStart = Handles.Slider(startBlendPosition, tangent, size * 0.8f, Handles.CubeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(uvSpline, "Adjust UV Split Blend Length");
                point.mirrorBlendLength = Mathf.Clamp01(Vector3.Dot(movedStart - positions[index], tangent) / segmentLength);
                EditorUtility.SetDirty(uvSpline);
                RefreshPreviewImmediately(uvSpline);
            }

            Handles.DrawDottedLine(positions[index], startBlendPosition, 3f);
            Handles.DrawDottedLine(positions[index + 1], endBlendPosition, 3f);
            Handles.Label(startBlendPosition + normal * size * 0.5f, $"Blend {point.mirrorBlendLength:0.00}", EditorStyles.whiteMiniLabel);

            if (uvSpline.Target != null && uvSpline.Target.GetComponent<MultiSplineLoft>() != null)
                return;

            float sensitivity = Mathf.Max(0.0001f, Mathf.Abs(uvSpline.SideUvPerWorldUnit));
            Vector3 widthHandlePosition = midpoint + side * (point.mirrorBlendWidth / sensitivity);
            Handles.color = new Color(0.3f, 1f, 0.85f, 1f);
            EditorGUI.BeginChangeCheck();
            Vector3 movedWidth = Handles.Slider(widthHandlePosition, side, size * 0.75f, Handles.ConeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(uvSpline, "Adjust UV Split Blend Width");
                point.mirrorBlendWidth = Mathf.Max(0f, Vector3.Dot(movedWidth - midpoint, side) * sensitivity);
                EditorUtility.SetDirty(uvSpline);
                RefreshPreviewImmediately(uvSpline);
            }
            Handles.DrawDottedLine(midpoint, widthHandlePosition, 3f);
            Handles.Label(widthHandlePosition + normal * size * 0.5f, $"Width {point.mirrorBlendWidth:0.00}", EditorStyles.whiteMiniLabel);
        }

        void DrawSelectedSplitBranchHandles(UVSpline uvSpline, Vector3[] positions, int index)
        {
            bool splitToNext = index < positions.Length - 1 && uvSpline.ControlPoints[index].mirrorSplitToNext;
            bool splitFromPrevious = index > 0 && uvSpline.ControlPoints[index - 1].mirrorSplitToNext;
            if (!splitToNext && !splitFromPrevious)
                return;

            Vector3 position = positions[index];
            Vector3 tangent = GetPointTangent(positions, index);
            Vector3 side = GetTrailSide(tangent);
            float sensitivity = Mathf.Max(0.0001f, Mathf.Abs(uvSpline.SideUvPerWorldUnit));
            UVSpline.ControlPoint point = uvSpline.ControlPoints[index];
            float size = HandleUtility.GetHandleSize(position) * 0.12f;
            UVSpline.ControlPoint segmentPoint = splitToNext ? point : uvSpline.ControlPoints[index - 1];
            bool flipLeft = segmentPoint.flipMirrorLeft;
            bool flipRight = segmentPoint.flipMirrorRight;
            float leftBranchUvDistance = 0.25f;
            float rightBranchUvDistance = 0.25f;
            if (uvSpline.TryGetCrossUvBounds(out float minCross, out float maxCross))
            {
                float range = Mathf.Max(0.0001f, maxCross - minCross);
                float pivot = uvSpline.AutoCrossPivot ? (minCross + maxCross) * 0.5f : uvSpline.CrossPivot;
                float leftBranchSpan = range
                    * Mathf.Max(0.01f, segmentPoint.mirrorBranchUvWidth)
                    * Mathf.Max(0.01f, segmentPoint.mirrorLeftScale);
                float rightBranchSpan = range
                    * Mathf.Max(0.01f, segmentPoint.mirrorBranchUvWidth)
                    * Mathf.Max(0.01f, segmentPoint.mirrorRightScale);
                float leftNormalizedDistance = flipLeft
                    ? (maxCross - pivot) / leftBranchSpan
                    : (pivot - minCross) / leftBranchSpan;
                float rightNormalizedDistance = flipRight
                    ? (maxCross - pivot) / rightBranchSpan
                    : (pivot - minCross) / rightBranchSpan;
                leftBranchUvDistance = Mathf.Max(0.0001f, leftNormalizedDistance * range * 0.5f);
                rightBranchUvDistance = Mathf.Max(0.0001f, rightNormalizedDistance * range * 0.5f);
            }

            float leftOffsetDirection = flipLeft ? -1f : 1f;
            float rightOffsetDirection = flipRight ? 1f : -1f;
            float branchWidth = Mathf.Max(0.01f, segmentPoint.mirrorBranchUvWidth);
            float leftBranchWidth = branchWidth * Mathf.Max(0.01f, segmentPoint.mirrorLeftScale);
            float rightBranchWidth = branchWidth * Mathf.Max(0.01f, segmentPoint.mirrorRightScale);
            Vector3 leftPosition = position + side * (-leftBranchUvDistance / sensitivity + point.mirrorLeftOffset * leftOffsetDirection / (2f * leftBranchWidth * sensitivity));
            Vector3 rightPosition = position + side * (rightBranchUvDistance / sensitivity + point.mirrorRightOffset * rightOffsetDirection / (2f * rightBranchWidth * sensitivity));

            Handles.color = new Color(0.2f, 0.8f, 1f, 1f);
            EditorGUI.BeginChangeCheck();
            Vector3 movedLeft = Handles.Slider(leftPosition, side, size, Handles.SphereHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(uvSpline, "Move Left UV Split Branch");
                point.mirrorLeftOffset += Vector3.Dot(movedLeft - leftPosition, side) * 2f * leftBranchWidth * sensitivity * leftOffsetDirection;
                EditorUtility.SetDirty(uvSpline);
                RefreshPreviewImmediately(uvSpline);
            }

            Handles.color = new Color(1f, 0.3f, 0.75f, 1f);
            EditorGUI.BeginChangeCheck();
            Vector3 movedRight = Handles.Slider(rightPosition, side, size, Handles.SphereHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(uvSpline, "Move Right UV Split Branch");
                point.mirrorRightOffset += Vector3.Dot(movedRight - rightPosition, side) * 2f * rightBranchWidth * sensitivity * rightOffsetDirection;
                EditorUtility.SetDirty(uvSpline);
                RefreshPreviewImmediately(uvSpline);
            }

            Handles.DrawDottedLine(leftPosition, rightPosition, 4f);
            Handles.Label(
                leftPosition + Vector3.up * size,
                flipLeft ? "Split L (Flipped)" : "Split L",
                EditorStyles.whiteMiniLabel);
            Handles.Label(
                rightPosition + Vector3.up * size,
                flipRight ? "Split R (Flipped)" : "Split R",
                EditorStyles.whiteMiniLabel);
        }

        void HandleSceneHotkeys(Event current)
        {
            if (current.type != EventType.KeyDown || current.shift || current.alt || current.control || current.command || GUIUtility.hotControl != 0)
                return;

            bool modeChanged = true;
            if (current.keyCode == KeyCode.W)
                m_SceneHandleMode = SceneHandleMode.MoveAndUv;
            else if (current.keyCode == KeyCode.E)
                m_SceneHandleMode = SceneHandleMode.SideOffset;
            else if (current.keyCode == KeyCode.R)
                m_SceneHandleMode = SceneHandleMode.UvScale;
            else if (current.keyCode == KeyCode.F && m_SelectedIndex >= 0)
            {
                modeChanged = false;
                var uvSpline = (UVSpline)target;
                Vector3 position = uvSpline.transform.TransformPoint((Vector3)uvSpline.Container.Spline[m_SelectedIndex].Position);
                SceneView.lastActiveSceneView?.LookAt(position, SceneView.lastActiveSceneView.rotation, Mathf.Max(0.5f, HandleUtility.GetHandleSize(position) * 1.5f));
            }
            else
                return;

            if (modeChanged)
                MBEditorToolState.UvMode = (MBUvHandleMode)m_SceneHandleMode;

            current.Use();
            Repaint();
            SceneView.RepaintAll();
        }

        void DrawSelectedPointHandle(UVSpline uvSpline, UnitySpline spline, Vector3[] positions, int index)
        {
            Vector3 position = positions[index];
            Vector3 tangent = GetPointTangent(positions, index);
            Vector3 side = GetTrailSide(tangent);
            Vector3 normal = Vector3.Cross(tangent, side).normalized;
            if (normal.sqrMagnitude <= Mathf.Epsilon)
                normal = Vector3.up;
            Quaternion splineRotation = Quaternion.LookRotation(tangent, normal);
            UVSpline.ControlPoint point = uvSpline.ControlPoints[index];

            if (m_SceneHandleMode == SceneHandleMode.MoveAndUv)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(position, splineRotation);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(uvSpline.Container, "Move UV Spline Knot");
                    BezierKnot knot = spline[index];
                    knot.Position = uvSpline.transform.InverseTransformPoint(moved);
                    spline.SetKnot(index, knot);
                    EditorUtility.SetDirty(uvSpline.Container);
                    RefreshPreviewImmediately(uvSpline);
                }
                return;
            }

            if (m_SceneHandleMode == SceneHandleMode.SideOffset)
            {
                float sensitivity = Mathf.Max(0.0001f, Mathf.Abs(uvSpline.SideUvPerWorldUnit));
                Vector3 handlePosition = position + side * (point.sideOffset / sensitivity);
                Handles.color = Color.magenta;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.Slider(handlePosition, side, HandleUtility.GetHandleSize(handlePosition) * 0.12f, Handles.ConeHandleCap, 0f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(uvSpline, "Offset UV Spline Section");
                    point.sideOffset = Vector3.Dot(moved - position, side) * sensitivity;
                    EditorUtility.SetDirty(uvSpline);
                    RefreshPreviewImmediately(uvSpline);
                }
                Handles.DrawLine(position, moved, 3f);
                return;
            }

            Vector3 handleScale = new Vector3(
                1f / Mathf.Max(0.01f, point.widthScale),
                1f,
                1f / Mathf.Max(0.01f, point.lengthScale));

            Handles.color = Color.yellow;
            EditorGUI.BeginChangeCheck();
            Vector3 scaled = Handles.ScaleHandle(handleScale, position, splineRotation, HandleUtility.GetHandleSize(position));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(uvSpline, "Scale UV Spline Section");
                point.widthScale = Mathf.Clamp(1f / Mathf.Max(0.0001f, scaled.x), 0.01f, 100f);
                point.lengthScale = Mathf.Clamp(1f / Mathf.Max(0.0001f, scaled.z), 0.01f, 100f);
                EditorUtility.SetDirty(uvSpline);
                RefreshPreviewImmediately(uvSpline);
            }

            Handles.Label(position + normal * HandleUtility.GetHandleSize(position) * 0.25f,
                $"Across {point.widthScale:0.00}  |  Along {point.lengthScale:0.00}", EditorStyles.whiteMiniLabel);
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

        static Vector3 GetTrailSide(Vector3 tangent)
        {
            Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
            if (side.sqrMagnitude <= Mathf.Epsilon)
                side = Vector3.Cross(Vector3.forward, tangent).normalized;
            return side.sqrMagnitude > Mathf.Epsilon ? side : Vector3.right;
        }

        void OnSplineChanged(UnitySpline spline, int knotIndex, SplineModification modification)
        {
            var uvSpline = target as UVSpline;
            if (uvSpline != null && uvSpline.Container != null && uvSpline.Container.Spline == spline)
                RequestPreview();
        }

        void OnUndoRedo()
        {
            RequestPreview();
            SceneView.RepaintAll();
            Repaint();
        }

        void OnSharedUvModeChanged()
        {
            m_SceneHandleMode = (SceneHandleMode)MBEditorToolState.UvMode;
            Repaint();
            SceneView.RepaintAll();
        }

        void RequestPreview()
        {
            var uvSpline = target as UVSpline;
            if (uvSpline == null || !uvSpline.LivePreview || !CanBuild(uvSpline) || m_PreviewQueued)
                return;

            m_PreviewQueued = true;
            EditorApplication.delayCall -= ProcessQueuedPreview;
            EditorApplication.delayCall += ProcessQueuedPreview;
        }

        void ProcessQueuedPreview()
        {
            EditorApplication.delayCall -= ProcessQueuedPreview;
            m_PreviewQueued = false;
            if (this == null)
                return;
            var uvSpline = target as UVSpline;
            if (uvSpline != null && uvSpline.LivePreview && CanBuild(uvSpline))
                RefreshPreview(uvSpline);
        }

        void RefreshPreviewImmediately(UVSpline uvSpline)
        {
            EditorApplication.delayCall -= ProcessQueuedPreview;
            m_PreviewQueued = false;
            if (uvSpline != null && uvSpline.LivePreview && CanBuild(uvSpline))
                RefreshPreview(uvSpline);
        }

        static bool CanBuild(UVSpline uvSpline)
        {
            return uvSpline != null && uvSpline.Target != null && uvSpline.Target.sharedMesh != null && uvSpline.Container.Spline.Count >= 2;
        }

        void RefreshPreview(UVSpline uvSpline)
        {
            m_PreviewMesh = uvSpline.RebuildOutputMesh();
            if (m_PreviewMesh == null)
                return;

            SceneView.RepaintAll();
        }

        void RestorePreview()
        {
            var uvSpline = target as UVSpline;
            if (uvSpline != null)
            {
                Undo.RecordObject(uvSpline, "Cancel UV Spline Preview");
                if (uvSpline.Target != null)
                    Undo.RecordObject(uvSpline.Target, "Cancel UV Spline Preview");
                uvSpline.RestoreSourceMesh();
                EditorUtility.SetDirty(uvSpline);
            }
            m_PreviewMesh = null;
        }

        void SaveMeshAsset(UVSpline uvSpline)
        {
            RestorePreview();
            Mesh mesh = uvSpline.CreateUvMesh();
            if (mesh == null)
                return;

            Mesh sourceMesh = uvSpline.SourceMesh;
            string defaultName = (sourceMesh != null ? sourceMesh.name : uvSpline.Target.sharedMesh.name) + "_UVSpline.asset";
            string path = EditorUtility.SaveFilePanelInProject("Save UV Spline Mesh", defaultName, "asset", "Choose where to save the UV-adjusted mesh.");
            if (string.IsNullOrEmpty(path))
            {
                DestroyImmediate(mesh);
                return;
            }

            path = AssetDatabase.GenerateUniqueAssetPath(path);
            mesh.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            Undo.RecordObject(uvSpline.Target, "Assign UV Spline Mesh");
            Undo.RecordObject(uvSpline, "Assign UV Spline Mesh");
            uvSpline.AdoptSourceMesh(mesh);
            EditorUtility.SetDirty(uvSpline);
            Selection.activeObject = mesh;
        }
    }

    [InitializeOnLoad]
    internal static class UVSplineSceneSelection
    {
        static UVSpline s_QueuedSelection;

        static UVSplineSceneSelection()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged -= OnSelectionChanged;
            Selection.selectionChanged += OnSelectionChanged;
        }

        static void OnSceneGUI(SceneView sceneView)
        {
            if (!MBEditorToolState.ActiveEditing
                || MBEditorToolState.Mode != MBEditorAuthoringMode.UVSpline)
                return;

            Event current = Event.current;
            if (current.type != EventType.MouseDown
                || current.button != 0
                || !current.shift
                || current.alt
                || current.control
                || current.command
                || GUIUtility.hotControl != 0)
                return;

            if (!TryPickLoftMesh(current.mousePosition, out MultiSplineLoft loft))
                return;

            UVSpline uvSpline = loft.GeneratedUvSpline != null
                ? loft.GeneratedUvSpline
                : loft.GetComponentInChildren<UVSpline>(true);
            if (uvSpline != null)
                return;

            bool create = EditorUtility.DisplayDialog(
                "Add UV Spline",
                $"'{loft.name}' does not have a UV spline. Create and generate one now?",
                "Create UV Spline",
                "Cancel");
            if (create)
                MultiSplineLoftEditor.GenerateUvSpline(loft);

            current.Use();
            sceneView.Repaint();
        }

        static void OnSelectionChanged()
        {
            if (!MBEditorToolState.ActiveEditing
                || MBEditorToolState.Mode != MBEditorAuthoringMode.UVSpline)
                return;

            UVSpline uvSpline = FindUvSplineForSelection(Selection.activeGameObject);
            if (uvSpline == null || Selection.activeGameObject == uvSpline.gameObject)
                return;

            s_QueuedSelection = uvSpline;
            EditorApplication.delayCall -= ApplyQueuedSelection;
            EditorApplication.delayCall += ApplyQueuedSelection;
        }

        static void ApplyQueuedSelection()
        {
            EditorApplication.delayCall -= ApplyQueuedSelection;
            UVSpline queued = s_QueuedSelection;
            s_QueuedSelection = null;
            if (queued == null
                || !MBEditorToolState.ActiveEditing
                || MBEditorToolState.Mode != MBEditorAuthoringMode.UVSpline
                || FindUvSplineForSelection(Selection.activeGameObject) != queued)
                return;

            Selection.activeGameObject = queued.gameObject;
            EditorGUIUtility.PingObject(queued.gameObject);
            SceneView.RepaintAll();
        }

        static UVSpline FindUvSplineForSelection(GameObject selected)
        {
            if (selected == null)
                return null;

            UVSpline selectedUvSpline = selected.GetComponent<UVSpline>()
                ?? selected.GetComponentInParent<UVSpline>();
            if (selectedUvSpline != null)
                return selectedUvSpline;

            MultiSplineLoft loft = selected.GetComponent<MultiSplineLoft>()
                ?? selected.GetComponentInParent<MultiSplineLoft>();
            if (loft == null)
                return null;

            return loft.GeneratedUvSpline != null
                ? loft.GeneratedUvSpline
                : loft.GetComponentInChildren<UVSpline>(true);
        }

        static bool TryPickLoftMesh(Vector2 mousePosition, out MultiSplineLoft pickedLoft)
        {
            pickedLoft = null;
            var loftMeshes = new List<GameObject>();
            var seenObjects = new HashSet<GameObject>();

            foreach (MultiSplineLoft loft in Resources.FindObjectsOfTypeAll<MultiSplineLoft>())
            {
                if (loft == null
                    || EditorUtility.IsPersistent(loft)
                    || !loft.gameObject.scene.IsValid()
                    || !loft.gameObject.activeInHierarchy)
                    continue;

                foreach (MeshFilter meshFilter in loft.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (meshFilter != null
                        && meshFilter.sharedMesh != null
                        && meshFilter.gameObject.activeInHierarchy
                        && seenObjects.Add(meshFilter.gameObject))
                        loftMeshes.Add(meshFilter.gameObject);
                }

                foreach (Renderer renderer in loft.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer != null
                        && renderer.gameObject.activeInHierarchy
                        && seenObjects.Add(renderer.gameObject))
                        loftMeshes.Add(renderer.gameObject);
                }
            }

            if (loftMeshes.Count == 0)
                return false;

            GameObject picked = HandleUtility.PickGameObject(
                mousePosition,
                false,
                null,
                loftMeshes.ToArray());
            if (picked == null)
                return false;

            pickedLoft = picked.GetComponent<MultiSplineLoft>()
                ?? picked.GetComponentInParent<MultiSplineLoft>();
            return pickedLoft != null;
        }
    }
}

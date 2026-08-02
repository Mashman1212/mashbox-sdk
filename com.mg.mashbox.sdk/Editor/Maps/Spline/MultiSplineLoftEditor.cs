using System.Collections.Generic;
using System.IO;
using MashBoxSDK.EditorResources;
using MashBoxSDK.MapTools;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditorInternal;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Splines;

namespace MashBoxSDK.Maps.Spline
{
    [CustomEditor(typeof(MultiSplineLoft))]
    public sealed class MultiSplineLoftEditor : Editor
    {
        ReorderableList m_SourceList;
        SerializedProperty m_Sources;
        SerializedProperty m_SamplesAlong;
        SerializedProperty m_SegmentsAcross;
        SerializedProperty m_AcrossInterpolation;
        SerializedProperty m_AlongResolutionMode;
        SerializedProperty m_AlongAlignment;
        SerializedProperty m_AlignmentReferenceSource;
        SerializedProperty m_TargetSegmentLength;
        SerializedProperty m_MaxDistanceSamples;
        SerializedProperty m_ResolutionZones;
        SerializedProperty m_GenerateResolutionSplineWithLoft;
        SerializedProperty m_ResolutionSplinePointCount;
        SerializedProperty m_ResolutionSpline;
        SerializedProperty m_AutoRegenerate;
        SerializedProperty m_AutoRegenerateDelay;
        SerializedProperty m_CloseAlongClosedSplines;
        SerializedProperty m_CloseAcrossSplines;
        SerializedProperty m_CapStart;
        SerializedProperty m_CapEnd;
        SerializedProperty m_DoubleSided;
        SerializedProperty m_UpdateMeshCollider;
        SerializedProperty m_ColliderChunkLength;
        SerializedProperty m_NormalMode;
        SerializedProperty m_FlipNormals;
        SerializedProperty m_MatchSideNormalsToTerrain;
        SerializedProperty m_MatchTerrainIntersectingFaces;
        SerializedProperty m_TerrainNormalContactDistance;
        SerializedProperty m_UvScaleAlong;
        SerializedProperty m_UvScaleAcross;
        SerializedProperty m_MatchUv0LengthToWidth;
        SerializedProperty m_Uv0AlongRatioMultiplier;
        SerializedProperty m_GeneratePackedUv2;
        SerializedProperty m_PackedUv2Padding;
        SerializedProperty m_GenerateUvSplineWithLoft;
        SerializedProperty m_UvSplineChannel;
        SerializedProperty m_UvSplineDirection;
        SerializedProperty m_UvSplinePointCount;
        SerializedProperty m_UvSpline;
        SerializedProperty m_SculptModifier;
        SerializedProperty m_VertexPaintModifier;
        SerializedProperty m_ShoulderModifier;
        SerializedProperty m_HeightOverlayModifier;
        SerializedProperty m_GeneratedMesh;

        void OnEnable()
        {
            m_Sources = serializedObject.FindProperty("m_Sources");
            m_SamplesAlong = serializedObject.FindProperty("m_SamplesAlong");
            m_SegmentsAcross = serializedObject.FindProperty("m_SegmentsAcross");
            m_AcrossInterpolation = serializedObject.FindProperty("m_AcrossInterpolation");
            m_AlongResolutionMode = serializedObject.FindProperty("m_AlongResolutionMode");
            m_AlongAlignment = serializedObject.FindProperty("m_AlongAlignment");
            m_AlignmentReferenceSource = serializedObject.FindProperty("m_AlignmentReferenceSource");
            m_TargetSegmentLength = serializedObject.FindProperty("m_TargetSegmentLength");
            m_MaxDistanceSamples = serializedObject.FindProperty("m_MaxDistanceSamples");
            m_ResolutionZones = serializedObject.FindProperty("m_ResolutionZones");
            m_GenerateResolutionSplineWithLoft = serializedObject.FindProperty("m_GenerateResolutionSplineWithLoft");
            m_ResolutionSplinePointCount = serializedObject.FindProperty("m_ResolutionSplinePointCount");
            m_ResolutionSpline = serializedObject.FindProperty("m_ResolutionSpline");
            m_AutoRegenerate = serializedObject.FindProperty("m_AutoRegenerate");
            m_AutoRegenerateDelay = serializedObject.FindProperty("m_AutoRegenerateDelay");
            m_CloseAlongClosedSplines = serializedObject.FindProperty("m_CloseAlongClosedSplines");
            m_CloseAcrossSplines = serializedObject.FindProperty("m_CloseAcrossSplines");
            m_CapStart = serializedObject.FindProperty("m_CapStart");
            m_CapEnd = serializedObject.FindProperty("m_CapEnd");
            m_DoubleSided = serializedObject.FindProperty("m_DoubleSided");
            m_UpdateMeshCollider = serializedObject.FindProperty("m_UpdateMeshCollider");
            m_ColliderChunkLength = serializedObject.FindProperty("m_ColliderChunkLength");
            m_NormalMode = serializedObject.FindProperty("m_NormalMode");
            m_FlipNormals = serializedObject.FindProperty("m_FlipNormals");
            m_MatchSideNormalsToTerrain = serializedObject.FindProperty("m_MatchSideNormalsToTerrain");
            m_MatchTerrainIntersectingFaces = serializedObject.FindProperty("m_MatchTerrainIntersectingFaces");
            m_TerrainNormalContactDistance = serializedObject.FindProperty("m_TerrainNormalContactDistance");
            m_UvScaleAlong = serializedObject.FindProperty("m_UvScaleAlong");
            m_UvScaleAcross = serializedObject.FindProperty("m_UvScaleAcross");
            m_MatchUv0LengthToWidth = serializedObject.FindProperty("m_MatchUv0LengthToWidth");
            m_Uv0AlongRatioMultiplier = serializedObject.FindProperty("m_Uv0AlongRatioMultiplier");
            m_GeneratePackedUv2 = serializedObject.FindProperty("m_GeneratePackedUv2");
            m_PackedUv2Padding = serializedObject.FindProperty("m_PackedUv2Padding");
            m_GenerateUvSplineWithLoft = serializedObject.FindProperty("m_GenerateUvSplineWithLoft");
            m_UvSplineChannel = serializedObject.FindProperty("m_UvSplineChannel");
            m_UvSplineDirection = serializedObject.FindProperty("m_UvSplineDirection");
            m_UvSplinePointCount = serializedObject.FindProperty("m_UvSplinePointCount");
            m_UvSpline = serializedObject.FindProperty("m_UvSpline");
            m_SculptModifier = serializedObject.FindProperty("m_SculptModifier");
            m_VertexPaintModifier = serializedObject.FindProperty("m_VertexPaintModifier");
            m_ShoulderModifier = serializedObject.FindProperty("m_ShoulderModifier");
            m_HeightOverlayModifier = serializedObject.FindProperty("m_HeightOverlayModifier");
            m_GeneratedMesh = serializedObject.FindProperty("m_GeneratedMesh");

            m_SourceList = new ReorderableList(serializedObject, m_Sources, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Loft Curves"),
                elementHeightCallback = index => EditorGUIUtility.singleLineHeight * 3f + 14f,
                drawElementCallback = DrawSourceElement,
                onAddCallback = AddEmptySource
            };
        }

        public override void OnInspectorGUI()
        {
            MashBoxInspectorHeaderUtility.DrawScriptHeader();

            serializedObject.Update();
            var loft = (MultiSplineLoft)target;
            EnsureSourceSplinesFollowLoft(loft);

            EditorGUILayout.Space();
            DrawToolbar();

            EditorGUILayout.Space(4f);
            m_SourceList.DoLayoutList();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Surface", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_AlongResolutionMode, new GUIContent("Along Resolution"));
            if (m_AlongResolutionMode.enumValueIndex == (int)MultiSplineLoft.AlongResolutionMode.Distance)
            {
                EditorGUILayout.PropertyField(m_TargetSegmentLength, new GUIContent("Target Segment Length"));
                EditorGUILayout.PropertyField(m_MaxDistanceSamples, new GUIContent("Max Distance Samples"));
                EditorGUILayout.PropertyField(m_ResolutionZones, new GUIContent("Resolution Zones"), true);
            }
            else
            {
                EditorGUILayout.PropertyField(m_SamplesAlong, new GUIContent("Samples Along"));
            }

            EditorGUILayout.PropertyField(m_AlongAlignment, new GUIContent("Cross-Section Alignment"));
            if (m_AlongAlignment.enumValueIndex == (int)MultiSplineLoft.AlongAlignment.ReferencePerpendicular)
            {
                int reference = m_AlignmentReferenceSource.intValue;
                reference = EditorGUILayout.IntField(new GUIContent("Reference Source", "Use -1 to automatically use the middle valid loft curve."), reference);
                m_AlignmentReferenceSource.intValue = Mathf.Clamp(reference, -1, Mathf.Max(-1, m_Sources.arraySize - 1));
                EditorGUILayout.HelpBox("Each cross-section is matched to a plane perpendicular to the reference curve. Reference Source -1 uses the middle valid curve.", MessageType.Info);
            }

            EditorGUILayout.PropertyField(m_SegmentsAcross, new GUIContent("Segments Across"));
            EditorGUILayout.PropertyField(m_AcrossInterpolation, new GUIContent("Across Interpolation"));
            EditorGUILayout.PropertyField(m_CloseAlongClosedSplines, new GUIContent("Close Along Closed Splines"));
            EditorGUILayout.PropertyField(m_CloseAcrossSplines, new GUIContent("Close Across Splines"));
            EditorGUILayout.PropertyField(m_CapStart, new GUIContent("Cap Start"));
            EditorGUILayout.PropertyField(m_CapEnd, new GUIContent("Cap End"));
            EditorGUILayout.PropertyField(m_DoubleSided, new GUIContent("Double Sided"));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_NormalMode);
            EditorGUILayout.PropertyField(m_FlipNormals, new GUIContent("Flip Normals"));
            EditorGUILayout.PropertyField(
                m_MatchSideNormalsToTerrain,
                new GUIContent(
                    "Match Terrain Contact Normals",
                    "Matches the loft side edges and any generated shoulders to the Terrain underneath."));
            using (new EditorGUI.DisabledScope(!m_MatchSideNormalsToTerrain.boolValue))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    m_MatchTerrainIntersectingFaces,
                    new GUIContent(
                        "Include Intersecting Faces",
                        "Also matches normals for loft and shoulder triangles that cross or closely touch the Terrain."));
                using (new EditorGUI.DisabledScope(!m_MatchTerrainIntersectingFaces.boolValue))
                    EditorGUILayout.PropertyField(
                        m_TerrainNormalContactDistance,
                        new GUIContent("Contact Distance", "How close a triangle vertex can be to the Terrain and still count as touching it."));
                EditorGUI.indentLevel--;
            }
            if (m_MatchSideNormalsToTerrain.boolValue &&
                m_NormalMode.enumValueIndex == (int)MultiSplineLoft.NormalMode.Face)
                EditorGUILayout.HelpBox(
                    "With Face normals, only intersecting faces can be matched; the shared side-edge vertices do not exist in this mode.",
                    MessageType.None);
            EditorGUILayout.PropertyField(
                m_UvScaleAcross,
                new GUIContent("UV Across Scale", "1 maps the full left-to-right loft width to U 0-1, preserving the original loft UV layout."));
            EditorGUILayout.PropertyField(
                m_MatchUv0LengthToWidth,
                new GUIContent(
                    "Match Length To Width",
                    "Automatically derives the along tiling from the physical loft width so a square texture keeps square proportions."));
            if (m_MatchUv0LengthToWidth.boolValue)
            {
                EditorGUILayout.PropertyField(
                    m_Uv0AlongRatioMultiplier,
                    new GUIContent("Along Ratio Multiplier", "Leave at 1 for square proportions. Use this only for intentional stretching or non-square source textures."));
                EditorGUILayout.HelpBox(
                    "The loft still maps its complete width to UV0 U. The V scale is calculated from that physical width, so the same texture footprint is used along the loft without squishing.",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.PropertyField(
                    m_UvScaleAlong,
                    new GUIContent("UV Along Scale", "Manual UV repeats per metre along the loft."));
            }
            EditorGUILayout.PropertyField(
                m_GeneratePackedUv2,
                new GUIContent(
                    "Generate Packed UV2",
                    "Creates paintable road-strip shells in UV2 / TEXCOORD2 while leaving UV1 / TEXCOORD1 free for lightmaps."));
            using (new EditorGUI.DisabledScope(!m_GeneratePackedUv2.boolValue))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    m_PackedUv2Padding,
                    new GUIContent("Shell Padding", "Empty border inside every packed shell cell."));
                EditorGUI.indentLevel--;
                EditorGUILayout.HelpBox(
                    "Packed UV2 writes TEXCOORD2 (Unity's mesh.uv3), leaving TEXCOORD1 / the lightmap UV channel untouched. The loft is automatically chopped into roughly square world-space islands and packed into 0-1 with duplicated seam vertices.",
                    MessageType.None);
            }
            EditorGUILayout.PropertyField(m_UpdateMeshCollider, new GUIContent("Generate Collider Chunks"));
            using (new EditorGUI.DisabledScope(!m_UpdateMeshCollider.boolValue))
                EditorGUILayout.PropertyField(m_ColliderChunkLength, new GUIContent("Collider Chopping Distance", "Creates a separate child MeshCollider for approximately this many meters of track."));
            EditorGUILayout.PropertyField(m_AutoRegenerate, new GUIContent("Live Regenerate"));
            using (new EditorGUI.DisabledScope(!m_AutoRegenerate.boolValue))
                EditorGUILayout.PropertyField(m_AutoRegenerateDelay, new GUIContent("Live Regenerate Delay", "Coalesces rapid spline edits before rebuilding the loft."));
            EditorGUILayout.PropertyField(m_SculptModifier, new GUIContent("Sculpt Modifier", "Replays recorded sculpt strokes after every loft regeneration."));
            EditorGUILayout.PropertyField(m_VertexPaintModifier, new GUIContent("Vertex Paint Modifier", "Replays recorded local vertex-paint strokes after every loft regeneration."));
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(m_ShoulderModifier, new GUIContent("Shoulder Modifier"));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(loft.ShoulderModifier == null ? "Add Shoulder Profiles" : "Rebuild Shoulders"))
                    CreateOrRebuildShoulders(loft);
                using (new EditorGUI.DisabledScope(loft.ShoulderModifier == null))
                {
                    if (GUILayout.Button("Select Shoulder Profiles"))
                        Selection.activeGameObject = loft.ShoulderModifier.gameObject;
                }
            }
            if (GUILayout.Button("Apply Eroded Trail Banks Preset"))
                CreateOrRebuildShoulders(loft, applyErodedTrailPreset: true);

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(m_HeightOverlayModifier, new GUIContent("MicroBump Layer Modifier"));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(loft.HeightOverlayModifier == null ? "Add MicroBump Layer" : "Rebuild MicroBump Layer"))
                    CreateOrRebuildHeightOverlay(loft);
                using (new EditorGUI.DisabledScope(loft.HeightOverlayModifier == null))
                {
                    if (GUILayout.Button("Select MicroBump Layer"))
                        Selection.activeGameObject = loft.HeightOverlayModifier.gameObject;
                }
            }
            EditorGUILayout.HelpBox(
                "Creates a separate visual mesh. UV2 remains the splat-paint channel; UV3/TEXCOORD3 is generated privately for the height bake. The original loft and collider chunks are unchanged.",
                MessageType.None);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Resolution Spline", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Generate an editable centerline profile. Select its cyan Scene points and use the scale handle to multiply local mesh density.", MessageType.None);
            EditorGUILayout.PropertyField(m_GenerateResolutionSplineWithLoft, new GUIContent("Generate With Loft"));
            EditorGUILayout.PropertyField(m_ResolutionSplinePointCount, new GUIContent("Generated Points"));
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(m_ResolutionSpline, new GUIContent("Generated Spline"));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate / Refresh"))
                    GenerateResolutionSpline(loft);
                using (new EditorGUI.DisabledScope(loft.GeneratedResolutionSpline == null))
                {
                    if (GUILayout.Button("Select Resolution Spline"))
                        Selection.activeGameObject = loft.GeneratedResolutionSpline.gameObject;
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("UV Spline", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_GenerateUvSplineWithLoft, new GUIContent("Generate With Loft"));
            EditorGUILayout.PropertyField(m_UvSplineChannel, new GUIContent("UV Channel"));
            EditorGUILayout.PropertyField(m_UvSplineDirection, new GUIContent("UV Direction"));
            EditorGUILayout.PropertyField(m_UvSplinePointCount, new GUIContent("Generated Points"));

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Generated Samples Along", loft.CurrentSamplesAlong);
                EditorGUILayout.PropertyField(m_GeneratedMesh, new GUIContent("Generated Mesh"));
                EditorGUILayout.PropertyField(m_UvSpline, new GUIContent("Generated UV Spline"));
            }

            bool changed = serializedObject.ApplyModifiedProperties();

            if (changed && loft.AutoRegenerate)
                QueueGenerate(loft);

            DrawActionButtons(loft);
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Selected", GUILayout.Height(24f)))
                    AddSelectedToTargets();

                if (GUILayout.Button("Clear", GUILayout.Height(24f)))
                    ClearSources();
            }
        }

        void DrawActionButtons(MultiSplineLoft loft)
        {
            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate Now", GUILayout.Height(26f)))
                {
                    Undo.RecordObject(loft, "Generate Multi Spline Loft");
                    loft.Regenerate();
                    EditorUtility.SetDirty(loft);
                }

                if (GUILayout.Button("Bake Mesh Asset", GUILayout.Height(26f)))
                    BakeMesh(loft);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate UV Spline", GUILayout.Height(26f)))
                    GenerateUvSpline(loft);

                using (new EditorGUI.DisabledScope(loft.GeneratedUvSpline == null))
                {
                    if (GUILayout.Button("Select UV Spline", GUILayout.Height(26f)))
                        Selection.activeGameObject = loft.GeneratedUvSpline.gameObject;
                }
            }
        }

        internal static bool GenerateUvSpline(MultiSplineLoft loft)
        {
            if (loft == null)
                return false;

            Undo.RecordObject(loft, "Generate UV Spline");
            if (!loft.RegenerateUvSpline(out string error))
            {
                EditorUtility.DisplayDialog("Generate UV Spline", error, "OK");
                return false;
            }

            EditorUtility.SetDirty(loft);
            EditorUtility.SetDirty(loft.GeneratedUvSpline);
            EditorUtility.SetDirty(loft.GeneratedUvSpline.Container);
            Selection.activeGameObject = loft.GeneratedUvSpline.gameObject;
            InternalEditorUtility.RepaintAllViews();
            SceneView.RepaintAll();
            return true;
        }

        static void GenerateResolutionSpline(MultiSplineLoft loft)
        {
            Undo.RecordObject(loft, "Generate Loft Resolution Spline");
            if (!loft.GenerateResolutionSpline(out string error))
            {
                EditorUtility.DisplayDialog("Generate Resolution Spline", error, "OK");
                return;
            }

            EditorUtility.SetDirty(loft);
            EditorUtility.SetDirty(loft.GeneratedResolutionSpline);
            EditorUtility.SetDirty(loft.GeneratedResolutionSpline.Container);
            Selection.activeGameObject = loft.GeneratedResolutionSpline.gameObject;
            SceneView.RepaintAll();
        }

        internal static void CreateOrRebuildShoulders(MultiSplineLoft loft, bool applyErodedTrailPreset = false)
        {
            Transform shouldersRoot = loft.transform.Find("Shoulders");
            if (shouldersRoot == null)
            {
                var shouldersObject = new GameObject("Shoulders");
                Undo.RegisterCreatedObjectUndo(shouldersObject, "Create Loft Shoulders");
                Undo.SetTransformParent(shouldersObject.transform, loft.transform, "Parent Loft Shoulders");
                shouldersObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                shouldersObject.transform.localScale = Vector3.one;
                shouldersRoot = shouldersObject.transform;
            }

            shouldersRoot.gameObject.layer = loft.gameObject.layer;
            shouldersRoot.gameObject.isStatic = loft.gameObject.isStatic;

            LoftShoulderModifier previousModifier = loft.ShoulderModifier;
            LoftShoulderModifier modifier = shouldersRoot.GetComponent<LoftShoulderModifier>();
            if (modifier == null)
                modifier = Undo.AddComponent<LoftShoulderModifier>(shouldersRoot.gameObject);

            if (previousModifier != null && previousModifier != modifier)
            {
                Undo.RecordObject(modifier, "Move Loft Shoulder Profiles");
                EditorUtility.CopySerialized(previousModifier, modifier);
                Undo.DestroyObjectImmediate(previousModifier);
            }

            Undo.RecordObject(loft, "Assign Loft Shoulder Profiles");
            modifier.Loft = loft;
            loft.ShoulderModifier = modifier;
            if (applyErodedTrailPreset)
            {
                Undo.RecordObject(modifier, "Apply Eroded Trail Banks");
                modifier.ApplyErodedTrailPreset();
            }

            loft.Regenerate();
            EditorUtility.SetDirty(loft);
            EditorUtility.SetDirty(modifier);
            Selection.activeGameObject = modifier.gameObject;
            SceneView.RepaintAll();
        }

        internal static void CreateOrRebuildHeightOverlay(MultiSplineLoft loft)
        {
            if (loft == null)
                return;

            Transform overlayRoot = loft.transform.Find(LoftHeightOverlayModifier.GeneratedObjectName);
            if (overlayRoot == null)
                overlayRoot = loft.transform.Find(LoftHeightOverlayModifier.LegacyGeneratedObjectName);
            if (overlayRoot == null)
            {
                var overlayObject = new GameObject(LoftHeightOverlayModifier.GeneratedObjectName);
                Undo.RegisterCreatedObjectUndo(overlayObject, "Create Loft MicroBump Layer");
                Undo.SetTransformParent(overlayObject.transform, loft.transform, "Parent Loft MicroBump Layer");
                overlayObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                overlayObject.transform.localScale = Vector3.one;
                overlayRoot = overlayObject.transform;
            }

            overlayRoot.gameObject.name = LoftHeightOverlayModifier.GeneratedObjectName;
            overlayRoot.gameObject.isStatic = loft.gameObject.isStatic;
            LoftHeightOverlayModifier.ApplyGeneratedIdentity(overlayRoot.gameObject);
            LoftHeightOverlayModifier previousModifier = loft.HeightOverlayModifier;
            LoftHeightOverlayModifier modifier = overlayRoot.GetComponent<LoftHeightOverlayModifier>();
            if (modifier == null)
                modifier = Undo.AddComponent<LoftHeightOverlayModifier>(overlayRoot.gameObject);

            if (previousModifier != null && previousModifier != modifier)
            {
                Undo.RecordObject(modifier, "Move Loft MicroBump Layer");
                EditorUtility.CopySerialized(previousModifier, modifier);
                Undo.DestroyObjectImmediate(previousModifier);
            }

            Undo.RecordObject(loft, "Assign Loft MicroBump Layer");
            Undo.RecordObject(modifier, "Rebuild Loft MicroBump Layer");
            modifier.LinkToLoft(loft);
            loft.HeightOverlayModifier = modifier;
            if (!modifier.Rebuild())
                EditorUtility.DisplayDialog("Loft MicroBump Layer", modifier.LastError, "OK");

            EditorUtility.SetDirty(loft);
            EditorUtility.SetDirty(modifier);
            Selection.activeGameObject = modifier.gameObject;
            SceneView.RepaintAll();
        }

        internal static void EnsureSourceSplinesFollowLoft(MultiSplineLoft loft)
        {
            if (loft == null)
                return;

            var sourceTransforms = new List<Transform>();
            foreach (MultiSplineLoft.SplineSource source in loft.Sources)
            {
                Transform sourceTransform = source?.container != null ? source.container.transform : null;
                if (sourceTransform == null || sourceTransform == loft.transform || sourceTransforms.Contains(sourceTransform))
                    continue;
                // Reparenting an ancestor beneath the loft would create a cycle.
                if (loft.transform.IsChildOf(sourceTransform))
                    continue;
                sourceTransforms.Add(sourceTransform);
            }

            if (sourceTransforms.Count == 0)
                return;

            foreach (Transform sourceTransform in sourceTransforms)
            {
                if (sourceTransform.parent != loft.transform)
                    Undo.SetTransformParent(sourceTransform, loft.transform, "Attach Source Spline To Loft");
            }

            Transform obsoleteSourceRoot = loft.transform.Find("Source Splines");
            if (obsoleteSourceRoot != null && obsoleteSourceRoot.childCount == 0)
                Undo.DestroyObjectImmediate(obsoleteSourceRoot.gameObject);
        }

        void DrawSourceElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = m_Sources.GetArrayElementAtIndex(index);
            var container = element.FindPropertyRelative("container");
            var splineIndex = element.FindPropertyRelative("splineIndex");
            var reverse = element.FindPropertyRelative("reverse");

            rect.y += 4f;
            float line = EditorGUIUtility.singleLineHeight;
            var containerRect = new Rect(rect.x, rect.y, rect.width, line);
            EditorGUI.PropertyField(containerRect, container, new GUIContent("Container"));

            rect.y += line + 4f;
            var indexRect = new Rect(rect.x, rect.y, rect.width * 0.62f, line);
            var reverseRect = new Rect(rect.x + rect.width * 0.66f, rect.y, rect.width * 0.34f, line);

            int maxIndex = 0;
            if (container.objectReferenceValue is SplineContainer splineContainer)
                maxIndex = Mathf.Max(0, splineContainer.Splines.Count - 1);

            splineIndex.intValue = EditorGUI.IntSlider(indexRect, "Spline Index", Mathf.Clamp(splineIndex.intValue, 0, maxIndex), 0, maxIndex);
            EditorGUI.PropertyField(reverseRect, reverse, new GUIContent("Reverse"));

            rect.y += line + 4f;
            var statusRect = new Rect(rect.x, rect.y, rect.width, line);
            EditorGUI.LabelField(statusRect, GetSourceStatus(container.objectReferenceValue as SplineContainer, splineIndex.intValue), EditorStyles.miniLabel);
        }

        static string GetSourceStatus(SplineContainer container, int splineIndex)
        {
            if (container == null)
                return "Missing SplineContainer";

            if (container.Splines == null || container.Splines.Count == 0)
                return "Container has no splines";

            if (splineIndex < 0 || splineIndex >= container.Splines.Count)
                return "Spline index is out of range";

            var spline = container.Splines[splineIndex];
            if (spline == null || spline.Count < 2)
                return "Spline needs at least two knots";

            return $"{spline.Count} knots, {(spline.Closed ? "closed" : "open")}";
        }

        void AddEmptySource(ReorderableList list)
        {
            m_Sources.arraySize++;
            var element = m_Sources.GetArrayElementAtIndex(m_Sources.arraySize - 1);
            element.FindPropertyRelative("container").objectReferenceValue = null;
            element.FindPropertyRelative("splineIndex").intValue = 0;
            element.FindPropertyRelative("reverse").boolValue = false;
        }

        void AddSelectedToTargets()
        {
            var containers = GetSelectedSplineContainers();
            if (containers.Count == 0)
                return;

            foreach (var currentTarget in targets)
            {
                var loft = (MultiSplineLoft)currentTarget;
                Undo.RecordObject(loft, "Add Selected Splines");

                foreach (var container in containers)
                    loft.AddSelectedSpline(container);

                EnsureSourceSplinesFollowLoft(loft);

                EditorUtility.SetDirty(loft);
            }
        }

        void ClearSources()
        {
            foreach (var currentTarget in targets)
            {
                var loft = (MultiSplineLoft)currentTarget;
                Undo.RecordObject(loft, "Clear Loft Splines");
                loft.ClearSources();
                EditorUtility.SetDirty(loft);
            }
        }

        static void QueueGenerate(MultiSplineLoft loft)
        {
            loft.QueueRegenerate();
            EditorUtility.SetDirty(loft);
            SceneView.RepaintAll();
        }

        static void BakeMesh(MultiSplineLoft loft)
        {
            loft.Regenerate();

            if (loft.GeneratedMesh == null || loft.GeneratedMesh.vertexCount == 0)
            {
                EditorUtility.DisplayDialog("Bake Multi Spline Loft", "Generate a valid loft mesh before baking.", "OK");
                return;
            }

            string defaultName = $"{loft.gameObject.name}_LoftMesh.asset";
            string path = EditorUtility.SaveFilePanelInProject("Bake Multi Spline Loft Mesh", defaultName, "asset", "Choose where to save the generated mesh asset.");
            if (string.IsNullOrEmpty(path))
                return;

            var bakedMesh = Object.Instantiate(loft.GeneratedMesh);
            bakedMesh.name = Path.GetFileNameWithoutExtension(path);

            AssetDatabase.CreateAsset(bakedMesh, AssetDatabase.GenerateUniqueAssetPath(path));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Undo.RecordObject(loft, "Assign Baked Loft Mesh");
            loft.SetGeneratedMesh(bakedMesh);

            var meshFilter = loft.GetComponent<MeshFilter>();
            Undo.RecordObject(meshFilter, "Assign Baked Loft Mesh");
            meshFilter.sharedMesh = bakedMesh;

            loft.Regenerate();
            EditorUtility.SetDirty(loft);
            EditorUtility.SetDirty(bakedMesh);
            AssetDatabase.SaveAssets();
            Selection.activeObject = bakedMesh;
        }

        static List<SplineContainer> GetSelectedSplineContainers()
        {
            var result = new List<SplineContainer>();
            var selection = Selection.GetFiltered<SplineContainer>(SelectionMode.Editable | SelectionMode.Deep);

            foreach (var container in selection)
            {
                if (container != null && !result.Contains(container))
                    result.Add(container);
            }

            return result;
        }

        public static void CreateLoftFromSelection()
        {
            var containers = GetSelectedSplineContainers();
            var gameObject = new GameObject("Multi-Spline Loft", typeof(MeshFilter), typeof(MeshRenderer), typeof(MultiSplineLoft));
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Multi-Spline Loft");

            if (Selection.activeTransform != null)
                gameObject.transform.SetPositionAndRotation(Selection.activeTransform.position, Selection.activeTransform.rotation);

            var loft = gameObject.GetComponent<MultiSplineLoft>();
            foreach (var container in containers)
                loft.AddSelectedSpline(container);

            EnsureSourceSplinesFollowLoft(loft);

            loft.Regenerate();
            Selection.activeGameObject = gameObject;
        }

        public static bool ValidateCreateLoftFromSelection()
        {
            return GetSelectedSplineContainers().Count > 0;
        }

        public static void OpenWindow()
        {
            MultiSplineLoftWindow.ShowWindow();
        }
    }

    public sealed class MultiSplineLoftWindow : EditorWindow
    {
        const float ReducedHandleCameraDistance = 100f;
        const float ReducedHandlePickRadius = 16f;
        static MultiSplineLoftWindow s_ActiveSceneToolOwner;

        public System.Action<UVSpline> UvSplineGenerated { get; set; }

        MultiSplineLoft m_ActiveLoft;
        Vector2 m_Scroll;
        bool m_SceneToolActive;
        bool m_ChangingSelection;
        bool m_SplineEditActivationQueued;
        UnityEngine.Object[] m_QueuedSplineTargets;
        int m_SplineToolActivationAttempts;
        SplineContainer m_QueuedKnotPlacementTarget;
        SplineContainer m_FocusedSpline;
        int m_FocusedSplineIndex = -1;
        int m_FocusedKnotIndex = -1;
        int m_FocusedTangent = -1;
        readonly List<ReducedSplineOverlayEntry> m_ReducedOverlayEntries = new List<ReducedSplineOverlayEntry>();
        MultiSplineLoft m_ReducedOverlayLoft;
        int m_ReducedOverlayGeneration = -1;

        sealed class ReducedSplineOverlayEntry
        {
            public SplineContainer container;
            public int splineIndex;
            public Vector3[] points;
        }

        internal static bool HasActiveSceneTool =>
            s_ActiveSceneToolOwner != null && s_ActiveSceneToolOwner.m_SceneToolActive;

        internal static void DeactivateActiveSceneTool()
        {
            s_ActiveSceneToolOwner?.DeactivateSceneTool();
        }

        public static void ShowWindow()
        {
            MultiSplineLoftWindow window = GetWindow<MultiSplineLoftWindow>("Multi-Spline Loft");
            window.ActivateSceneTool();
        }

        void OnGUI()
        {
            Draw();
        }

        void OnDisable()
        {
            DeactivateSceneTool();
        }

        public void ActivateSceneTool()
        {
            if (s_ActiveSceneToolOwner != null && s_ActiveSceneToolOwner != this)
                s_ActiveSceneToolOwner.DeactivateSceneTool();
            s_ActiveSceneToolOwner = this;

            if (m_SceneToolActive)
            {
                MultiSplineLoft selectedLoft = FindLoftInSelection();
                if (selectedLoft != null)
                    EnterUnitySplineEditMode(selectedLoft);
                return;
            }
            m_SceneToolActive = true;
            Selection.selectionChanged += OnSelectionChanged;
            SceneView.duringSceneGui += OnSceneGUI;
            UseSelectedLoftAndEditSplines();
        }

        public void DeactivateSceneTool()
        {
            if (!m_SceneToolActive)
            {
                if (s_ActiveSceneToolOwner == this)
                    s_ActiveSceneToolOwner = null;
                return;
            }
            m_SceneToolActive = false;
            if (s_ActiveSceneToolOwner == this)
                s_ActiveSceneToolOwner = null;
            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.delayCall -= ApplyQueuedSplineSelection;
            EditorApplication.delayCall -= ActivateQueuedSplineTool;
            EditorApplication.delayCall -= ApplyQueuedKnotPlacementSelection;
            EditorApplication.delayCall -= ActivateKnotPlacementTool;
            m_SplineEditActivationQueued = false;
            m_QueuedSplineTargets = null;
            if (ToolManager.activeContextType == typeof(SplineToolContext))
                ToolManager.SetActiveContext<GameObjectToolContext>();
        }

        void OnSelectionChanged()
        {
            if (!m_SceneToolActive || m_ChangingSelection) return;
            MultiSplineLoft selectedLoft = FindLoftInSelection();
            if (selectedLoft == null)
            {
                if (Selection.activeGameObject != null && !IsSourceSplineObject(Selection.activeGameObject))
                {
                    EditorApplication.delayCall -= ActivateQueuedSplineTool;
                    m_SplineEditActivationQueued = false;
                    m_QueuedSplineTargets = null;
                    if (ToolManager.activeContextType == typeof(SplineToolContext))
                        ToolManager.SetActiveContext<GameObjectToolContext>();
                }
                return;
            }

            EnterUnitySplineEditMode(selectedLoft);
            Repaint();
        }

        void OnSceneGUI(SceneView sceneView)
        {
            Event current = Event.current;
            if (m_SceneToolActive && m_ActiveLoft != null && UsesReducedSplineHandles())
            {
                DrawReducedSplineOverlay(sceneView, m_ActiveLoft);
                DrawFocusedKnot(sceneView);

                if (current.type == EventType.KeyDown
                    && current.keyCode == KeyCode.F
                    && !current.alt
                    && !current.control
                    && !current.command
                    && !current.shift
                    && TryGetFocusedKnotWorldPosition(out Vector3 focusedPosition))
                {
                    float framingSize = Mathf.Max(
                        1f,
                        HandleUtility.GetHandleSize(focusedPosition) * 1.5f);
                    sceneView.LookAt(focusedPosition, sceneView.rotation, framingSize);
                    current.Use();
                    return;
                }
            }

            // While editing a loft, empty clicks belong to the spline tool. This
            // prevents terrain and other scene meshes from stealing selection.
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (m_SceneToolActive && current.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            if (!m_SceneToolActive
                || current.type != EventType.MouseDown
                || current.button != 0
                || current.alt
                || current.control
                || current.command
                || current.shift)
            {
                return;
            }

            if (UsesReducedSplineHandles() && TryFocusKnot(current.mousePosition))
            {
                current.Use();
                return;
            }

            // Empty clicks remain owned by the spline editor. In particular, do
            // not forward them to the terrain or ordinary meshes underneath the
            // curve. Another loft is a valid editing target, however, so allow a
            // direct click on its generated mesh to switch the active loft.
            if (UsesReducedSplineHandles())
            {
                if (TryPickOtherLoft(current.mousePosition, out MultiSplineLoft pickedLoft))
                {
                    m_FocusedSpline = null;
                    m_FocusedSplineIndex = -1;
                    m_FocusedKnotIndex = -1;
                    m_FocusedTangent = -1;
                    Selection.activeGameObject = pickedLoft.gameObject;
                    current.Use();
                    return;
                }

                current.Use();
            }
        }

        bool TryPickOtherLoft(Vector2 mousePosition, out MultiSplineLoft pickedLoft)
        {
            pickedLoft = null;
            var loftObjects = new List<GameObject>();
            foreach (MultiSplineLoft loft in Resources.FindObjectsOfTypeAll<MultiSplineLoft>())
            {
                if (loft == null
                    || loft == m_ActiveLoft
                    || EditorUtility.IsPersistent(loft)
                    || !loft.gameObject.scene.IsValid()
                    || !loft.gameObject.activeInHierarchy)
                    continue;

                foreach (Transform child in loft.GetComponentsInChildren<Transform>(true))
                    loftObjects.Add(child.gameObject);
            }

            if (loftObjects.Count == 0)
                return false;

            // Restrict Unity's scene picker to other loft hierarchies. Terrain,
            // vegetation, and the current loft can no longer hide the intended
            // loft behind dozens of front-most pick results.
            GameObject picked = HandleUtility.PickGameObject(
                mousePosition,
                false,
                null,
                loftObjects.ToArray());
            if (picked == null)
                return false;

            pickedLoft = picked.GetComponent<MultiSplineLoft>()
                ?? picked.GetComponentInParent<MultiSplineLoft>();
            return pickedLoft != null && pickedLoft != m_ActiveLoft;
        }

        void DrawReducedSplineOverlay(SceneView sceneView, MultiSplineLoft loft)
        {
            RefreshReducedSplineOverlay(loft);
            Vector3 cameraPosition = sceneView.camera != null
                ? sceneView.camera.transform.position
                : Vector3.zero;
            float handleDistanceSquared = ReducedHandleCameraDistance * ReducedHandleCameraDistance;
            Handles.color = new Color(0.05f, 0.9f, 1f, 0.9f);
            foreach (ReducedSplineOverlayEntry entry in m_ReducedOverlayEntries)
                Handles.DrawAAPolyLine(3f, entry.points);

            foreach (MultiSplineLoft.SplineSource source in loft.Sources)
            {
                SplineContainer container = source?.container;
                if (container == null || source.splineIndex < 0 || source.splineIndex >= container.Splines.Count)
                    continue;

                var spline = container.Splines[source.splineIndex];
                // Keep the complete road visible, but only draw knot controls in
                // the camera's local editing window. This avoids asking Unity to
                // lay out thousands of distant handles on long multi-lofts.
                for (int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
                {
                    Vector3 knotPosition = container.transform.TransformPoint((Vector3)spline[knotIndex].Position);
                    if ((knotPosition - cameraPosition).sqrMagnitude > handleDistanceSquared)
                        continue;

                    bool focused = container == m_FocusedSpline
                        && source.splineIndex == m_FocusedSplineIndex
                        && knotIndex == m_FocusedKnotIndex;
                    float size = HandleUtility.GetHandleSize(knotPosition) * (focused ? 0.22f : 0.15f);
                    Handles.color = focused
                        ? new Color(1f, 0.88f, 0.15f, 1f)
                        : new Color(1f, 0.38f, 0.06f, 0.95f);
                    Handles.SphereHandleCap(0, knotPosition, Quaternion.identity, size, EventType.Repaint);
                }
            }
        }

        void RefreshReducedSplineOverlay(MultiSplineLoft loft)
        {
            if (m_ReducedOverlayLoft == loft
                && m_ReducedOverlayGeneration == loft.GenerationVersion)
                return;

            m_ReducedOverlayLoft = loft;
            m_ReducedOverlayGeneration = loft.GenerationVersion;
            int entryIndex = 0;

            foreach (MultiSplineLoft.SplineSource source in loft.Sources)
            {
                SplineContainer container = source?.container;
                if (container == null
                    || source.splineIndex < 0
                    || source.splineIndex >= container.Splines.Count)
                    continue;

                var spline = container.Splines[source.splineIndex];
                int curveCount = spline.Closed ? spline.Count : spline.Count - 1;
                if (curveCount <= 0)
                    continue;

                int sampleCount = Mathf.Clamp(curveCount * 3, 16, 96);
                ReducedSplineOverlayEntry entry;
                if (entryIndex < m_ReducedOverlayEntries.Count)
                    entry = m_ReducedOverlayEntries[entryIndex];
                else
                {
                    entry = new ReducedSplineOverlayEntry();
                    m_ReducedOverlayEntries.Add(entry);
                }

                entry.container = container;
                entry.splineIndex = source.splineIndex;
                if (entry.points == null || entry.points.Length != sampleCount + 1)
                    entry.points = new Vector3[sampleCount + 1];

                int cachedCurveIndex = -1;
                BezierCurve curve = default;
                for (int sample = 0; sample <= sampleCount; sample++)
                {
                    float curvePosition = sample == sampleCount
                        ? curveCount
                        : sample * curveCount / (float)sampleCount;
                    int curveIndex = sample == sampleCount
                        ? curveCount - 1
                        : Mathf.Min(Mathf.FloorToInt(curvePosition), curveCount - 1);
                    if (curveIndex != cachedCurveIndex)
                    {
                        cachedCurveIndex = curveIndex;
                        curve = spline.GetCurve(curveIndex);
                    }

                    float curveT = sample == sampleCount ? 1f : curvePosition - curveIndex;
                    entry.points[sample] = container.transform.TransformPoint(
                        (Vector3)CurveUtility.EvaluatePosition(curve, curveT));
                }

                entryIndex++;
            }

            if (entryIndex < m_ReducedOverlayEntries.Count)
                m_ReducedOverlayEntries.RemoveRange(entryIndex, m_ReducedOverlayEntries.Count - entryIndex);
        }

        bool TryFocusKnot(Vector2 mousePosition)
        {
            if (TryFocusVisibleKnot(mousePosition))
                return true;

            if (TryFocusTangent(mousePosition))
                return true;

            float bestCurveDistance = ReducedHandlePickRadius * ReducedHandlePickRadius;
            SplineContainer bestContainer = null;
            int bestSpline = -1;
            RefreshReducedSplineOverlay(m_ActiveLoft);
            foreach (ReducedSplineOverlayEntry entry in m_ReducedOverlayEntries)
            {
                Vector2 previous = HandleUtility.WorldToGUIPoint(entry.points[0]);
                for (int sample = 1; sample < entry.points.Length; sample++)
                {
                    Vector2 point = HandleUtility.WorldToGUIPoint(entry.points[sample]);
                    float distance = DistanceToSegmentSquared(mousePosition, previous, point);
                    if (distance < bestCurveDistance)
                    {
                        bestCurveDistance = distance;
                        bestContainer = entry.container;
                        bestSpline = entry.splineIndex;
                    }
                    previous = point;
                }
            }

            if (bestContainer == null)
                return false;

            // Clicking anywhere on a visible curve focuses its nearest authored
            // knot; the user does not need to hit a tiny point exactly.
            var bestSourceSpline = bestContainer.Splines[bestSpline];
            float bestKnotDistance = float.PositiveInfinity;
            int bestKnot = -1;
            for (int knotIndex = 0; knotIndex < bestSourceSpline.Count; knotIndex++)
            {
                Vector3 world = bestContainer.transform.TransformPoint((Vector3)bestSourceSpline[knotIndex].Position);
                float distance = (HandleUtility.WorldToGUIPoint(world) - mousePosition).sqrMagnitude;
                if (distance < bestKnotDistance)
                {
                    bestKnotDistance = distance;
                    bestKnot = knotIndex;
                }
            }

            if (bestKnot < 0)
                return false;

            m_FocusedSpline = bestContainer;
            m_FocusedSplineIndex = bestSpline;
            m_FocusedKnotIndex = bestKnot;
            m_FocusedTangent = -1;
            SceneView.RepaintAll();
            return true;
        }

        bool TryFocusVisibleKnot(Vector2 mousePosition)
        {
            const float knotPickRadius = 24f;
            float bestDistance = knotPickRadius * knotPickRadius;
            SplineContainer bestContainer = null;
            int bestSpline = -1;
            int bestKnot = -1;

            Camera sceneCamera = SceneView.lastActiveSceneView != null
                ? SceneView.lastActiveSceneView.camera
                : null;
            Vector3 cameraPosition = sceneCamera != null
                ? sceneCamera.transform.position
                : Vector3.zero;
            float handleDistanceSquared = ReducedHandleCameraDistance * ReducedHandleCameraDistance;

            foreach (MultiSplineLoft.SplineSource source in m_ActiveLoft.Sources)
            {
                SplineContainer container = source?.container;
                if (container == null
                    || source.splineIndex < 0
                    || source.splineIndex >= container.Splines.Count)
                    continue;

                var spline = container.Splines[source.splineIndex];
                for (int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
                {
                    Vector3 world = container.transform.TransformPoint((Vector3)spline[knotIndex].Position);
                    if (sceneCamera != null
                        && (world - cameraPosition).sqrMagnitude > handleDistanceSquared)
                        continue;

                    float distance = (HandleUtility.WorldToGUIPoint(world) - mousePosition).sqrMagnitude;
                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    bestContainer = container;
                    bestSpline = source.splineIndex;
                    bestKnot = knotIndex;
                }
            }

            if (bestContainer == null)
                return false;

            m_FocusedSpline = bestContainer;
            m_FocusedSplineIndex = bestSpline;
            m_FocusedKnotIndex = bestKnot;
            m_FocusedTangent = -1;
            SceneView.RepaintAll();
            return true;
        }

        bool TryFocusTangent(Vector2 mousePosition)
        {
            if (!TryGetFocusedTangentWorldPositions(out Vector3 tangentIn, out Vector3 tangentOut))
                return false;

            const float tangentPickRadius = 16f;
            float inDistance = (HandleUtility.WorldToGUIPoint(tangentIn) - mousePosition).sqrMagnitude;
            float outDistance = (HandleUtility.WorldToGUIPoint(tangentOut) - mousePosition).sqrMagnitude;
            float maximumDistance = tangentPickRadius * tangentPickRadius;
            if (inDistance > maximumDistance && outDistance > maximumDistance)
                return false;

            m_FocusedTangent = inDistance <= outDistance ? 0 : 1;
            SceneView.RepaintAll();
            return true;
        }

        void DrawFocusedKnot(SceneView sceneView)
        {
            if (!TryGetFocusedKnotWorldPosition(out Vector3 position))
                return;

            var spline = m_FocusedSpline.Splines[m_FocusedSplineIndex];
            BezierKnot knot = spline[m_FocusedKnotIndex];
            if (sceneView.camera != null
                && Vector3.Distance(sceneView.camera.transform.position, position) > ReducedHandleCameraDistance)
                return;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(m_FocusedSpline, "Move Loft Spline Knot");
                knot.Position = m_FocusedSpline.transform.InverseTransformPoint(moved);
                spline.SetKnot(m_FocusedKnotIndex, knot);
                EditorUtility.SetDirty(m_FocusedSpline);
            }

            DrawFocusedTangents(spline, ref knot, position);
        }

        bool TryGetFocusedKnotWorldPosition(out Vector3 position)
        {
            position = Vector3.zero;
            if (m_FocusedSpline == null
                || m_FocusedSplineIndex < 0
                || m_FocusedSplineIndex >= m_FocusedSpline.Splines.Count
                || m_FocusedKnotIndex < 0)
                return false;

            var spline = m_FocusedSpline.Splines[m_FocusedSplineIndex];
            if (m_FocusedKnotIndex >= spline.Count)
                return false;

            position = m_FocusedSpline.transform.TransformPoint(
                (Vector3)spline[m_FocusedKnotIndex].Position);
            return true;
        }

        void DrawFocusedTangents(UnityEngine.Splines.Spline spline, ref BezierKnot knot, Vector3 knotPosition)
        {
            TangentMode tangentMode = spline.GetTangentMode(m_FocusedKnotIndex);
            if (tangentMode == TangentMode.Linear)
                return;

            Quaternion knotRotation = new Quaternion(
                knot.Rotation.value.x,
                knot.Rotation.value.y,
                knot.Rotation.value.z,
                knot.Rotation.value.w);
            Vector3 localKnotPosition = knot.Position;
            Vector3 tangentIn = m_FocusedSpline.transform.TransformPoint(
                localKnotPosition + knotRotation * (Vector3)knot.TangentIn);
            Vector3 tangentOut = m_FocusedSpline.transform.TransformPoint(
                localKnotPosition + knotRotation * (Vector3)knot.TangentOut);

            Handles.color = new Color(1f, 0.65f, 0.1f, 0.9f);
            Handles.DrawLine(knotPosition, tangentIn, 2f);
            Handles.DrawLine(knotPosition, tangentOut, 2f);
            float tangentSphereSize = HandleUtility.GetHandleSize(knotPosition) * 0.065f;
            Handles.color = m_FocusedTangent == 0
                ? new Color(1f, 0.9f, 0.15f, 1f)
                : new Color(0.95f, 0.2f, 0.7f, 0.95f);
            Handles.SphereHandleCap(0, tangentIn, Quaternion.identity, tangentSphereSize, EventType.Repaint);
            Handles.color = m_FocusedTangent == 1
                ? new Color(1f, 0.9f, 0.15f, 1f)
                : new Color(0.95f, 0.2f, 0.7f, 0.95f);
            Handles.SphereHandleCap(0, tangentOut, Quaternion.identity, tangentSphereSize, EventType.Repaint);

            if (m_FocusedTangent == 0)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 movedIn = Handles.PositionHandle(tangentIn, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(m_FocusedSpline, "Move Loft Spline Tangent");
                    if (tangentMode == TangentMode.AutoSmooth)
                    {
                        // Auto-smooth tangents are derived and cannot retain a manual
                        // edit. Match Unity's spline workflow by converting only the
                        // edited knot to independent tangents on the first drag.
                        spline.SetTangentMode(m_FocusedKnotIndex, TangentMode.Broken);
                    }
                    Vector3 localEndpoint = m_FocusedSpline.transform.InverseTransformPoint(movedIn);
                    knot.TangentIn = Quaternion.Inverse(knotRotation) * (localEndpoint - localKnotPosition);
                    spline.SetKnot(m_FocusedKnotIndex, knot, BezierTangent.In);
                    EditorUtility.SetDirty(m_FocusedSpline);
                }
            }

            if (m_FocusedTangent == 1)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 movedOut = Handles.PositionHandle(tangentOut, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(m_FocusedSpline, "Move Loft Spline Tangent");
                    if (tangentMode == TangentMode.AutoSmooth)
                        spline.SetTangentMode(m_FocusedKnotIndex, TangentMode.Broken);
                    Vector3 localEndpoint = m_FocusedSpline.transform.InverseTransformPoint(movedOut);
                    knot.TangentOut = Quaternion.Inverse(knotRotation) * (localEndpoint - localKnotPosition);
                    spline.SetKnot(m_FocusedKnotIndex, knot, BezierTangent.Out);
                    EditorUtility.SetDirty(m_FocusedSpline);
                }
            }
        }

        bool TryGetFocusedTangentWorldPositions(out Vector3 tangentIn, out Vector3 tangentOut)
        {
            tangentIn = Vector3.zero;
            tangentOut = Vector3.zero;
            if (!TryGetFocusedKnotWorldPosition(out _))
                return false;

            var spline = m_FocusedSpline.Splines[m_FocusedSplineIndex];
            if (spline.GetTangentMode(m_FocusedKnotIndex) == TangentMode.Linear)
                return false;

            BezierKnot knot = spline[m_FocusedKnotIndex];
            Quaternion knotRotation = new Quaternion(
                knot.Rotation.value.x,
                knot.Rotation.value.y,
                knot.Rotation.value.z,
                knot.Rotation.value.w);
            Vector3 localKnotPosition = knot.Position;
            tangentIn = m_FocusedSpline.transform.TransformPoint(
                localKnotPosition + knotRotation * (Vector3)knot.TangentIn);
            tangentOut = m_FocusedSpline.transform.TransformPoint(
                localKnotPosition + knotRotation * (Vector3)knot.TangentOut);
            return true;
        }

        static float DistanceToSegmentSquared(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
                return (point - start).sqrMagnitude;

            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return (point - (start + segment * t)).sqrMagnitude;
        }

        bool IsSourceSplineObject(GameObject candidate)
        {
            if (candidate == null || m_ActiveLoft == null)
                return false;

            foreach (MultiSplineLoft.SplineSource source in m_ActiveLoft.Sources)
            {
                Transform sourceTransform = source?.container != null ? source.container.transform : null;
                if (sourceTransform != null
                    && (candidate.transform == sourceTransform || candidate.transform.IsChildOf(sourceTransform)))
                {
                    return true;
                }
            }

            return false;
        }

        void UseSelectedLoftAndEditSplines()
        {
            MultiSplineLoft selectedLoft = FindLoftInSelection();
            MultiSplineLoft loft = selectedLoft != null ? selectedLoft : m_ActiveLoft;
            if (loft != null)
                EnterUnitySplineEditMode(loft);
        }

        static MultiSplineLoft FindLoftInSelection()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
                return null;

            return selected.GetComponent<MultiSplineLoft>()
                ?? selected.GetComponentInParent<MultiSplineLoft>();
        }

        void EnterUnitySplineEditMode(MultiSplineLoft loft)
        {
            if (loft == null)
                return;

            if (m_ActiveLoft != loft)
            {
                m_FocusedSpline = null;
                m_FocusedSplineIndex = -1;
                m_FocusedKnotIndex = -1;
                m_FocusedTangent = -1;
            }

            m_ActiveLoft = loft;
            EditorApplication.delayCall -= ApplyQueuedSplineSelection;
            EditorApplication.delayCall -= ActivateQueuedSplineTool;
            m_SplineEditActivationQueued = false;
            m_QueuedSplineTargets = null;

            // Multi-loft mode always uses the lightweight custom knot editor.
            // Keep the loft root as the authored selection instead of replacing
            // it with every source SplineContainer.
            if (ToolManager.activeContextType == typeof(SplineToolContext))
                ToolManager.SetActiveContext<GameObjectToolContext>();
            SceneView.RepaintAll();
        }

        void ApplyQueuedSplineSelection()
        {
            EditorApplication.delayCall -= ApplyQueuedSplineSelection;
            if (!m_SceneToolActive || m_QueuedSplineTargets == null || m_QueuedSplineTargets.Length == 0)
            {
                m_SplineEditActivationQueued = false;
                return;
            }

            m_ChangingSelection = true;
            Selection.objects = m_QueuedSplineTargets;
            m_ChangingSelection = false;
            EditorApplication.delayCall -= ActivateQueuedSplineTool;
            EditorApplication.delayCall += ActivateQueuedSplineTool;
        }

        void ActivateQueuedSplineTool()
        {
            EditorApplication.delayCall -= ActivateQueuedSplineTool;
            m_SplineEditActivationQueued = false;
            m_QueuedSplineTargets = null;
            if (!m_SceneToolActive || Selection.GetFiltered<SplineContainer>(SelectionMode.Editable | SelectionMode.Deep).Length == 0)
                return;

            // Multi-loft editing selects every source spline at once. Unity then
            // renders a handle and tangent for every knot in every source, which
            // makes a long road unusable even when no loft is rebuilding.
            if (UsesReducedSplineHandles())
            {
                ToolManager.SetActiveContext<GameObjectToolContext>();
                SceneView.RepaintAll();
                return;
            }

            try
            {
                ToolManager.SetActiveContext<SplineToolContext>();
                ToolManager.SetActiveTool<SplineMoveTool>();
            }
            catch (System.InvalidOperationException)
            {
                if (++m_SplineToolActivationAttempts < 3 && m_SceneToolActive)
                {
                    EditorApplication.delayCall += ActivateQueuedSplineTool;
                    return;
                }
                throw;
            }

            if (ToolManager.activeContextType != typeof(SplineToolContext) && ++m_SplineToolActivationAttempts < 3)
            {
                EditorApplication.delayCall += ActivateQueuedSplineTool;
                return;
            }
            SceneView.RepaintAll();
        }

        static bool UsesReducedSplineHandles()
        {
            // Multi-loft editing always uses our camera-distance knot renderer.
            // This keeps selection and editing behavior consistent for short and
            // long lofts and avoids Unity rendering every source handle at once.
            return true;
        }

        void QueueSplineMoveTool()
        {
            EditorApplication.delayCall -= ActivateQueuedSplineTool;
            EditorApplication.delayCall += ActivateQueuedSplineTool;
        }

        void QueueKnotPlacementTool(SplineContainer target = null)
        {
            m_QueuedKnotPlacementTarget = target;
            EditorApplication.delayCall -= ApplyQueuedKnotPlacementSelection;
            EditorApplication.delayCall += ApplyQueuedKnotPlacementSelection;
        }

        void ApplyQueuedKnotPlacementSelection()
        {
            EditorApplication.delayCall -= ApplyQueuedKnotPlacementSelection;
            if (!m_SceneToolActive) return;
            if (m_QueuedKnotPlacementTarget != null)
                Selection.activeGameObject = m_QueuedKnotPlacementTarget.gameObject;
            EditorApplication.delayCall -= ActivateKnotPlacementTool;
            EditorApplication.delayCall += ActivateKnotPlacementTool;
        }

        void ActivateKnotPlacementTool()
        {
            EditorApplication.delayCall -= ActivateKnotPlacementTool;
            m_QueuedKnotPlacementTarget = null;
            if (!m_SceneToolActive) return;
            EditorSplineUtility.SetKnotPlacementTool();
            SceneView.RepaintAll();
        }

        void CreateAndAddLoftSpline()
        {
            if (m_ActiveLoft == null) return;
            var splineObject = new GameObject("SPLINE [NEW]", typeof(SplineContainer));
            Undo.RegisterCreatedObjectUndo(splineObject, "Create Loft Spline");
            Transform parent = m_ActiveLoft.transform.parent;
            if (parent != null)
                splineObject.transform.SetParent(parent, false);

            SplineContainer container = splineObject.GetComponent<SplineContainer>();
            Undo.RecordObject(m_ActiveLoft, "Add Loft Spline");
            m_ActiveLoft.AddSelectedSpline(container);
            MultiSplineLoftEditor.EnsureSourceSplinesFollowLoft(m_ActiveLoft);
            EditorUtility.SetDirty(m_ActiveLoft);
            QueueKnotPlacementTool(container);
        }

        public void CreateLoftSplineFromOverlay()
        {
            MultiSplineLoft selectedLoft = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<MultiSplineLoft>()
                : null;
            if (selectedLoft != null)
                m_ActiveLoft = selectedLoft;
            CreateAndAddLoftSpline();
        }

        public void SelectMoveToolFromOverlay()
        {
            QueueSplineMoveTool();
        }

        public void SelectDrawToolFromOverlay()
        {
            QueueKnotPlacementTool();
        }

        public void Draw(bool embeddedInParentWindow = false)
        {
            if (!embeddedInParentWindow)
                m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
            m_ActiveLoft = (MultiSplineLoft)EditorGUILayout.ObjectField("Loft Component", m_ActiveLoft, typeof(MultiSplineLoft), true);
            MultiSplineLoftEditor.EnsureSourceSplinesFollowLoft(m_ActiveLoft);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Selection"))
                {
                    MultiSplineLoft selectedLoft = Selection.activeGameObject != null
                        ? Selection.activeGameObject.GetComponent<MultiSplineLoft>()
                        : null;
                    if (selectedLoft != null)
                        EnterUnitySplineEditMode(selectedLoft);
                }

                if (GUILayout.Button("Create From Splines"))
                    MultiSplineLoftEditor.CreateLoftFromSelection();
            }

            var selected = GetSelectedSplineContainersForWindow();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Spline Editing", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!MBEditorToolState.ActiveEditing || m_ActiveLoft == null))
            {
                if (GUILayout.Button("Draw / Add Knots"))
                    QueueKnotPlacementTool();
            }
            using (new EditorGUI.DisabledScope(m_ActiveLoft == null))
            {
                if (GUILayout.Button("Create New Loft Spline", GUILayout.Height(24f)))
                    CreateAndAddLoftSpline();
            }
            EditorGUILayout.HelpBox(
                "Knots are directly editable in the Scene view. Long roads keep the full curves visible and show controls only within 100 m of the Scene camera.",
                MessageType.None);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Selected Splines", EditorStyles.boldLabel);
            if (selected.Count == 0)
                EditorGUILayout.HelpBox("Select three or more SplineContainer objects, then add them to a loft.", MessageType.Info);
            else
                EditorGUILayout.LabelField($"{selected.Count} SplineContainer object(s) selected.");

            using (new EditorGUI.DisabledScope(m_ActiveLoft == null || selected.Count == 0))
            {
                if (GUILayout.Button("Add Selected To Loft", GUILayout.Height(26f)))
                {
                    Undo.RecordObject(m_ActiveLoft, "Add Selected Splines");
                    foreach (var container in selected)
                        m_ActiveLoft.AddSelectedSpline(container);
                    MultiSplineLoftEditor.EnsureSourceSplinesFollowLoft(m_ActiveLoft);
                    EditorUtility.SetDirty(m_ActiveLoft);
                }
            }

            using (new EditorGUI.DisabledScope(m_ActiveLoft == null))
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Shoulder Profiles", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Add independent AnimationCurve profiles to the left, right, start, or finish edge for verges, ditches, banks, and berms.", MessageType.None);
                if (GUILayout.Button(m_ActiveLoft != null && m_ActiveLoft.ShoulderModifier != null ? "Rebuild And Select Shoulder Profiles" : "Add Shoulder Profiles", GUILayout.Height(26f)))
                    MultiSplineLoftEditor.CreateOrRebuildShoulders(m_ActiveLoft);
                if (GUILayout.Button("Apply Eroded Trail Banks Preset", GUILayout.Height(26f)))
                    MultiSplineLoftEditor.CreateOrRebuildShoulders(m_ActiveLoft, applyErodedTrailPreset: true);

                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Collider Chunks", EditorStyles.boldLabel);

                if (m_ActiveLoft != null)
                {
                    EditorGUI.BeginChangeCheck();
                    bool generateColliderChunks = EditorGUILayout.Toggle("Generate Collider Chunks", m_ActiveLoft.UpdateMeshCollider);
                    using (new EditorGUI.DisabledScope(!generateColliderChunks))
                    {
                        float choppingDistance = EditorGUILayout.FloatField("Chopping Distance", m_ActiveLoft.ColliderChunkLength);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(m_ActiveLoft, "Edit Loft Collider Chunks");
                            m_ActiveLoft.UpdateMeshCollider = generateColliderChunks;
                            m_ActiveLoft.ColliderChunkLength = choppingDistance;
                            m_ActiveLoft.QueueRegenerate();
                            EditorUtility.SetDirty(m_ActiveLoft);
                        }
                    }
                }

                EditorGUILayout.HelpBox("Collision is generated beneath the render object in distance-based chunks.", MessageType.None);
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Resolution Spline", EditorStyles.boldLabel);

                if (m_ActiveLoft != null)
                {
                    EditorGUI.BeginChangeCheck();
                    bool generateResolutionWithLoft = EditorGUILayout.Toggle("Generate With Loft", m_ActiveLoft.GenerateResolutionSplineWithLoft);
                    int resolutionPointCount = EditorGUILayout.IntSlider("Generated Points", m_ActiveLoft.ResolutionSplinePointCount, 2, 200);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(m_ActiveLoft, "Edit Loft Resolution Spline Settings");
                        m_ActiveLoft.GenerateResolutionSplineWithLoft = generateResolutionWithLoft;
                        m_ActiveLoft.ResolutionSplinePointCount = resolutionPointCount;
                        EditorUtility.SetDirty(m_ActiveLoft);
                    }
                }

                if (GUILayout.Button("Generate And Select Resolution Spline", GUILayout.Height(26f)))
                {
                    if (!m_ActiveLoft.GenerateResolutionSpline(out string error))
                        EditorUtility.DisplayDialog("Generate Resolution Spline", error, "OK");
                    else
                        Selection.activeGameObject = m_ActiveLoft.GeneratedResolutionSpline.gameObject;
                }

                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("UV Spline", EditorStyles.boldLabel);

                if (m_ActiveLoft != null)
                {
                    EditorGUI.BeginChangeCheck();
                    bool generateWithLoft = EditorGUILayout.Toggle("Generate With Loft", m_ActiveLoft.GenerateUvSplineWithLoft);
                    int uvChannel = EditorGUILayout.IntSlider("UV Channel", m_ActiveLoft.UvSplineChannel, 0, 3);
                    var direction = (UVSpline.LongitudinalAxis)EditorGUILayout.EnumPopup("UV Direction", m_ActiveLoft.UvSplineDirection);
                    int pointCount = EditorGUILayout.IntSlider("Generated Points", m_ActiveLoft.UvSplinePointCount, 2, 200);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(m_ActiveLoft, "Edit Loft UV Spline Settings");
                        m_ActiveLoft.GenerateUvSplineWithLoft = generateWithLoft;
                        m_ActiveLoft.UvSplineChannel = uvChannel;
                        m_ActiveLoft.UvSplineDirection = direction;
                        m_ActiveLoft.UvSplinePointCount = pointCount;
                        EditorUtility.SetDirty(m_ActiveLoft);
                    }
                }

                if (GUILayout.Button("Generate Active Loft", GUILayout.Height(26f)))
                {
                    Undo.RecordObject(m_ActiveLoft, "Generate Multi Spline Loft");
                    m_ActiveLoft.Regenerate();
                    EditorUtility.SetDirty(m_ActiveLoft);
                }

                if (GUILayout.Button("Generate And Select UV Spline", GUILayout.Height(26f)))
                {
                    if (MultiSplineLoftEditor.GenerateUvSpline(m_ActiveLoft))
                        UvSplineGenerated?.Invoke(m_ActiveLoft.GeneratedUvSpline);
                }
            }

            if (!embeddedInParentWindow)
                EditorGUILayout.EndScrollView();
        }

        static List<SplineContainer> GetSelectedSplineContainersForWindow()
        {
            var result = new List<SplineContainer>();
            var selection = Selection.GetFiltered<SplineContainer>(SelectionMode.Editable | SelectionMode.Deep);

            foreach (var container in selection)
            {
                if (container != null && !result.Contains(container))
                    result.Add(container);
            }

            return result;
        }
    }
}

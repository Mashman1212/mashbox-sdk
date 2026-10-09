using System.Collections.Generic;
using System.IO;
using MashBoxSDK.EditorResources;
using MashBoxSDK.MapTools;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Splines;

namespace MashBoxSDK.Maps.Spline
{
    [CustomEditor(typeof(MultiSplineLoft))]
    public sealed class MultiSplineLoftEditor : Editor
    {
        // Optional project tools can draw directly in the loft inspector.
        public static event System.Action<MultiSplineLoft> TerrainConformGUI;

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
        SerializedProperty m_VisualCullDistance;
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
            m_VisualCullDistance = serializedObject.FindProperty("m_VisualCullDistance");
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

            TerrainConformGUI?.Invoke(loft);

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
            EditorGUILayout.PropertyField(m_ColliderChunkLength, new GUIContent("Chunk Length (Visuals / Colliders)", "Approximate metres along the loft per visual section and collider chunk."));
            EditorGUILayout.PropertyField(m_VisualCullDistance, new GUIContent("Visual Cull Distance"));
            EditorGUILayout.HelpBox("Visual chunks are mandatory in Play Mode and builds. Builds bake them automatically; the editable loft stays whole. Culling uses the gameplay camera and distance to each chunk bounding sphere. Colliders remain independent.", MessageType.None);
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
            if (GUILayout.Button("Loft Mesh Stamp", GUILayout.Height(26f)))
                LoftMeshStampWindow.Open(loft);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate Now", GUILayout.Height(26f)))
                {
                    Undo.RecordObject(loft, "Generate Multi Spline Loft");
                    // Regeneration can remove collider components and children.
                    // Let the editor update perform it after Inspector drawing.
                    QueueGenerate(loft);
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

        internal static MultiSplineLoft CreateStarterLoft(Vector3 position, Quaternion rotation)
        {
            var root = new GameObject("Multi-Spline Loft", typeof(MultiSplineLoft));
            root.transform.SetPositionAndRotation(position, rotation);
            Undo.RegisterCreatedObjectUndo(root, "Create Multi-Spline Loft");
            var loft = root.GetComponent<MultiSplineLoft>();
            loft.AutoRegenerate = false;

            string[] names = { "SPLINE [LEFT]", "SPLINE [CENTER]", "SPLINE [RIGHT]" };
            for (int side = 0; side < names.Length; side++)
            {
                var source = new GameObject(names[side], typeof(SplineContainer));
                source.transform.SetParent(root.transform, false);
                var container = source.GetComponent<SplineContainer>();
                float x = (side - 1) * 3f;
                container.Spline = new UnityEngine.Splines.Spline(new[]
                {
                    new BezierKnot(new Unity.Mathematics.float3(x, 0f, -10f)),
                    new BezierKnot(new Unity.Mathematics.float3(x, 0f, 0f)),
                    new BezierKnot(new Unity.Mathematics.float3(x, 0f, 10f))
                });
                container.Spline.SetTangentMode(TangentMode.AutoSmooth);
                loft.AddSelectedSpline(container);
                Undo.RegisterCreatedObjectUndo(source, "Create Loft Source");
            }

            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            root.GetComponent<MeshRenderer>().sharedMaterial = pipeline != null
                ? pipeline.defaultMaterial
                : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");

            var shoulders = new GameObject("Shoulders", typeof(LoftShoulderModifier));
            shoulders.transform.SetParent(root.transform, false);
            var modifier = shoulders.GetComponent<LoftShoulderModifier>();
            modifier.Loft = loft;
            modifier.Left.enabled = true;
            modifier.Right.enabled = true;
            loft.ShoulderModifier = modifier;
            Undo.RegisterCreatedObjectUndo(shoulders, "Create Loft Shoulders");

            loft.GenerateUvSplineWithLoft = true;
            loft.AutoRegenerate = true;
            loft.Regenerate();
            EditorUtility.SetDirty(loft);
            return loft;
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
        readonly List<SelectableKnot> m_SelectedKnots = new List<SelectableKnot>();
        readonly List<SelectableKnot> m_PreMarqueeSelection = new List<SelectableKnot>();
        int m_MarqueeControl;
        SceneView m_MarqueeView;
        Vector2 m_MarqueeStart;
        Rect m_MarqueeRect;
        bool m_MarqueeDragged;
        bool m_MarqueeAdditive;
        bool m_InsertHeld;
        readonly List<LoftKnotInsertion> m_InsertionPlan = new List<LoftKnotInsertion>();

        struct LoftKnotInsertion
        {
            public SplineInfo info;
            public int curve;
            public float t;
            public int existingKnot;
            public Vector3 position;
        }
        readonly List<LoftKnotExtension> m_ExtensionPlan = new List<LoftKnotExtension>();

        struct LoftKnotExtension
        {
            public SplineInfo info;
            public bool reverse;
            public bool prepend;
            public BezierKnot knot;
            public TangentMode mode;
            public float tension;
        }
        bool m_HidingObjectTools;
        bool m_PreviousToolsHidden;
        Quaternion m_HandleRotation = Quaternion.identity;
        Quaternion m_HandleStartRotation = Quaternion.identity;
        Vector3 m_HandleScale = Vector3.one;
        Vector3 m_HandlePivot;
        readonly List<KnotTransformSnapshot> m_KnotTransformSnapshots = new List<KnotTransformSnapshot>();

        struct KnotTransformSnapshot
        {
            public SelectableKnot selection;
            public BezierKnot knot;
        }
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

        internal static MultiSplineLoftWindow ActiveSceneTool =>
            HasActiveSceneTool ? s_ActiveSceneToolOwner : null;

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
            Undo.undoRedoPerformed += OnSplineUndoRedo;
            EditorApplication.hierarchyChanged += InvalidateSplineCaches;
            UnityEngine.Splines.Spline.Changed += OnSourceSplineChanged;
            SceneView.beforeSceneGui += OnBeforeSceneGUI;
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
            m_InsertHeld = false;
            RestoreObjectTools();
            CancelMarquee();
            if (s_ActiveSceneToolOwner == this)
                s_ActiveSceneToolOwner = null;
            Selection.selectionChanged -= OnSelectionChanged;
            Undo.undoRedoPerformed -= OnSplineUndoRedo;
            EditorApplication.hierarchyChanged -= InvalidateSplineCaches;
            UnityEngine.Splines.Spline.Changed -= OnSourceSplineChanged;
            SceneView.beforeSceneGui -= OnBeforeSceneGUI;
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

        static bool IsDrawingSpline => ToolManager.activeToolType?.Name == "CreateSplineTool";

        void OnBeforeSceneGUI(SceneView sceneView)
        {
            // Hide the GameObject handles before Unity lays them out. The loft
            // stays selected in the Inspector, but transforms edit its knots.
            bool editingLoft = m_SceneToolActive && m_ActiveLoft != null
                && MBGameplayGizmoVisibility.LoftSplinesVisible && !IsDrawingSpline
                && (FindLoftInSelection() == m_ActiveLoft || IsSourceSplineObject(Selection.activeGameObject));
            if (!editingLoft)
            {
                m_InsertHeld = false;
                RestoreObjectTools();
                return;
            }
            if (!m_HidingObjectTools)
            {
                m_PreviousToolsHidden = Tools.hidden;
                m_HidingObjectTools = true;
            }
            Tools.hidden = true;
            HandleInsertionKey(Event.current);
            Event current = Event.current;
            if (m_InsertHeld && current.type == EventType.MouseDown && current.button == 0
                && !current.alt && !current.control && !current.command && !current.shift && GUIUtility.hotControl == 0)
            {
                if (TryPlanLoftInsertion(sceneView, current.mousePosition))
                    ApplyLoftInsertion();
                current.Use();
                return;
            }
            // SceneView's GameObject command handler runs before duringSceneGui.
            // Claim both keyboard and menu deletion here, including validation.
            HandleKnotDelete(Event.current);
        }

        void HandleInsertionKey(Event current)
        {
            if (current.type == EventType.MouseLeaveWindow || current.type == EventType.Ignore)
                m_InsertHeld = false;
            if (current.keyCode != KeyCode.I)
                return;
            if (current.type == EventType.KeyUp)
            {
                m_InsertHeld = false;
                current.Use();
                SceneView.RepaintAll();
            }
            else if (current.type == EventType.KeyDown && !current.alt && !current.control && !current.command && !current.shift
                && !EditorGUIUtility.editingTextField && GUIUtility.hotControl == 0)
            {
                m_InsertHeld = true;
                current.Use();
                SceneView.RepaintAll();
            }
        }

        bool HandleKnotDelete(Event current)
        {
            if (EditorGUIUtility.editingTextField)
                return false;
            bool key = (current.type == EventType.KeyDown || current.type == EventType.KeyUp)
                && (current.keyCode == KeyCode.Delete || current.keyCode == KeyCode.Backspace);
            bool command = (current.type == EventType.ValidateCommand || current.type == EventType.ExecuteCommand)
                && (current.commandName == "Delete" || current.commandName == "SoftDelete");
            if (!key && !command)
                return false;
            // Even an empty knot selection must not fall through and delete the
            // loft or its source GameObject while this Scene editing mode owns input.
            if (GUIUtility.hotControl == 0 && (current.type == EventType.KeyDown || current.type == EventType.ExecuteCommand))
                DeleteSelectedKnots();
            current.Use();
            return true;
        }

        void DeleteSelectedKnots()
        {
            PruneKnotSelection();
            var knotsBySpline = new Dictionary<SplineInfo, HashSet<int>>();
            var targets = new HashSet<UnityEngine.Object>();
            foreach (SelectableKnot knot in m_SelectedKnots)
            {
                if (!knotsBySpline.TryGetValue(knot.SplineInfo, out HashSet<int> indices))
                    knotsBySpline.Add(knot.SplineInfo, indices = new HashSet<int>());
                indices.Add(knot.KnotIndex);
                targets.Add(knot.SplineInfo.Object);
            }
            if (targets.Count == 0)
                return;
            var undoTargets = new UnityEngine.Object[targets.Count];
            targets.CopyTo(undoTargets);
            Undo.RecordObjects(undoTargets, "Delete Loft Spline Knots");
            ClearKnotEditingState();
            foreach (var pair in knotsBySpline)
            {
                var indices = new List<int>(pair.Value);
                indices.Sort((a, b) => b.CompareTo(a));
                foreach (int index in indices)
                    pair.Key.Spline.RemoveAt(index);
            }
            foreach (UnityEngine.Object target in targets)
            {
                EditorUtility.SetDirty(target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                EditorSceneManager.MarkSceneDirty(((SplineContainer)target).gameObject.scene);
            }
            InvalidateSplineCaches();
            SceneView.RepaintAll();
        }

        void ClearKnotEditingState()
        {
            CancelMarquee();
            m_SelectedKnots.Clear();
            m_KnotTransformSnapshots.Clear();
            m_ExtensionPlan.Clear();
            FocusLastSelectedKnot();
        }

        void InvalidateSplineCaches()
        {
            m_ReducedOverlayGeneration = -1;
            m_ReducedOverlayLoft = null;
            m_ReducedOverlayEntries.Clear();
        }

        void OnSplineUndoRedo()
        {
            // Knot indices may refer to different knots after a topology undo.
            ClearKnotEditingState();
            InvalidateSplineCaches();
            SceneView.RepaintAll();
        }

        void OnSourceSplineChanged(UnityEngine.Splines.Spline spline, int knotIndex, SplineModification modification)
        {
            if (m_ActiveLoft == null)
                return;
            foreach (MultiSplineLoft.SplineSource source in m_ActiveLoft.Sources)
                if (source?.container != null && source.splineIndex >= 0 && source.splineIndex < source.container.Splines.Count
                    && source.container.Splines[source.splineIndex] == spline)
                {
                    InvalidateSplineCaches();
                    return;
                }
        }

        void RestoreObjectTools()
        {
            if (!m_HidingObjectTools)
                return;
            Tools.hidden = m_PreviousToolsHidden;
            m_HidingObjectTools = false;
        }

        void OnSceneGUI(SceneView sceneView)
        {
            if (!MBGameplayGizmoVisibility.LoftSplinesVisible || !m_SceneToolActive || m_ActiveLoft == null)
            {
                CancelMarquee();
                return;
            }
            // Knot placement owns Scene input until drawing finishes. The reduced
            // knot editor would otherwise consume clicks before Unity can add knots.
            if (IsDrawingSpline)
            {
                CancelMarquee();
                return;
            }

            Event current = Event.current;
            PruneKnotSelection();
            if (m_InsertHeld)
            {
                sceneView.wantsMouseMove = true;
                DrawReducedSplineOverlay(sceneView, m_ActiveLoft);
                if (current.type == EventType.MouseMove)
                    sceneView.Repaint();
                if (current.type == EventType.Repaint && !current.alt && !current.control && !current.command && !current.shift
                    && EditorWindow.mouseOverWindow == sceneView && TryPlanLoftInsertion(sceneView, current.mousePosition))
                    DrawLoftInsertionPreview();
                return;
            }
            HandleTransformShortcuts(current);
            if (current.shift && !current.alt && !current.control && !current.command)
            {
                sceneView.wantsMouseMove = true;
                if (current.type == EventType.MouseMove || current.type == EventType.KeyDown || current.type == EventType.KeyUp)
                    sceneView.Repaint();
                if (current.type == EventType.Repaint && GUIUtility.hotControl == 0
                    && EditorWindow.mouseOverWindow == sceneView && !IsOverLoftSelectionTarget(sceneView, current.mousePosition)
                    && TryPlanLoftExtension(current.mousePosition, out bool atStart))
                    DrawLoftExtensionPreview(atStart);
            }
            // Allocate before the optional knot/tangent handles so the ID stays
            // stable as a rectangle changes the number of selected knots.
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
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
            if (m_SceneToolActive && current.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            if (HandleMarquee(sceneView, current))
                return;

            if (!m_SceneToolActive
                || current.type != EventType.MouseDown
                || current.button != 0
                || current.alt
                || current.control
                || current.command
                || Tools.current == Tool.View
                || Tools.viewToolActive
                || GUIUtility.hotControl != 0
                || HandleUtility.nearestControl != controlId)
            {
                return;
            }

            if (UsesReducedSplineHandles() && TryFocusKnot(current.mousePosition))
            {
                if (m_FocusedTangent < 0)
                    SelectFocusedKnot(current.shift);
                current.Use();
                return;
            }

            // Empty clicks remain owned by the spline editor. In particular, do
            // not forward them to the terrain or ordinary meshes underneath the
            // curve. Another loft is a valid editing target, however, so allow a
            // direct click on its generated mesh to switch the active loft.
            if (UsesReducedSplineHandles())
            {
                // Delay empty-click picking until mouse-up so a drag can select
                // knots even when it starts over another loft's mesh.
                m_MarqueeControl = controlId;
                m_MarqueeView = sceneView;
                m_MarqueeStart = current.mousePosition;
                m_MarqueeRect = new Rect(m_MarqueeStart, Vector2.zero);
                m_MarqueeDragged = false;
                m_MarqueeAdditive = current.shift;
                m_PreMarqueeSelection.Clear();
                m_PreMarqueeSelection.AddRange(m_SelectedKnots);
                GUIUtility.hotControl = controlId;
                current.Use();
            }
        }

        void HandleTransformShortcuts(Event current)
        {
            if (current.type != EventType.KeyDown || current.alt || current.control || current.command || current.shift
                || EditorGUIUtility.editingTextField || GUIUtility.hotControl != 0)
                return;
            switch (current.keyCode)
            {
                case KeyCode.W: Tools.current = Tool.Move; break;
                case KeyCode.E: Tools.current = Tool.Rotate; break;
                case KeyCode.R: Tools.current = Tool.Scale; break;
                default: return;
            }
            m_FocusedTangent = -1;
            current.Use();
            SceneView.RepaintAll();
        }

        void SelectFocusedKnot(bool additive)
        {
            var knot = new SelectableKnot(new SplineInfo(m_FocusedSpline, m_FocusedSplineIndex), m_FocusedKnotIndex);
            if (!additive)
                m_SelectedKnots.Clear();
            if (!additive || !m_SelectedKnots.Remove(knot))
                m_SelectedKnots.Add(knot);
            FocusLastSelectedKnot();
            SceneView.RepaintAll();
        }

        void FocusLastSelectedKnot()
        {
            m_FocusedTangent = -1;
            if (m_SelectedKnots.Count == 0)
            {
                m_FocusedSpline = null;
                m_FocusedSplineIndex = m_FocusedKnotIndex = -1;
                return;
            }
            SelectableKnot knot = m_SelectedKnots[m_SelectedKnots.Count - 1];
            m_FocusedSpline = knot.SplineInfo.Container as SplineContainer;
            m_FocusedSplineIndex = knot.SplineInfo.Index;
            m_FocusedKnotIndex = knot.KnotIndex;
        }

        void PruneKnotSelection()
        {
            if (m_ActiveLoft == null)
            {
                ClearKnotEditingState();
                return;
            }
            if (m_SelectedKnots.RemoveAll(knot => knot.SplineInfo.Object == null || !knot.IsValid()
                || !m_ActiveLoft.Sources.Exists(source => source != null
                    && source.container == knot.SplineInfo.Object && source.splineIndex == knot.SplineInfo.Index)) > 0)
                FocusLastSelectedKnot();
        }

        void CancelMarquee()
        {
            if (m_MarqueeControl != 0 && GUIUtility.hotControl == m_MarqueeControl)
                GUIUtility.hotControl = 0;
            m_MarqueeControl = 0;
            m_MarqueeView = null;
            m_PreMarqueeSelection.Clear();
        }

        bool HandleMarquee(SceneView view, Event current)
        {
            if (m_MarqueeControl == 0 || m_MarqueeView != view)
                return false;
            if (GUIUtility.hotControl != m_MarqueeControl)
            {
                CancelMarquee();
                return false;
            }
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape
                || current.type == EventType.Ignore)
            {
                m_SelectedKnots.Clear();
                m_SelectedKnots.AddRange(m_PreMarqueeSelection);
                FocusLastSelectedKnot();
                CancelMarquee();
                if (current.type != EventType.Ignore)
                    current.Use();
                view.Repaint();
                return true;
            }
            if (current.type == EventType.MouseDrag)
            {
                m_MarqueeDragged |= (current.mousePosition - m_MarqueeStart).sqrMagnitude > 16f;
                if (m_MarqueeDragged)
                {
                    m_MarqueeRect = Rect.MinMaxRect(
                        Mathf.Min(m_MarqueeStart.x, current.mousePosition.x), Mathf.Min(m_MarqueeStart.y, current.mousePosition.y),
                        Mathf.Max(m_MarqueeStart.x, current.mousePosition.x), Mathf.Max(m_MarqueeStart.y, current.mousePosition.y));
                    UpdateMarqueeSelection(view);
                }
                current.Use();
                view.Repaint();
            }
            else if (current.type == EventType.MouseUp && current.button == 0)
            {
                // Wait for release to distinguish Shift-click extension from
                // Shift-drag's additive rectangle selection.
                bool extend = !m_MarqueeDragged && m_MarqueeAdditive
                    && !current.alt && !current.control && !current.command;
                bool pickLoft = !m_MarqueeDragged && !m_MarqueeAdditive;
                if (pickLoft)
                {
                    m_SelectedKnots.Clear();
                    FocusLastSelectedKnot();
                }
                CancelMarquee();
                if (extend && TryPlanLoftExtension(current.mousePosition, out _))
                    ApplyLoftExtension();
                if (pickLoft && TryPickOtherLoft(current.mousePosition, out MultiSplineLoft loft))
                    Selection.activeGameObject = loft.gameObject;
                current.Use();
                SceneView.RepaintAll();
            }
            else if (current.type == EventType.Repaint && m_MarqueeDragged)
            {
                Handles.BeginGUI();
                GUI.skin.GetStyle("selectionRect").Draw(m_MarqueeRect, GUIContent.none, false, false, false, false);
                Handles.EndGUI();
            }
            return true;
        }

        bool IsOverLoftSelectionTarget(SceneView view, Vector2 mouse)
        {
            RefreshReducedSplineOverlay(m_ActiveLoft);
            foreach (ReducedSplineOverlayEntry entry in m_ReducedOverlayEntries)
            {
                if (!IsOverlayEntryValid(entry))
                    continue;
                for (int i = 1; i < entry.points.Length; i++)
                    if (DistanceToSegmentSquared(mouse, HandleUtility.WorldToGUIPoint(entry.points[i - 1]),
                        HandleUtility.WorldToGUIPoint(entry.points[i])) < ReducedHandlePickRadius * ReducedHandlePickRadius)
                        return true;
                var spline = entry.container.Splines[entry.splineIndex];
                for (int i = 0; i < spline.Count; i++)
                {
                    Vector3 world = entry.container.transform.TransformPoint((Vector3)spline[i].Position);
                    if (view.camera != null && ((world - view.camera.transform.position).sqrMagnitude > ReducedHandleCameraDistance * ReducedHandleCameraDistance
                        || view.camera.WorldToViewportPoint(world).z <= 0f))
                        continue;
                    if ((HandleUtility.WorldToGUIPoint(world) - mouse).sqrMagnitude < 24f * 24f)
                        return true;
                }
            }
            return false;
        }

        bool TryPlanLoftInsertion(SceneView view, Vector2 mouse)
        {
            m_InsertionPlan.Clear();
            MultiSplineLoft.SplineSource nearestSource = null;
            float nearestT = 0f;
            float nearestDistance = 24f * 24f;
            float interval = 0f;
            foreach (MultiSplineLoft.SplineSource source in m_ActiveLoft.Sources)
            {
                if (source == null || !source.IsValid)
                    continue;
                var spline = source.container.Splines[source.splineIndex];
                int samples = Mathf.Clamp(spline.Count * 16, 32, 2048);
                Vector2 previous = default;
                bool previousVisible = false;
                for (int i = 0; i <= samples; i++)
                {
                    float t = i / (float)samples;
                    Vector3 world = source.container.transform.TransformPoint((Vector3)SplineUtility.EvaluatePosition(spline, t));
                    Vector2 point = HandleUtility.WorldToGUIPoint(world);
                    bool visible = view.camera == null || view.camera.WorldToViewportPoint(world).z > 0f;
                    if (i == 0 || !visible || !previousVisible)
                    {
                        previous = point;
                        previousVisible = visible;
                        continue;
                    }
                    Vector2 segment = point - previous;
                    float u = segment.sqrMagnitude > .000001f
                        ? Mathf.Clamp01(Vector2.Dot(mouse - previous, segment) / segment.sqrMagnitude) : 0f;
                    float distance = (mouse - (previous + u * segment)).sqrMagnitude;
                    previous = point;
                    previousVisible = visible;
                    if (distance >= nearestDistance)
                        continue;
                    nearestDistance = distance;
                    nearestSource = source;
                    nearestT = (i - 1 + u) / samples;
                    interval = 1f / samples;
                }
            }
            if (nearestSource == null)
                return false;
            // Refine the local hit instead of snapping insertion to a sample.
            float low = Mathf.Max(0f, nearestT - interval);
            float high = Mathf.Min(1f, nearestT + interval);
            for (int i = 0; i < 12; i++)
            {
                float a = Mathf.Lerp(low, high, 1f / 3f);
                float b = Mathf.Lerp(low, high, 2f / 3f);
                if (InsertionScreenDistance(view, nearestSource, a, mouse) < InsertionScreenDistance(view, nearestSource, b, mouse))
                    high = b;
                else
                    low = a;
            }
            float sourceT = (low + high) * .5f;
            int curve = SplineUtility.SplineToCurveT(
                nearestSource.container.Splines[nearestSource.splineIndex], sourceT, out float curveT);
            return PlanLoftInsertion(nearestSource, curve, curveT);
        }

        static float InsertionScreenDistance(SceneView view, MultiSplineLoft.SplineSource source, float t, Vector2 mouse)
        {
            Vector3 position = source.container.transform.TransformPoint(
                (Vector3)SplineUtility.EvaluatePosition(source.container.Splines[source.splineIndex], t));
            if (view.camera != null && view.camera.WorldToViewportPoint(position).z <= 0f)
                return float.PositiveInfinity;
            return (HandleUtility.WorldToGUIPoint(position) - mouse).sqrMagnitude;
        }

        bool PlanLoftInsertion(MultiSplineLoft.SplineSource reference, int referenceCurve, float referenceT)
        {
            m_InsertionPlan.Clear();
            if (reference == null || !reference.IsValid)
                return false;
            var referenceSpline = reference.container.Splines[reference.splineIndex];
            var referenceBezier = referenceSpline.GetCurve(referenceCurve);
            Transform referenceTransform = reference.container.transform;
            Vector3 planePoint = referenceTransform.TransformPoint(
                (Vector3)CurveUtility.EvaluatePosition(referenceBezier, referenceT));
            Vector3 normal = referenceTransform.TransformVector(
                (Vector3)CurveUtility.EvaluateTangent(referenceBezier, referenceT));
            if (normal.sqrMagnitude < .000001f)
                normal = referenceTransform.TransformVector((Vector3)(referenceBezier.P3 - referenceBezier.P0));
            if (normal.sqrMagnitude < .000001f)
                return false;
            normal.Normalize();
            int referenceCount = referenceSpline.Closed ? referenceSpline.Count : referenceSpline.Count - 1;
            float logicalProgress = (referenceCurve + referenceT) / referenceCount;
            int logicalCurve = reference.reverse ? referenceCount - 1 - referenceCurve : referenceCurve;
            if (reference.reverse)
                logicalProgress = 1f - logicalProgress;
            var seen = new HashSet<SplineInfo>();
            foreach (MultiSplineLoft.SplineSource source in m_ActiveLoft.Sources)
            {
                if (source == null || !source.IsValid)
                    continue;
                var info = new SplineInfo(source.container, source.splineIndex);
                if (!seen.Add(info))
                    continue;
                int count = info.Spline.Closed ? info.Spline.Count : info.Spline.Count - 1;
                float preferred = (source.reverse ? 1f - logicalProgress : logicalProgress) * count;
                int curve = -1;
                float t = 0f;
                if (source.container == reference.container && source.splineIndex == reference.splineIndex)
                {
                    curve = referenceCurve;
                    t = referenceT;
                }
                else
                {
                    // Matching knot rows must stay between their surrounding
                    // rows, even when a winding lane crosses this plane elsewhere.
                    bool matchingRows = count == referenceCount && info.Spline.Closed == referenceSpline.Closed;
                    int first = matchingRows ? (source.reverse ? count - 1 - logicalCurve : logicalCurve) : 0;
                    int last = matchingRows ? first : count - 1;
                    float best = float.PositiveInfinity;
                    for (int candidate = first; candidate <= last; candidate++)
                    {
                        if (!TryInsertionPlaneIntersection(info, candidate, planePoint, normal,
                            Mathf.Clamp01(preferred - candidate), out float hit))
                            continue;
                        float distance = Mathf.Abs(candidate + hit - preferred);
                        if (info.Spline.Closed)
                            distance = Mathf.Min(distance, count - distance);
                        if (distance >= best)
                            continue;
                        best = distance;
                        curve = candidate;
                        t = hit;
                    }
                }
                // Never silently insert a partial or diagonal row if a lane
                // cannot meet the cross-section in its surrounding segment.
                if (curve < 0)
                {
                    m_InsertionPlan.Clear();
                    return false;
                }
                int existing = t <= .000001f ? curve : t >= .999999f ? (curve + 1) % info.Spline.Count : -1;
                Vector3 position = info.Transform.TransformPoint(existing >= 0
                    ? (Vector3)info.Spline[existing].Position
                    : (Vector3)CurveUtility.EvaluatePosition(info.Spline.GetCurve(curve), t));
                m_InsertionPlan.Add(new LoftKnotInsertion { info = info, curve = curve, t = t, existingKnot = existing, position = position });
            }
            return m_InsertionPlan.Count > 0;
        }

        static bool TryInsertionPlaneIntersection(SplineInfo info, int curveIndex, Vector3 point,
            Vector3 normal, float preferred, out float t)
        {
            var curve = info.Spline.GetCurve(curveIndex);
            Transform transform = info.Transform;
            double p0 = Vector3.Dot(transform.TransformPoint((Vector3)curve.P0) - point, normal);
            double p1 = Vector3.Dot(transform.TransformPoint((Vector3)curve.P1) - point, normal);
            double p2 = Vector3.Dot(transform.TransformPoint((Vector3)curve.P2) - point, normal);
            double p3 = Vector3.Dot(transform.TransformPoint((Vector3)curve.P3) - point, normal);
            double a = -p0 + 3 * p1 - 3 * p2 + p3;
            double b = 3 * p0 - 6 * p1 + 3 * p2;
            double c = -3 * p0 + 3 * p1;
            double Distance(double u) => ((a * u + b) * u + c) * u + p0;

            // Split at the cubic's extrema. Each interval is monotonic, so
            // bisection finds every crossing, including tightly spaced roots.
            var boundaries = new List<double> { 0, 1 };
            void AddBoundary(double u) { if (u > 0 && u < 1) boundaries.Add(u); }
            if (System.Math.Abs(a) < 1e-12)
            {
                if (System.Math.Abs(b) > 1e-12)
                    AddBoundary(-c / (2 * b));
            }
            else
            {
                double discriminant = b * b - 3 * a * c;
                if (discriminant >= 0)
                {
                    double root = System.Math.Sqrt(discriminant);
                    AddBoundary((-b - root) / (3 * a));
                    AddBoundary((-b + root) / (3 * a));
                }
            }
            boundaries.Sort();
            float bestT = 0f;
            float bestDistance = float.PositiveInfinity;
            void Consider(double u)
            {
                float distance = Mathf.Abs((float)u - preferred);
                if (distance < bestDistance) { bestDistance = distance; bestT = (float)u; }
            }
            if (System.Math.Abs(Distance(preferred)) < .00001)
                Consider(preferred);
            for (int i = 0; i < boundaries.Count; i++)
            {
                double low = boundaries[i];
                double lowDistance = Distance(low);
                if (System.Math.Abs(lowDistance) < .00001)
                    Consider(low);
                if (i + 1 == boundaries.Count)
                    continue;
                double high = boundaries[i + 1];
                if (lowDistance * Distance(high) >= 0)
                    continue;
                for (int step = 0; step < 30; step++)
                {
                    double mid = (low + high) * .5;
                    if (lowDistance * Distance(mid) > 0)
                        low = mid;
                    else
                        high = mid;
                }
                Consider((low + high) * .5);
            }
            t = bestT;
            return !float.IsPositiveInfinity(bestDistance);
        }

        void DrawLoftInsertionPreview()
        {
            using (new Handles.DrawingScope(Color.yellow))
            {
                Vector3 center = Vector3.zero;
                for (int i = 0; i < m_InsertionPlan.Count; i++)
                {
                    Vector3 position = m_InsertionPlan[i].position;
                    Handles.DrawWireDisc(position, Vector3.up, HandleUtility.GetHandleSize(position) * .08f);
                    if (i > 0)
                        Handles.DrawDottedLine(m_InsertionPlan[i - 1].position, position, 4f);
                    center += position;
                }
                Handles.Label(center / m_InsertionPlan.Count, "I + click: insert loft knot row");
            }
        }

        void ApplyLoftInsertion()
        {
            m_InsertionPlan.RemoveAll(plan => plan.info.Object == null || plan.info.Spline == null || plan.info.Spline.Count < 2);
            var targets = new HashSet<UnityEngine.Object>();
            foreach (LoftKnotInsertion plan in m_InsertionPlan)
                if (plan.existingKnot < 0)
                    targets.Add(plan.info.Object);
            if (targets.Count > 0)
            {
                var undoTargets = new UnityEngine.Object[targets.Count];
                targets.CopyTo(undoTargets);
                Undo.RecordObjects(undoTargets, "Insert Loft Spline Knots");
            }
            m_SelectedKnots.Clear();
            foreach (LoftKnotInsertion plan in m_InsertionPlan)
            {
                int index = plan.existingKnot;
                if (index < 0)
                {
                    SplineToolWindow.InsertKnot(plan.info.Spline, plan.curve, plan.t);
                    index = plan.curve + 1;
                }
                m_SelectedKnots.Add(new SelectableKnot(plan.info, index));
            }
            FocusLastSelectedKnot();
            foreach (UnityEngine.Object target in targets)
            {
                EditorUtility.SetDirty(target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                EditorSceneManager.MarkSceneDirty(((SplineContainer)target).gameObject.scene);
            }
            InvalidateSplineCaches();
            SceneView.RepaintAll();
        }

        bool TryPlanLoftExtension(Vector2 mouse, out bool atStart)
        {
            m_ExtensionPlan.Clear();
            atStart = false;
            Vector3 startCenter = Vector3.zero;
            Vector3 endCenter = Vector3.zero;
            var seen = new HashSet<SplineInfo>();
            foreach (MultiSplineLoft.SplineSource source in m_ActiveLoft.Sources)
            {
                if (source?.container == null || source.splineIndex < 0 || source.splineIndex >= source.container.Splines.Count)
                    continue;
                var info = new SplineInfo(source.container, source.splineIndex);
                var spline = info.Spline;
                if (spline == null || spline.Count == 0 || spline.Closed || !seen.Add(info))
                    continue;
                startCenter += source.container.transform.TransformPoint((Vector3)spline[source.reverse ? spline.Count - 1 : 0].Position);
                endCenter += source.container.transform.TransformPoint((Vector3)spline[source.reverse ? 0 : spline.Count - 1].Position);
                m_ExtensionPlan.Add(new LoftKnotExtension { info = info, reverse = source.reverse });
            }
            if (m_ExtensionPlan.Count == 0)
                return false;
            startCenter /= m_ExtensionPlan.Count;
            endCenter /= m_ExtensionPlan.Count;

            Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
            Vector3 target = default;
            float nearest = float.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, 100000f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= nearest || hit.collider.GetComponentInParent<MultiSplineLoft>() == m_ActiveLoft
                    || IsSourceSplineObject(hit.collider.gameObject))
                    continue;
                nearest = hit.distance;
                target = hit.point;
            }
            if (float.IsPositiveInfinity(nearest))
            {
                Vector3 endpoint = (HandleUtility.WorldToGUIPoint(startCenter) - mouse).sqrMagnitude
                    < (HandleUtility.WorldToGUIPoint(endCenter) - mouse).sqrMagnitude ? startCenter : endCenter;
                if (!new Plane(Vector3.up, endpoint).Raycast(ray, out float distance))
                    return false;
                target = ray.GetPoint(distance);
            }

            atStart = (target - startCenter).sqrMagnitude < (target - endCenter).sqrMagnitude;
            return PlanLoftExtensionRow(target, atStart ? startCenter : endCenter, atStart);
        }

        bool PlanLoftExtensionRow(Vector3 target, Vector3 center, bool atStart)
        {
            Vector3 delta = target - center;
            if (delta.sqrMagnitude < 0.0001f)
                return false;

            Vector3 outward = Vector3.zero;
            Vector3 first = Vector3.zero;
            Vector3 across = Vector3.zero;
            for (int i = 0; i < m_ExtensionPlan.Count; i++)
            {
                LoftKnotExtension extension = m_ExtensionPlan[i];
                var spline = extension.info.Spline;
                bool prepend = atStart != extension.reverse;
                int endpoint = prepend ? 0 : spline.Count - 1;
                Vector3 world = extension.info.Transform.TransformPoint((Vector3)spline[endpoint].Position);
                if (i == 0)
                    first = world;
                else if ((world - first).sqrMagnitude > across.sqrMagnitude)
                    across = world - first;
                if (spline.Count > 1)
                {
                    int neighbour = prepend ? 1 : spline.Count - 2;
                    outward += extension.info.Transform.TransformVector(
                        (Vector3)(spline[endpoint].Position - spline[neighbour].Position)).normalized;
                }
            }

            // Build the old frame from the endpoint row itself, removing any
            // longitudinal stagger before turning the row into the new segment.
            Vector3 oldForward = across.sqrMagnitude > 0.000001f
                ? Vector3.ProjectOnPlane(outward, across.normalized) : outward;
            if (oldForward.sqrMagnitude < 0.000001f)
                oldForward = Vector3.Cross(across, Vector3.up);
            if (oldForward.sqrMagnitude < 0.000001f)
                oldForward = delta;
            oldForward.Normalize();
            Vector3 oldBaseUp = ExtensionFrameUp(oldForward);
            Vector3 oldUp = Vector3.Cross(oldForward, across);
            if (oldUp.sqrMagnitude < 0.000001f)
                oldUp = oldBaseUp;
            if (Vector3.Dot(oldUp, oldBaseUp) < 0f)
                oldUp = -oldUp;
            oldUp.Normalize();

            Vector3 newForward = delta.normalized;
            float bank = Vector3.SignedAngle(oldBaseUp, oldUp, oldForward);
            Vector3 newUp = Quaternion.AngleAxis(bank, newForward) * ExtensionFrameUp(newForward);
            Quaternion rowRotation = Quaternion.LookRotation(newForward, newUp)
                * Quaternion.Inverse(Quaternion.LookRotation(oldForward, oldUp));

            for (int i = 0; i < m_ExtensionPlan.Count; i++)
            {
                LoftKnotExtension extension = m_ExtensionPlan[i];
                var spline = extension.info.Spline;
                // Reverse sources use their opposite authored endpoint for the
                // same logical end of the loft.
                extension.prepend = atStart != extension.reverse;
                int endpoint = extension.prepend ? 0 : spline.Count - 1;
                extension.knot = spline[endpoint];
                Transform transform = extension.info.Transform;
                Vector3 world = transform.TransformPoint((Vector3)extension.knot.Position);
                Vector3 offset = Vector3.ProjectOnPlane(world - center, oldForward);
                extension.knot.Position = transform.InverseTransformPoint(target + rowRotation * offset);

                // Turn the new knot's authored tangents with the row as well.
                // Transform vectors explicitly to support nonuniform source scale.
                Quaternion oldRotation = extension.knot.Rotation;
                Quaternion newRotation = Quaternion.Inverse(transform.rotation) * rowRotation * transform.rotation * oldRotation;
                extension.knot.TangentIn = Quaternion.Inverse(newRotation) * transform.InverseTransformVector(
                    rowRotation * transform.TransformVector(oldRotation * (Vector3)extension.knot.TangentIn));
                extension.knot.TangentOut = Quaternion.Inverse(newRotation) * transform.InverseTransformVector(
                    rowRotation * transform.TransformVector(oldRotation * (Vector3)extension.knot.TangentOut));
                extension.knot.Rotation = newRotation;
                extension.mode = spline.GetTangentMode(endpoint);
                extension.tension = spline.GetAutoSmoothTension(endpoint);
                m_ExtensionPlan[i] = extension;
            }
            return true;
        }

        static Vector3 ExtensionFrameUp(Vector3 forward)
        {
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (up.sqrMagnitude < 0.000001f)
                up = Vector3.ProjectOnPlane(Vector3.forward, forward);
            return up.normalized;
        }

        void DrawLoftExtensionPreview(bool atStart)
        {
            using (new Handles.DrawingScope(Color.cyan))
            {
                Vector3 center = Vector3.zero;
                foreach (LoftKnotExtension extension in m_ExtensionPlan)
                {
                    if (!IsExtensionValid(extension))
                        continue;
                    var spline = extension.info.Spline;
                    Vector3 from = extension.info.Transform.TransformPoint((Vector3)spline[extension.prepend ? 0 : spline.Count - 1].Position);
                    Vector3 to = extension.info.Transform.TransformPoint((Vector3)extension.knot.Position);
                    Handles.DrawDottedLine(from, to, 4f);
                    Handles.DrawWireDisc(to, Vector3.up, HandleUtility.GetHandleSize(to) * .08f);
                    center += to;
                }
                Handles.Label(center / m_ExtensionPlan.Count,
                    atStart ? "Shift-click: extend loft START" : "Shift-click: extend loft END");
            }
        }

        void ApplyLoftExtension()
        {
            m_ExtensionPlan.RemoveAll(extension => !IsExtensionValid(extension));
            if (m_ExtensionPlan.Count == 0)
                return;
            var targets = new HashSet<UnityEngine.Object>();
            foreach (LoftKnotExtension extension in m_ExtensionPlan)
                targets.Add(extension.info.Object);
            var undoTargets = new UnityEngine.Object[targets.Count];
            targets.CopyTo(undoTargets);
            Undo.RecordObjects(undoTargets, "Extend Loft Splines");
            m_SelectedKnots.Clear();
            foreach (LoftKnotExtension extension in m_ExtensionPlan)
            {
                var spline = extension.info.Spline;
                int index = extension.prepend ? 0 : spline.Count;
                spline.Insert(index, extension.knot, extension.mode, extension.tension);
                m_SelectedKnots.Add(new SelectableKnot(extension.info, index));
            }
            FocusLastSelectedKnot();
            foreach (UnityEngine.Object target in targets)
            {
                EditorUtility.SetDirty(target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                EditorSceneManager.MarkSceneDirty(((SplineContainer)target).gameObject.scene);
            }
            m_ReducedOverlayGeneration = -1;
            SceneView.RepaintAll();
        }

        static bool IsExtensionValid(LoftKnotExtension extension)
        {
            // Check Unity's destroyed-object null before SplineInfo.Container,
            // whose interface reference can still hold a destroyed component.
            return extension.info.Object != null && extension.info.Spline != null
                && extension.info.Spline.Count > 0 && !extension.info.Spline.Closed;
        }

        void UpdateMarqueeSelection(SceneView view)
        {
            m_SelectedKnots.Clear();
            if (m_MarqueeAdditive)
                m_SelectedKnots.AddRange(m_PreMarqueeSelection);
            Camera camera = view.camera;
            foreach (MultiSplineLoft.SplineSource source in m_ActiveLoft.Sources)
            {
                SplineContainer container = source?.container;
                if (container == null || source.splineIndex < 0 || source.splineIndex >= container.Splines.Count)
                    continue;
                var info = new SplineInfo(container, source.splineIndex);
                for (int i = 0; i < info.Spline.Count; i++)
                {
                    var knot = new SelectableKnot(info, i);
                    Vector3 world = knot.Position;
                    // Select the same local, front-facing knots that are drawn.
                    if (camera != null && ((world - camera.transform.position).sqrMagnitude > ReducedHandleCameraDistance * ReducedHandleCameraDistance
                        || camera.WorldToViewportPoint(world).z <= 0f))
                        continue;
                    if (m_MarqueeRect.Contains(HandleUtility.WorldToGUIPoint(world)) && !m_SelectedKnots.Contains(knot))
                        m_SelectedKnots.Add(knot);
                }
            }
            FocusLastSelectedKnot();
        }

        void MoveSelectedKnots(Vector3 delta)
        {
            var targets = new HashSet<UnityEngine.Object>();
            var positions = new Vector3[m_SelectedKnots.Count];
            for (int i = 0; i < m_SelectedKnots.Count; i++)
            {
                targets.Add(m_SelectedKnots[i].SplineInfo.Object);
                positions[i] = m_SelectedKnots[i].Position;
            }
            var undoTargets = new UnityEngine.Object[targets.Count];
            targets.CopyTo(undoTargets);
            Undo.RecordObjects(undoTargets, "Move Loft Spline Knots");
            for (int i = 0; i < m_SelectedKnots.Count; i++)
            {
                var knot = m_SelectedKnots[i];
                knot.Position = (Unity.Mathematics.float3)(positions[i] + delta);
            }
            foreach (UnityEngine.Object target in targets)
            {
                EditorUtility.SetDirty(target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                EditorSceneManager.MarkSceneDirty(((SplineContainer)target).gameObject.scene);
            }
            m_ReducedOverlayGeneration = -1;
            SceneView.RepaintAll();
        }

        void CaptureKnotTransform(Vector3 pivot, Quaternion orientation)
        {
            m_HandlePivot = pivot;
            m_HandleRotation = m_HandleStartRotation = orientation;
            m_HandleScale = Vector3.one;
            m_KnotTransformSnapshots.Clear();
            foreach (SelectableKnot selected in m_SelectedKnots)
                m_KnotTransformSnapshots.Add(new KnotTransformSnapshot
                {
                    selection = selected,
                    knot = selected.SplineInfo.Spline[selected.KnotIndex]
                });
        }

        void TransformSelectedKnots(Matrix4x4 worldTransform, Quaternion rotation, string undoName)
        {
            var targets = new HashSet<UnityEngine.Object>();
            foreach (KnotTransformSnapshot snapshot in m_KnotTransformSnapshots)
                if (snapshot.selection.SplineInfo.Object != null && snapshot.selection.IsValid())
                    targets.Add(snapshot.selection.SplineInfo.Object);
            var undoTargets = new UnityEngine.Object[targets.Count];
            targets.CopyTo(undoTargets);
            Undo.RecordObjects(undoTargets, undoName);

            // Always transform the drag-start knots. Incremental scaling loses
            // information at zero scale and accumulates tangent/rounding errors.
            foreach (KnotTransformSnapshot snapshot in m_KnotTransformSnapshots)
            {
                SelectableKnot selected = snapshot.selection;
                if (selected.SplineInfo.Object == null || !selected.IsValid())
                    continue;
                Transform transform = selected.SplineInfo.Transform;
                Matrix4x4 localTransform = transform.worldToLocalMatrix * worldTransform * transform.localToWorldMatrix;
                BezierKnot knot = snapshot.knot;
                Quaternion oldRotation = knot.Rotation;
                Quaternion newRotation = Quaternion.Inverse(transform.rotation) * rotation * transform.rotation * oldRotation;
                knot.Position = localTransform.MultiplyPoint3x4((Vector3)knot.Position);
                knot.TangentIn = Quaternion.Inverse(newRotation) * localTransform.MultiplyVector(oldRotation * (Vector3)knot.TangentIn);
                knot.TangentOut = Quaternion.Inverse(newRotation) * localTransform.MultiplyVector(oldRotation * (Vector3)knot.TangentOut);
                knot.Rotation = newRotation;
                selected.SplineInfo.Spline.SetKnot(selected.KnotIndex, knot);
            }
            foreach (UnityEngine.Object target in targets)
            {
                EditorUtility.SetDirty(target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                EditorSceneManager.MarkSceneDirty(((SplineContainer)target).gameObject.scene);
            }
            m_ReducedOverlayGeneration = -1;
            SceneView.RepaintAll();
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
            if (Event.current.type != EventType.Repaint)
                return;
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
                    if ((knotPosition - cameraPosition).sqrMagnitude > handleDistanceSquared
                        || sceneView.camera != null && sceneView.camera.WorldToViewportPoint(knotPosition).z <= 0f)
                        continue;

                    bool focused = m_SelectedKnots.Contains(
                        new SelectableKnot(new SplineInfo(container, source.splineIndex), knotIndex));
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
            if (loft == null)
            {
                InvalidateSplineCaches();
                return;
            }
            if (m_ReducedOverlayLoft == loft
                && m_ReducedOverlayGeneration == loft.GenerationVersion
                && m_ReducedOverlayEntries.TrueForAll(IsOverlayEntryValid))
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

        bool IsOverlayEntryValid(ReducedSplineOverlayEntry entry)
        {
            return entry != null && entry.container != null && entry.splineIndex >= 0
                && entry.splineIndex < entry.container.Splines.Count
                && entry.container.Splines[entry.splineIndex] != null
                && entry.container.Splines[entry.splineIndex].Count > 1
                && entry.points != null && entry.points.Length > 1
                && m_ActiveLoft != null && m_ActiveLoft.Sources.Exists(source => source != null
                    && source.container == entry.container && source.splineIndex == entry.splineIndex);
        }

        bool TryFocusKnot(Vector2 mousePosition)
        {
            if (TryFocusVisibleKnot(mousePosition))
                return true;

            if (!Event.current.shift && m_SelectedKnots.Count == 1 && TryFocusTangent(mousePosition))
                return true;

            float bestCurveDistance = ReducedHandlePickRadius * ReducedHandlePickRadius;
            SplineContainer bestContainer = null;
            int bestSpline = -1;
            RefreshReducedSplineOverlay(m_ActiveLoft);
            foreach (ReducedSplineOverlayEntry entry in m_ReducedOverlayEntries)
            {
                if (!IsOverlayEntryValid(entry))
                    continue;
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
                Camera camera = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
                if (camera != null && ((world - camera.transform.position).sqrMagnitude > ReducedHandleCameraDistance * ReducedHandleCameraDistance
                    || camera.WorldToViewportPoint(world).z <= 0f))
                    continue;
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
                        && ((world - cameraPosition).sqrMagnitude > handleDistanceSquared
                            || sceneCamera.WorldToViewportPoint(world).z <= 0f))
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

        Quaternion GetSelectionHandleOrientation()
        {
            // Keep authored local axes for a single knot. A group needs a frame
            // from its geometry, independent of marquee/Shift-click ordering.
            if (m_SelectedKnots.Count == 1)
            {
                SelectableKnot selected = m_SelectedKnots[0];
                if (selected.SplineInfo.Object != null && selected.IsValid())
                    return selected.SplineInfo.Transform.rotation * (Quaternion)selected.SplineInfo.Spline[selected.KnotIndex].Rotation;
            }
            if (m_ActiveLoft == null)
                return Quaternion.identity;

            Vector3 forward = Vector3.zero;
            Vector3 fallbackForward = Vector3.zero;
            Vector3 firstSourceCenter = Vector3.zero;
            Vector3 lastSourceCenter = Vector3.zero;
            int sourceCount = 0;
            var seen = new HashSet<SplineInfo>();
            foreach (MultiSplineLoft.SplineSource source in m_ActiveLoft.Sources)
            {
                if (source?.container == null || source.splineIndex < 0 || source.splineIndex >= source.container.Splines.Count)
                    continue;
                var info = new SplineInfo(source.container, source.splineIndex);
                if (!seen.Add(info) || info.Spline == null)
                    continue;
                var spline = info.Spline;
                Vector3 center = Vector3.zero;
                int count = 0;
                int firstKnotIndex = int.MaxValue;
                Vector3 sourceForward = Vector3.zero;
                foreach (SelectableKnot selected in m_SelectedKnots)
                {
                    if (!selected.SplineInfo.Equals(info) || !selected.IsValid())
                        continue;
                    int index = selected.KnotIndex;
                    center += (Vector3)selected.Position;
                    count++;
                    int previous = spline.Closed ? (index + spline.Count - 1) % spline.Count : Mathf.Max(0, index - 1);
                    int next = spline.Closed ? (index + 1) % spline.Count : Mathf.Min(spline.Count - 1, index + 1);
                    Vector3 direction = source.container.transform.TransformVector(
                        (Vector3)(spline[next].Position - spline[previous].Position));
                    if (source.reverse)
                        direction = -direction;
                    if (direction.sqrMagnitude < 0.000001f)
                        continue;
                    direction.Normalize();
                    forward += direction;
                    if (index < firstKnotIndex)
                    {
                        firstKnotIndex = index;
                        sourceForward = direction;
                    }
                }
                if (count == 0)
                    continue;
                center /= count;
                if (sourceCount++ == 0)
                    firstSourceCenter = center;
                lastSourceCenter = center;
                if (fallbackForward.sqrMagnitude < 0.000001f)
                    fallbackForward = sourceForward;
            }

            Vector3 across = lastSourceCenter - firstSourceCenter;
            if (forward.sqrMagnitude < 0.000001f)
                forward = fallbackForward;
            if (forward.sqrMagnitude < 0.000001f)
                forward = Vector3.Cross(across, Vector3.up);
            if (forward.sqrMagnitude < 0.000001f)
                forward = Vector3.forward;
            forward.Normalize();

            // Across-source positions describe the local banking. Flip only
            // the normal's sign so source ordering cannot turn Y upside down.
            Vector3 up = Vector3.Cross(forward, across);
            if (up.sqrMagnitude < 0.000001f)
                up = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (up.sqrMagnitude < 0.000001f)
                up = Vector3.ProjectOnPlane(Vector3.forward, forward);
            if (Vector3.Dot(up, Vector3.up) < 0f)
                up = -up;
            return Quaternion.LookRotation(forward, up.normalized);
        }

        void DrawFocusedKnot(SceneView sceneView)
        {
            if (m_MarqueeControl != 0 || !TryGetFocusedKnotWorldPosition(out Vector3 position))
                return;

            var spline = m_FocusedSpline.Splines[m_FocusedSplineIndex];
            BezierKnot knot = spline[m_FocusedKnotIndex];
            if (sceneView.camera != null
                && Vector3.Distance(sceneView.camera.transform.position, position) > ReducedHandleCameraDistance)
                return;

            Vector3 pivot = Vector3.zero;
            foreach (SelectableKnot selected in m_SelectedKnots)
                pivot += (Vector3)selected.Position;
            if (m_SelectedKnots.Count == 0)
                return;
            pivot /= m_SelectedKnots.Count;

            Quaternion orientation = Tools.pivotRotation == PivotRotation.Local
                ? GetSelectionHandleOrientation()
                : Quaternion.identity;
            if (GUIUtility.hotControl == 0)
                CaptureKnotTransform(pivot, orientation);

            switch (Tools.current)
            {
                case Tool.Rotate:
                    EditorGUI.BeginChangeCheck();
                    Quaternion rotated = Handles.RotationHandle(m_HandleRotation, m_HandlePivot);
                    if (EditorGUI.EndChangeCheck())
                    {
                        m_HandleRotation = rotated;
                        Quaternion delta = rotated * Quaternion.Inverse(m_HandleStartRotation);
                        TransformSelectedKnots(Matrix4x4.TRS(m_HandlePivot, delta, Vector3.one)
                            * Matrix4x4.Translate(-m_HandlePivot), delta, "Rotate Loft Spline Knots");
                    }
                    break;
                case Tool.Scale:
                    EditorGUI.BeginChangeCheck();
                    Vector3 scaled = Handles.ScaleHandle(m_HandleScale, m_HandlePivot, m_HandleStartRotation,
                        HandleUtility.GetHandleSize(m_HandlePivot));
                    if (EditorGUI.EndChangeCheck())
                    {
                        m_HandleScale = scaled;
                        TransformSelectedKnots(Matrix4x4.TRS(m_HandlePivot, m_HandleStartRotation, scaled)
                            * Matrix4x4.Rotate(Quaternion.Inverse(m_HandleStartRotation))
                            * Matrix4x4.Translate(-m_HandlePivot), Quaternion.identity, "Scale Loft Spline Knots");
                    }
                    break;
                case Tool.Move:
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.PositionHandle(pivot, m_HandleStartRotation);
                    if (EditorGUI.EndChangeCheck())
                        MoveSelectedKnots(moved - pivot);
                    break;
            }

            if (m_SelectedKnots.Count == 1 && Tools.current == Tool.Move)
            {
                knot = spline[m_FocusedKnotIndex];
                position = m_FocusedSpline.transform.TransformPoint((Vector3)knot.Position);
                DrawFocusedTangents(spline, ref knot, position);
            }
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

            // Selection updates and embedded-window activation must not cancel
            // knot placement for a source of the loft already being edited.
            if (m_ActiveLoft == loft && IsDrawingSpline)
                return;

            if (m_ActiveLoft != loft)
            {
                CancelMarquee();
                m_SelectedKnots.Clear();
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
            if (Tools.current != Tool.Move && Tools.current != Tool.Rotate && Tools.current != Tool.Scale)
                Tools.current = Tool.Move;
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
                Tools.current = Tool.Move;
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

        public void CreateLoftFromOverlay()
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Multi-Spline Loft");

            // Cancel pending drawing callbacks before selecting the new system.
            EditorApplication.delayCall -= ApplyQueuedKnotPlacementSelection;
            EditorApplication.delayCall -= ActivateKnotPlacementTool;
            m_QueuedKnotPlacementTarget = null;
            if (ToolManager.activeContextType == typeof(SplineToolContext))
                ToolManager.SetActiveContext<GameObjectToolContext>();

            SceneView view = SceneView.lastActiveSceneView;
            Vector3 position = view != null ? view.pivot : Vector3.zero;
            Vector3 forward = view != null ? Vector3.ProjectOnPlane(view.rotation * Vector3.forward, Vector3.up) : Vector3.forward;
            Quaternion rotation = forward.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : Quaternion.identity;
            if (view != null && view.camera != null
                && Physics.Raycast(view.camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out RaycastHit hit))
                position = hit.point;

            CancelMarquee();
            m_SelectedKnots.Clear();
            FocusLastSelectedKnot();
            m_ActiveLoft = MultiSplineLoftEditor.CreateStarterLoft(position + Vector3.up * 0.1f, rotation);
            Selection.activeGameObject = m_ActiveLoft.gameObject;
            EnterUnitySplineEditMode(m_ActiveLoft);
            Undo.CollapseUndoOperations(undoGroup);
            SceneView.RepaintAll();
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
                "Shift-click empty ground to extend all open source splines at the nearest loft end, preserving their spacing. Shift-click a knot to toggle selection; Shift-drag to add a box selection. Long roads show knot controls within 100 m of the Scene camera.",
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

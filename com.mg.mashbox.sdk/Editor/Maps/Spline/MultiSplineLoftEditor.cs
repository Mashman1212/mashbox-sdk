using System.Collections.Generic;
using System.IO;
using MashBoxSDK.EditorResources;
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
        SerializedProperty m_CloseAlongClosedSplines;
        SerializedProperty m_CloseAcrossSplines;
        SerializedProperty m_CapStart;
        SerializedProperty m_CapEnd;
        SerializedProperty m_DoubleSided;
        SerializedProperty m_UpdateMeshCollider;
        SerializedProperty m_ColliderChunkLength;
        SerializedProperty m_NormalMode;
        SerializedProperty m_FlipNormals;
        SerializedProperty m_UvScaleAlong;
        SerializedProperty m_UvScaleAcross;
        SerializedProperty m_GenerateUvSplineWithLoft;
        SerializedProperty m_UvSplineChannel;
        SerializedProperty m_UvSplineDirection;
        SerializedProperty m_UvSplinePointCount;
        SerializedProperty m_UvSpline;
        SerializedProperty m_SculptModifier;
        SerializedProperty m_ShoulderModifier;
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
            m_CloseAlongClosedSplines = serializedObject.FindProperty("m_CloseAlongClosedSplines");
            m_CloseAcrossSplines = serializedObject.FindProperty("m_CloseAcrossSplines");
            m_CapStart = serializedObject.FindProperty("m_CapStart");
            m_CapEnd = serializedObject.FindProperty("m_CapEnd");
            m_DoubleSided = serializedObject.FindProperty("m_DoubleSided");
            m_UpdateMeshCollider = serializedObject.FindProperty("m_UpdateMeshCollider");
            m_ColliderChunkLength = serializedObject.FindProperty("m_ColliderChunkLength");
            m_NormalMode = serializedObject.FindProperty("m_NormalMode");
            m_FlipNormals = serializedObject.FindProperty("m_FlipNormals");
            m_UvScaleAlong = serializedObject.FindProperty("m_UvScaleAlong");
            m_UvScaleAcross = serializedObject.FindProperty("m_UvScaleAcross");
            m_GenerateUvSplineWithLoft = serializedObject.FindProperty("m_GenerateUvSplineWithLoft");
            m_UvSplineChannel = serializedObject.FindProperty("m_UvSplineChannel");
            m_UvSplineDirection = serializedObject.FindProperty("m_UvSplineDirection");
            m_UvSplinePointCount = serializedObject.FindProperty("m_UvSplinePointCount");
            m_UvSpline = serializedObject.FindProperty("m_UvSpline");
            m_SculptModifier = serializedObject.FindProperty("m_SculptModifier");
            m_ShoulderModifier = serializedObject.FindProperty("m_ShoulderModifier");
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
            EditorGUILayout.PropertyField(m_UvScaleAcross, new GUIContent("UV Across Scale", "1 maps the full left-to-right width to U 0-1. Higher values repeat across the width."));
            EditorGUILayout.PropertyField(m_UvScaleAlong, new GUIContent("UV Along Scale"));
            EditorGUILayout.PropertyField(m_UpdateMeshCollider, new GUIContent("Generate Collider Chunks"));
            using (new EditorGUI.DisabledScope(!m_UpdateMeshCollider.boolValue))
                EditorGUILayout.PropertyField(m_ColliderChunkLength, new GUIContent("Collider Chopping Distance", "Creates a separate child MeshCollider for approximately this many meters of track."));
            EditorGUILayout.PropertyField(m_AutoRegenerate, new GUIContent("Live Regenerate"));
            EditorGUILayout.PropertyField(m_SculptModifier, new GUIContent("Sculpt Modifier", "Replays recorded sculpt strokes after every loft regeneration."));
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

        static void GenerateUvSpline(MultiSplineLoft loft)
        {
            Undo.RecordObject(loft, "Generate UV Spline");
            if (!loft.RegenerateUvSpline(out string error))
            {
                EditorUtility.DisplayDialog("Generate UV Spline", error, "OK");
                return;
            }

            EditorUtility.SetDirty(loft);
            EditorUtility.SetDirty(loft.GeneratedUvSpline);
            Selection.activeGameObject = loft.GeneratedUvSpline.gameObject;
            SceneView.RepaintAll();
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

        internal static void CreateOrRebuildShoulders(MultiSplineLoft loft)
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

            loft.Regenerate();
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
        MultiSplineLoft m_ActiveLoft;
        Vector2 m_Scroll;
        bool m_SceneToolActive;
        bool m_ChangingSelection;
        bool m_SplineEditActivationQueued;
        UnityEngine.Object[] m_QueuedSplineTargets;
        int m_SplineToolActivationAttempts;
        SplineContainer m_QueuedKnotPlacementTarget;

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
            if (m_SceneToolActive)
            {
                MultiSplineLoft selectedLoft = Selection.activeGameObject != null
                    ? Selection.activeGameObject.GetComponent<MultiSplineLoft>()
                    : null;
                if (selectedLoft != null)
                {
                    m_ActiveLoft = selectedLoft;
                    EnterUnitySplineEditMode(selectedLoft);
                }
                return;
            }
            m_SceneToolActive = true;
            Selection.selectionChanged += OnSelectionChanged;
            UseSelectedLoftAndEditSplines();
        }

        public void DeactivateSceneTool()
        {
            if (!m_SceneToolActive) return;
            m_SceneToolActive = false;
            Selection.selectionChanged -= OnSelectionChanged;
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
            MultiSplineLoft selectedLoft = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<MultiSplineLoft>()
                : null;
            if (selectedLoft == null) return;

            m_ActiveLoft = selectedLoft;
            EnterUnitySplineEditMode(selectedLoft);
            Repaint();
        }

        void UseSelectedLoftAndEditSplines()
        {
            MultiSplineLoft selectedLoft = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<MultiSplineLoft>()
                : null;
            if (selectedLoft != null)
                m_ActiveLoft = selectedLoft;
            if (m_ActiveLoft != null)
                EnterUnitySplineEditMode(m_ActiveLoft);
        }

        void EnterUnitySplineEditMode(MultiSplineLoft loft)
        {
            if (loft == null) return;
            var targets = new List<UnityEngine.Object>();
            foreach (MultiSplineLoft.SplineSource source in loft.Sources)
            {
                GameObject splineObject = source?.container != null ? source.container.gameObject : null;
                if (splineObject != null && !targets.Contains(splineObject))
                    targets.Add(splineObject);
            }
            if (targets.Count == 0) return;

            m_QueuedSplineTargets = targets.ToArray();
            m_SplineToolActivationAttempts = 0;
            if (m_SplineEditActivationQueued) return;
            m_SplineEditActivationQueued = true;
            EditorApplication.delayCall -= ApplyQueuedSplineSelection;
            EditorApplication.delayCall += ApplyQueuedSplineSelection;
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
                    m_ActiveLoft = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<MultiSplineLoft>() : null;
                    if (m_ActiveLoft != null)
                        EnterUnitySplineEditMode(m_ActiveLoft);
                }

                if (GUILayout.Button("Create From Splines"))
                    MultiSplineLoftEditor.CreateLoftFromSelection();
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Spline Editing", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Move / Edit Knots"))
                    QueueSplineMoveTool();
                if (GUILayout.Button("Draw / Add Knots"))
                    QueueKnotPlacementTool();
            }
            using (new EditorGUI.DisabledScope(m_ActiveLoft == null))
            {
                if (GUILayout.Button("Create New Loft Spline", GUILayout.Height(24f)))
                    CreateAndAddLoftSpline();
            }
            EditorGUILayout.HelpBox("Select knots with Unity's spline handles. Press Delete or Backspace to remove selected knots.", MessageType.None);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Selected Splines", EditorStyles.boldLabel);
            var selected = GetSelectedSplineContainersForWindow();
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
                    if (!m_ActiveLoft.RegenerateUvSpline(out string error))
                        EditorUtility.DisplayDialog("Generate UV Spline", error, "OK");
                    else
                        Selection.activeGameObject = m_ActiveLoft.GeneratedUvSpline.gameObject;
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

using System.Collections.Generic;
using System.IO;
using MashBoxSDK.EditorResources;
using UnityEditor;
using UnityEditorInternal;
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
        SerializedProperty m_AutoRegenerate;
        SerializedProperty m_CloseAlongClosedSplines;
        SerializedProperty m_CloseAcrossSplines;
        SerializedProperty m_CapStart;
        SerializedProperty m_CapEnd;
        SerializedProperty m_DoubleSided;
        SerializedProperty m_UpdateMeshCollider;
        SerializedProperty m_NormalMode;
        SerializedProperty m_FlipNormals;
        SerializedProperty m_UvScaleAlong;
        SerializedProperty m_UvScaleAcross;
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
            m_AutoRegenerate = serializedObject.FindProperty("m_AutoRegenerate");
            m_CloseAlongClosedSplines = serializedObject.FindProperty("m_CloseAlongClosedSplines");
            m_CloseAcrossSplines = serializedObject.FindProperty("m_CloseAcrossSplines");
            m_CapStart = serializedObject.FindProperty("m_CapStart");
            m_CapEnd = serializedObject.FindProperty("m_CapEnd");
            m_DoubleSided = serializedObject.FindProperty("m_DoubleSided");
            m_UpdateMeshCollider = serializedObject.FindProperty("m_UpdateMeshCollider");
            m_NormalMode = serializedObject.FindProperty("m_NormalMode");
            m_FlipNormals = serializedObject.FindProperty("m_FlipNormals");
            m_UvScaleAlong = serializedObject.FindProperty("m_UvScaleAlong");
            m_UvScaleAcross = serializedObject.FindProperty("m_UvScaleAcross");
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
            EditorGUILayout.PropertyField(m_UvScaleAcross, new GUIContent("UV Across Scale"));
            EditorGUILayout.PropertyField(m_UvScaleAlong, new GUIContent("UV Along Scale"));
            EditorGUILayout.PropertyField(m_UpdateMeshCollider, new GUIContent("Update Mesh Collider"));
            EditorGUILayout.PropertyField(m_AutoRegenerate, new GUIContent("Live Regenerate"));

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Generated Samples Along", loft.CurrentSamplesAlong);
                EditorGUILayout.PropertyField(m_GeneratedMesh, new GUIContent("Generated Mesh"));
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

            if (loft.TryGetComponent<MeshCollider>(out var meshCollider))
            {
                Undo.RecordObject(meshCollider, "Assign Baked Loft Collider");
                meshCollider.sharedMesh = bakedMesh;
            }

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

        public static void ShowWindow()
        {
            GetWindow<MultiSplineLoftWindow>("Multi-Spline Loft");
        }

        void OnGUI()
        {
            Draw();
        }

        public void Draw(bool embeddedInParentWindow = false)
        {
            if (!embeddedInParentWindow)
                m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
            m_ActiveLoft = (MultiSplineLoft)EditorGUILayout.ObjectField("Loft Component", m_ActiveLoft, typeof(MultiSplineLoft), true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Selection"))
                    m_ActiveLoft = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<MultiSplineLoft>() : null;

                if (GUILayout.Button("Create From Splines"))
                    MultiSplineLoftEditor.CreateLoftFromSelection();
            }

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
                    EditorUtility.SetDirty(m_ActiveLoft);
                }
            }

            using (new EditorGUI.DisabledScope(m_ActiveLoft == null))
            {
                if (GUILayout.Button("Generate Active Loft", GUILayout.Height(26f)))
                {
                    Undo.RecordObject(m_ActiveLoft, "Generate Multi Spline Loft");
                    m_ActiveLoft.Regenerate();
                    EditorUtility.SetDirty(m_ActiveLoft);
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

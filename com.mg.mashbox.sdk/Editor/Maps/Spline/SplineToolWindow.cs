using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Splines;
using MashBoxSDK.MapTools;

namespace MashBoxSDK.Maps.Spline
{
    public sealed class SplineToolWindow : EditorWindow
    {
        const int ReducedHandleKnotThreshold = 64;
        static SplineToolWindow s_ActiveSceneToolOwner;

        SplineContainer m_ActiveSpline;
        UnityEditor.Editor m_SplineInspector;
        bool m_SceneToolActive;
        bool m_ChangingSelection;

        internal static bool HasActiveSceneTool =>
            s_ActiveSceneToolOwner != null && s_ActiveSceneToolOwner.m_SceneToolActive;

        internal static void DeactivateActiveSceneTool()
        {
            s_ActiveSceneToolOwner?.DeactivateSceneTool();
        }

        public static void ShowWindow()
        {
            var window = GetWindow<SplineToolWindow>("Spline");
            window.ActivateSceneTool();
        }

        void OnGUI()
        {
            Draw();
        }

        void OnDisable()
        {
            DeactivateSceneTool();
            DestroyCachedInspector();
        }

        public void ActivateSceneTool()
        {
            if (s_ActiveSceneToolOwner != null && s_ActiveSceneToolOwner != this)
                s_ActiveSceneToolOwner.DeactivateSceneTool();
            s_ActiveSceneToolOwner = this;

            if (m_SceneToolActive)
            {
                UseSplineFromSelection(activateMoveTool: true, selectOnlyThisSpline: true);
                return;
            }

            m_SceneToolActive = true;
            Selection.selectionChanged += OnSelectionChanged;
            SceneView.duringSceneGui += OnSceneGui;
            UseSplineFromSelection(activateMoveTool: true, selectOnlyThisSpline: true);
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
            SceneView.duringSceneGui -= OnSceneGui;
            EditorApplication.delayCall -= ActivateMoveTool;
            EditorApplication.delayCall -= ActivateKnotPlacementTool;
            if (ToolManager.activeContextType == typeof(SplineToolContext))
                ToolManager.SetActiveContext<GameObjectToolContext>();
        }

        public void Draw(bool embeddedInParentWindow = false)
        {
            EditorGUILayout.LabelField("Spline", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var target = (SplineContainer)EditorGUILayout.ObjectField(
                "Spline Container", m_ActiveSpline, typeof(SplineContainer), true);
            if (EditorGUI.EndChangeCheck())
                SetActiveSpline(target, select: false);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(FindSplineInSelection() == null))
                {
                    if (GUILayout.Button("Use Selection"))
                        UseSplineFromSelection();
                }

                if (GUILayout.Button("Create New Spline"))
                    CreateSpline();
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Spline Editing", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!MBEditorToolState.ActiveEditing || m_ActiveSpline == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Move / Edit Knots"))
                        QueueMoveTool();
                    if (GUILayout.Button("Draw / Add Knots"))
                        QueueKnotPlacementTool();
                }
            }

            using (new EditorGUI.DisabledScope(m_ActiveSpline == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select In Hierarchy"))
                    {
                        Selection.activeGameObject = m_ActiveSpline.gameObject;
                        EditorGUIUtility.PingObject(m_ActiveSpline.gameObject);
                    }

                    if (GUILayout.Button("Frame In Scene"))
                    {
                        Selection.activeGameObject = m_ActiveSpline.gameObject;
                        SceneView.lastActiveSceneView?.FrameSelected();
                    }
                }
            }

            EditorGUILayout.HelpBox(
                "Select knots with Unity's spline handles. Press Delete or Backspace to remove selected knots.",
                MessageType.None);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Spline Container", EditorStyles.boldLabel);
            if (m_ActiveSpline == null)
            {
                DestroyCachedInspector();
                EditorGUILayout.HelpBox(
                    "Create a spline or select a GameObject with a SplineContainer to manage its splines and knots.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "The controls below are Unity's full Spline Container inspector. Use them to add, remove, reorder, close, and edit individual splines.",
                MessageType.None);
            Editor.CreateCachedEditor(m_ActiveSpline, null, ref m_SplineInspector);
            m_SplineInspector?.OnInspectorGUI();
        }

        void OnSelectionChanged()
        {
            if (!m_SceneToolActive || m_ChangingSelection)
                return;

            var selected = FindSplineInSelection();
            if (selected != null)
            {
                SetActiveSpline(selected, select: false);
                SelectOnlySpline(selected);
                QueueMoveTool();
            }
            Repaint();
        }

        // Single-spline editing should select visible spline curves, not the mesh
        // collider behind them. Unity's default scene selection only knows about
        // renderers and colliders, so provide a spline-first pick control here.
        void OnSceneGui(SceneView sceneView)
        {
            if (!m_SceneToolActive || !MBEditorToolState.ActiveEditing || MBEditorToolState.Mode != MBEditorAuthoringMode.Spline)
                return;

            if (m_ActiveSpline != null
                && UsesReducedHandles(m_ActiveSpline)
                && ToolManager.activeContextType == typeof(SplineToolContext))
            {
                ToolManager.SetActiveContext<GameObjectToolContext>();
            }

            Event current = Event.current;
            if (current.alt || current.button != 0)
                return;

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (current.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlId);
                return;
            }

            if (current.type != EventType.MouseDown || GUIUtility.hotControl != 0)
                return;

            if (!TryFindSplineAtMouse(current.mousePosition, out SplineContainer spline))
                return;

            SetActiveSpline(spline, select: false);
            SelectOnlySpline(spline);
            QueueMoveTool();
            current.Use();
        }

        static bool TryFindSplineAtMouse(Vector2 mousePosition, out SplineContainer closestSpline)
        {
            const float pickRadius = 14f;
            float closestDistance = pickRadius * pickRadius;
            closestSpline = null;

            foreach (SplineContainer container in Resources.FindObjectsOfTypeAll<SplineContainer>())
            {
                if (container == null || EditorUtility.IsPersistent(container) || !container.gameObject.scene.IsValid())
                    continue;

                for (int splineIndex = 0; splineIndex < container.Splines.Count; splineIndex++)
                {
                    var spline = container.Splines[splineIndex];
                    if (spline == null || spline.Count == 0)
                        continue;

                    int sampleCount = Mathf.Max(16, spline.Count * 12);
                    Vector2 previous = HandleUtility.WorldToGUIPoint(container.EvaluatePosition(splineIndex, 0f));
                    for (int sample = 1; sample <= sampleCount; sample++)
                    {
                        Vector2 current = HandleUtility.WorldToGUIPoint(container.EvaluatePosition(splineIndex, sample / (float)sampleCount));
                        float distance = DistanceToSegmentSquared(mousePosition, previous, current);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestSpline = container;
                        }
                        previous = current;
                    }
                }
            }

            return closestSpline != null;
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

        void UseSplineFromSelection(bool activateMoveTool = false, bool selectOnlyThisSpline = false)
        {
            var selected = FindSplineInSelection();
            if (selected != null)
            {
                SetActiveSpline(selected, select: false);
                if (selectOnlyThisSpline)
                    SelectOnlySpline(selected);
                if (activateMoveTool)
                    QueueMoveTool();
            }
        }

        void SelectOnlySpline(SplineContainer spline)
        {
            if (spline == null)
                return;

            UnityEngine.Object[] selectedObjects = Selection.objects;
            if (selectedObjects != null
                && selectedObjects.Length == 1
                && selectedObjects[0] == spline.gameObject)
            {
                return;
            }

            m_ChangingSelection = true;
            Selection.objects = new UnityEngine.Object[] { spline.gameObject };
            m_ChangingSelection = false;
        }

        static SplineContainer FindSplineInSelection()
        {
            if (Selection.activeGameObject == null)
                return null;

            return Selection.activeGameObject.GetComponent<SplineContainer>()
                ?? Selection.activeGameObject.GetComponentInParent<SplineContainer>();
        }

        void SetActiveSpline(SplineContainer spline, bool select)
        {
            if (m_ActiveSpline != spline)
            {
                m_ActiveSpline = spline;
                DestroyCachedInspector();
            }

            if (select && spline != null)
                Selection.activeGameObject = spline.gameObject;
        }

        void CreateSpline()
        {
            var splineObject = new GameObject("Spline", typeof(SplineContainer));
            Undo.RegisterCreatedObjectUndo(splineObject, "Create Spline");

            Transform parent = Selection.activeTransform;
            if (parent != null && parent.GetComponent<SplineContainer>() != null)
                parent = parent.parent;
            if (parent != null)
                splineObject.transform.SetParent(parent, false);

            SetActiveSpline(splineObject.GetComponent<SplineContainer>(), select: true);
            QueueKnotPlacementTool();
        }

        public void CreateSplineFromOverlay()
        {
            CreateSpline();
        }

        public void SelectMoveToolFromOverlay()
        {
            UseSplineFromSelection();
            QueueMoveTool();
        }

        public void SelectDrawToolFromOverlay()
        {
            UseSplineFromSelection();
            QueueKnotPlacementTool();
        }

        void QueueMoveTool()
        {
            if (m_ActiveSpline == null)
                return;

            Selection.activeGameObject = m_ActiveSpline.gameObject;
            EditorApplication.delayCall -= ActivateMoveTool;
            EditorApplication.delayCall += ActivateMoveTool;
        }

        void ActivateMoveTool()
        {
            EditorApplication.delayCall -= ActivateMoveTool;
            if (!m_SceneToolActive || m_ActiveSpline == null)
                return;

            // Unity's SplineMoveTool renders a control for every knot and tangent.
            // That becomes the dominant editor cost on long road splines. Selection
            // still works through our lightweight curve picker, so skip those
            // handles until the spline is back to a manageable size.
            if (UsesReducedHandles(m_ActiveSpline))
            {
                ToolManager.SetActiveContext<GameObjectToolContext>();
                SceneView.RepaintAll();
                return;
            }

            ToolManager.SetActiveContext<SplineToolContext>();
            ToolManager.SetActiveTool<SplineMoveTool>();
            SceneView.RepaintAll();
        }

        static bool UsesReducedHandles(SplineContainer container)
        {
            if (container == null)
                return false;

            int knotCount = 0;
            foreach (var spline in container.Splines)
            {
                knotCount += spline.Count;
                if (knotCount > ReducedHandleKnotThreshold)
                    return true;
            }

            return false;
        }

        void QueueKnotPlacementTool()
        {
            if (m_ActiveSpline == null)
                return;

            Selection.activeGameObject = m_ActiveSpline.gameObject;
            EditorApplication.delayCall -= ActivateKnotPlacementTool;
            EditorApplication.delayCall += ActivateKnotPlacementTool;
        }

        void ActivateKnotPlacementTool()
        {
            EditorApplication.delayCall -= ActivateKnotPlacementTool;
            if (!m_SceneToolActive || m_ActiveSpline == null)
                return;

            EditorSplineUtility.SetKnotPlacementTool();
            SceneView.RepaintAll();
        }

        void DestroyCachedInspector()
        {
            if (m_SplineInspector == null)
                return;

            DestroyImmediate(m_SplineInspector);
            m_SplineInspector = null;
        }
    }
}

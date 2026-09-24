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
        static SplineToolWindow s_ActiveSceneToolOwner;

        SplineContainer m_ActiveSpline;
        UnityEditor.Editor m_SplineInspector;
        bool m_SceneToolActive;
        bool m_ChangingSelection;
        Tool m_RequestedTool = Tool.Move;

        // Unity's drawing tool is internal; the public utility activates it.
        static bool IsDrawing => ToolManager.activeToolType?.Name == "CreateSplineTool";

        internal static bool HasActiveSceneTool =>
            s_ActiveSceneToolOwner != null && s_ActiveSceneToolOwner.m_SceneToolActive;

        internal static SplineToolWindow ActiveSceneTool =>
            HasActiveSceneTool ? s_ActiveSceneToolOwner : null;

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
                return;

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
                "In the Scene view: D to draw points; W to move, E to rotate, R to scale. "
                + "Shift-click to select multiple points. Delete or Backspace removes selected points.",
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
            if (selected != null && selected != m_ActiveSpline)
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

            Event current = Event.current;
            DrawSceneControls();
            HandleShortcuts(current);
            // Drawing owns clicks on both empty space and existing curves.
            // Never let our container picker steal a knot placement click.
            if (IsDrawing || current.alt || current.button != 0)
                return;

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (current.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlId);
                return;
            }

            if (current.type != EventType.MouseDown || GUIUtility.hotControl != 0
                || HandleUtility.nearestControl != controlId
                || current.shift || current.control || current.command)
                return;

            if (!TryFindSplineAtMouse(current.mousePosition, out SplineContainer spline))
                return;

            SetActiveSpline(spline, select: false);
            SelectOnlySpline(spline);
            QueueMoveTool();
            current.Use();
        }

        void DrawSceneControls()
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(12f, 12f, 440f, 88f), GUI.skin.box);
            GUILayout.Label("Single Spline", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(m_ActiveSpline == null))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(IsDrawing, "Draw (D)", EditorStyles.miniButtonLeft) && !IsDrawing)
                    QueueKnotPlacementTool();
                DrawManipulationButton("Move (W)", Tool.Move, typeof(SplineMoveTool));
                DrawManipulationButton("Rotate (E)", Tool.Rotate, typeof(SplineRotateTool));
                DrawManipulationButton("Scale (R)", Tool.Scale, typeof(SplineScaleTool));
            }
            GUILayout.Label(m_ActiveSpline == null
                ? "Select a spline curve or create a new spline."
                : IsDrawing
                    ? "Click to add points. Click an endpoint to extend. W: edit points."
                    : "Click points to edit. Shift: multi-select. Delete: remove points.", EditorStyles.miniLabel);
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        void DrawManipulationButton(string label, Tool tool, System.Type toolType)
        {
            bool active = ToolManager.activeToolType == toolType;
            if (GUILayout.Toggle(active, label, EditorStyles.miniButtonMid) && !active)
                QueueManipulationTool(tool);
        }

        void HandleShortcuts(Event current)
        {
            if (m_ActiveSpline == null || current.type != EventType.KeyDown
                || current.alt || current.control || current.command || current.shift
                || EditorGUIUtility.editingTextField || GUIUtility.hotControl != 0)
                return;

            switch (current.keyCode)
            {
                case KeyCode.D: QueueKnotPlacementTool(); break;
                case KeyCode.W: QueueManipulationTool(Tool.Move); break;
                case KeyCode.E: QueueManipulationTool(Tool.Rotate); break;
                case KeyCode.R: QueueManipulationTool(Tool.Scale); break;
                default: return;
            }
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
            QueueManipulationTool(Tool.Move);
        }

        void QueueManipulationTool(Tool tool)
        {
            if (m_ActiveSpline == null)
                return;

            SelectOnlySpline(m_ActiveSpline);
            m_RequestedTool = tool;
            EditorApplication.delayCall -= ActivateKnotPlacementTool;
            EditorApplication.delayCall -= ActivateMoveTool;
            EditorApplication.delayCall += ActivateMoveTool;
        }

        void ActivateMoveTool()
        {
            EditorApplication.delayCall -= ActivateMoveTool;
            if (!m_SceneToolActive || m_ActiveSpline == null)
                return;

            ToolManager.SetActiveContext<SplineToolContext>();
            switch (m_RequestedTool)
            {
                case Tool.Rotate: ToolManager.SetActiveTool<SplineRotateTool>(); break;
                case Tool.Scale: ToolManager.SetActiveTool<SplineScaleTool>(); break;
                default: ToolManager.SetActiveTool<SplineMoveTool>(); break;
            }
            SceneView.RepaintAll();
        }

        void QueueKnotPlacementTool()
        {
            if (m_ActiveSpline == null)
                return;

            SelectOnlySpline(m_ActiveSpline);
            EditorApplication.delayCall -= ActivateMoveTool;
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

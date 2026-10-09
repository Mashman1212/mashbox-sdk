using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using MashBoxSDK.MapTools;
using UnitySpline = UnityEngine.Splines.Spline;

namespace MashBoxSDK.Maps.Spline
{
    public sealed class SplineToolWindow : EditorWindow
    {
        static SplineToolWindow s_ActiveSceneToolOwner;

        SplineContainer m_ActiveSpline;
        UnityEditor.Editor m_SplineInspector;
        bool m_SceneToolActive;
        bool m_ChangingSelection;
        bool m_InsertHeld;
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
            SceneView.beforeSceneGui += OnBeforeSceneGui;
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
            m_InsertHeld = false;
            if (s_ActiveSceneToolOwner == this)
                s_ActiveSceneToolOwner = null;
            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.beforeSceneGui -= OnBeforeSceneGui;
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

        // Claim placement clicks only. Shift in edit mode belongs to Unity's
        // knot selection and rectangle selector. Preview drawing stays
        // in duringSceneGui, where Scene view Handles are ready to repaint.
        void OnBeforeSceneGui(SceneView sceneView)
        {
            if (!m_SceneToolActive || !MBEditorToolState.ActiveEditing || MBEditorToolState.Mode != MBEditorAuthoringMode.Spline)
            {
                m_InsertHeld = false;
                return;
            }

            Event current = Event.current;
            if (current.type == EventType.MouseLeaveWindow || current.type == EventType.Ignore)
                m_InsertHeld = false;
            if (current.keyCode == KeyCode.I && current.type == EventType.KeyUp)
            {
                m_InsertHeld = false;
                current.Use();
                sceneView.Repaint();
                return;
            }
            if (current.keyCode == KeyCode.I && current.type == EventType.KeyDown
                && !current.alt && !current.control && !current.command && !current.shift
                && !EditorGUIUtility.editingTextField && GUIUtility.hotControl == 0)
            {
                m_InsertHeld = true;
                current.Use();
                sceneView.Repaint();
                return;
            }
            if (current.type == EventType.MouseDown && current.button == 0
                && ((current.control || m_InsertHeld) && !current.shift || IsDrawing && current.shift && !current.control))
            {
                HandleKnotPlacement(sceneView, current, earlyClick: true);
                if (m_InsertHeld && !current.alt && !current.command && GUIUtility.hotControl == 0 && current.type != EventType.Used)
                    current.Use();
            }
        }

        // Single-spline editing should select visible spline curves, not the mesh
        // collider behind them. Unity's default scene selection only knows about
        // renderers and colliders, so provide a spline-first pick control here.
        void OnSceneGui(SceneView sceneView)
        {
            if (!m_SceneToolActive || !MBEditorToolState.ActiveEditing || MBEditorToolState.Mode != MBEditorAuthoringMode.Spline)
                return;

            Event current = Event.current;
            HandleShortcuts(current);
            if (!MBEditorToolState.ActiveEditing || current.type == EventType.Used)
                return;
            if (HandleKnotPlacement(sceneView, current, earlyClick: false))
                return;
            // Drawing owns clicks on both empty space and existing curves.
            // Never let our container picker steal a knot placement click.
            if (IsDrawing || current.alt || current.shift || current.control || current.command || current.button != 0)
                return;

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (current.type == EventType.Layout)
            {
                // Unity's rectangle selector must keep empty-space drags.
                // Only compete for clicks on a different spline container.
                if (TryFindSplineAtMouse(current.mousePosition, out SplineContainer hovered)
                    && hovered != m_ActiveSpline)
                    HandleUtility.AddControl(controlId, 0f);
                return;
            }

            if (current.type != EventType.MouseDown || GUIUtility.hotControl != 0
                || HandleUtility.nearestControl != controlId)
                return;

            if (!TryFindSplineAtMouse(current.mousePosition, out SplineContainer spline))
                return;

            SetActiveSpline(spline, select: false);
            SelectOnlySpline(spline);
            QueueMoveTool();
            current.Use();
        }

        bool HandleKnotPlacement(SceneView view, Event current, bool earlyClick)
        {
            if (m_ActiveSpline == null || current.alt || current.command)
                return false;

            // Leave Ctrl+Shift to Unity's surface snapping.
            bool inserting = (current.control || m_InsertHeld) && !current.shift;
            bool extending = IsDrawing && current.shift && !current.control;
            if (!inserting && !extending)
                return false;

            view.wantsMouseMove = true;
            if (current.type == EventType.MouseMove || current.type == EventType.KeyDown || current.type == EventType.KeyUp)
                view.Repaint();

            if (!earlyClick)
            {
                int controlId = GUIUtility.GetControlID(FocusType.Passive);
                if (current.type == EventType.Layout)
                    HandleUtility.AddDefaultControl(controlId);
            }

            if (GUIUtility.hotControl != 0 || (!earlyClick && EditorWindow.mouseOverWindow != view))
                return false;

            // Unity's knot handles can be the nearest control even while our
            // placement preview is visible. The modifier and Scene view check
            // above reserve this click for Mappy instead.
            bool click = current.type == EventType.MouseDown && current.button == 0;
            UnitySpline editedSpline;
            int editedKnot;
            if (inserting)
            {
                if (!TryFindInsertion(current.mousePosition, out UnitySpline spline, out int curve, out float t, out Vector3 position))
                    return false;

                if (current.type == EventType.Repaint)
                    DrawPlacementMarker(position, "I / Ctrl-click: insert knot", Color.yellow);
                if (!click)
                    return false;

                Undo.RecordObject(m_ActiveSpline, "Insert Spline Knot");
                InsertKnot(spline, curve, t);
                editedSpline = spline;
                editedKnot = curve + 1;
            }
            else
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
                if (!TryPlacement(ray, out Vector3 position))
                    return false;
                UnitySpline spline = FindNearestEndpointSpline(position, out bool prepend);
                if (spline == null)
                    return false;

                if (current.type == EventType.Repaint)
                    DrawPlacementMarker(position, spline.Count == 0 ? "Shift-click: first knot"
                        : prepend ? "Shift-click: extend START" : "Shift-click: extend END", Color.cyan);
                if (!click)
                    return false;

                Undo.RecordObject(m_ActiveSpline, prepend ? "Extend Spline Start" : "Extend Spline End");
                var knot = new BezierKnot((float3)m_ActiveSpline.transform.InverseTransformPoint(position));
                if (prepend)
                    spline.Insert(0, knot, TangentMode.AutoSmooth);
                else
                    spline.Add(knot, TangentMode.AutoSmooth);
                editedSpline = spline;
                editedKnot = prepend ? 0 : spline.Count - 1;
            }

            for (int i = 0; i < m_ActiveSpline.Splines.Count; i++)
                if (m_ActiveSpline.Splines[i] == editedSpline)
                {
                    SplineSelection.Set(new SelectableKnot(new SplineInfo(m_ActiveSpline, i), editedKnot));
                    break;
                }
            EditorUtility.SetDirty(m_ActiveSpline);
            PrefabUtility.RecordPrefabInstancePropertyModifications(m_ActiveSpline);
            EditorSceneManager.MarkSceneDirty(m_ActiveSpline.gameObject.scene);
            SceneView.RepaintAll();
            current.Use();
            return true;
        }

        bool TryPlacement(Ray ray, out Vector3 position)
        {
            float nearest = float.PositiveInfinity;
            position = default;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, 100000f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(m_ActiveSpline.transform) || hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                position = hit.point;
            }
            if (!float.IsPositiveInfinity(nearest))
                return true;

            float height = m_ActiveSpline.transform.position.y;
            UnitySpline spline = m_ActiveSpline.Spline;
            if (spline != null && spline.Count > 0)
                height = m_ActiveSpline.transform.TransformPoint((Vector3)spline[spline.Count - 1].Position).y;
            if (!new Plane(Vector3.up, new Vector3(0f, height, 0f)).Raycast(ray, out float distance))
                return false;
            position = ray.GetPoint(distance);
            return true;
        }

        UnitySpline FindNearestEndpointSpline(Vector3 position, out bool prepend)
        {
            UnitySpline nearestSpline = null;
            UnitySpline emptySpline = null;
            float nearestDistance = float.PositiveInfinity;
            prepend = false;
            foreach (UnitySpline spline in m_ActiveSpline.Splines)
            {
                if (spline == null)
                    continue;
                if (spline.Count == 0)
                {
                    emptySpline ??= spline;
                    continue;
                }

                Vector3 end = m_ActiveSpline.transform.TransformPoint((Vector3)spline[spline.Count - 1].Position);
                float endDistance = (position - end).sqrMagnitude;
                if (endDistance < nearestDistance)
                {
                    nearestDistance = endDistance;
                    nearestSpline = spline;
                    prepend = false;
                }
                if (spline.Closed || spline.Count < 2)
                    continue;
                Vector3 start = m_ActiveSpline.transform.TransformPoint((Vector3)spline[0].Position);
                float startDistance = (position - start).sqrMagnitude;
                if (startDistance < nearestDistance)
                {
                    nearestDistance = startDistance;
                    nearestSpline = spline;
                    prepend = true;
                }
            }
            return nearestSpline ?? emptySpline;
        }

        bool TryFindInsertion(Vector2 mouse, out UnitySpline nearestSpline, out int nearestCurve, out float nearestT, out Vector3 nearestPosition)
        {
            nearestSpline = null;
            nearestCurve = -1;
            nearestT = 0f;
            nearestPosition = default;
            float nearestDistance = 30f * 30f;
            foreach (UnitySpline spline in m_ActiveSpline.Splines)
            {
                if (spline == null || spline.Count < 2)
                    continue;
                int curves = spline.Closed ? spline.Count : spline.Count - 1;
                for (int curve = 0; curve < curves; curve++)
                {
                    var bezier = spline.GetCurve(curve);
                    for (int sample = 1; sample < 64; sample++)
                    {
                        float t = sample / 64f;
                        Vector3 position = m_ActiveSpline.transform.TransformPoint((Vector3)CurveUtility.EvaluatePosition(bezier, t));
                        float distance = (HandleUtility.WorldToGUIPoint(position) - mouse).sqrMagnitude;
                        if (distance >= nearestDistance)
                            continue;
                        nearestDistance = distance;
                        nearestSpline = spline;
                        nearestCurve = curve;
                        nearestT = t;
                        nearestPosition = position;
                    }
                }
            }
            return nearestSpline != null;
        }

        internal static void InsertKnot(UnitySpline spline, int curve, float t)
        {
            CurveUtility.Split(spline.GetCurve(curve), t, out var left, out var right);
            int next = (curve + 1) % spline.Count;
            var start = spline[curve];
            var end = spline[next];
            quaternion rotation = math.slerp(start.Rotation, end.Rotation, t);
            spline.SetTangentMode(curve, TangentMode.Broken);
            spline.SetTangentMode(next, TangentMode.Broken);
            start.TangentOut = math.rotate(math.inverse(start.Rotation), left.P1 - left.P0);
            end.TangentIn = math.rotate(math.inverse(end.Rotation), right.P2 - right.P3);
            spline[curve] = start;
            spline[next] = end;
            spline.Insert(curve + 1, new BezierKnot(left.P3,
                math.rotate(math.inverse(rotation), left.P2 - left.P3),
                math.rotate(math.inverse(rotation), right.P1 - right.P0), rotation), TangentMode.Broken);
        }

        static void DrawPlacementMarker(Vector3 position, string label, Color color)
        {
            using (new Handles.DrawingScope(color))
            {
                Handles.DrawWireDisc(position, Vector3.up, HandleUtility.GetHandleSize(position) * .08f);
                Handles.Label(position, label);
            }
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
                case KeyCode.Escape: MBEditorToolState.ActiveEditing = false; break;
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

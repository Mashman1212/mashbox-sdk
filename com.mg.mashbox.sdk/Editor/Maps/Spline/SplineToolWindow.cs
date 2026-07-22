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
        SplineContainer m_ActiveSpline;
        UnityEditor.Editor m_SplineInspector;
        bool m_SceneToolActive;

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
            if (m_SceneToolActive)
                return;

            m_SceneToolActive = true;
            Selection.selectionChanged += OnSelectionChanged;
            UseSplineFromSelection();
        }

        public void DeactivateSceneTool()
        {
            if (!m_SceneToolActive)
                return;

            m_SceneToolActive = false;
            Selection.selectionChanged -= OnSelectionChanged;
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
            if (!m_SceneToolActive)
                return;

            var selected = FindSplineInSelection();
            if (selected != null)
                SetActiveSpline(selected, select: false);
            Repaint();
        }

        void UseSplineFromSelection()
        {
            var selected = FindSplineInSelection();
            if (selected != null)
                SetActiveSpline(selected, select: false);
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

            ToolManager.SetActiveContext<SplineToolContext>();
            ToolManager.SetActiveTool<SplineMoveTool>();
            SceneView.RepaintAll();
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

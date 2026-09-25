using UnityEditor;
using MashBoxSDK.Maps.Roads.Editor;
using UnityEngine;
using MashBoxSDK.Maps.Spline;

namespace MashBoxSDK.MapTools
{
    /// <summary>
    /// Keeps Mappy's authoring Scene tools alive even when the full MashBox SDK
    /// window is closed or hidden. Individual tool windows enforce a single
    /// active owner, so an open SDK window can take over without duplicate
    /// Scene callbacks.
    /// </summary>
    [InitializeOnLoad]
    internal static class MBBrushSceneToolHost
    {
        const double HealthCheckInterval = 0.5d;

        static MGBrushWindow s_BrushTool;
        static MeshSculptWindow s_SculptTool;
        static MultiSplineLoftWindow s_LoftTool;
        static SplineToolWindow s_SplineTool;
        static MGRoadTool s_RoadTool;
        static double s_NextHealthCheck;

        static MBBrushSceneToolHost()
        {
            MBEditorToolState.ModeChanged += Sync;
            MBEditorToolState.ActiveEditingChanged += Sync;
            EditorApplication.update += Update;
            Sync();
        }

        static void Update()
        {
            if (EditorApplication.timeSinceStartup < s_NextHealthCheck)
                return;

            s_NextHealthCheck = EditorApplication.timeSinceStartup + HealthCheckInterval;
            Sync();
        }

        internal static void ExecuteAction(MBEditorToolAction action)
        {
            if (!MBEditorToolState.ActiveEditing)
                return;

            // Ensure an owner exists immediately, without waiting for the health
            // check. Use the current owner to preserve the open window's state.
            Sync();
            switch (action)
            {
                case MBEditorToolAction.CreateSpline when MBEditorToolState.Mode == MBEditorAuthoringMode.Spline:
                    SplineToolWindow.ActiveSceneTool.CreateSplineFromOverlay();
                    break;
                case MBEditorToolAction.CreateLoft when MBEditorToolState.Mode == MBEditorAuthoringMode.SplineLoft:
                    MultiSplineLoftWindow.ActiveSceneTool.CreateLoftFromOverlay();
                    break;
            }
        }

        internal static MGRoadTool RoadTool
        {
            get
            {
                if (MGRoadTool.ActiveSceneTool != null) return MGRoadTool.ActiveSceneTool;
                if (s_RoadTool == null)
                {
                    s_RoadTool = ScriptableObject.CreateInstance<MGRoadTool>();
                    s_RoadTool.hideFlags = HideFlags.HideAndDontSave;
                }
                s_RoadTool.ActivateSceneTool();
                return s_RoadTool;
            }
        }

        static void Sync()
        {
            if (!MBEditorToolState.ActiveEditing || MBEditorToolState.Mode != MBEditorAuthoringMode.Road)
                MGRoadTool.ActiveSceneTool?.SuspendEditing();
            if (!MBEditorToolState.ActiveEditing)
            {
                MGBrushWindow.DeactivateActiveSceneTool();
                MeshSculptWindow.DeactivateActiveSceneTool();
                MultiSplineLoftWindow.DeactivateActiveSceneTool();
                SplineToolWindow.DeactivateActiveSceneTool();
                return;
            }

            switch (MBEditorToolState.Mode)
            {
                case MBEditorAuthoringMode.Brush:
                    MeshSculptWindow.DeactivateActiveSceneTool();
                    MultiSplineLoftWindow.DeactivateActiveSceneTool();
                    SplineToolWindow.DeactivateActiveSceneTool();
                    if (!MGBrushWindow.HasActiveSceneTool)
                    {
                        EnsureBrushTool();
                        s_BrushTool.ActivateSceneTool();
                    }
                    break;

                case MBEditorAuthoringMode.MeshSculpt:
                    MGBrushWindow.DeactivateActiveSceneTool();
                    MultiSplineLoftWindow.DeactivateActiveSceneTool();
                    SplineToolWindow.DeactivateActiveSceneTool();
                    if (!MeshSculptWindow.HasActiveSceneTool)
                    {
                        EnsureSculptTool();
                        s_SculptTool.ActivateSceneTool();
                    }
                    break;

                case MBEditorAuthoringMode.SplineLoft:
                    MGBrushWindow.DeactivateActiveSceneTool();
                    MeshSculptWindow.DeactivateActiveSceneTool();
                    SplineToolWindow.DeactivateActiveSceneTool();
                    if (!MultiSplineLoftWindow.HasActiveSceneTool)
                    {
                        EnsureLoftTool();
                        s_LoftTool.ActivateSceneTool();
                    }
                    break;

                case MBEditorAuthoringMode.Spline:
                    MGBrushWindow.DeactivateActiveSceneTool();
                    MeshSculptWindow.DeactivateActiveSceneTool();
                    MultiSplineLoftWindow.DeactivateActiveSceneTool();
                    if (!SplineToolWindow.HasActiveSceneTool)
                    {
                        EnsureSplineTool();
                        s_SplineTool.ActivateSceneTool();
                    }
                    break;

                case MBEditorAuthoringMode.Road:
                    MGBrushWindow.DeactivateActiveSceneTool();
                    MeshSculptWindow.DeactivateActiveSceneTool();
                    MultiSplineLoftWindow.DeactivateActiveSceneTool();
                    SplineToolWindow.DeactivateActiveSceneTool();
                    _ = RoadTool;
                    break;

                default:
                    MGBrushWindow.DeactivateActiveSceneTool();
                    MeshSculptWindow.DeactivateActiveSceneTool();
                    MultiSplineLoftWindow.DeactivateActiveSceneTool();
                    SplineToolWindow.DeactivateActiveSceneTool();
                    break;
            }
        }

        static void EnsureBrushTool()
        {
            if (s_BrushTool != null)
                return;

            s_BrushTool = ScriptableObject.CreateInstance<MGBrushWindow>();
            s_BrushTool.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
        }

        static void EnsureSculptTool()
        {
            if (s_SculptTool != null)
                return;

            s_SculptTool = ScriptableObject.CreateInstance<MeshSculptWindow>();
            s_SculptTool.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
        }

        static void EnsureLoftTool()
        {
            if (s_LoftTool != null)
                return;

            s_LoftTool = ScriptableObject.CreateInstance<MultiSplineLoftWindow>();
            s_LoftTool.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
        }

        static void EnsureSplineTool()
        {
            if (s_SplineTool != null)
                return;

            s_SplineTool = ScriptableObject.CreateInstance<SplineToolWindow>();
            s_SplineTool.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
        }
    }
}

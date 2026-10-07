using System;
using System.Collections.Generic;
using System.Reflection;
using MashBoxSDK.Maps;
using MashBoxSDK.Maps.Spline;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Splines;

namespace MashBoxSDK.MapTools
{
    /// <summary>Routes Unity's spline gizmos through the MashBox display layers.</summary>
    [InitializeOnLoad]
    internal static class MBSplineGizmoDisplay
    {
        private static readonly HashSet<SplineContainer> LoftSources = new HashSet<SplineContainer>();
        private static readonly Action<ISplineContainer, GizmoType> DrawUnityGizmo;
        private static bool sourcesDirty = true;
        private static bool ownsDrawing;
        private static bool originalGizmoEnabled;

        static MBSplineGizmoDisplay()
        {
            // Unity exposes drawing and a type-wide switch, but no per-container filter.
            // Keep its original drawer for unrelated splines (including user colors),
            // and use Transform's gizmo pass to retain Unity's culling and picking.
            var drawer = typeof(SplineGizmoUtility).Assembly
                .GetType("UnityEditor.Splines.EditorSplineGizmos")?
                .GetMethod("DrawUnselectedSplineGizmos", BindingFlags.Static | BindingFlags.NonPublic);
            if (drawer == null) return;
            DrawUnityGizmo = (Action<ISplineContainer, GizmoType>)Delegate.CreateDelegate(
                typeof(Action<ISplineContainer, GizmoType>), drawer);
            EditorApplication.delayCall += TakeOverDrawing;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreUnityDrawing;
            EditorApplication.quitting += RestoreUnityDrawing;
            EditorApplication.hierarchyChanged += InvalidateSources;
            Undo.undoRedoPerformed += InvalidateSources;
            ObjectChangeEvents.changesPublished += OnObjectsChanged;
        }

        private static void InvalidateSources() => sourcesDirty = true;
        private static void OnObjectsChanged(ref ObjectChangeEventStream stream) => InvalidateSources();

        private static void TakeOverDrawing()
        {
            if (ownsDrawing || DrawUnityGizmo == null
                || !GizmoUtility.TryGetGizmoInfo(typeof(SplineContainer), out var info)) return;
            originalGizmoEnabled = info.gizmoEnabled;
            // Respect an existing choice to hide all Unity spline gizmos.
            if (!originalGizmoEnabled) return;
            ownsDrawing = true;
            GizmoUtility.SetGizmoEnabled(typeof(SplineContainer), false, false);
            SceneView.RepaintAll();
        }

        private static void RestoreUnityDrawing()
        {
            if (!ownsDrawing) return;
            GizmoUtility.SetGizmoEnabled(typeof(SplineContainer), originalGizmoEnabled, false);
            ownsDrawing = false;
        }

        private static bool IsLoftSpline(SplineContainer container)
        {
            if (sourcesDirty)
            {
                sourcesDirty = false;
                LoftSources.Clear();
                foreach (var loft in UnityEngine.Object.FindObjectsByType<MultiSplineLoft>(FindObjectsInactive.Include))
                    foreach (var source in loft.Sources)
                        if (source?.container != null) LoftSources.Add(source.container);
            }
            return LoftSources.Contains(container) || container.GetComponentInParent<MultiSplineLoft>() != null;
        }

        [DrawGizmo(GizmoType.Active | GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable)]
        private static void DrawSpline(Transform transform, GizmoType gizmoType)
        {
            if (!ownsDrawing || !transform.TryGetComponent<SplineContainer>(out var container)) return;
            // If the native drawer has been re-enabled in Unity's Gizmos menu, let
            // it take over until the next reload instead of drawing duplicate curves.
            if (GizmoUtility.TryGetGizmoInfo(typeof(SplineContainer), out var info) && info.gizmoEnabled) return;

            bool trail = container.TryGetComponent<MBTrailSpline>(out _);
            bool loft = !trail && IsLoftSpline(container);
            if (!trail && !loft)
            {
                DrawUnityGizmo(container, gizmoType);
                return;
            }

            if (trail ? !MBGameplayGizmoVisibility.TrailNetworkVisible : !MBGameplayGizmoVisibility.LoftSplinesVisible)
                return;

            // Unity's active spline tool owns its selected knot/tangent handles.
            if (typeof(SplineTool).IsAssignableFrom(ToolManager.activeToolType)
                && (gizmoType & GizmoType.Selected) != 0) return;

            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            try
            {
                if (trail)
                {
                    Gizmos.color = MBTrailSpline.GizmoColor;
                    SplineGizmoUtility.DrawGizmos(container);
                }
                else
                    DrawUnityGizmo(container, gizmoType);
            }
            finally
            {
                Gizmos.color = previousColor;
                Gizmos.matrix = previousMatrix;
            }
        }
    }
}

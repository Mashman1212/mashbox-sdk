using System;
using System.Collections.Generic;
using System.Reflection;
using MashBoxSDK.Maps;
using MashBoxSDK.Maps.Spline;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

namespace MashBoxSDK.MapTools
{
    /// <summary>Draw spline display layers once per Scene view, never once per Transform.</summary>
    [InitializeOnLoad]
    internal static class MBSplineGizmoDisplay
    {
        private delegate void GetPositions(Spline spline, out Vector3[] positions);
        private static readonly GetPositions GetCachedPositions;
        private static readonly ProfilerMarker DrawMarker = new ProfilerMarker("MashBox.DrawSplineDisplay");
        private static readonly object UnityColorSetting;
        private static readonly PropertyInfo UnityColorValue;
        private static readonly HashSet<SplineContainer> LoftSources = new HashSet<SplineContainer>();
        private static readonly Plane[] Frustum = new Plane[6];
        private static readonly Dictionary<Spline, CurveBounds> BoundsCache = new Dictionary<Spline, CurveBounds>();
        private static SplineContainer[] containers = Array.Empty<SplineContainer>();
        private static bool sourcesDirty = true;
        private static bool ownsDrawing;
        private static bool originalGizmoEnabled;

        private struct CurveBounds
        {
            public Vector3[] Positions;
            public Bounds Bounds;
        }

        static MBSplineGizmoDisplay()
        {
            // Reuse Unity's curve cache (including its spline-change/undo invalidation).
            // If a package update removes this API, leave the native drawer enabled.
            var cache = typeof(SplineGizmoUtility).Assembly.GetType("UnityEditor.Splines.SplineCacheUtility")?
                .GetMethod("GetCachedPositions", BindingFlags.Static | BindingFlags.Public,
                    null, new[] { typeof(Spline), typeof(Vector3[]).MakeByRefType() }, null);
            if (cache == null) return;
            GetCachedPositions = (GetPositions)Delegate.CreateDelegate(typeof(GetPositions), cache);
            UnityColorSetting = typeof(SplineGizmoUtility)
                .GetField("s_GizmosLineColor", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
            UnityColorValue = UnityColorSetting?.GetType().GetProperty("value");
            EditorApplication.delayCall += TakeOverDrawing;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreUnityDrawing;
            EditorApplication.quitting += RestoreUnityDrawing;
            EditorApplication.hierarchyChanged += InvalidateSources;
            Undo.undoRedoPerformed += InvalidateSources;
            ObjectChangeEvents.changesPublished += OnObjectsChanged;
            Spline.Changed += OnSplineChanged;
            PrefabStage.prefabStageOpened += _ => InvalidateSources();
            PrefabStage.prefabStageClosing += _ => InvalidateSources();
            SceneView.duringSceneGui += DrawSplines;
        }

        private static void InvalidateSources() => sourcesDirty = true;
        private static void OnSplineChanged(Spline spline, int knotIndex, SplineModification modification)
            => BoundsCache.Remove(spline);

        private static void OnObjectsChanged(ref ObjectChangeEventStream stream)
        {
            // Moving unrelated objects must not trigger a scene-wide discovery pass.
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeGameObjectOrComponentProperties) continue;
                stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var change);
                var changed = EditorUtility.InstanceIDToObject(change.instanceId);
                if (changed is MultiSplineLoft || changed is MBTrailSpline)
                {
                    InvalidateSources();
                    return;
                }
            }
        }

        private static void TakeOverDrawing()
        {
            if (ownsDrawing || GetCachedPositions == null
                || !GizmoUtility.TryGetGizmoInfo(typeof(SplineContainer), out var info)) return;
            originalGizmoEnabled = info.gizmoEnabled;
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

        private static void RefreshSources()
        {
            if (!sourcesDirty) return;
            sourcesDirty = false;
            // Includes prefab-stage splines; stage and visibility filters run below.
            containers = Resources.FindObjectsOfTypeAll<SplineContainer>();
            LoftSources.Clear();
            BoundsCache.Clear();
            foreach (var loft in Resources.FindObjectsOfTypeAll<MultiSplineLoft>())
            {
                if (!loft.gameObject.scene.IsValid()) continue;
                foreach (var source in loft.Sources)
                    if (source?.container != null) LoftSources.Add(source.container);
            }
        }

        private static bool IsVisible(SplineContainer container)
        {
            if (container.TryGetComponent<MBTrailSpline>(out _))
                return MBGameplayGizmoVisibility.TrailNetworkVisible;
            if (LoftSources.Contains(container) || container.GetComponentInParent<MultiSplineLoft>() != null)
                return MBGameplayGizmoVisibility.LoftSplinesVisible;
            return true;
        }

        private static void DrawSplines(SceneView sceneView)
        {
            var current = Event.current;
            if (!ownsDrawing || !sceneView.drawGizmos || sceneView.camera == null || current == null) return;
            // Respect the user handing drawing back to Unity's Gizmos menu.
            if (GizmoUtility.TryGetGizmoInfo(typeof(SplineContainer), out var info) && info.gizmoEnabled) return;
            bool processCurves = current.type == EventType.Repaint || current.type == EventType.Layout
                || current.type == EventType.MouseMove;

            using var drawSample = DrawMarker.Auto();
            RefreshSources();
            GeometryUtility.CalculateFrustumPlanes(sceneView.camera, Frustum);
            var stage = StageUtility.GetCurrentStageHandle();
            bool splineTool = typeof(SplineTool).IsAssignableFrom(ToolManager.activeToolType);
            Color splineColor = UnityColorValue?.GetValue(UnityColorSetting) is Color color ? color : Color.blue;
            var previousZTest = Handles.zTest;
            try
            {
                Handles.zTest = CompareFunction.LessEqual;
                foreach (var container in containers)
                {
                    if (container == null) continue;
                    var go = container.gameObject;
                    if (!go.scene.IsValid() || !go.activeInHierarchy
                        || StageUtility.GetStageHandle(go) != stage
                        || SceneVisibilityManager.instance.IsHidden(go)
                        || (sceneView.camera.cullingMask & (1 << go.layer)) == 0
                        || !IsVisible(container)) continue;
                    bool selected = Selection.Contains(go);
                    // The native spline tool still owns its selected knots and tangents.
                    if (selected && splineTool) continue;
                    int control = GUIUtility.GetControlID(container.GetInstanceID(), FocusType.Passive);
                    var matrix = container.transform.localToWorldMatrix;
                    Color drawColor = container.TryGetComponent<MBTrailSpline>(out _)
                        ? MBTrailSpline.GizmoColor : selected ? Handles.selectedColor : splineColor;
                    // Allocate control IDs on every event so other Scene tools retain stable IDs
                    // during dragging, but only process curve geometry for drawing and hit testing.
                    if (processCurves)
                    using (new Handles.DrawingScope(drawColor, matrix))
                    {
                        foreach (var spline in container.Splines)
                        {
                            if (spline == null || spline.Count < 2) continue;
                            GetCachedPositions(spline, out var positions);
                            if (!IntersectsView(spline, positions, matrix)) continue;
                            if (current.type == EventType.Repaint)
                                Handles.DrawPolyLine(positions);
                            else if ((current.type == EventType.Layout || current.type == EventType.MouseMove)
                                && !SceneVisibilityManager.instance.IsPickingDisabled(go))
                            {
                                float distance = float.MaxValue;
                                for (int i = 1; i < positions.Length; i++)
                                    distance = Mathf.Min(distance, HandleUtility.DistanceToLine(positions[i - 1], positions[i]));
                                HandleUtility.AddControl(control, distance);
                            }
                        }
                    }
                    if (current.type == EventType.MouseDown && current.button == 0 && !current.alt
                        && !Tools.viewToolActive && GUIUtility.hotControl == 0
                        && HandleUtility.nearestControl == control
                        && !SceneVisibilityManager.instance.IsPickingDisabled(go))
                    {
                        if (current.shift || current.control || current.command)
                        {
                            var selection = new List<UnityEngine.Object>(Selection.objects);
                            if (!selection.Remove(go)) selection.Add(go);
                            Selection.objects = selection.ToArray();
                        }
                        else Selection.activeGameObject = go;
                        current.Use();
                    }
                }
            }
            finally { Handles.zTest = previousZTest; }
        }

        private static bool IntersectsView(Spline spline, Vector3[] positions, Matrix4x4 matrix)
        {
            if (positions.Length == 0) return false;
            if (!BoundsCache.TryGetValue(spline, out var cached) || cached.Positions != positions)
            {
                var bounds = new Bounds(positions[0], Vector3.zero);
                for (int i = 1; i < positions.Length; i++) bounds.Encapsulate(positions[i]);
                cached = new CurveBounds { Positions = positions, Bounds = bounds };
                BoundsCache[spline] = cached;
            }
            var extents = cached.Bounds.extents;
            var x = matrix.MultiplyVector(new Vector3(extents.x, 0, 0));
            var y = matrix.MultiplyVector(new Vector3(0, extents.y, 0));
            var z = matrix.MultiplyVector(new Vector3(0, 0, extents.z));
            var worldExtents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return GeometryUtility.TestPlanesAABB(Frustum,
                new Bounds(matrix.MultiplyPoint3x4(cached.Bounds.center), worldExtents * 2f));
        }
    }
}

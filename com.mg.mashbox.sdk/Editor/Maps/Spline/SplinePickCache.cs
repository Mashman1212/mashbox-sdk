using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;

namespace MashBoxSDK.Maps.Spline
{
    // Owned by the active spline tool. Idle Layout events only query cached polylines;
    // normalized SplineContainer.EvaluatePosition walks the curve list for every sample.
    internal sealed class SplinePickCache : IDisposable
    {
        static readonly ProfilerMarker PickMarker = new ProfilerMarker("MashBox.SplinePicking");
        static readonly ProfilerMarker DiscoverMarker = new ProfilerMarker("MashBox.SplinePicking.Discover");
        static readonly ProfilerMarker SampleMarker = new ProfilerMarker("MashBox.SplinePicking.SampleCurves");
        static readonly ProfilerMarker ProjectMarker = new ProfilerMarker("MashBox.SplinePicking.ProjectCurves");
        const int SamplesPerCurve = 12;

        sealed class Geometry
        {
            public Vector3[] points;
            public Bounds bounds;
        }

        sealed class Projection
        {
            public Geometry geometry;
            public Matrix4x4 matrix;
            public int viewVersion = -1;
            public Vector3[] points;
            public Rect bounds;
            public bool crossesCamera;
            public bool hasDistance;
            public Vector2 mouse;
            public float distance;
        }

        readonly Dictionary<UnityEngine.Splines.Spline, Geometry> geometry = new();
        readonly Dictionary<(SplineContainer, UnityEngine.Splines.Spline), Projection> projections = new();
        readonly Plane[] frustum = new Plane[6];
        SplineContainer[] containers;
        Camera lastCamera;
        Matrix4x4 lastView, lastProjection;
        Rect lastPixelRect;
        float lastPixelsPerPoint;
        int viewVersion;
        bool disposed;

        internal int GeometryBuildCount { get; private set; }
        internal int ProjectionBuildCount { get; private set; }
        internal int DiscoveryCount { get; private set; }

        internal SplinePickCache()
        {
            EditorApplication.hierarchyChanged += Clear;
            Undo.undoRedoPerformed += Clear;
            UnityEngine.Splines.Spline.Changed += OnSplineChanged;
            SplineContainer.SplineAdded += OnSplineSetChanged;
            SplineContainer.SplineRemoved += OnSplineSetChanged;
            SplineContainer.SplineReordered += OnSplineReordered;
        }

        void OnSplineChanged(UnityEngine.Splines.Spline spline, int knot, SplineModification modification)
            => geometry.Remove(spline);
        void OnSplineSetChanged(SplineContainer container, int index) => Clear();
        void OnSplineReordered(SplineContainer container, int before, int after) => Clear();

        internal void Clear()
        {
            containers = null;
            geometry.Clear();
            projections.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            EditorApplication.hierarchyChanged -= Clear;
            Undo.undoRedoPerformed -= Clear;
            UnityEngine.Splines.Spline.Changed -= OnSplineChanged;
            SplineContainer.SplineAdded -= OnSplineSetChanged;
            SplineContainer.SplineRemoved -= OnSplineSetChanged;
            SplineContainer.SplineReordered -= OnSplineReordered;
            Clear();
        }

        internal bool TryPick(Camera camera, Vector2 mouse, out SplineContainer closest,
            Func<Vector3, Vector3> project = null)
        {
            using var sample = PickMarker.Auto();
            closest = null;
            if (camera == null || disposed) return false;
            project ??= HandleUtility.WorldToGUIPointWithDepth;
            if (containers == null)
            {
                using var discover = DiscoverMarker.Auto();
                containers = Resources.FindObjectsOfTypeAll<SplineContainer>();
                DiscoveryCount++;
            }
            var view = camera.worldToCameraMatrix;
            var projection = camera.projectionMatrix;
            var pixelRect = camera.pixelRect;
            float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
            if (lastCamera != camera || lastView != view || lastProjection != projection
                || lastPixelRect != pixelRect || lastPixelsPerPoint != pixelsPerPoint)
            {
                lastCamera = camera;
                lastView = view;
                lastProjection = projection;
                lastPixelRect = pixelRect;
                lastPixelsPerPoint = pixelsPerPoint;
                viewVersion++;
                GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            }
            var stage = StageUtility.GetCurrentStageHandle();
            float closestDistance = 14f * 14f;
            using var scope = new Handles.DrawingScope(Matrix4x4.identity);
            foreach (SplineContainer container in containers)
            {
                if (container == null || EditorUtility.IsPersistent(container)) continue;
                GameObject go = container.gameObject;
                if (!go.scene.IsValid() || !go.activeInHierarchy
                    || StageUtility.GetStageHandle(go) != stage
                    || (camera.cullingMask & (1 << go.layer)) == 0
                    || SceneVisibilityManager.instance.IsHidden(go)
                    || SceneVisibilityManager.instance.IsPickingDisabled(go)) continue;
                Matrix4x4 matrix = container.transform.localToWorldMatrix;
                foreach (var spline in container.Splines)
                {
                    if (spline == null || spline.Count == 0) continue;
                    if (!geometry.TryGetValue(spline, out Geometry shape))
                    {
                        shape = Sample(spline);
                        geometry.Add(spline, shape);
                    }
                    if (!GeometryUtility.TestPlanesAABB(frustum, TransformBounds(shape.bounds, matrix))) continue;
                    var key = (container, spline);
                    if (!projections.TryGetValue(key, out Projection screen))
                    {
                        screen = new Projection();
                        projections.Add(key, screen);
                    }
                    if (screen.geometry != shape || screen.matrix != matrix || screen.viewVersion != viewVersion)
                        Project(screen, shape, matrix, project);
                    if (!screen.hasDistance || screen.mouse != mouse)
                    {
                        screen.distance = Distance(screen, mouse);
                        screen.mouse = mouse;
                        screen.hasDistance = true;
                    }
                    if (screen.distance < closestDistance)
                    {
                        closestDistance = screen.distance;
                        closest = container;
                    }
                }
            }
            return closest != null;
        }

        Geometry Sample(UnityEngine.Splines.Spline spline)
        {
            using var sample = SampleMarker.Auto();
            GeometryBuildCount++;
            int curves = spline.Closed ? spline.Count : spline.Count - 1;
            var points = new Vector3[Mathf.Max(1, curves * SamplesPerCurve + 1)];
            if (curves == 0) points[0] = spline[0].Position;
            for (int curve = 0; curve < curves; curve++)
            {
                BezierCurve bezier = spline.GetCurve(curve);
                for (int i = 0; i < SamplesPerCurve; i++)
                    points[curve * SamplesPerCurve + i] = CurveUtility.EvaluatePosition(bezier, i / (float)SamplesPerCurve);
                if (curve == curves - 1) points[points.Length - 1] = CurveUtility.EvaluatePosition(bezier, 1f);
            }
            var bounds = new Bounds(points[0], Vector3.zero);
            for (int i = 1; i < points.Length; i++) bounds.Encapsulate(points[i]);
            return new Geometry { points = points, bounds = bounds };
        }

        void Project(Projection screen, Geometry shape, Matrix4x4 matrix, Func<Vector3, Vector3> project)
        {
            using var sample = ProjectMarker.Auto();
            ProjectionBuildCount++;
            screen.geometry = shape;
            screen.matrix = matrix;
            screen.viewVersion = viewVersion;
            screen.hasDistance = false;
            screen.crossesCamera = false;
            if (screen.points == null || screen.points.Length != shape.points.Length)
                screen.points = new Vector3[shape.points.Length];
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < shape.points.Length; i++)
            {
                Vector3 point = project(matrix.MultiplyPoint3x4(shape.points[i]));
                screen.points[i] = point;
                screen.crossesCamera |= point.z <= 0f;
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            screen.bounds = Rect.MinMaxRect(min.x - 14f, min.y - 14f, max.x + 14f, max.y + 14f);
        }

        static float Distance(Projection screen, Vector2 mouse)
        {
            if (!screen.crossesCamera && !screen.bounds.Contains(mouse)) return float.PositiveInfinity;
            if (screen.points.Length == 1)
                return screen.points[0].z > 0f ? ((Vector2)screen.points[0] - mouse).sqrMagnitude : float.PositiveInfinity;
            float best = float.PositiveInfinity;
            for (int i = 1; i < screen.points.Length; i++)
            {
                Vector3 a = screen.points[i - 1], b = screen.points[i];
                if (a.z <= 0f || b.z <= 0f) continue;
                Vector2 segment = (Vector2)(b - a);
                float t = segment.sqrMagnitude > Mathf.Epsilon
                    ? Mathf.Clamp01(Vector2.Dot(mouse - (Vector2)a, segment) / segment.sqrMagnitude) : 0f;
                best = Mathf.Min(best, (mouse - ((Vector2)a + segment * t)).sqrMagnitude);
            }
            return best;
        }

        static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 e = bounds.extents;
            Vector3 x = matrix.MultiplyVector(new Vector3(e.x, 0, 0));
            Vector3 y = matrix.MultiplyVector(new Vector3(0, e.y, 0));
            Vector3 z = matrix.MultiplyVector(new Vector3(0, 0, e.z));
            return new Bounds(matrix.MultiplyPoint3x4(bounds.center), 2f * new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)));
        }
    }
}

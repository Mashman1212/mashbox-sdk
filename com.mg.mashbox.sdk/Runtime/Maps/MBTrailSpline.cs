using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using UnitySpline = UnityEngine.Splines.Spline;

namespace MashBoxSDK.Maps
{
    public enum MBTrailDifficulty { GreenCircle, BlueSquare, BlackDiamond, DoubleBlackDiamond }

    /// <summary>A named map trail shared by steering, discovery, and trail challenges.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(SplineContainer))]
    [AddComponentMenu("MashBox/Maps/Trail Spline")]
    public class MBTrailSpline : MonoBehaviour
    {
        public static readonly HashSet<MBTrailSpline> Active = new HashSet<MBTrailSpline>();
        public static readonly Color GizmoColor = new Color(0.55f, 0.82f, 1f, 1f);
        [Header("Trail")]
        [SerializeField, Tooltip("Name shown in the Trails menu and compass. Map Tools keeps this in sync with the trail object name.")] private string trailName;
        [SerializeField, HideInInspector] private string persistentId;
        [TextArea] public string Description;
        public MBTrailDifficulty Difficulty = MBTrailDifficulty.GreenCircle;
        [Tooltip("How close the rider must pass to discover this trail and show its name.")]
        [HideInInspector, Min(1f)] public float DiscoveryDistance = 12f;
        [Tooltip("Distance from the first and last spline points for a full-trail ride.")]
        [HideInInspector, Min(1f)] public float EndpointDistance = 12f;
        public string TrailName => string.IsNullOrWhiteSpace(trailName) ? name : trailName.Trim();
        public bool HasAuthoredTrailName => !string.IsNullOrWhiteSpace(trailName);
        public string PersistentId => persistentId;
        public void SetTrailName(string value) => trailName = value;
        public void EnsurePersistentId()
        {
            if (string.IsNullOrEmpty(persistentId)) persistentId = Guid.NewGuid().ToString("N");
        }
        public void RegeneratePersistentId() => persistentId = Guid.NewGuid().ToString("N");
        [HideInInspector, Min(0)] public int SplineIndex;
        [Tooltip("Capture radius in world metres. Includes height, so stacked trails do not attract each other.")]
        [HideInInspector, Min(0.1f)] public float CaptureDistance = 5f;
        [Tooltip("Allow riding either direction; otherwise follow increasing spline knot order.")]
        [HideInInspector] public bool Bidirectional = true;
        [Tooltip("Samples per knot span. Increase for very long spans or tight curves.")]
        [HideInInspector, Range(8, 256)] public int SamplesPerSpan = 64;

        private SplineContainer _container;
        private UnitySpline _spline;
        private Vector3[] _points;
        private float[] _distances;
        private Bounds _bounds;
        private Matrix4x4 _matrix;
        private bool _dirty = true;
        private int _geometryVersion;
        public int GeometryVersion => _geometryVersion;
        public float Length => _distances == null ? 0f : _distances[_distances.Length - 1];
        public bool Closed => _spline != null && _spline.Closed;

        protected virtual void OnEnable()
        {
            Active.Add(this);
            UnitySpline.Changed += OnSplineChanged;
            _dirty = true;
        }

        protected virtual void OnDisable()
        {
            Active.Remove(this);
            UnitySpline.Changed -= OnSplineChanged;
        }

        protected virtual void OnValidate()
        {
            if (!HasAuthoredTrailName) trailName = name;
            EnsurePersistentId();
            _dirty = true;
        }
        private void OnSplineChanged(UnitySpline spline, int knot, SplineModification change)
        {
            if (spline == _spline) _dirty = true;
        }

        public bool RebuildIfNeeded()
        {
            if (!_container) _container = GetComponent<SplineContainer>();
            if (!_container || SplineIndex < 0 || SplineIndex >= _container.Splines.Count)
                return false;
            UnitySpline spline = _container.Splines[SplineIndex];
            if (spline == null || spline.Count < 2) return false;
            if (!_dirty && _spline == spline && _matrix == transform.localToWorldMatrix)
                return Length > 0.001f;
            _spline = spline;
            _matrix = transform.localToWorldMatrix;
            _dirty = false;
            _geometryVersion++;
            int count = Mathf.Clamp((spline.Closed ? spline.Count : spline.Count - 1) * Mathf.Clamp(SamplesPerSpan, 8, 256), 8, 8192);
            _points = new Vector3[count + 1];
            _distances = new float[count + 1];
            for (int i = 0; i <= count; i++)
            {
                _points[i] = _container.EvaluatePosition(SplineIndex, i / (float)count);
                if (i == 0) _bounds = new Bounds(_points[i], Vector3.zero);
                else
                {
                    _bounds.Encapsulate(_points[i]);
                    _distances[i] = _distances[i - 1] + Vector3.Distance(_points[i - 1], _points[i]);
                }
            }
            return Length > 0.001f;
        }

        // A progress window keeps a locked rider on the same branch at crossings/hairpins.
        public bool TryNearest(Vector3 position, float previousDistance, float progressWindow,
            out float distance, out Vector3 point, out Vector3 tangent) =>
            TryNearest(position, previousDistance, progressWindow, CaptureDistance,
                out distance, out point, out tangent);

        public bool TryNearest(Vector3 position, float previousDistance, float progressWindow,
            float captureDistance, out float distance, out Vector3 point, out Vector3 tangent)
        {
            distance = 0f; point = tangent = Vector3.zero;
            if (!isActiveAndEnabled || !RebuildIfNeeded()) return false;
            float best = Mathf.Max(0.1f, captureDistance);
            best *= best;
            if (_bounds.SqrDistance(position) > best) return false;
            bool found = false;
            int firstSegment = 1;
            bool bounded = previousDistance >= 0f && !Closed && !float.IsPositiveInfinity(progressWindow);
            float maximum = previousDistance + progressWindow;
            if (bounded)
            {
                // Distances are sorted. Only project segments touching the progress window.
                float minimum = previousDistance - progressWindow;
                int lo = 1, hi = _distances.Length - 1;
                while (lo < hi)
                {
                    int mid = lo + (hi - lo) / 2;
                    if (_distances[mid] < minimum) lo = mid + 1;
                    else hi = mid;
                }
                firstSegment = lo;
            }
            for (int i = firstSegment; i < _points.Length; i++)
            {
                if (bounded && _distances[i - 1] > maximum) break;
                Vector3 segment = _points[i] - _points[i - 1];
                float squared = segment.sqrMagnitude;
                if (squared < 0.000001f) continue;
                float fraction = Mathf.Clamp01(Vector3.Dot(position - _points[i - 1], segment) / squared);
                float along = Mathf.Lerp(_distances[i - 1], _distances[i], fraction);
                float delta = Mathf.Abs(along - previousDistance);
                if (Closed) delta = Mathf.Min(delta, Length - delta);
                if (previousDistance >= 0f && delta > progressWindow) continue;
                Vector3 candidate = _points[i - 1] + segment * fraction;
                float error = (candidate - position).sqrMagnitude;
                if (error >= best) continue;
                best = error; distance = along; point = candidate; tangent = segment.normalized; found = true;
            }
            return found;
        }

        public Vector3 PointAtDistance(float distance)
        {
            if (!RebuildIfNeeded()) return transform.position;
            distance = Closed ? Mathf.Repeat(distance, Length) : Mathf.Clamp(distance, 0f, Length);
            int lo = 0, hi = _distances.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_distances[mid] < distance) lo = mid; else hi = mid;
            }
            return Vector3.Lerp(_points[lo], _points[hi], Mathf.InverseLerp(_distances[lo], _distances[hi], distance));
        }

        public Vector3 StartPoint => PointAtDistance(0f);
        public Vector3 FinishPoint
        {
            get { RebuildIfNeeded(); return PointAtDistance(Length); }
        }
        public Vector3 StartForward
        {
            get { RebuildIfNeeded(); return (PointAtDistance(Mathf.Min(2f, Length)) - StartPoint).normalized; }
        }

        private void OnDrawGizmosSelected()
        {
            if (!MBGameplayGizmoVisibility.TrailNetworkVisible) return;
            if (!RebuildIfNeeded()) return;
            Color previousColor = Gizmos.color;
            Gizmos.color = GizmoColor;

            Gizmos.DrawWireSphere(_points[0], Mathf.Max(0.1f, CaptureDistance));
            Gizmos.color = previousColor;
        }
    }
}

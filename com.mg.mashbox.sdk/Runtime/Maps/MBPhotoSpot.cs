using System;
using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MashBoxSDK.Maps
{
    [DisallowMultipleComponent]
    public class MBPhotoSpot : MonoBehaviour
    {
        public event Action Activated;
        public event Action Completed;
        public event Action ResetOccurred;

        private const float SnapStartHeight = 1f;
        private const float SnapDistance = 10f;
        private const float CapsuleHeight = 2f;
        private const float CapsuleRadius = 0.4f;
        private const float FrustumNearOffset = 0.25f;
        private const float FrustumWidth = 2.2f;
        private const float FrustumHeight = 1.4f;

        private Vector3 lastSnapCheckPosition;
        private Quaternion lastSnapCheckRotation;
        private bool hasSnapCheckState;

        private readonly Color fillColor = new Color(0.35f, 0.85f, 1f, 0.14f);
        private readonly Color wireColor = new Color(0.35f, 0.85f, 1f, 0.95f);
        private readonly Color frustumColor = new Color(1f, 0.78f, 0.25f, 0.95f);
        private readonly Color triggerZoneFillColor = new Color(0.65f, 1f, 0.55f, 0.12f);
        private readonly Color triggerZoneWireColor = new Color(0.65f, 1f, 0.55f, 0.95f);
        [SerializeField] private bool activated;
        [SerializeField] private bool completed;
        [Header("Trigger Zone")]
        [Tooltip("Local position of the camera challenge trigger zone relative to this photo spot.")]
        [SerializeField] private Vector3 triggerZoneLocalPosition = new Vector3(0f, 1.5f, 2.25f);
        [Min(0.1f)]
        [Tooltip("Radius of the trigger zone sphere used by the gameplay camera challenge.")]
        [SerializeField] private float triggerZoneRadius = 1f;

        [Header("Events")]
        [SerializeField] private UnityEvent onActivated;
        [SerializeField] private UnityEvent onCompleted;
        [SerializeField] private UnityEvent onReset;

        private MBPhotoSpotGroup group;

        public bool IsActivated => activated;
        public bool IsCompleted => completed;
        public Vector3 TriggerZoneLocalPosition => triggerZoneLocalPosition;
        public float TriggerZoneRadius => triggerZoneRadius;
        public Vector3 TriggerZoneWorldPosition => transform.TransformPoint(triggerZoneLocalPosition);

        private void Awake()
        {
            CacheGroupReference();
        }

        private void OnValidate()
        {
            CacheGroupReference();
            triggerZoneRadius = Mathf.Max(0.1f, triggerZoneRadius);
            group?.RegisterPhotoSpot(this);
        }

        public bool IsGrounded()
        {
            return TryGetGroundHit(out _);
        }

        public void SetTriggerZoneLocalPosition(Vector3 localPosition)
        {
            triggerZoneLocalPosition = localPosition;
        }

        public void SetTriggerZoneRadius(float radius)
        {
            triggerZoneRadius = Mathf.Max(0.1f, radius);
        }

        public void Activate()
        {
            if (!MBGameplayStateGuard.IsGameplayActive)
                return;

            if (activated)
                return;

            activated = true;
            group?.NotifyActivated(this);
            Activated?.Invoke();
            onActivated?.Invoke();
        }

        public void Complete()
        {
            if (!MBGameplayStateGuard.IsGameplayActive)
                return;

            if (!activated)
                activated = true;

            if (completed)
                return;

            completed = true;
            group?.NotifyCompleted(this);
            Completed?.Invoke();
            onCompleted?.Invoke();
        }

        public void ResetPhotoSpot()
        {
            SetState(false, false);
        }

        private void OnDrawGizmos()
        {
            SnapToGround();
            DrawCapsuleGizmo();
        }

        private void DrawCapsuleGizmo()
        {
            var previousMatrix = Gizmos.matrix;
            var previousColor = Gizmos.color;
            var isSelected = IsSelected();
            var bodyHeight = Mathf.Max(0f, CapsuleHeight - (CapsuleRadius * 2f));
            var bodyCenter = new Vector3(0f, CapsuleRadius + (bodyHeight * 0.5f), 0f);
            var bottomCenter = new Vector3(0f, CapsuleRadius, 0f);
            var topCenter = new Vector3(0f, CapsuleHeight - CapsuleRadius, 0f);
            var cameraOrigin = new Vector3(0f, CapsuleHeight * 0.75f, 0f);

            Gizmos.matrix = transform.localToWorldMatrix;

            Gizmos.color = GetSelectedTint(fillColor, isSelected, 0.08f);
            Gizmos.DrawSphere(bottomCenter, CapsuleRadius);
            Gizmos.DrawSphere(topCenter, CapsuleRadius);
            if (bodyHeight > 0f)
                Gizmos.DrawCube(bodyCenter, new Vector3(CapsuleRadius * 2f, bodyHeight, CapsuleRadius * 2f));

            Gizmos.color = GetSelectedTint(wireColor, isSelected, 0.12f, false);
            Gizmos.DrawWireSphere(bottomCenter, CapsuleRadius);
            Gizmos.DrawWireSphere(topCenter, CapsuleRadius);
            if (bodyHeight > 0f)
            {
                var extents = CapsuleRadius;
                var yMin = CapsuleRadius;
                var yMax = CapsuleHeight - CapsuleRadius;
                DrawWireSegment(new Vector3(-extents, yMin, -extents), new Vector3(-extents, yMax, -extents));
                DrawWireSegment(new Vector3(extents, yMin, -extents), new Vector3(extents, yMax, -extents));
                DrawWireSegment(new Vector3(-extents, yMin, extents), new Vector3(-extents, yMax, extents));
                DrawWireSegment(new Vector3(extents, yMin, extents), new Vector3(extents, yMax, extents));
            }

            DrawFrustumGizmo(cameraOrigin);
            DrawTriggerZoneGizmo();

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        private void DrawFrustumGizmo(Vector3 origin)
        {
            var farCenter = triggerZoneLocalPosition;
            var direction = farCenter - origin;
            if (direction.sqrMagnitude < 0.0001f)
                direction = Vector3.forward;

            var rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            var nearCenter = origin + (rotation * Vector3.forward * FrustumNearOffset);
            var halfWidth = FrustumWidth * 0.5f;
            var halfHeight = FrustumHeight * 0.5f;

            var nearTopLeft = nearCenter + rotation * new Vector3(-halfWidth * 0.35f, halfHeight * 0.35f, 0f);
            var nearTopRight = nearCenter + rotation * new Vector3(halfWidth * 0.35f, halfHeight * 0.35f, 0f);
            var nearBottomLeft = nearCenter + rotation * new Vector3(-halfWidth * 0.35f, -halfHeight * 0.35f, 0f);
            var nearBottomRight = nearCenter + rotation * new Vector3(halfWidth * 0.35f, -halfHeight * 0.35f, 0f);

            var farTopLeft = farCenter + rotation * new Vector3(-halfWidth, halfHeight, 0f);
            var farTopRight = farCenter + rotation * new Vector3(halfWidth, halfHeight, 0f);
            var farBottomLeft = farCenter + rotation * new Vector3(-halfWidth, -halfHeight, 0f);
            var farBottomRight = farCenter + rotation * new Vector3(halfWidth, -halfHeight, 0f);

            Gizmos.color = GetSelectedTint(frustumColor, IsSelected(), 0.12f, false);
            DrawWireQuad(nearTopLeft, nearTopRight, nearBottomRight, nearBottomLeft);
            DrawWireQuad(farTopLeft, farTopRight, farBottomRight, farBottomLeft);
            DrawWireSegment(nearTopLeft, farTopLeft);
            DrawWireSegment(nearTopRight, farTopRight);
            DrawWireSegment(nearBottomLeft, farBottomLeft);
            DrawWireSegment(nearBottomRight, farBottomRight);
            DrawWireSegment(origin, farCenter);
        }

        private void DrawTriggerZoneGizmo()
        {
            var isSelected = IsSelected();
            Gizmos.color = GetSelectedTint(triggerZoneFillColor, isSelected, 0.08f);
            Gizmos.DrawSphere(triggerZoneLocalPosition, triggerZoneRadius);

            Gizmos.color = GetSelectedTint(triggerZoneWireColor, isSelected, 0.12f, false);
            Gizmos.DrawWireSphere(triggerZoneLocalPosition, triggerZoneRadius);
        }

        private Color GetSelectedTint(Color baseColor, bool isSelected, float amount, bool boostAlpha = true)
        {
            if (!isSelected)
                return baseColor;

            return new Color(
                Mathf.Min(baseColor.r + amount, 1f),
                Mathf.Min(baseColor.g + amount, 1f),
                Mathf.Min(baseColor.b + amount, 1f),
                boostAlpha ? Mathf.Clamp01(baseColor.a + amount) : baseColor.a);
        }

        private bool IsSelected()
        {
#if UNITY_EDITOR
            return Selection.activeTransform == transform;
#else
            return false;
#endif
        }

        private static void DrawWireQuad(Vector3 topLeft, Vector3 topRight, Vector3 bottomRight, Vector3 bottomLeft)
        {
            DrawWireSegment(topLeft, topRight);
            DrawWireSegment(topRight, bottomRight);
            DrawWireSegment(bottomRight, bottomLeft);
            DrawWireSegment(bottomLeft, topLeft);
        }

        private static void DrawWireSegment(Vector3 from, Vector3 to)
        {
            Gizmos.DrawLine(from, to);
        }

        private void SnapToGround()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
                return;

            if (Selection.activeTransform == transform && GUIUtility.hotControl != 0)
                return;

            if (hasSnapCheckState &&
                Vector3.SqrMagnitude(transform.position - lastSnapCheckPosition) < 0.0001f &&
                Quaternion.Angle(transform.rotation, lastSnapCheckRotation) < 0.01f)
            {
                return;
            }

            hasSnapCheckState = true;
            lastSnapCheckPosition = transform.position;
            lastSnapCheckRotation = transform.rotation;

            if (!TryGetGroundHit(out var hit))
                return;

            if (Mathf.Approximately(transform.position.y, hit.point.y))
                return;

            Undo.RecordObject(transform, "Snap Photo Spot To Ground");
            transform.position = new Vector3(transform.position.x, hit.point.y, transform.position.z);
            EditorUtility.SetDirty(transform);
            lastSnapCheckPosition = transform.position;
#endif
        }

        private bool TryGetGroundHit(out RaycastHit hit)
        {
            var rayOrigin = transform.position + Vector3.up * SnapStartHeight;
            return Physics.Raycast(rayOrigin, Vector3.down, out hit, SnapDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        internal void SetState(bool isActivated, bool isCompleted, bool notifyGroup = true)
        {
            if (activated == isActivated && completed == isCompleted)
                return;

            activated = isActivated;
            completed = isCompleted;

            if (notifyGroup)
                group?.RegisterPhotoSpot(this);

            if (!activated && !completed)
            {
                ResetOccurred?.Invoke();
                onReset?.Invoke();
            }
        }

        internal void AssignGroup(MBPhotoSpotGroup owningGroup)
        {
            group = owningGroup;
        }

        private void CacheGroupReference()
        {
            group = GetComponentInParent<MBPhotoSpotGroup>();
        }
    }
}

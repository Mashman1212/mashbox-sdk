using System;
using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class MBRaceGate : MonoBehaviour
    {
        private const float MinimumGateAxisScale = 0.01f;
        private const float MinimumTopClearance = 3f;
        private const float MinimumGateWidth = 3f;
        private const float GroundProbeDistance = 1000f;

        private static readonly Color InvalidFillColor = new Color(1f, 0.18f, 0.18f, 0.18f);
        private static readonly Color InvalidWireColor = new Color(1f, 0.18f, 0.18f, 1f);
        private static readonly Color InvalidLabelColor = new Color(1f, 0.75f, 0.75f, 1f);
        private static readonly Color InvalidArrowColor = new Color(1f, 0.3f, 0.3f, 1f);
        private static readonly Color SelectedFillBoost = new Color(0.08f, 0.08f, 0.08f, 0.08f);
        private static readonly Color SelectedWireBoost = new Color(0.12f, 0.12f, 0.12f, 0f);

        [SerializeField] private Vector3 boxSize = new Vector3(2f, 2f, 0.5f);
        [SerializeField] private bool showStats = true;
        [Header("Runtime Trigger")]
        [SerializeField] private Vector3 triggerSizeMultiplier = Vector3.one;
        [SerializeField] private Vector3 triggerOffset = Vector3.zero;
        [SerializeField] private BoxCollider triggerCollider;
        [Header("Events")]
        [SerializeField] private UnityEvent onArmedUnity;
        [SerializeField] private UnityEvent onPassedUnity;
        [SerializeField] private UnityEvent onResetUnity;

        public Vector3 BoxSize => boxSize;
        public MBRace Race => GetComponentInParent<MBRace>();
        public int GateNumber => Race?.GetGateIndex(this) ?? 0;
        public float DistanceToNextGate => Race?.GetDistanceToNextGate(this) ?? 0f;
        public float DistanceFromStart => Race?.GetDistanceFromStart(this) ?? 0f;
        public float TotalRaceDistance => Race?.GetTotalGatePathDistance() ?? 0f;
        public bool IsArmed => armed;
        public bool HasPassed => passed;
        public BoxCollider TriggerCollider => triggerCollider;

        public event System.Action Armed;
        public event System.Action Passed;
        public event System.Action ResetOccurred;

        private bool armed;
        private bool passed;
        private bool triggerShapeIsExternallyManaged;

        private void Reset()
        {
            EnsureTriggerCollider();
            ResetGate();
        }

        private void Awake()
        {
            EnsureTriggerCollider();
        }

        private void OnEnable()
        {
            EnsureTriggerCollider();
        }

        private void OnValidate()
        {
            EnforceValidScale();
            triggerSizeMultiplier.x = Mathf.Max(triggerSizeMultiplier.x, 0.01f);
            triggerSizeMultiplier.y = Mathf.Max(triggerSizeMultiplier.y, 0.01f);
            triggerSizeMultiplier.z = Mathf.Max(triggerSizeMultiplier.z, 0.01f);
            EnsureTriggerCollider();
            SyncTriggerColliderShape();
        }

        private void OnDrawGizmos()
        {
            if (!MBGameplayGizmoVisibility.RacesVisible)
                return;

            EnforceValidScale();

            var previousMatrix = Gizmos.matrix;
            var previousColor = Gizmos.color;
            var center = GetLocalBoxCenter();
            var isSelected = IsSelected();
            var raceColor = GetRaceColor();
            var hasClearance = TryGetTopClearance(out var topClearance);
            var isInvalid = hasClearance && topClearance < MinimumTopClearance;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = GetDisplayColor(isInvalid ? InvalidFillColor : GetFillColor(raceColor), isSelected, true);
            Gizmos.DrawCube(center, boxSize);

            Gizmos.color = GetDisplayColor(isInvalid ? InvalidWireColor : GetWireColor(raceColor), isSelected, false);
            Gizmos.DrawWireCube(center, boxSize);

            Gizmos.color = previousColor;
            Gizmos.matrix = previousMatrix;

#if UNITY_EDITOR
            DrawValidationGuides(hasClearance, topClearance, raceColor, isSelected);
            DrawForwardArrow(isInvalid, raceColor, isSelected);
            DrawLabels(isInvalid, raceColor, isSelected);
#endif
        }

        public Vector3 GetTopPointWorld()
        {
            return transform.position + (transform.up * GetScaledHeight());
        }

        public bool TryGetTopClearance(out float topClearance)
        {
            topClearance = 0f;
            var topPoint = GetTopPointWorld();
            if (!Physics.Raycast(topPoint, Vector3.down, out var hit, GroundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;

            topClearance = topPoint.y - hit.point.y;
            return true;
        }

        public MBRaceGate GetNextGate()
        {
            return Race?.GetNextGate(this);
        }

        public void Arm()
        {
            EnsureTriggerCollider();
            passed = false;
            armed = true;
            if (triggerCollider != null)
                triggerCollider.enabled = true;

            Armed?.Invoke();
            onArmedUnity?.Invoke();
        }

        public void Pass()
        {
            TryPass(null);
        }

        public bool TryPass(Collider triggeringCollider)
        {
            if (!MBGameplayStateGuard.IsGameplayActive)
                return false;

            if (!armed || passed)
                return false;

            if (triggeringCollider != null && !CanBePassedBy(triggeringCollider))
                return false;

            passed = true;
            armed = false;
            if (triggerCollider != null)
                triggerCollider.enabled = false;

            Passed?.Invoke();
            onPassedUnity?.Invoke();
            return true;
        }

        public void ResetGate()
        {
            passed = false;
            armed = false;
            EnsureTriggerCollider();
            if (triggerCollider != null)
                triggerCollider.enabled = false;

            ResetOccurred?.Invoke();
            onResetUnity?.Invoke();
        }

        public void SetTriggerCollider(BoxCollider collider)
        {
            if (triggerCollider != null && triggerCollider != collider)
                triggerCollider.enabled = false;

            triggerCollider = collider;
            if (triggerCollider == null)
            {
                triggerShapeIsExternallyManaged = false;
                return;
            }

            triggerShapeIsExternallyManaged = triggerCollider.transform != transform;
            triggerCollider.isTrigger = true;
            if (!triggerShapeIsExternallyManaged)
                SyncTriggerColliderShape();
            triggerCollider.enabled = armed;
        }

        public bool CanBePassedBy(Collider other)
        {
            if (other == null)
                return false;

            if (other.transform == transform || other.transform.IsChildOf(transform))
                return false;

            return ContainsMixamoRigName(other.transform);
        }

        public void EnforceValidScale()
        {
            // Scale is an authoring control only. At runtime ChallengeSystemManager
            // caches its dimensions, then normalizes this root so spawned physics
            // objects and trigger children live beneath a (1,1,1) transform.
            if (Application.isPlaying)
                return;

            var localScale = transform.localScale;
            var minimumXScale = GetMinimumRequiredXScale();
            var minimumYScale = GetMinimumRequiredYScale();
            var sanitizedScale = new Vector3(
                Mathf.Max(Mathf.Abs(localScale.x), minimumXScale),
                Mathf.Max(Mathf.Abs(localScale.y), minimumYScale),
                1f);

            if (localScale == sanitizedScale)
                return;

            transform.localScale = sanitizedScale;
#if UNITY_EDITOR
            EditorUtility.SetDirty(transform);
#endif
        }

        private float GetMinimumRequiredXScale()
        {
            var minimumScaleFromWidth = boxSize.x > 0.0001f
                ? MinimumGateWidth / boxSize.x
                : MinimumGateAxisScale;

            return Mathf.Max(MinimumGateAxisScale, minimumScaleFromWidth);
        }

        private Vector3 GetLocalBoxCenter()
        {
            return new Vector3(0f, boxSize.y * 0.5f, 0f);
        }

        private float GetScaledHeight()
        {
            return boxSize.y * Mathf.Abs(transform.lossyScale.y);
        }

        private float GetMinimumRequiredYScale()
        {
            if (!TryGetGroundPointBelowBase(out var groundPoint))
                return MinimumGateAxisScale;

            var baseClearance = Vector3.Dot(transform.position - groundPoint, transform.up);
            var requiredHeight = Mathf.Max(MinimumTopClearance - baseClearance, 0f);
            var minimumScaleFromClearance = boxSize.y > 0.0001f
                ? requiredHeight / boxSize.y
                : MinimumGateAxisScale;

            return Mathf.Max(MinimumGateAxisScale, minimumScaleFromClearance);
        }

        private bool TryGetGroundPointBelowBase(out Vector3 groundPoint)
        {
            groundPoint = Vector3.zero;

            var rayOrigin = transform.position + (transform.up * 0.05f);
            if (!Physics.Raycast(rayOrigin, -transform.up, out var hit, GroundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;

            groundPoint = hit.point;
            return true;
        }

        public Vector3 GetTriggerZoneSize()
        {
            return new Vector3(
                Mathf.Max(boxSize.x * triggerSizeMultiplier.x, 0.01f),
                Mathf.Max(boxSize.y * triggerSizeMultiplier.y, 0.01f),
                Mathf.Max(boxSize.z * triggerSizeMultiplier.z, 0.01f));
        }

        public Vector3 GetTriggerZoneCenter()
        {
            var size = GetTriggerZoneSize();
            return new Vector3(
                triggerOffset.x,
                size.y * 0.5f + triggerOffset.y,
                triggerOffset.z);
        }

        private Color GetRaceColor()
        {
            return Race != null ? Race.GizmoColor : new Color(1f, 0.45f, 0.15f, 1f);
        }

        private void EnsureTriggerCollider()
        {
            // Runtime race gates bind this to the CheckPointTriggerZone child.
            // Never create a second trigger on the authoring/root MBRaceGate.
            if (triggerCollider != null && triggerCollider.transform == transform)
            {
                triggerCollider.enabled = false;
                triggerCollider = null;
                triggerShapeIsExternallyManaged = false;
            }

            if (triggerCollider == null)
                return;

            triggerCollider.isTrigger = true;
            SyncTriggerColliderShape();
            triggerCollider.enabled = armed;
        }

        private void SyncTriggerColliderShape()
        {
            if (triggerCollider == null || triggerShapeIsExternallyManaged)
                return;

            triggerCollider.isTrigger = true;
            triggerCollider.size = GetTriggerZoneSize();
            triggerCollider.center = GetTriggerZoneCenter();
        }

        private void OnTriggerEnter(Collider other)
        {
            TryPass(other);
        }

        private static bool ContainsMixamoRigName(Transform target)
        {
            var current = target;
            while (current != null)
            {
                if (current.name.IndexOf("mixamorig", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                current = current.parent;
            }

            return false;
        }

        private static Color GetFillColor(Color raceColor)
        {
            return new Color(raceColor.r, raceColor.g, raceColor.b, 0.12f);
        }

        private static Color GetWireColor(Color raceColor)
        {
            return new Color(raceColor.r, raceColor.g, raceColor.b, 0.95f);
        }

        private static Color GetArrowColor(Color raceColor)
        {
            return Color.Lerp(raceColor, Color.white, 0.2f);
        }

        private static Color GetLabelColor(Color raceColor)
        {
            return Color.Lerp(raceColor, Color.white, 0.45f);
        }

        private Color GetDisplayColor(Color baseColor, bool isSelected, bool includeAlpha)
        {
            if (!isSelected)
                return baseColor;

            var boost = includeAlpha ? SelectedFillBoost : SelectedWireBoost;
            return new Color(
                Mathf.Min(baseColor.r + boost.r, 1f),
                Mathf.Min(baseColor.g + boost.g, 1f),
                Mathf.Min(baseColor.b + boost.b, 1f),
                includeAlpha ? Mathf.Clamp01(baseColor.a + boost.a) : baseColor.a);
        }

        private bool IsSelected()
        {
#if UNITY_EDITOR
            return Selection.activeTransform == transform;
#else
            return false;
#endif
        }

#if UNITY_EDITOR
        private static GUIStyle gateLabelStyle;
        private static GUIStyle gateStatsStyle;
        private static GUIStyle gateGuideStyle;
        private static readonly Vector3[] ArrowPoints = new Vector3[2];

        private void DrawForwardArrow(bool isInvalid, Color raceColor, bool isSelected)
        {
            var previousColor = Handles.color;
            Handles.color = GetDisplayColor(isInvalid ? InvalidArrowColor : GetArrowColor(raceColor), isSelected, false);

            var start = transform.position + (transform.up * Mathf.Max(GetScaledHeight() * 0.35f, 0.75f));
            var end = start + (transform.forward * Mathf.Max(boxSize.z + 1.75f, 2f));
            ArrowPoints[0] = start;
            ArrowPoints[1] = end;
            Handles.DrawAAPolyLine(4f, ArrowPoints);
            Handles.ArrowHandleCap(0, end, transform.rotation, 1f, EventType.Repaint);

            Handles.color = previousColor;
        }

        private void DrawLabels(bool isInvalid, Color raceColor, bool isSelected)
        {
            var labelColor = GetDisplayColor(isInvalid ? InvalidLabelColor : GetLabelColor(raceColor), isSelected, false);
            var labelStyle = gateLabelStyle ?? (gateLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            });
            labelStyle.normal.textColor = labelColor;

            // Reuse one ordered gate list for all label statistics, instead of five hierarchy scans.
            var race = Race;
            var gates = race != null ? race.GetOrderedGates() : null;
            int index = gates != null ? gates.IndexOf(this) : -1;
            var labelPosition = GetTopPointWorld() + (Vector3.up * 0.35f);
            Handles.Label(labelPosition, $"Gate {index + 1:00}", labelStyle);

            if (!showStats)
                return;

            var statsStyle = gateStatsStyle ?? (gateStatsStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.UpperLeft
            });
            statsStyle.normal.textColor = labelColor;

            float totalDistance = 0f, distanceFromStart = 0f, distanceToNext = 0f;
            if (gates != null)
            {
                for (int i = 0; i < gates.Count - 1; i++)
                {
                    float distance = Vector3.Distance(gates[i].transform.position, gates[i + 1].transform.position);
                    totalDistance += distance;
                    if (i < index) distanceFromStart += distance;
                    if (i == index) distanceToNext = distance;
                }
            }
            bool hasNext = gates != null && index >= 0 && index < gates.Count - 1;
            var nextDistance = hasNext ? $"{distanceToNext:0.0}m to next" : "Finish gate";
            var statsText = $"Race {totalDistance:0.0}m\nFrom start {distanceFromStart:0.0}m\n{nextDistance}";
            Handles.Label(labelPosition + (Vector3.right * 0.35f), statsText, statsStyle);
        }

        private void DrawValidationGuides(bool hasClearance, float topClearance, Color raceColor, bool isSelected)
        {
            if (!hasClearance)
                return;

            var topPoint = GetTopPointWorld();
            if (!TryGetGroundPointBelowBase(out var groundPoint))
                return;

            var isInvalid = topClearance < MinimumTopClearance;
            var raceGuideColor = GetWireColor(raceColor);
            raceGuideColor.a = 0.8f;
            var guideColor = GetDisplayColor(isInvalid ? InvalidWireColor : raceGuideColor, isSelected, false);
            var requiredTopPoint = groundPoint + (transform.up * MinimumTopClearance);

            var previousColor = Handles.color;
            Handles.color = guideColor;
            Handles.DrawDottedLine(groundPoint, topPoint, 4f);
            Handles.DrawDottedLine(groundPoint, requiredTopPoint, 4f);
            Handles.DrawWireDisc(groundPoint, transform.up, 0.18f);
            Handles.DrawWireDisc(requiredTopPoint, transform.up, 0.18f);

            var style = gateGuideStyle ?? (gateGuideStyle = new GUIStyle(EditorStyles.miniBoldLabel));
            style.normal.textColor = guideColor;
            Handles.Label(topPoint + (Vector3.left * 0.35f), isInvalid
                ? $"Top clearance {topClearance:0.0}m / min {MinimumTopClearance:0.0}m"
                : $"Top clearance {topClearance:0.0}m", style);

            Handles.color = previousColor;
        }

#endif
    }
}

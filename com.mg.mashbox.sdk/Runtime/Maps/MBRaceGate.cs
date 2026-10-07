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

            var previousMatrix = Gizmos.matrix;
            var previousColor = Gizmos.color;
            var center = GetLocalBoxCenter();
            var isSelected = IsSelected();
#if UNITY_EDITOR
            var race = EditorRace;
            var raceColor = race != null ? race.EditorGizmoColor : new Color(1f, 0.45f, 0.15f, 1f);
            bool showRaceLabels = race != null && Selection.Contains(race.gameObject);
            // Only the gate being edited needs ground probes and clearance diagnostics.
            if (isSelected) RefreshEditorValidation();
            bool isInvalid = isSelected && editorHasClearance && editorTopClearance < MinimumTopClearance;
#else
            var raceColor = GetRaceColor();
            bool isInvalid = false;
#endif

            Gizmos.matrix = transform.localToWorldMatrix;
            if (isSelected)
            {
                Gizmos.color = GetDisplayColor(isInvalid ? InvalidFillColor : GetFillColor(raceColor), true, true);
                Gizmos.DrawCube(center, boxSize);
            }

            Gizmos.color = GetDisplayColor(isInvalid ? InvalidWireColor : GetWireColor(raceColor), isSelected, false);
            Gizmos.DrawWireCube(center, boxSize);

            Gizmos.color = previousColor;
            Gizmos.matrix = previousMatrix;

#if UNITY_EDITOR
            // Handles.Label allocates inside Unity even with cached GUIContent. Do not submit
            // hundreds of labels for unrelated races, or any handles during gizmo picking.
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            if (isSelected)
                DrawValidationGuides(raceColor);
            if (isSelected || showRaceLabels)
            {
                DrawForwardArrow(isInvalid, raceColor, isSelected);
                DrawLabels(isInvalid, raceColor, isSelected, race);
            }
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
            return Selection.Contains(gameObject);
#else
            return false;
#endif
        }

#if UNITY_EDITOR
        private static GUIStyle gateLabelStyle;
        private static GUIStyle gateStatsStyle;
        private static GUIStyle gateGuideStyle;
        private static readonly Vector3[] ArrowPoints = new Vector3[2];
        private static readonly GUIContent UnassignedGateName = new GUIContent("Gate 00");
        private static readonly GUIContent UnassignedGateStats = new GUIContent("Race 0.0m\nFrom start 0.0m\nFinish gate");
        private readonly GUIContent editorClearanceLabel = new GUIContent();
        private int editorRaceRevision = -1;
        private MBRace editorRace;
        private bool editorValidationCached;
        private Matrix4x4 editorValidationMatrix;
        private Vector3 editorValidationBoxSize;
        private double editorNextValidationTime;
        private bool editorHasClearance;
        private float editorTopClearance;
        private bool editorHasGround;
        private Vector3 editorGroundPoint;

        private MBRace EditorRace
        {
            get
            {
                if (editorRaceRevision != MBRace.EditorGizmoRevision)
                {
                    editorRaceRevision = MBRace.EditorGizmoRevision;
                    editorRace = Race;
                }
                return editorRace;
            }
        }

        private void RefreshEditorValidation()
        {
            var matrix = transform.localToWorldMatrix;
            bool changed = !editorValidationCached || matrix != editorValidationMatrix || boxSize != editorValidationBoxSize;
            if (!changed && EditorApplication.timeSinceStartup < editorNextValidationTime) return;
            // Preserve authoring scale constraints when a selected gate moves, without probing
            // the ground under every gate on every Scene view repaint.
            if (changed) EnforceValidScale();
            editorValidationCached = true;
            editorValidationMatrix = transform.localToWorldMatrix;
            editorValidationBoxSize = boxSize;
            editorNextValidationTime = EditorApplication.timeSinceStartup + 0.25d;
            editorHasClearance = TryGetTopClearance(out editorTopClearance);
            editorHasGround = TryGetGroundPointBelowBase(out editorGroundPoint);
            editorClearanceLabel.text = editorTopClearance < MinimumTopClearance
                ? $"Top clearance {editorTopClearance:0.0}m / min {MinimumTopClearance:0.0}m"
                : $"Top clearance {editorTopClearance:0.0}m";
        }

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

        private void DrawLabels(bool isInvalid, Color raceColor, bool isSelected, MBRace race)
        {
            var labelColor = GetDisplayColor(isInvalid ? InvalidLabelColor : GetLabelColor(raceColor), isSelected, false);
            var labelStyle = gateLabelStyle ?? (gateLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            });
            labelStyle.normal.textColor = labelColor;

            var labels = race != null ? race.GetEditorGateLabels(this) : null;
            var labelPosition = GetTopPointWorld() + (Vector3.up * 0.35f);
            Handles.Label(labelPosition, labels != null ? labels.Name : UnassignedGateName, labelStyle);

            if (!showStats || !isSelected)
                return;

            var statsStyle = gateStatsStyle ?? (gateStatsStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.UpperLeft
            });
            statsStyle.normal.textColor = labelColor;

            Handles.Label(labelPosition + (Vector3.right * 0.35f), labels != null ? labels.Stats : UnassignedGateStats, statsStyle);
        }

        private void DrawValidationGuides(Color raceColor)
        {
            if (!editorHasClearance || !editorHasGround)
                return;

            var topPoint = GetTopPointWorld();
            var groundPoint = editorGroundPoint;

            var isInvalid = editorTopClearance < MinimumTopClearance;
            var raceGuideColor = GetWireColor(raceColor);
            raceGuideColor.a = 0.8f;
            var guideColor = GetDisplayColor(isInvalid ? InvalidWireColor : raceGuideColor, true, false);
            var requiredTopPoint = groundPoint + (transform.up * MinimumTopClearance);

            var previousColor = Handles.color;
            Handles.color = guideColor;
            Handles.DrawDottedLine(groundPoint, topPoint, 4f);
            Handles.DrawDottedLine(groundPoint, requiredTopPoint, 4f);
            Handles.DrawWireDisc(groundPoint, transform.up, 0.18f);
            Handles.DrawWireDisc(requiredTopPoint, transform.up, 0.18f);

            var style = gateGuideStyle ?? (gateGuideStyle = new GUIStyle(EditorStyles.miniBoldLabel));
            style.normal.textColor = guideColor;
            Handles.Label(topPoint + (Vector3.left * 0.35f), editorClearanceLabel, style);

            Handles.color = previousColor;
        }

#endif
    }
}

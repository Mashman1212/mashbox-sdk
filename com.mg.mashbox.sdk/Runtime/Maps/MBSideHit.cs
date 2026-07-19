using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MashBoxSDK.Maps
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public class MBSideHit : MonoBehaviour
    {
        [System.Serializable]
        public class FlagSet
        {
            public string name = "Flag Set";
            public Vector3 localCenter = Vector3.zero;
            public Vector3 localEulerAngles = Vector3.zero;
            public Vector3 localScale = Vector3.one;
        }

        public enum FlagVisualColor
        {
            Orange,
            Blue
        }

        private const float MinimumAxisSize = 0.01f;
        private const float PoleRadius = 0.035f;
        private const float PoleHeight = 0.55f;
        private const float FlagWidth = 0.22f;
        private const float FlagHeight = 0.14f;
        private const float GroundProbeStartHeight = 2.5f;
        private const float GroundProbeDistance = 20f;

        private static readonly Color FillColor = new Color(0.98f, 0.63f, 0.17f, 0.14f);
        private static readonly Color WireColor = new Color(0.98f, 0.63f, 0.17f, 0.95f);
        private static readonly Color LabelColor = new Color(1f, 0.86f, 0.55f, 1f);
        private static readonly Color PoleColor = new Color(0.86f, 0.86f, 0.78f, 0.95f);
        private static readonly Color OrangeFlagFillColor = new Color(1f, 0.56f, 0.08f, 0.55f);
        private static readonly Color OrangeFlagWireColor = new Color(1f, 0.72f, 0.24f, 1f);
        private static readonly Color OrangeRuntimeFlagColor = new Color(1f, 0.48f, 0.06f, 1f);
        private static readonly Color BlueFlagFillColor = new Color(0.08f, 0.54f, 1f, 0.55f);
        private static readonly Color BlueFlagWireColor = new Color(0.35f, 0.82f, 1f, 1f);
        private static readonly Color BlueRuntimeFlagColor = new Color(0.08f, 0.46f, 1f, 1f);

        [SerializeField] private string sideHitName = "Side Hit";
        [SerializeField] private Vector3 boxSize = new Vector3(4f, 2f, 1f);
        [SerializeField] private bool showLabel = true;
        [SerializeField] private FlagVisualColor flagColor = FlagVisualColor.Orange;
        [SerializeField] private FlagSet[] flagSets = { new FlagSet { name = "Flag Set 1", localCenter = Vector3.zero } };
        [SerializeField, Min(0f)] private float resetDelaySeconds = 10f;

        [Header("Completion")]
        [SerializeField] private bool requireAllFlagSets;

        [Header("Runtime Trigger")]
        [SerializeField] private Vector3 triggerSizeMultiplier = Vector3.one;
        [SerializeField] private Vector3 triggerOffset = Vector3.zero;
        [SerializeField] private BoxCollider triggerCollider;

        [Header("Events")]
        [SerializeField] private UnityEvent onEntered;
        [SerializeField] private UnityEvent onCompleted;
        [SerializeField] private UnityEvent onReset;

        private MBSideHitGroup group;
        private bool entered;
        private bool completed;

        public event System.Action Entered;
        public event System.Action Completed;
        public event System.Action ResetOccurred;

        public string SideHitName
        {
            get => sideHitName;
            set
            {
                sideHitName = string.IsNullOrWhiteSpace(value) ? "Side Hit" : value.Trim();
                SyncGameObjectName();
            }
        }

        public Vector3 BoxSize => boxSize;
        public FlagVisualColor FlagColor => flagColor;
        public int FlagSetCount => flagSets != null ? Mathf.Max(flagSets.Length, 1) : 1;
        public float ResetDelaySeconds => resetDelaySeconds;
        public bool RequireAllFlagSets => requireAllFlagSets;
        public BoxCollider TriggerCollider => triggerCollider;
        public bool HasEntered => entered;
        public bool IsCompleted => completed;

        private void Awake()
        {
            CacheGroupReference();
            EnsureTriggerCollider();
        }

        private void Reset()
        {
            sideHitName = string.IsNullOrWhiteSpace(sideHitName) ? gameObject.name : sideHitName;
            EnsureTriggerCollider();
            SyncGameObjectName();
        }

        private void OnValidate()
        {
            CacheGroupReference();
            sideHitName = string.IsNullOrWhiteSpace(sideHitName) ? "Side Hit" : sideHitName.Trim();
            resetDelaySeconds = Mathf.Max(0f, resetDelaySeconds);
            boxSize = SanitizeVector(boxSize);
            triggerSizeMultiplier = SanitizeVector(triggerSizeMultiplier);
            EnsureFlagSets();
            SanitizeFlagSets();
            EnsureTriggerCollider();
            SyncTriggerColliderShape();
            SyncGameObjectName();
            group?.RegisterSideHit(this);
        }

        private void OnDrawGizmos()
        {
            if (!MBGameplayGizmoVisibility.Visible)
                return;

            var previousMatrix = Gizmos.matrix;
            var previousColor = Gizmos.color;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = FillColor;
            Gizmos.DrawCube(GetLocalBoxCenter(), boxSize);
            Gizmos.color = WireColor;
            Gizmos.DrawWireCube(GetLocalBoxCenter(), boxSize);

            Gizmos.matrix = previousMatrix;
            DrawFlagSetGizmos();

            Gizmos.color = previousColor;
            Gizmos.matrix = previousMatrix;

#if UNITY_EDITOR
            if (showLabel)
                DrawLabel();
#endif
        }

        public Vector3 GetTriggerZoneSize()
        {
            return new Vector3(
                Mathf.Max(boxSize.x * triggerSizeMultiplier.x, MinimumAxisSize),
                Mathf.Max(boxSize.y * triggerSizeMultiplier.y, MinimumAxisSize),
                Mathf.Max(boxSize.z * triggerSizeMultiplier.z, MinimumAxisSize));
        }

        public Vector3 GetFlagSetTriggerZoneSize(int index)
        {
            return Vector3.Scale(GetTriggerZoneSize(), GetFlagSetLocalScale(index));
        }

        public Vector3 GetTriggerZoneCenter()
        {
            Vector3 size = GetTriggerZoneSize();
            return new Vector3(
                triggerOffset.x,
                size.y * 0.5f + triggerOffset.y,
                triggerOffset.z);
        }

        public Color GetFlagRuntimeColor()
        {
            return flagColor == FlagVisualColor.Blue ? BlueRuntimeFlagColor : OrangeRuntimeFlagColor;
        }

        public void ToggleFlagVisualColor()
        {
            flagColor = flagColor == FlagVisualColor.Blue ? FlagVisualColor.Orange : FlagVisualColor.Blue;
        }

        public Vector3 GetFlagSetLocalCenter(int index)
        {
            EnsureFlagSets();
            return flagSets[Mathf.Clamp(index, 0, flagSets.Length - 1)].localCenter;
        }

        public Vector3 GetFlagSetTriggerZoneCenter(int index)
        {
            Vector3 triggerSize = GetFlagSetTriggerZoneSize(index);
            return new Vector3(
                triggerOffset.x,
                triggerSize.y * 0.5f + triggerOffset.y,
                triggerOffset.z);
        }

        public Quaternion GetFlagSetLocalRotation(int index)
        {
            EnsureFlagSets();
            return Quaternion.Euler(flagSets[Mathf.Clamp(index, 0, flagSets.Length - 1)].localEulerAngles);
        }

        public Vector3 GetFlagSetLocalScale(int index)
        {
            EnsureFlagSets();
            return SanitizeScale(flagSets[Mathf.Clamp(index, 0, flagSets.Length - 1)].localScale);
        }

        public void AddFlagSet()
        {
            EnsureFlagSets();
            int oldCount = flagSets.Length;
            var newFlagSets = new FlagSet[oldCount + 1];
            for (int i = 0; i < oldCount; i++)
                newFlagSets[i] = flagSets[i];

            float zStep = Mathf.Max(boxSize.z * 0.5f, 1f);
            newFlagSets[oldCount] = new FlagSet
            {
                name = $"Flag Set {oldCount + 1}",
                localCenter = flagSets[oldCount - 1].localCenter + new Vector3(0f, 0f, zStep),
                localEulerAngles = flagSets[oldCount - 1].localEulerAngles,
                localScale = flagSets[oldCount - 1].localScale
            };
            flagSets = newFlagSets;
        }

        public void RemoveLastFlagSet()
        {
            EnsureFlagSets();
            if (flagSets.Length <= 1)
                return;

            var newFlagSets = new FlagSet[flagSets.Length - 1];
            for (int i = 0; i < newFlagSets.Length; i++)
                newFlagSets[i] = flagSets[i];

            flagSets = newFlagSets;
        }

        public void EnterSideHit()
        {
            if (!MBGameplayStateGuard.IsGameplayActive)
                return;

            if (entered)
                return;

            entered = true;
            group?.NotifyEntered(this);
            Entered?.Invoke();
            onEntered?.Invoke();
        }

        public void CompleteSideHit()
        {
            if (!MBGameplayStateGuard.IsGameplayActive)
                return;

            if (!entered)
                entered = true;

            if (completed)
                return;

            completed = true;
            SetTriggerEnabled(false);
            group?.NotifyCompleted(this);
            Completed?.Invoke();
            onCompleted?.Invoke();
        }

        public void ResetSideHit()
        {
            SetState(false, false);
        }

        internal void SetState(bool hasEntered, bool isCompleted, bool notifyGroup = true)
        {
            if (entered == hasEntered && completed == isCompleted)
                return;

            entered = hasEntered;
            completed = isCompleted;
            SetTriggerEnabled(!completed);

            if (notifyGroup)
                group?.RegisterSideHit(this);

            if (!entered && !completed)
            {
                ResetOccurred?.Invoke();
                onReset?.Invoke();
            }
        }

        internal void AssignGroup(MBSideHitGroup owningGroup)
        {
            group = owningGroup;
        }

        private void EnsureTriggerCollider()
        {
            if (triggerCollider == null)
                triggerCollider = GetComponent<BoxCollider>();

            if (triggerCollider == null)
                triggerCollider = gameObject.AddComponent<BoxCollider>();

            triggerCollider.isTrigger = true;
            SyncTriggerColliderShape();
            SetTriggerEnabled(!completed);
        }

        private void SyncTriggerColliderShape()
        {
            if (triggerCollider == null)
                return;

            triggerCollider.isTrigger = true;
            triggerCollider.size = GetTriggerZoneSize();
            triggerCollider.center = GetTriggerZoneCenter();
        }

        private void SetTriggerEnabled(bool enabled)
        {
            if (triggerCollider != null)
                triggerCollider.enabled = enabled;
        }

        private void CacheGroupReference()
        {
            group = GetComponentInParent<MBSideHitGroup>();
        }

        private void SyncGameObjectName()
        {
            if (gameObject.name != sideHitName)
                gameObject.name = sideHitName;
        }

        private void EnsureFlagSets()
        {
            if (flagSets != null && flagSets.Length > 0)
                return;

            flagSets = new[] { new FlagSet { name = "Flag Set 1", localCenter = Vector3.zero } };
        }

        private void SanitizeFlagSets()
        {
            EnsureFlagSets();
            for (int i = 0; i < flagSets.Length; i++)
            {
                if (flagSets[i] == null)
                    flagSets[i] = new FlagSet { name = $"Flag Set {i + 1}" };

                flagSets[i].localScale = SanitizeScale(flagSets[i].localScale);
            }
        }

        private Vector3 GetLocalBoxCenter()
        {
            return new Vector3(0f, boxSize.y * 0.5f, 0f);
        }

        private void DrawFlagSetGizmos()
        {
            SanitizeFlagSets();
            for (int i = 0; i < flagSets.Length; i++)
            {
                Vector3 localCenter = flagSets[i].localCenter;
                Quaternion localRotation = Quaternion.Euler(flagSets[i].localEulerAngles);
                Vector3 localScale = SanitizeScale(flagSets[i].localScale);
                DrawFlagPoleGizmo(localCenter + (localRotation * Vector3.Scale(new Vector3(-boxSize.x * 0.5f, 0f, 0f), localScale)), localRotation);
                DrawFlagPoleGizmo(localCenter + (localRotation * Vector3.Scale(new Vector3(boxSize.x * 0.5f, 0f, 0f), localScale)), localRotation);
            }
        }

        private Vector3 DrawFlagPoleGizmo(Vector3 localBase, Quaternion localRotation)
        {
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Vector3 worldBase = transform.TransformPoint(localBase);
            Vector3 groundedBase = GetGroundedFlagBase(worldBase);

            Gizmos.color = GetFlagGuideColor();
            Gizmos.DrawLine(worldBase, groundedBase);
            Gizmos.DrawWireSphere(groundedBase, PoleRadius * 1.75f);

            Gizmos.matrix = Matrix4x4.TRS(groundedBase, transform.rotation * localRotation, Vector3.one);
            Vector3 poleCenter = Vector3.up * (PoleHeight * 0.5f);
            Vector3 flagCenter = new Vector3(FlagWidth * -0.35f, PoleHeight, 0f);

            Gizmos.color = PoleColor;
            Gizmos.DrawCube(poleCenter, new Vector3(PoleRadius, PoleHeight, PoleRadius));
            Gizmos.color = GetFlagFillColor();
            Gizmos.DrawCube(flagCenter, new Vector3(FlagWidth, FlagHeight, FlagWidth * 0.35f));
            Gizmos.color = GetFlagWireColor();
            Gizmos.DrawWireCube(flagCenter, new Vector3(FlagWidth, FlagHeight, FlagWidth * 0.35f));

            Gizmos.matrix = previousMatrix;
            return groundedBase;
        }

        private Color GetFlagFillColor()
        {
            return flagColor == FlagVisualColor.Blue ? BlueFlagFillColor : OrangeFlagFillColor;
        }

        private Color GetFlagWireColor()
        {
            return flagColor == FlagVisualColor.Blue ? BlueFlagWireColor : OrangeFlagWireColor;
        }

        private Color GetFlagGuideColor()
        {
            Color color = GetFlagWireColor();
            color.a = 0.75f;
            return color;
        }

        private static Vector3 GetGroundedFlagBase(Vector3 worldPosition)
        {
            Vector3 rayOrigin = worldPosition + (Vector3.up * GroundProbeStartHeight);
            float rayDistance = GroundProbeStartHeight + GroundProbeDistance;

            return Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.point
                : worldPosition;
        }

        private static Vector3 SanitizeVector(Vector3 value)
        {
            return new Vector3(
                Mathf.Max(Mathf.Abs(value.x), MinimumAxisSize),
                Mathf.Max(Mathf.Abs(value.y), MinimumAxisSize),
                Mathf.Max(Mathf.Abs(value.z), MinimumAxisSize));
        }

        private static Vector3 SanitizeScale(Vector3 value)
        {
            if (value == Vector3.zero)
                return Vector3.one;

            return new Vector3(
                Mathf.Max(Mathf.Abs(value.x), MinimumAxisSize),
                Mathf.Max(Mathf.Abs(value.y), MinimumAxisSize),
                Mathf.Max(Mathf.Abs(value.z), MinimumAxisSize));
        }

#if UNITY_EDITOR
        private void DrawLabel()
        {
            var previousColor = Handles.color;
            Handles.color = LabelColor;

            var labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            labelStyle.normal.textColor = LabelColor;

            Handles.Label(transform.position + (transform.up * Mathf.Max(boxSize.y + 0.35f, 0.75f)), sideHitName, labelStyle);
            Handles.color = previousColor;
        }
#endif
    }

}

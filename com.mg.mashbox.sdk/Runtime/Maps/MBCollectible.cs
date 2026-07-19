using System.Linq;
using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MashBoxSDK.Maps
{
    [DisallowMultipleComponent]
    public class MBCollectible : MonoBehaviour
    {
        private static bool isRenumbering;

        public event System.Action Collected;
        public event System.Action ResetOccurred;

        [Header("State")]
        [SerializeField] private bool collected;

        [Header("Events")]
        [SerializeField] private UnityEvent onCollected;
        [SerializeField] private UnityEvent onReset;

        [SerializeField] private float gizmoRadius = 0.5f;
        private readonly Color fillColor = new Color(1f, 0.85f, 0.2f, 0.15f);
        private readonly Color wireColor = new Color(1f, 0.85f, 0.2f, 0.95f);
        private readonly Color blockedFillColor = new Color(1f, 0.2f, 0.2f, 0.18f);
        private readonly Color blockedWireColor = new Color(1f, 0.2f, 0.2f, 0.95f);

        private MBCollectibleGroup group;

        public bool IsCollected => collected;
        public bool IsPlacementBlocked => IsInsideGeometry();

        private void Reset()
        {
            CacheGroupReference();
            RenumberSiblingCollectibles();
        }

        private void Awake()
        {
            CacheGroupReference();
        }

        private void OnValidate()
        {
            CacheGroupReference();
            RenumberSiblingCollectibles();
            group?.RegisterCollectible(this);
        }

        private void OnDrawGizmos()
        {
            if (!MBGameplayGizmoVisibility.Visible)
                return;

            var previousMatrix = Gizmos.matrix;
            var previousColor = Gizmos.color;
            var isBlocked = IsInsideGeometry();
            var isSelected = IsSelected();

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = GetFillColor(isBlocked, isSelected);
            Gizmos.DrawSphere(Vector3.zero, gizmoRadius);

            Gizmos.color = GetWireColor(isBlocked, isSelected);
            Gizmos.DrawWireSphere(Vector3.zero, gizmoRadius);

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        private Color GetFillColor(bool isBlocked, bool isSelected)
        {
            var baseColor = isBlocked ? blockedFillColor : fillColor;
            if (!isSelected)
                return baseColor;

            return new Color(
                Mathf.Min(baseColor.r + 0.08f, 1f),
                Mathf.Min(baseColor.g + 0.08f, 1f),
                Mathf.Min(baseColor.b + 0.08f, 1f),
                Mathf.Clamp01(baseColor.a + 0.08f));
        }

        private Color GetWireColor(bool isBlocked, bool isSelected)
        {
            var baseColor = isBlocked ? blockedWireColor : wireColor;
            if (!isSelected)
                return baseColor;

            return new Color(
                Mathf.Min(baseColor.r + 0.12f, 1f),
                Mathf.Min(baseColor.g + 0.12f, 1f),
                Mathf.Min(baseColor.b + 0.12f, 1f),
                baseColor.a);
        }

        private bool IsSelected()
        {
#if UNITY_EDITOR
            return Selection.activeTransform == transform;
#else
            return false;
#endif
        }

        public bool IsInsideGeometry()
        {
            var overlaps = Physics.OverlapSphere(transform.position, gizmoRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var index = 0; index < overlaps.Length; index++)
            {
                var overlap = overlaps[index];
                if (overlap == null)
                    continue;

                if (overlap.transform == transform || overlap.transform.IsChildOf(transform))
                    continue;

                return true;
            }

            return false;
        }

        private void RenumberSiblingCollectibles()
        {
            if (isRenumbering || transform.parent == null)
                return;

            isRenumbering = true;
            try
            {
                var collectibles = transform.parent
                    .Cast<Transform>()
                    .Where(child => child.GetComponent<MBCollectible>() != null)
                    .OrderBy(child => child.GetSiblingIndex())
                    .ToList();

                for (var index = 0; index < collectibles.Count; index++)
                {
                    var collectible = collectibles[index];
                    var expectedName = $"Collectible_{index + 1:00}";
                    if (collectible.name != expectedName)
                        collectible.name = expectedName;
                }
            }
            finally
            {
                isRenumbering = false;
            }
        }

        public void Collect()
        {
            if (!MBGameplayStateGuard.IsGameplayActive)
                return;

            SetCollectedState(true);
        }

        public void ResetCollected()
        {
            SetCollectedState(false);
        }

        internal void SetCollectedState(bool isCollected, bool notifyGroup = true)
        {
            if (collected == isCollected)
                return;

            collected = isCollected;

            if (notifyGroup)
            {
                if (collected)
                    group?.NotifyCollected(this);
                else
                    group?.RegisterCollectible(this);
            }

            if (collected)
            {
                Collected?.Invoke();
                onCollected?.Invoke();
            }
            else
            {
                ResetOccurred?.Invoke();
                onReset?.Invoke();
            }
        }

        internal void AssignGroup(MBCollectibleGroup owningGroup)
        {
            group = owningGroup;
        }

        private void CacheGroupReference()
        {
            group = GetComponentInParent<MBCollectibleGroup>();
        }
    }
}

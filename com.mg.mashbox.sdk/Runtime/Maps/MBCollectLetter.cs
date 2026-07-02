using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MashBoxSDK.Maps
{
    public class MBCollectLetter : MonoBehaviour
    {
        public event System.Action Collected;
        public event System.Action ResetOccurred;

        public enum LetterType
        {
            B,
            I,
            K,
            E,
            S
        }

        [SerializeField] private LetterType letter;
        [SerializeField] private Vector3 boxSize = new Vector3(1f, 1f, 1f);
        private readonly Color fillColor = new Color(0.45f, 0.95f, 0.45f, 0.15f);
        private readonly Color wireColor = new Color(0.45f, 0.95f, 0.45f, 0.95f);
        private readonly Color blockedFillColor = new Color(1f, 0.2f, 0.2f, 0.18f);
        private readonly Color blockedWireColor = new Color(1f, 0.2f, 0.2f, 0.95f);
        [SerializeField] private bool collected;

        private MBCollectLettersChallenge challenge;

        public LetterType Letter
        {
            get => letter;
            set
            {
                letter = value;
                SyncName();
                challenge?.RegisterLetter(this);
            }
        }

        public bool IsCollected => collected;
        public bool IsPlacementBlocked => IsInsideGeometry();

        private void Reset()
        {
            CacheChallengeReference();
            SyncName();
        }

        private void Awake()
        {
            CacheChallengeReference();
        }

        private void OnValidate()
        {
            CacheChallengeReference();
            SyncName();
            challenge?.RegisterLetter(this);
        }

        private void OnDrawGizmos()
        {
            var previousMatrix = Gizmos.matrix;
            var previousColor = Gizmos.color;
            var isBlocked = IsInsideGeometry();
            var isSelected = IsSelected();

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = GetFillColor(isBlocked, isSelected);
            Gizmos.DrawCube(Vector3.zero, boxSize);
            Gizmos.color = GetWireColor(isBlocked, isSelected);
            Gizmos.DrawWireCube(Vector3.zero, boxSize);

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
            var worldCenter = transform.TransformPoint(Vector3.zero);
            var halfExtents = Vector3.Scale(boxSize * 0.5f, transform.lossyScale);
            var overlaps = Physics.OverlapBox(worldCenter, halfExtents, transform.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

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

        private void SyncName()
        {
            var expectedName = letter.ToString();
            if (gameObject.name != expectedName)
                gameObject.name = expectedName;
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

            if (!notifyGroup)
                return;

            if (collected)
            {
                Collected?.Invoke();
                challenge?.CollectLetter(this);
            }
            else
            {
                ResetOccurred?.Invoke();
                challenge?.RegisterLetter(this);
            }
        }

        internal void AssignChallenge(MBCollectLettersChallenge owningChallenge)
        {
            challenge = owningChallenge;
        }

        private void CacheChallengeReference()
        {
            challenge = GetComponentInParent<MBCollectLettersChallenge>();
        }
    }
}

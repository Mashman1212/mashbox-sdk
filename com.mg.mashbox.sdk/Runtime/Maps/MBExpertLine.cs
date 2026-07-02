using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MashBoxSDK.Maps
{
    [DisallowMultipleComponent]
    public class MBExpertLine : MonoBehaviour
    {
        public const string SignProxyName = "Sign";

        private static readonly Color PathColor = new Color(0.02f, 0.02f, 0.02f, 0.95f);
        private static readonly Color LabelColor = new Color(0.94f, 0.94f, 0.9f, 1.0f);

        public event System.Action Entered;
        public event System.Action Completed;
        public event System.Action Failed;
        public event System.Action ResetOccurred;

        [SerializeField] private string lineName = "Expert Line";
        [SerializeField, Min(0.1f)] private float timeLimitSeconds = 5.0f;
        [SerializeField, HideInInspector] private bool allowGroundedTouches = true;
        [SerializeField] private bool entered;
        [SerializeField] private bool completed;
        [SerializeField] private bool failed;

        [Header("Events")]
        [SerializeField] private UnityEvent onEntered;
        [SerializeField] private UnityEvent onCompleted;
        [SerializeField] private UnityEvent onFailed;
        [SerializeField] private UnityEvent onReset;

        private MBExpertLineGroup group;

        public string LineName
        {
            get => lineName;
            set
            {
                lineName = string.IsNullOrWhiteSpace(value) ? "Expert Line" : value.Trim();
                SyncGameObjectName();
            }
        }

        public float TimeLimitSeconds
        {
            get => timeLimitSeconds;
            set => timeLimitSeconds = Mathf.Max(0.1f, value);
        }

        public bool AllowGroundedTouches
        {
            get => true;
            set => allowGroundedTouches = true;
        }

        public bool HasEntered => entered;
        public bool IsCompleted => completed;
        public bool HasFailed => failed;
        public Transform SignProxyTransform => FindSignProxyTransform();

        private void Awake()
        {
            CacheGroupReference();
        }

        private void Reset()
        {
            lineName = string.IsNullOrWhiteSpace(lineName) ? gameObject.name : lineName;
            SyncGameObjectName();
        }

        private void OnValidate()
        {
            CacheGroupReference();
            lineName = string.IsNullOrWhiteSpace(lineName) ? "Expert Line" : lineName.Trim();
            timeLimitSeconds = Mathf.Max(0.1f, timeLimitSeconds);
            allowGroundedTouches = true;
            SyncGameObjectName();
            group?.RegisterLine(this);
        }

        private void OnDrawGizmos()
        {
            var gates = GetGateTransforms();
            if (gates.Count < 2)
                return;

            Color previousColor = Gizmos.color;
            Gizmos.color = IsSelected() ? Color.white : PathColor;

            for (int i = 0; i < gates.Count - 1; i++)
                Gizmos.DrawLine(gates[i].position, gates[i + 1].position);

            Gizmos.color = previousColor;

#if UNITY_EDITOR
            if (IsSelected())
            {
                Handles.color = LabelColor;
                Handles.Label(transform.position + Vector3.up * 1.4f, $"{lineName}\n{timeLimitSeconds:0.#}s");
            }
#endif
        }

        public void EnterLine()
        {
            if (!MBGameplayStateGuard.IsGameplayActive)
                return;

            if (entered)
                return;

            entered = true;
            failed = false;
            group?.NotifyEntered(this);
            Entered?.Invoke();
            onEntered?.Invoke();
        }

        public void CompleteLine()
        {
            if (!MBGameplayStateGuard.IsGameplayActive)
                return;

            if (!entered)
                entered = true;

            if (completed)
                return;

            completed = true;
            failed = false;
            group?.NotifyCompleted(this);
            Completed?.Invoke();
            onCompleted?.Invoke();
        }

        public void FailLine()
        {
            if (!MBGameplayStateGuard.IsGameplayActive)
                return;

            if (failed || completed)
                return;

            failed = true;
            group?.NotifyFailed(this);
            Failed?.Invoke();
            onFailed?.Invoke();
        }

        public void ResetLine()
        {
            SetState(false, false, false);
        }

        internal void SetState(bool hasEntered, bool isCompleted, bool hasFailed, bool notifyGroup = true)
        {
            if (entered == hasEntered && completed == isCompleted && failed == hasFailed)
                return;

            entered = hasEntered;
            completed = isCompleted;
            failed = hasFailed;

            if (notifyGroup)
                group?.RegisterLine(this);

            if (!entered && !completed)
            {
                ResetOccurred?.Invoke();
                onReset?.Invoke();
            }
        }

        internal void AssignGroup(MBExpertLineGroup owningGroup)
        {
            group = owningGroup;
        }

        public Transform EnsureSignProxy()
        {
            Transform signProxy = FindSignProxyTransform();
            if (signProxy == null)
            {
                var signObject = new GameObject(SignProxyName);
                signProxy = signObject.transform;
                signProxy.SetParent(transform, false);
                signProxy.localPosition = new Vector3(-1.25f, 0f, 0f);
                signProxy.localRotation = Quaternion.identity;
                signProxy.localScale = Vector3.one;
            }

            if (signProxy.GetComponent<MBExpertLineSignGizmo>() == null)
                signProxy.gameObject.AddComponent<MBExpertLineSignGizmo>();

            return signProxy;
        }

        private void CacheGroupReference()
        {
            group = GetComponentInParent<MBExpertLineGroup>();
        }

        private void SyncGameObjectName()
        {
            if (gameObject.name != lineName)
                gameObject.name = lineName;
        }

        private System.Collections.Generic.List<Transform> GetGateTransforms()
        {
            var gates = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("Gate"))
                    gates.Add(child);
            }

            return gates;
        }

        private Transform FindSignProxyTransform()
        {
            Transform signProxy = transform.Find(SignProxyName);
            if (signProxy != null)
                return signProxy;

            foreach (Transform child in transform)
            {
                if (child.GetComponent<MBExpertLineSignGizmo>() != null)
                    return child;
            }

            return null;
        }

        private bool IsSelected()
        {
#if UNITY_EDITOR
            return Selection.activeTransform == transform;
#else
            return false;
#endif
        }
    }
}

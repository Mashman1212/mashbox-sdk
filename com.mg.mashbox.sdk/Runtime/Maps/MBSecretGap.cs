using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MashBoxSDK.Maps
{
    internal static class MBGameplayStateGuard
    {
        private static System.Type gameLoopServiceType;
        private static System.Reflection.PropertyInfo stateProperty;
        private static bool searched;

        public static bool IsGameplayActive
        {
            get
            {
                if (!TryGetStateProperty(out System.Reflection.PropertyInfo property))
                    return true;

                object state = property.GetValue(null);
                return string.Equals(state?.ToString(), "Gameplay", System.StringComparison.Ordinal);
            }
        }

        private static bool TryGetStateProperty(out System.Reflection.PropertyInfo property)
        {
            if (!searched)
                CacheStateProperty();

            property = stateProperty;
            return property != null;
        }

        private static void CacheStateProperty()
        {
            searched = true;
            gameLoopServiceType = System.Type.GetType("GameLoopService");

            if (gameLoopServiceType == null)
            {
                System.Reflection.Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length && gameLoopServiceType == null; i++)
                    gameLoopServiceType = assemblies[i].GetType("GameLoopService");
            }

            if (gameLoopServiceType == null)
                return;

            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
            stateProperty = gameLoopServiceType.GetProperty("State", flags)
                            ?? gameLoopServiceType.GetProperty("CurrentState", flags);
        }
    }

    [DisallowMultipleComponent]
    public class MBSecretGap : MonoBehaviour
    {
        public event System.Action Entered;
        public event System.Action Completed;
        public event System.Action ResetOccurred;

        [SerializeField] private string gapName = "New Secret Gap";
        private readonly Color pathColor = new Color(1f, 0.8f, 0.2f, 0.9f);
        [SerializeField] private bool entered;
        [SerializeField] private bool completed;

        [Header("Events")]
        [SerializeField] private UnityEvent onEntered;
        [SerializeField] private UnityEvent onCompleted;
        [SerializeField] private UnityEvent onReset;

        private MBSecretGapGroup group;

        public string GapName
        {
            get => gapName;
            set
            {
                gapName = string.IsNullOrWhiteSpace(value) ? "Secret Gap" : value.Trim();
                SyncGameObjectName();
            }
        }

        public bool HasEntered => entered;
        public bool IsCompleted => completed;

        private void Awake()
        {
            CacheGroupReference();
        }

        private void OnValidate()
        {
            CacheGroupReference();
            gapName = string.IsNullOrWhiteSpace(gapName) ? "Secret Gap" : gapName.Trim();
            SyncGameObjectName();
            group?.RegisterGap(this);
        }

        private void Reset()
        {
            gapName = string.IsNullOrWhiteSpace(gapName) ? gameObject.name : gapName;
            SyncGameObjectName();
        }

        private void SyncGameObjectName()
        {
            if (gameObject.name != gapName)
                gameObject.name = gapName;
        }

        private void OnDrawGizmos()
        {
            if (!MBGameplayGizmoVisibility.Visible)
                return;

            var gates = GetGateTransforms();
            if (gates.Count < 2)
                return;

            var previousColor = Gizmos.color;
            Gizmos.color = IsSelected() ? GetSelectedPathColor() : pathColor;

            for (var index = 0; index < gates.Count - 1; index++)
            {
                var fromGate = gates[index];
                var toGate = gates[index + 1];
                Gizmos.DrawLine(fromGate.position, toGate.position);
            }

            Gizmos.color = previousColor;
        }

        private Color GetSelectedPathColor()
        {
            return new Color(
                Mathf.Min(pathColor.r + 0.12f, 1f),
                Mathf.Min(pathColor.g + 0.12f, 1f),
                Mathf.Min(pathColor.b + 0.12f, 1f),
                pathColor.a);
        }

        private bool IsSelected()
        {
#if UNITY_EDITOR
            return Selection.activeTransform == transform;
#else
            return false;
#endif
        }

        private List<Transform> GetGateTransforms()
        {
            var gates = new List<Transform>();
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("Gate"))
                    gates.Add(child);
            }

            return gates;
        }

        public void EnterGap()
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

        public void CompleteGap()
        {
            if (!MBGameplayStateGuard.IsGameplayActive)
                return;

            if (!entered)
                entered = true;

            if (completed)
                return;

            completed = true;
            group?.NotifyCompleted(this);
            Completed?.Invoke();
            onCompleted?.Invoke();
        }

        public void ResetGap()
        {
            SetState(false, false);
        }

        internal void SetState(bool hasEntered, bool isCompleted, bool notifyGroup = true)
        {
            if (entered == hasEntered && completed == isCompleted)
                return;

            entered = hasEntered;
            completed = isCompleted;

            if (notifyGroup)
                group?.RegisterGap(this);

            if (!entered && !completed)
            {
                ResetOccurred?.Invoke();
                onReset?.Invoke();
            }
        }

        internal void AssignGroup(MBSecretGapGroup owningGroup)
        {
            group = owningGroup;
        }

        private void CacheGroupReference()
        {
            group = GetComponentInParent<MBSecretGapGroup>();
        }
    }
}

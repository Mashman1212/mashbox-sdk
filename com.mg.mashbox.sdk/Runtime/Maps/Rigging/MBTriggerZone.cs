using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps.Rigging
{
    [AddComponentMenu("MashBox/Maps/Rigging/Trigger Zone")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class MBTriggerZone : MonoBehaviour
    {
        [Header("Filtering")]
        [Tooltip("Only objects on these layers can fire the trigger zone events.")]
        [SerializeField] private LayerMask layerMask = ~0;
        [Tooltip("Optional tag filter. Leave empty to accept any tag.")]
        [SerializeField] private string requiredTag;
        [Tooltip("Only fire the entered event once until this object is disabled and re-enabled.")]
        [SerializeField] private bool oneShot;

        [Header("Events")]
        [SerializeField] private UnityEvent onEntered;
        [SerializeField] private UnityEvent onExited;
        [SerializeField] private UnityEvent onFirstEntered;
        [SerializeField] private UnityEvent onLastExited;

        private readonly HashSet<Collider> occupants = new HashSet<Collider>();
        private bool hasTriggered;

        private void Reset()
        {
            if (TryGetComponent(out Collider collider))
                collider.isTrigger = true;
        }

        private void OnDisable()
        {
            occupants.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!ShouldAccept(other))
                return;

            if (!occupants.Add(other))
                return;

            if (occupants.Count == 1)
                onFirstEntered?.Invoke();

            if (oneShot && hasTriggered)
                return;

            hasTriggered = true;
            onEntered?.Invoke();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!occupants.Remove(other))
                return;

            onExited?.Invoke();

            if (occupants.Count == 0)
                onLastExited?.Invoke();
        }

        private bool ShouldAccept(Collider other)
        {
            if ((layerMask.value & (1 << other.gameObject.layer)) == 0)
                return false;

            if (!string.IsNullOrWhiteSpace(requiredTag) && !other.CompareTag(requiredTag))
                return false;

            return true;
        }
    }
}

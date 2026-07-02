using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps.Rigging
{
    [AddComponentMenu("MashBox/Maps/Rigging/Collision Events")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class MBCollisionEvents : MonoBehaviour
    {
        [Header("Filtering")]
        [Tooltip("Only objects on these layers can fire the collision events.")]
        [SerializeField] private LayerMask layerMask = ~0;
        [Tooltip("Optional tag filter. Leave empty to accept any tag.")]
        [SerializeField] private string requiredTag;
        [Tooltip("Only fire the collision entered event once until this object is disabled and re-enabled.")]
        [SerializeField] private bool oneShot;

        [Header("Events")]
        [SerializeField] private UnityEvent onCollisionEntered;
        [SerializeField] private UnityEvent onCollisionExited;
        [SerializeField] private UnityEvent onFirstCollisionEntered;
        [SerializeField] private UnityEvent onLastCollisionExited;

        private readonly HashSet<Collider> collidersInContact = new HashSet<Collider>();
        private bool hasTriggered;

        private void Reset()
        {
            if (TryGetComponent(out Collider collider))
                collider.isTrigger = false;
        }

        private void OnDisable()
        {
            collidersInContact.Clear();
        }

        private void OnCollisionEnter(Collision collision)
        {
            var other = collision.collider;
            if (!ShouldAccept(other))
                return;

            if (!collidersInContact.Add(other))
                return;

            if (collidersInContact.Count == 1)
                onFirstCollisionEntered?.Invoke();

            if (oneShot && hasTriggered)
                return;

            hasTriggered = true;
            onCollisionEntered?.Invoke();
        }

        private void OnCollisionExit(Collision collision)
        {
            var other = collision.collider;
            if (!collidersInContact.Remove(other))
                return;

            onCollisionExited?.Invoke();

            if (collidersInContact.Count == 0)
                onLastCollisionExited?.Invoke();
        }

        private bool ShouldAccept(Collider other)
        {
            if (other == null)
                return false;

            if ((layerMask.value & (1 << other.gameObject.layer)) == 0)
                return false;

            if (!string.IsNullOrWhiteSpace(requiredTag) && !other.CompareTag(requiredTag))
                return false;

            return true;
        }
    }
}

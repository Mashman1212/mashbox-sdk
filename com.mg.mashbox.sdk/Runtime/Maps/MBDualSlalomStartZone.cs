using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class MBDualSlalomStartZone : MonoBehaviour
    {
        private const string InteractionLayerName = "Interactable";

        [SerializeField, Min(0.25f)] private float radius = 1.75f;
        [SerializeField] private Vector3 center = new Vector3(0f, 1f, 0f);
        [SerializeField] private SphereCollider triggerCollider;
        [Header("Events")]
        [SerializeField] private UnityEvent onOccupancyChanged;

        private readonly HashSet<Collider> occupants = new HashSet<Collider>();

        public float Radius => radius;
        public Vector3 Center => center;
        public SphereCollider TriggerCollider => triggerCollider;
        public bool IsOccupied
        {
            get
            {
                RemoveMissingOccupants();
                return occupants.Count > 0;
            }
        }

        public int OccupantColliderCount
        {
            get
            {
                RemoveMissingOccupants();
                return occupants.Count;
            }
        }

        public event Action OccupancyChanged;

        public void SetRadius(float value)
        {
            radius = Mathf.Max(0.25f, value);
            SyncTriggerCollider();
        }

        private void Reset()
        {
            EnsureInteractionLayer();
            EnsureTriggerCollider(createIfMissing: true);
            SyncTriggerCollider();
        }

        private void Awake()
        {
            EnsureInteractionLayer();
            EnsureTriggerCollider(createIfMissing: true);
            SyncTriggerCollider();
        }

        private void OnEnable()
        {
            EnsureInteractionLayer();
            EnsureTriggerCollider(createIfMissing: Application.isPlaying);
            SyncTriggerCollider();
        }

        private void OnDisable()
        {
            if (occupants.Count == 0)
                return;

            occupants.Clear();
            NotifyOccupancyChanged();
        }

        private void OnValidate()
        {
            radius = Mathf.Max(0.25f, radius);
            EnsureInteractionLayer();
            EnsureTriggerCollider(createIfMissing: false);
            SyncTriggerCollider();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsCandidateCollider(other) || !occupants.Add(other))
                return;

            NotifyOccupancyChanged();
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null || !occupants.Remove(other))
                return;

            NotifyOccupancyChanged();
        }

        public bool Contains(Transform participantRoot)
        {
            if (participantRoot == null)
                return false;

            RemoveMissingOccupants();
            foreach (Collider occupant in occupants)
            {
                if (occupant == null)
                    continue;

                Transform occupantTransform = occupant.transform;
                if (occupantTransform == participantRoot || occupantTransform.IsChildOf(participantRoot) ||
                    participantRoot.IsChildOf(occupantTransform))
                {
                    return true;
                }

                Rigidbody body = occupant.attachedRigidbody;
                if (body != null && (body.transform == participantRoot || body.transform.IsChildOf(participantRoot) ||
                                     participantRoot.IsChildOf(body.transform)))
                {
                    return true;
                }
            }

            return false;
        }

        public bool ContainsComponentInParent<T>(T component) where T : Component
        {
            if (component == null)
                return false;

            RemoveMissingOccupants();
            foreach (Collider occupant in occupants)
            {
                if (occupant != null && occupant.GetComponentInParent<T>() == component)
                    return true;
            }

            return false;
        }

        public void CopyOccupants(List<Collider> destination)
        {
            if (destination == null)
                return;

            RemoveMissingOccupants();
            destination.Clear();
            destination.AddRange(occupants);
        }

        private void EnsureTriggerCollider(bool createIfMissing)
        {
            if (triggerCollider == null)
                triggerCollider = GetComponent<SphereCollider>();
            if (triggerCollider == null && createIfMissing)
                triggerCollider = gameObject.AddComponent<SphereCollider>();
        }

        private void SyncTriggerCollider()
        {
            if (triggerCollider == null)
                return;

            triggerCollider.isTrigger = true;
            triggerCollider.radius = radius;
            triggerCollider.center = center;
        }

        private void EnsureInteractionLayer()
        {
            int interactionLayer = LayerMask.NameToLayer(InteractionLayerName);
            if (interactionLayer >= 0 && gameObject.layer != interactionLayer)
                gameObject.layer = interactionLayer;
        }

        private void RemoveMissingOccupants()
        {
            occupants.RemoveWhere(occupant =>
                occupant == null || !occupant.enabled || !occupant.gameObject.activeInHierarchy);
        }

        private void NotifyOccupancyChanged()
        {
            OccupancyChanged?.Invoke();
            onOccupancyChanged?.Invoke();
        }

        private bool IsCandidateCollider(Collider other)
        {
            if (other == null || other == triggerCollider || !other.enabled ||
                !other.gameObject.activeInHierarchy)
                return false;

            Transform otherTransform = other.transform;
            return otherTransform != transform && !otherTransform.IsChildOf(transform);
        }

        private void OnDrawGizmos()
        {
            if (!MBGameplayGizmoVisibility.Visible)
                return;

            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.16f);
            Gizmos.DrawSphere(center, radius);
            Gizmos.color = new Color(0.2f, 0.95f, 1f, 0.95f);
            Gizmos.DrawWireSphere(center, radius);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}

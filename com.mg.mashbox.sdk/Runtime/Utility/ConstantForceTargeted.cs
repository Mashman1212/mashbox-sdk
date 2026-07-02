using System;
using UnityEngine;

namespace MashBoxSDK.Utility
{
    [AddComponentMenu("MashBox/Utility/Constant Force Targeted")]
    public class ConstantForceTargeted : MonoBehaviour
    {
        [Header("Target Lookup")]
        [Tooltip("Optional. If set, searches UP the hierarchy for this name.")]
        public string parentName;

        [Tooltip("Optional. If empty, the found parent/root is used directly.")]
        public string childName;

        [Header("Force")]
        public float force = 0.0f;
        public Vector3 direction = Vector3.zero;
        
        public float relativeForce = 0.0f;
        public Vector3 relativeDirection = Vector3.zero;
        
        public ForceMode forceMode = ForceMode.Force;

        [Header("Options")]
        public bool disableIfNotFound = true;

        private Rigidbody _target;

        void OnEnable()
        {
            ResolveTarget();
        }

        void ResolveTarget()
        {
            _target = null;

            // Start from this object
            Transform root = transform;

            // ── Seek UPWARD recursively for parent
            if (!string.IsNullOrEmpty(parentName))
            {
                root = FindParentUpward(transform, parentName);
                if (!root)
                {
                    Fail($"Parent not found upward: {parentName}");
                    return;
                }
            }

            Transform targetTransform = root;

            // ── Seek DOWNWARD recursively for child
            if (!string.IsNullOrEmpty(childName))
            {
                targetTransform = FindDeepChild(root, childName);
                if (!targetTransform)
                {
                    Fail($"Child not found (recursive): {childName}");
                    return;
                }
            }

            _target = targetTransform.GetComponent<Rigidbody>();
            if (!_target)
            {
                Fail("No Rigidbody on target");
            }
        }

        void FixedUpdate()
        {
            if (_target == null)
                return;

            _target.AddForce(direction * force, forceMode);
            _target.AddRelativeForce(relativeDirection * relativeForce, forceMode);
        }

        void Fail(string reason)
        {
            Debug.LogWarning($"[ConstantForceTargeted] {reason}", this);
            if (disableIfNotFound)
                enabled = false;
        }

        // ─────────────────────────────────────────────
        // Hierarchy helpers
        // ─────────────────────────────────────────────

        static Transform FindParentUpward(Transform start, string name)
        {
            var current = start;

            while (current != null)
            {
                if (current.name == name)
                    return current;

                current = current.parent;
            }

            return null;
        }

        static Transform FindDeepChild(Transform parent, string name)
        {
            if (parent.name == name)
                return parent;

            foreach (Transform child in parent)
            {
                var result = FindDeepChild(child, name);
                if (result)
                    return result;
            }

            return null;
        }

        private void OnValidate()
        {
            direction = direction.normalized;
        }
    }
}

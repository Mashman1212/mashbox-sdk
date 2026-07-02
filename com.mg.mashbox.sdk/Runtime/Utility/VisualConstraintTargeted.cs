using System.Collections;
using UnityEngine;
using UnityEngine.Animations;

namespace MashBoxSDK.Utility
{
    [AddComponentMenu("MashBox/Utility/Visual Constraint Targeted")]
    [RequireComponent(typeof(Transform))]
    public class VisualConstraintTargeted : MonoBehaviour
    {
        public enum ConstraintMode
        {
            PositionOnly,
            RotationOnly,
            PositionAndRotation,
            Custom
        }

        [Header("Target Lookup")]

        [Tooltip("Search UP the hierarchy for a parent containing this string.")]
        public string parentNameContains;

        [Tooltip("Search DOWN recursively from found parent for child containing this string.")]
        public string childNameContains;

        [Header("Constraint")]

        public ConstraintMode constraintMode = ConstraintMode.PositionAndRotation;

        [Tooltip("Maintain current offset when constraint activates.")]
        public bool maintainOffset = true;

        [Header("Custom Axis (Only used in Custom mode)")]

        public bool constrainPositionX = true;
        public bool constrainPositionY = true;
        public bool constrainPositionZ = true;

        public bool constrainRotationX = true;
        public bool constrainRotationY = true;
        public bool constrainRotationZ = true;

        [Header("Options")]
        public bool disableIfNotFound = true;

        private ParentConstraint _constraint;

        IEnumerator Start()
        {
            yield return new WaitForEndOfFrame();
            ResolveAndApply();
        }

        void ResolveAndApply()
        {
            Transform root = null;

            // ── Seek upward by contains
            if (!string.IsNullOrEmpty(parentNameContains))
            {
                root = FindParentUpwardContains(transform, parentNameContains);
                if (!root)
                {
                    Fail($"Parent containing '{parentNameContains}' not found.");
                    return;
                }
            }

            Transform target = root;

            // ── Seek deep child by contains
            if (!string.IsNullOrEmpty(childNameContains))
            {
                target = FindDeepChildContains(root, childNameContains);
                if (!target)
                {
                    Fail($"Child containing '{childNameContains}' not found.");
                    return;
                }
            }

            if(target)
                SetupConstraint(target);
        }

        void SetupConstraint(Transform target)
        {
            if (!_constraint)
                _constraint = GetComponent<ParentConstraint>();

            if (!_constraint)
                _constraint = gameObject.AddComponent<ParentConstraint>();

            _constraint.constraintActive = false;
            _constraint.locked = false;
            

            ConstraintSource source = new ConstraintSource
            {
                sourceTransform = target,
                weight = 1f
            };

            _constraint.constraintActive = false;

            _constraint.SetSources(new System.Collections.Generic.List<ConstraintSource>());
            _constraint.AddSource(source);

            ApplyAxisSettings();

            if (maintainOffset)
            {
                // Compute proper quaternion delta
                Quaternion delta = Quaternion.Inverse(target.rotation) * transform.rotation;
                Vector3 rotationOffset = delta.eulerAngles;

                Vector3 positionOffset = transform.position - target.position;

                _constraint.SetTranslationOffset(0, positionOffset);
                _constraint.SetRotationOffset(0, rotationOffset);
            }
            else
            {
                _constraint.SetTranslationOffset(0, Vector3.zero);
                _constraint.SetRotationOffset(0, Vector3.zero);
            }

            _constraint.constraintActive = true;
            _constraint.locked = true;

        }

        void ApplyAxisSettings()
        {
            switch (constraintMode)
            {
                case ConstraintMode.PositionOnly:
                    SetPositionAxes(true, true, true);
                    SetRotationAxes(false, false, false);
                    break;

                case ConstraintMode.RotationOnly:
                    SetPositionAxes(false, false, false);
                    SetRotationAxes(true, true, true);
                    break;

                case ConstraintMode.PositionAndRotation:
                    SetPositionAxes(true, true, true);
                    SetRotationAxes(true, true, true);
                    break;

                case ConstraintMode.Custom:
                    SetPositionAxes(constrainPositionX, constrainPositionY, constrainPositionZ);
                    SetRotationAxes(constrainRotationX, constrainRotationY, constrainRotationZ);
                    break;
            }
        }

        void SetPositionAxes(bool x, bool y, bool z)
        {
            _constraint.translationAxis =
                (x ? Axis.X : 0) |
                (y ? Axis.Y : 0) |
                (z ? Axis.Z : 0);
        }

        void SetRotationAxes(bool x, bool y, bool z)
        {
            _constraint.rotationAxis =
                (x ? Axis.X : 0) |
                (y ? Axis.Y : 0) |
                (z ? Axis.Z : 0);
        }

        void Fail(string reason)
        {
            Debug.LogWarning($"[VisualConstraintTargeted] {reason}", this);
            if (disableIfNotFound)
                enabled = false;
        }

        // ────────────────────────────────
        // Hierarchy Helpers (Contains)
        // ────────────────────────────────

        static Transform FindParentUpwardContains(Transform start, string contains)
        {
            Transform current = start;

            while (current != null)
            {
                if (current.name.Contains(contains))
                    return current;

                current = current.parent;
            }

            return null;
        }

        static Transform FindDeepChildContains(Transform parent, string contains)
        {
            if (parent.name.Contains(contains))
                return parent;

            foreach (Transform child in parent)
            {
                Transform result = FindDeepChildContains(child, contains);
                if (result)
                    return result;
            }

            return null;
        }
    }
}

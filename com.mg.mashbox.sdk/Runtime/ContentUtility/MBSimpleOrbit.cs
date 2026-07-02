using UnityEngine;

namespace MashBoxSDK.ContentUtility
{
    [AddComponentMenu("MashBox/Content Utility/Simple Orbit")]
    public class MBSimpleOrbit : MonoBehaviour
    {
        [SerializeField] private Transform pivot;
        [SerializeField] private Vector3 localPivotOffset = Vector3.zero;
        [SerializeField] private Vector3 orbitAxis = Vector3.up;
        [SerializeField] private float degreesPerSecond = 45f;
        [SerializeField] private bool useLocalSpace = true;
        [SerializeField] private bool lookAtPivot;
        [SerializeField] private bool useUnscaledTime;

        private void Update()
        {
            var deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            var pivotPosition = GetPivotPosition();
            var axis = GetOrbitAxis();
            if (axis.sqrMagnitude <= 0.0001f)
                axis = Vector3.up;

            transform.RotateAround(pivotPosition, axis.normalized, degreesPerSecond * deltaTime);

            if (lookAtPivot)
                transform.LookAt(pivotPosition, Vector3.up);
        }

        private Vector3 GetPivotPosition()
        {
            if (pivot != null)
                return pivot.TransformPoint(localPivotOffset);

            if (transform.parent != null && useLocalSpace)
                return transform.parent.TransformPoint(localPivotOffset);

            return localPivotOffset;
        }

        private Vector3 GetOrbitAxis()
        {
            if (pivot != null && useLocalSpace)
                return pivot.TransformDirection(orbitAxis);

            if (transform.parent != null && useLocalSpace)
                return transform.parent.TransformDirection(orbitAxis);

            return orbitAxis;
        }

        private void OnValidate()
        {
            if (orbitAxis.sqrMagnitude <= 0.0001f)
                orbitAxis = Vector3.up;
        }
    }
}

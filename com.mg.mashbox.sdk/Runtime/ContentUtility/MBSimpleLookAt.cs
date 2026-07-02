using UnityEngine;

namespace MashBoxSDK.ContentUtility
{
    [AddComponentMenu("MashBox/Content Utility/Simple Look At")]
    public class MBSimpleLookAt : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private bool lookAtMainCamera = true;
        [SerializeField] private Vector3 localForward = Vector3.forward;
        [SerializeField] private Vector3 worldUp = Vector3.up;
        [SerializeField] private bool lockX;
        [SerializeField] private bool lockY;
        [SerializeField] private bool lockZ;

        private void LateUpdate()
        {
            var resolvedTarget = GetResolvedTarget();
            if (resolvedTarget == null)
                return;

            var direction = resolvedTarget.position - transform.position;
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            var targetRotation = Quaternion.LookRotation(direction.normalized, worldUp);
            if (localForward != Vector3.forward)
                targetRotation *= Quaternion.FromToRotation(Vector3.forward, localForward.normalized);

            var euler = targetRotation.eulerAngles;
            var currentEuler = transform.rotation.eulerAngles;
            if (lockX) euler.x = currentEuler.x;
            if (lockY) euler.y = currentEuler.y;
            if (lockZ) euler.z = currentEuler.z;

            transform.rotation = Quaternion.Euler(euler);
        }

        private Transform GetResolvedTarget()
        {
            if (target != null)
                return target;

            if (lookAtMainCamera && Camera.main != null)
                return Camera.main.transform;

            return null;
        }

        private void OnValidate()
        {
            if (localForward.sqrMagnitude <= 0.0001f)
                localForward = Vector3.forward;

            if (worldUp.sqrMagnitude <= 0.0001f)
                worldUp = Vector3.up;
        }
    }
}

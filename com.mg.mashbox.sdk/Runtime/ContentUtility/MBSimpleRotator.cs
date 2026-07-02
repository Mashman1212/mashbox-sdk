using UnityEngine;

namespace MashBoxSDK.ContentUtility
{
    [AddComponentMenu("MashBox/Content Utility/Simple Rotator")]
    public class MBSimpleRotator : MonoBehaviour
    {
        [SerializeField] private Vector3 axis = Vector3.up;
        [SerializeField] private float degreesPerSecond = 90f;
        [SerializeField] private Space rotationSpace = Space.Self;
        [SerializeField] private bool useUnscaledTime;

        private void Update()
        {
            var normalizedAxis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.up;
            var deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            transform.Rotate(normalizedAxis, degreesPerSecond * deltaTime, rotationSpace);
        }

        private void OnValidate()
        {
            if (axis.sqrMagnitude <= 0.0001f)
                axis = Vector3.up;
        }
    }
}

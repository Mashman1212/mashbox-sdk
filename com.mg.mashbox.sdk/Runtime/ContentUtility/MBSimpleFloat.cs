using UnityEngine;

namespace MashBoxSDK.ContentUtility
{
    [AddComponentMenu("MashBox/Content Utility/Simple Float")]
    public class MBSimpleFloat : MonoBehaviour
    {
        [SerializeField] private Vector3 localAxis = Vector3.up;
        [SerializeField] private float amplitude = 0.1f;
        [SerializeField] private float frequency = 1f;
        [SerializeField] private float phaseOffset;
        [SerializeField] private bool useUnscaledTime;

        private Vector3 initialLocalPosition;

        private void Awake()
        {
            CacheInitialState();
        }

        private void OnEnable()
        {
            CacheInitialState();
        }

        private void Update()
        {
            var normalizedAxis = localAxis.sqrMagnitude > 0.0001f ? localAxis.normalized : Vector3.up;
            var time = useUnscaledTime ? Time.unscaledTime : Time.time;
            var offset = Mathf.Sin((time * frequency * Mathf.PI * 2f) + phaseOffset) * amplitude;
            transform.localPosition = initialLocalPosition + (normalizedAxis * offset);
        }

        private void CacheInitialState()
        {
            initialLocalPosition = transform.localPosition;
        }

        private void OnValidate()
        {
            amplitude = Mathf.Max(0f, amplitude);
            frequency = Mathf.Max(0f, frequency);
            if (localAxis.sqrMagnitude <= 0.0001f)
                localAxis = Vector3.up;
        }
    }
}

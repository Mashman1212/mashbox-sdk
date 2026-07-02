using UnityEngine;

namespace MashBoxSDK.ContentUtility
{
    [AddComponentMenu("MashBox/Content Utility/Simple Pulse Scale")]
    public class MBSimplePulseScale : MonoBehaviour
    {
        [SerializeField] private Vector3 pulseAxis = Vector3.one;
        [SerializeField] private float amplitude = 0.08f;
        [SerializeField] private float frequency = 1f;
        [SerializeField] private float phaseOffset;
        [SerializeField] private bool useUnscaledTime;

        private Vector3 initialLocalScale;

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
            var time = useUnscaledTime ? Time.unscaledTime : Time.time;
            var pulse = Mathf.Sin((time * frequency * Mathf.PI * 2f) + phaseOffset) * amplitude;
            transform.localScale = initialLocalScale + Vector3.Scale(pulseAxis, Vector3.one * pulse);
        }

        private void CacheInitialState()
        {
            initialLocalScale = transform.localScale;
        }

        private void OnValidate()
        {
            amplitude = Mathf.Max(0f, amplitude);
            frequency = Mathf.Max(0f, frequency);
            if (pulseAxis.sqrMagnitude <= 0.0001f)
                pulseAxis = Vector3.one;
        }
    }
}

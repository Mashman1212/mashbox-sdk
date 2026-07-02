using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.ContentUtility
{
    [AddComponentMenu("MashBox/Content Utility/Simple Visibility Toggle")]
    public class MBSimpleVisibilityToggle : MonoBehaviour
    {
        [SerializeField] private List<Renderer> targetRenderers = new List<Renderer>();
        [SerializeField] private float visibleDuration = 1f;
        [SerializeField] private float hiddenDuration = 1f;
        [SerializeField] private bool startVisible = true;
        [SerializeField] private bool useUnscaledTime;

        private bool isVisible;
        private float stateTimer;

        private void Awake()
        {
            CacheDefaultRenderer();
            ApplyState(startVisible, true);
        }

        private void OnEnable()
        {
            CacheDefaultRenderer();
            ApplyState(startVisible, true);
        }

        private void Update()
        {
            var deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            stateTimer += deltaTime;

            var duration = isVisible ? Mathf.Max(0.01f, visibleDuration) : Mathf.Max(0.01f, hiddenDuration);
            if (stateTimer < duration)
                return;

            ApplyState(!isVisible, false);
        }

        private void ApplyState(bool visible, bool resetTimer)
        {
            isVisible = visible;
            if (resetTimer)
                stateTimer = 0f;
            else
                stateTimer = 0f;

            for (var index = 0; index < targetRenderers.Count; index++)
            {
                var renderer = targetRenderers[index];
                if (renderer != null)
                    renderer.enabled = visible;
            }
        }

        private void CacheDefaultRenderer()
        {
            if (targetRenderers.Count > 0)
                return;

            var renderer = GetComponent<Renderer>();
            if (renderer != null)
                targetRenderers.Add(renderer);
        }

        private void OnValidate()
        {
            visibleDuration = Mathf.Max(0.01f, visibleDuration);
            hiddenDuration = Mathf.Max(0.01f, hiddenDuration);
        }
    }
}

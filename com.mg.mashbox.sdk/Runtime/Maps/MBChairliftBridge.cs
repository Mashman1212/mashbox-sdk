#if UNITY_6000_0_OR_NEWER
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    public class MBChairliftBridge : MonoBehaviour
    {
        [SerializeField] private MBChairlift chairlift;
        [SerializeField] private UnityEvent onBound;
        public MBChairlift Chairlift => chairlift;
        public void Bind(MBChairlift target)
        {
            Unsubscribe();
            chairlift = target;
            if (isActiveAndEnabled) Subscribe();
            ApplySettings();
            onBound?.Invoke();
        }
        protected virtual void OnEnable()
        {
            if (chairlift == null) chairlift = GetComponentInParent<MBChairlift>();
            Subscribe();
            ApplySettings();
        }
        protected virtual void OnDisable() => Unsubscribe();
        private void Subscribe()
        {
            if (chairlift == null) return;
            chairlift.SettingsChanged -= ApplySettings;
            chairlift.SettingsChanged += ApplySettings;
        }
        private void Unsubscribe() { if (chairlift != null) chairlift.SettingsChanged -= ApplySettings; }
        protected virtual void ApplySettings() { }
        public void Stop() => chairlift?.Stop();
        public void Resume() => chairlift?.Resume();
        public void SetForward() => chairlift?.SetForward();
        public void SetReverse() => chairlift?.SetReverse();
        public void SetSpeed(float value) => chairlift?.SetSpeed(value);
    }
}
#endif

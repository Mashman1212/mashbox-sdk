using UnityEngine;
using MashBoxSDK.Utility;

namespace MashBoxSDK.Maps.Audio
{
    [AddComponentMenu("MashBox/Maps/Audio/Simple FMOD Event Audio")]
    [DisallowMultipleComponent]
    public class MBSimpleFmodEventAudio : MonoBehaviour
    {
        [Tooltip("FMOD event path to create when StartAudioEvent is called.")]
        [SerializeField] private string eventPath = string.Empty;

        [Tooltip("Optional volume multiplier applied to the FMOD event instance.")]
        [Range(0.0f, 10.0f)]
        [SerializeField] private float volume = 1.0f;

        [Tooltip("Start the FMOD event automatically when this component enables.")]
        [SerializeField] private bool startOnEnable;

        [Tooltip("Stop and release the FMOD event when this component disables.")]
        [SerializeField] private bool stopOnDisable = true;

        [Tooltip("Allow the FMOD event to fade out when StopAudioEvent is called.")]
        [SerializeField] private bool allowFadeOutOnStop = true;

        [Tooltip("Keep the FMOD event positioned on this object while it is playing.")]
        [SerializeField] private bool updatePositionWhilePlaying = true;

        private Rigidbody attachedBody;

#if MGFMOD
        private object eventInstance;
#endif

        public bool IsPlaying { get; private set; }

        private void Awake()
        {
            attachedBody = GetComponentInParent<Rigidbody>();
        }

        private void OnEnable()
        {
            attachedBody = GetComponentInParent<Rigidbody>();

            if (startOnEnable)
                StartAudioEvent();
        }

        private void Update()
        {
#if MGFMOD
            if (IsPlaying && updatePositionWhilePlaying)
                Update3DAttributes();
#endif
        }

        public void StartAudioEvent()
        {
            if (IsPlaying || string.IsNullOrWhiteSpace(eventPath))
                return;

#if MGFMOD
            if (!CreateEventInstance())
                return;

            try
            {
                Update3DAttributes();
                MBFmodReflection.SetVolume(eventInstance, volume);
                MBFmodReflection.Start(eventInstance);
                IsPlaying = true;
            }
            catch
            {
                ReleaseEventInstance(immediate: true);
            }
#endif
        }

        public void StopAudioEvent()
        {
#if MGFMOD
            ReleaseEventInstance(immediate: !allowFadeOutOnStop);
#else
            IsPlaying = false;
#endif
        }

        public void StopAudioEventImmediate()
        {
#if MGFMOD
            ReleaseEventInstance(immediate: true);
#else
            IsPlaying = false;
#endif
        }

        public void RestartAudioEvent()
        {
            StopAudioEventImmediate();
            StartAudioEvent();
        }

        private void OnDisable()
        {
            if (stopOnDisable)
                StopAudioEventImmediate();
        }

        private void OnDestroy()
        {
            StopAudioEventImmediate();
        }

#if MGFMOD
        private bool CreateEventInstance()
        {
            if (eventInstance != null)
                return true;

            eventInstance = MBFmodReflection.CreateInstance(eventPath);
            return eventInstance != null;
        }

        private void Update3DAttributes()
        {
            if (eventInstance == null)
                return;

            MBFmodReflection.Set3DAttributes(eventInstance, gameObject, attachedBody);
        }

        private void ReleaseEventInstance(bool immediate)
        {
            if (eventInstance == null)
            {
                IsPlaying = false;
                return;
            }

            try
            {
                MBFmodReflection.Stop(eventInstance, immediate);
                MBFmodReflection.Release(eventInstance);
            }
            catch
            {
                // Optional FMOD integration only.
            }
            finally
            {
                eventInstance = null;
                IsPlaying = false;
            }
        }
#endif
    }
}

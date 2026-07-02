using UnityEngine;
using MashBoxSDK.Services;
using MashBoxSDK.Utility;

namespace MashBoxSDK.Maps.Audio
{
    [AddComponentMenu("MashBox/Maps/Audio/Simple One Shot Audio")]
    [DisallowMultipleComponent]
    public class MBSimpleOneShotAudio : MonoBehaviour
    {
        [Tooltip("Audio event path to fire when PlayOneShot is called.")]
        [SerializeField] private string eventPath = string.Empty;

        [Tooltip("Optional volume multiplier used by the SDK audio service or FMOD fallback.")]
        [Range(0.0f, 10.0f)]
        [SerializeField] private float volume = 1.0f;
        
        [SerializeField] bool _fireOnlyOnce;
        private bool _fired;
        
        [ContextMenu("PlayOneShot")]
        public void PlayOneShot()
        {
            if (enabled == false)
                return;
            
            if (string.IsNullOrWhiteSpace(eventPath))
                return;

            if (_fireOnlyOnce && _fired)
            {
                return;
            }
            
            _fired = true;
            
            if (AudioService.Service != null)
            {
                AudioService.PlayOneShot(eventPath, gameObject, 0.0f, 0.0f, 0.0f, volume);
                return;
            }

#if MGFMOD
            var eventInstance = MBFmodReflection.CreateInstance(eventPath);
            if (eventInstance == null)
                return;

            MBFmodReflection.Set3DAttributes(eventInstance, gameObject, GetComponent<Rigidbody>());
            MBFmodReflection.SetVolume(eventInstance, volume);
            MBFmodReflection.Start(eventInstance);
            MBFmodReflection.Release(eventInstance);
#endif
        }
        
        [ContextMenu("PlayOneShot2D")]
        public void PlayOneShot2D()
        {
            if (enabled == false)
                return;
            
            if (string.IsNullOrWhiteSpace(eventPath))
                return;
          
            if (_fireOnlyOnce && _fired)
            {
                return;
            }
            
            _fired = true;

            
#if MGFMOD
            var eventInstance = MBFmodReflection.CreateInstance(eventPath);
            if (eventInstance == null)
                return;

            MBFmodReflection.Set3DAttributes(eventInstance, gameObject, GetComponent<Rigidbody>());
            MBFmodReflection.SetVolume(eventInstance, volume);
            MBFmodReflection.Start(eventInstance);
            MBFmodReflection.Release(eventInstance);
#endif
        }
    }
}

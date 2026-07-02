using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using MashBoxSDK.Services;
using MashBoxSDK.Utility;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    public class ObjectLauncher : MonoBehaviour
    {
        [SerializeField] private GameObject objectToSpawn;
        [SerializeField] private float launchForce = 10f;
        [SerializeField] private string fireEventPath = "event:/Gameplay/Gravity Whip/Gravity Whip Fire";
        [Range(0.0f, 1.0f)]
        [SerializeField] private float fireVolume = 0.03f;

        public UnityEvent OnFire;

        private void Update()
        {
#if !ENABLE_INPUT_SYSTEM
            return;
#else
            if (Gamepad.current == null || !Gamepad.current.rightTrigger.wasPressedThisFrame)
                return;

            SpawnAndLaunch();
#endif
        }

        private void SpawnAndLaunch()
        {
            if (objectToSpawn == null)
                return;

            var muzzlePoint = transform.position + (transform.forward * 1f) + (transform.up * 0.1f);
            var spawnedObject = Instantiate(objectToSpawn, muzzlePoint, Quaternion.identity);
            var body = spawnedObject.GetComponent<Rigidbody>();
            if (body != null)
                body.AddForce(transform.forward * launchForce, ForceMode.VelocityChange);

            PlayFireAudio();
            OnFire?.Invoke();
        }

        private void PlayFireAudio()
        {
            if (string.IsNullOrWhiteSpace(fireEventPath))
                return;

            if (AudioService.Service != null)
            {
                AudioService.PlayOneShot(fireEventPath, gameObject, 1.0f, 0.0f, 0.0f, fireVolume);
                return;
            }

#if MGFMOD
            var eventInstance = MBFmodReflection.CreateInstance(fireEventPath);
            if (eventInstance == null)
                return;

            MBFmodReflection.Set3DAttributes(eventInstance, gameObject, GetComponent<Rigidbody>());
            MBFmodReflection.SetVolume(eventInstance, fireVolume);
            MBFmodReflection.Start(eventInstance);
            MBFmodReflection.Release(eventInstance);
#endif
        }
    }
}

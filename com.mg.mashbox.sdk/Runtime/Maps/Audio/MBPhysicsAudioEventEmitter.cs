using System;
using UnityEngine;
using UnityEngine.Events;
using MashBoxSDK.Services;
using MashBoxSDK.Utility;

namespace MashBoxSDK.Maps.Audio
{
    [AddComponentMenu("MashBox/Maps/Audio/Physics Audio Event Emitter")]
    [DisallowMultipleComponent]
    public class MBPhysicsAudioEventEmitter : MonoBehaviour
    {
        [Header("Impact")]
        [Tooltip("Enable an impact one-shot when this object collides with something at meaningful speed.")]
        [SerializeField] private bool hasImpactEvent = true;
        [Tooltip("Audio event path used for the collision impact one-shot.")]
        [SerializeField] private string impactEventPath = string.Empty;
        [Tooltip("Minimum time between impact one-shots so repeated contacts do not spam audio.")]
        [Range(0.0f, 0.5f)]
        [SerializeField] private float impactCooldown = 0.01f;

        [Header("Continuous")]
        [Tooltip("Enable a looping or sustained audio event that follows this rigidbody's motion.")]
        [SerializeField] private bool hasContinuousEvent;
        [Tooltip("Audio event path used for the continuous physics loop.")]
        [SerializeField] private string continuousEventPath = string.Empty;
        [Tooltip("Parameter name on the continuous event that receives the current motion value.")]
        [SerializeField] private string continuousParameterName = "Velocity";
        [Tooltip("Start the continuous event automatically when this component enables.")]
        [SerializeField] private bool startContinuousEventOnStart = true;
        [Tooltip("Drive the continuous event from the attached rigidbody's linear velocity.")]
        [SerializeField] private bool useAttachedBodyVelocity = true;
        [Tooltip("Drive the continuous event from the attached rigidbody's angular velocity.")]
        [SerializeField] private bool useAttachedBodyAngularVelocity;
        [Tooltip("Multiplier applied to the sampled velocity before it is sent to the audio event.")]
        [Range(0.0f, 10.0f)]
        [SerializeField] private float velocityMultiplier = 1.0f;
        [Tooltip("Any sampled velocity below this threshold is treated as silence.")]
        [Range(0.0f, 5.0f)]
        [SerializeField] private float minimumVelocityThreshold;
        [Tooltip("Only allow the continuous loop to stay active while collision stay is happening.")]
        [SerializeField] private bool requiresCollisionStay = true;
        [Tooltip("Velocity needed before the continuous event starts playing.")]
        [Range(0.0f, 5.0f)]
        [SerializeField] private float startVelocityThreshold = 0.1f;
        [Tooltip("Velocity below which the continuous event fades out and stops.")]
        [Range(0.0f, 5.0f)]
        [SerializeField] private float stopVelocityThreshold = 0.05f;
        [Tooltip("Minimum delay between start/stop toggles on the continuous event.")]
        [Range(0.0f, 1.0f)]
        [SerializeField] private float toggleCooldown = 0.25f;

        [Header("Release")]
        [Tooltip("Enable a release one-shot when you trigger PlayRelease from script or events.")]
        [SerializeField] private bool hasReleaseEvent;
        [Tooltip("Audio event path used for the release one-shot.")]
        [SerializeField] private string releaseEventPath = string.Empty;
        [Tooltip("Minimum time between release one-shots.")]
        [Range(0.0f, 1.0f)]
        [SerializeField] private float releaseCooldown = 0.2f;

        [Tooltip("Invoked whenever a qualifying collision impact is detected.")]
        public UnityEvent OnCollisionEvent;
        [Tooltip("Invoked with the collision velocity projected into the impact direction.")]
        public UnityEvent<float> OnCollisionEventVelocity;

        private Rigidbody attachedBody;
        private float timeAtLastImpact;
        private float timeAtLastRelease;
        private float timeAtAwake;
        private float lastToggleTime;
        private float continuousVelocity;
        private bool collisionStay;
        private bool continuousIsPlaying;
        private object continuousEventInstance;

        private float TimeSinceAwake => Time.time - timeAtAwake;

        private void Awake()
        {
            timeAtAwake = Time.time;
            attachedBody = GetComponentInParent<Rigidbody>();
        }

        private void OnEnable()
        {
            attachedBody = GetComponentInParent<Rigidbody>();

#if MGFMOD
            if (hasContinuousEvent && AudioService.Service == null)
            {
                continuousEventInstance = CreateFmodInstance(continuousEventPath);
                if (continuousEventInstance != null)
                {
                    SetContinuousParameter(0.0f);
                    UpdateContinuous3DAttributes();

                    if (startContinuousEventOnStart)
                    {
                        StartFmodEvent(continuousEventInstance);
                        continuousIsPlaying = true;
                    }
                }
            }
#endif
        }

        private void FixedUpdate()
        {
            if (!hasContinuousEvent)
            {
                collisionStay = !requiresCollisionStay;
                return;
            }

            if (useAttachedBodyVelocity && attachedBody != null)
                #if UNITY_6000_0_OR_NEWER
                UpdateContinuous(attachedBody.linearVelocity.magnitude);
#else
                UpdateContinuous(attachedBody.velocity.magnitude);
#endif

            if (useAttachedBodyAngularVelocity && attachedBody != null)
                UpdateContinuous(attachedBody.angularVelocity.magnitude);

#if MGFMOD
            if (continuousEventInstance != null)
            {
                SetContinuousParameter(continuousVelocity);
                UpdateContinuous3DAttributes();
            }
#endif

            collisionStay = !requiresCollisionStay;
        }

        public void PlayImpact(float velocity)
        {
            if (TimeSinceAwake < 0.1f)
                return;

            if (!hasImpactEvent || string.IsNullOrWhiteSpace(impactEventPath))
                return;

            if (Time.time - timeAtLastImpact < impactCooldown)
                return;

            timeAtLastImpact = Time.time;

            if (AudioService.Service != null)
            {
                AudioService.PlayOneShotRecorded(impactEventPath, gameObject, velocity, 0.0f, 0.0f);
                return;
            }

#if MGFMOD
            PlayFmodOneShot(impactEventPath, velocity);
#endif
        }

        public void PlayRelease(float velocity)
        {
            if (!hasReleaseEvent || string.IsNullOrWhiteSpace(releaseEventPath))
                return;

            if (Time.time - timeAtLastRelease < releaseCooldown)
                return;

            timeAtLastRelease = Time.time;

            if (AudioService.Service != null)
            {
                AudioService.PlayOneShotRecorded(releaseEventPath, gameObject, velocity, 0.0f, 0.0f);
                return;
            }

#if MGFMOD
            PlayFmodOneShot(releaseEventPath, velocity);
#endif
        }

        public void UpdateContinuous(float velocity)
        {
            velocity = Mathf.Abs(velocity);
            if (velocity < minimumVelocityThreshold)
                velocity = 0.0f;

            if (requiresCollisionStay && !collisionStay)
                velocity = 0.0f;

            continuousVelocity = Mathf.Lerp(continuousVelocity, velocity * velocityMultiplier, Time.fixedDeltaTime * 12.0f);
            UpdateContinuousPlaybackState();
        }

        public void StartContinuousEvent()
        {
#if MGFMOD
            if (continuousEventInstance != null)
            {
                StartFmodEvent(continuousEventInstance);
                continuousIsPlaying = true;
            }
#endif
        }

        public void StopContinuousEvent()
        {
#if MGFMOD
            if (continuousEventInstance != null)
            {
                StopFmodEvent(continuousEventInstance, immediate: false);
                continuousIsPlaying = false;
            }
#endif
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.relativeVelocity.sqrMagnitude <= 0.05f)
                return;

            var impulse = collision.impulse;
            var relativeVelocity = collision.relativeVelocity;
            var impulseDirection = impulse.sqrMagnitude > 0f
                ? impulse.normalized
                : (relativeVelocity.sqrMagnitude > 0f ? relativeVelocity.normalized : Vector3.up);
            var velocityInImpulseDirection = Mathf.Abs(Vector3.Dot(relativeVelocity, impulseDirection));

            OnCollisionEvent?.Invoke();
            OnCollisionEventVelocity?.Invoke(velocityInImpulseDirection);
            PlayImpact(velocityInImpulseDirection);
        }

        private void OnCollisionStay(Collision other)
        {
            collisionStay = true;
        }

        private void OnDisable()
        {
#if MGFMOD
            ReleaseContinuousEventInstance();
#endif
        }

        private void OnDestroy()
        {
#if MGFMOD
            ReleaseContinuousEventInstance();
#endif
        }

        private void UpdateContinuousPlaybackState()
        {
            var now = Time.time;

            if (continuousIsPlaying)
            {
                if (continuousVelocity < stopVelocityThreshold && now - lastToggleTime > toggleCooldown)
                {
#if MGFMOD
                    if (continuousEventInstance != null)
                        StopFmodEvent(continuousEventInstance, immediate: false);
#endif
                    continuousIsPlaying = false;
                    lastToggleTime = now;
                }
            }
            else
            {
                if (continuousVelocity > startVelocityThreshold && now - lastToggleTime > toggleCooldown)
                {
#if MGFMOD
                    if (continuousEventInstance != null)
                        StartFmodEvent(continuousEventInstance);
#endif
                    continuousIsPlaying = true;
                    lastToggleTime = now;
                }
            }
        }

        private static Rigidbody GetFallbackRigidbody(GameObject sourceObject)
        {
            return sourceObject != null ? sourceObject.GetComponent<Rigidbody>() : null;
        }

#if MGFMOD
        private object CreateFmodInstance(string eventPath)
        {
            return MBFmodReflection.CreateInstance(eventPath);
        }

        private void PlayFmodOneShot(string eventPath, float velocity)
        {
            var instance = CreateFmodInstance(eventPath);
            if (instance == null)
                return;

            try
            {
                MBFmodReflection.Set3DAttributes(instance, gameObject, attachedBody != null ? attachedBody : GetFallbackRigidbody(gameObject));
                MBFmodReflection.SetParameter(instance, continuousParameterName, velocity);
                MBFmodReflection.Start(instance);
                MBFmodReflection.Release(instance);
            }
            catch
            {
                // Optional FMOD integration only.
            }
        }

        private void UpdateContinuous3DAttributes()
        {
            if (continuousEventInstance == null)
                return;

            MBFmodReflection.Set3DAttributes(continuousEventInstance, gameObject, attachedBody != null ? attachedBody : GetFallbackRigidbody(gameObject));
        }

        private void SetContinuousParameter(float value)
        {
            if (continuousEventInstance == null || string.IsNullOrWhiteSpace(continuousParameterName))
                return;

            MBFmodReflection.SetParameter(continuousEventInstance, continuousParameterName, value);
        }

        private static void StartFmodEvent(object eventInstance)
        {
            MBFmodReflection.Start(eventInstance);
        }

        private static void ReleaseFmodEvent(object eventInstance)
        {
            MBFmodReflection.Release(eventInstance);
        }

        private static void StopFmodEvent(object eventInstance, bool immediate)
        {
            MBFmodReflection.Stop(eventInstance, immediate);
        }

        private void ReleaseContinuousEventInstance()
        {
            if (continuousEventInstance == null)
                return;

            try
            {
                StopFmodEvent(continuousEventInstance, immediate: true);
                ReleaseFmodEvent(continuousEventInstance);
            }
            catch
            {
                // Optional FMOD integration only.
            }
            finally
            {
                continuousEventInstance = null;
                continuousIsPlaying = false;
            }
        }
#endif
    }
}

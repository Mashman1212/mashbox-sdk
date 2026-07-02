using System;
using UnityEngine;
using MashBoxSDK.Services;
using MashBoxSDK.Utility;

namespace MashBoxSDK.Maps.Audio
{
    [AddComponentMenu("MashBox/Maps/Audio/Physics Impact Audio Source")]
    [DisallowMultipleComponent]
    public class MBPhysicsImpactAudioSource : MonoBehaviour
    {
        private const float RepeatColliderCooldown = 0.08f;
        private const float NewColliderCooldown = 0.01f;

        [SerializeField] private bool ignoreGenericFallback;

        private Rigidbody rootRigidbody;
        private float lastCollisionTime;
        private Collider lastCollidedCollider;

        private void Awake()
        {
            rootRigidbody = GetRootRigidbody(transform);
        }

        private void OnCollisionEnter(Collision other)
        {
            if (other.gameObject.layer != 0)
                return;

            var collidedCollider = other.collider;
            var cooldown = collidedCollider != null && collidedCollider != lastCollidedCollider
                ? NewColliderCooldown
                : RepeatColliderCooldown;

            if (Time.time - lastCollisionTime < cooldown)
                return;

            if (other.transform.GetComponentInParent<MBPhysicsImpactAudioSource>() != null)
                return;

            var collidedRigidbody = GetRootRigidbody(other.collider.transform);
            if (collidedRigidbody != null && collidedRigidbody == rootRigidbody)
                return;

            var impactPath = ResolveImpactPath(other, out var materialHit);
            if (!materialHit && ignoreGenericFallback)
                return;

            lastCollisionTime = Time.time;
            lastCollidedCollider = collidedCollider;

            var velocity = other.relativeVelocity.magnitude;
            if (AudioService.Service != null)
            {
                AudioService.PlayOneShotRecorded(impactPath, gameObject, velocity, 0.0f, 0.0f);
                return;
            }

#if MGFMOD
            TryPlayFmodImpact(impactPath, velocity);
#endif
        }

        private string ResolveImpactPath(Collision other, out bool materialHit)
        {
            materialHit = false;

            var materialName = other.collider.sharedMaterial != null
                ? other.collider.sharedMaterial.name
                : "null";

            materialName = materialName.ToLowerInvariant().Replace(" (instance)", string.Empty);

            if (materialName.StartsWith("pbs_", StringComparison.Ordinal))
            {
                var fmodEvent = materialName.Replace("pbs_", string.Empty).Replace("_", "/");
                materialHit = true;
                return "event:/Environment/Common/Physics Based Surface/" + fmodEvent;
            }

            var colliderTag = other.collider.tag.ToLowerInvariant();

            if (materialName.Contains("wood") || colliderTag.Contains("wood"))
            {
                materialHit = true;
                return "event:/Environment/Common/Physics Based Surface/Wood/Impact Heavy";
            }

            if (materialName.Contains("metal") || colliderTag.Contains("metal"))
            {
                materialHit = true;
                return "event:/Environment/Common/Physics Based Surface/Metal/Impact Heavy";
            }

            if (materialName.Contains("plastic") || colliderTag.Contains("plastic"))
            {
                materialHit = true;
                return "event:/Environment/Common/Physics Based Surface/Plastic/Impact Heavy";
            }

            if (materialName.Contains("dirt") || colliderTag.Contains("dirt"))
            {
                materialHit = true;
                return "event:/Environment/Common/Physics Based Surface/Dirt/Impact Heavy";
            }

            if (materialName.Contains("grass") || colliderTag.Contains("grass"))
            {
                materialHit = true;
                return "event:/Environment/Common/Physics Based Surface/Grass/Impact Heavy";
            }

            if (materialName.Contains("glass") || colliderTag.Contains("glass"))
            {
                materialHit = true;
                return "event:/Environment/Common/Physics Based Surface/Glass/Impact Heavy";
            }

            return "event:/Environment/Common/Physics Based Surface/Generic/Impact Heavy";
        }

        private static Rigidbody GetRootRigidbody(Transform trans)
        {
            var currentTransform = trans;
            Rigidbody foundRigidbody = null;

            while (currentTransform != null)
            {
                var currentRigidbody = currentTransform.GetComponent<Rigidbody>();
                if (currentRigidbody != null)
                    foundRigidbody = currentRigidbody;

                currentTransform = currentTransform.parent;
            }

            return foundRigidbody;
        }

#if MGFMOD
        private void TryPlayFmodImpact(string impactPath, float velocity)
        {
            try
            {
                var eventInstance = MBFmodReflection.CreateInstance(impactPath);
                if (eventInstance == null)
                    return;

                MBFmodReflection.Set3DAttributes(eventInstance, gameObject, GetComponent<Rigidbody>());
                MBFmodReflection.SetParameter(eventInstance, "Velocity", velocity);
                MBFmodReflection.Start(eventInstance);
                MBFmodReflection.Release(eventInstance);
            }
            catch
            {
                // Optional FMOD fallback only.
            }
        }
#endif
    }
}

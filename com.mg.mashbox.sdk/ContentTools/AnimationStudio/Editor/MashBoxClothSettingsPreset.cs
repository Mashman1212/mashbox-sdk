using UnityEngine;
using MashBoxSDK.Cloth;

namespace MashBoxSDK.AnimationStudio
{
    [CreateAssetMenu(menuName = "MashBox/Cloth Settings Preset", fileName = "Cloth Settings Preset")]
    public sealed class MashBoxClothSettingsPreset : ScriptableObject
    {
        public bool bodyCollisions = true;
        [Tooltip("Use skinning to restrict each cloth region to its associated body bones. Unskinned meshes use all body colliders.")]
        public bool localBoneCollisions = true;
        [Tooltip("Keep particles outside spheres behind the animated mesh surface. Uses the animated mesh normals; independent of body capsules.")]
        public bool backstop;
        [Min(0), Tooltip("Inward travel allowed before contacting the backstop, in metres.")]
        public float backstopDistance = .005f;
        [Min(.001f), Tooltip("Radius of the sphere behind each particle, in metres. Larger spheres block a wider area.")]
        public float backstopRadius = .05f;
        [Range(0, 1), Tooltip("Tangential velocity removed on body contact.")] public float collisionFriction;
        [Min(0)] public float stretchCompliance = 0.000001f;
        [Min(0)] public float bendCompliance = 0.001f;
        [Range(0, 20)] public float damping = 3;
        [Range(0, 1), Tooltip("Follow the animated bones and restore the animated shape. Zero retains full world-space inertia; one follows closely while allowing cloth motion.")]
        public float animationFollow = 1;
        [Range(0, 1), Tooltip("Inertia from changes in animation velocity. Higher gives more lag on acceleration and overshoot on stopping, even with Animation Follow enabled. Zero disables this extra inertia.")]
        public float animationInertia = .5f;
        [Range(0, 1), Tooltip("Additional soft support toward the animated shape. Zero disables it; higher values reduce sag and bunching. Pins and collision still take priority.")]
        public float shapeRetention;
        [Range(0, 2), Tooltip("Damping of shape recovery. Around 0.7 settles gently; lower values allow more bounce.")]
        public float shapeDamping = .7f;
        public Vector3 gravity = new Vector3(0, -9.81f, 0);
        public Vector3 wind = new Vector3(0, 0, 4);
        [Range(0, 1)] public float gust = .25f;
        [Range(0.001f, .05f)] public float thickness = .008f;


        public void Capture(MashBoxClothProfile profile)
        {
            if (!profile) throw new System.ArgumentNullException(nameof(profile));
            bodyCollisions = profile.bodyCollisions;
            localBoneCollisions = profile.localBoneCollisions;
            backstop = profile.backstop;
            backstopDistance = profile.backstopDistance;
            backstopRadius = profile.backstopRadius;
            collisionFriction = profile.collisionFriction;
            stretchCompliance = profile.stretchCompliance;
            bendCompliance = profile.bendCompliance;
            damping = profile.damping;
            animationFollow = profile.animationFollow;
            animationInertia = profile.animationInertia;
            shapeRetention = profile.shapeRetention;
            shapeDamping = profile.shapeDamping;
            gravity = profile.gravity;
            wind = profile.wind;
            gust = profile.gust;
            thickness = profile.thickness;
        }
        public void ApplyTo(MashBoxClothProfile profile)
        {
            if (!profile) throw new System.ArgumentNullException(nameof(profile));
            profile.ApplyFixedSettings();
            profile.bodyCollisions = bodyCollisions;
            profile.localBoneCollisions = localBoneCollisions;
            profile.backstop = backstop;
            profile.backstopDistance = backstopDistance;
            profile.backstopRadius = backstopRadius;
            profile.collisionFriction = collisionFriction;
            profile.stretchCompliance = stretchCompliance;
            profile.bendCompliance = bendCompliance;
            profile.damping = damping;
            profile.animationFollow = animationFollow;
            profile.animationInertia = animationInertia;
            profile.shapeRetention = shapeRetention;
            profile.shapeDamping = shapeDamping;
            profile.gravity = gravity;
            profile.wind = wind;
            profile.gust = gust;
            profile.thickness = thickness;
        }
    }
}

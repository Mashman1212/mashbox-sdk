
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace MashBoxSDK.Maching
{
    [ExecuteAlways]
    public class AnimationPoser : MonoBehaviour
    {
        [System.Serializable]
        public class PoseClip
        {
            public string name;
            public AnimationClip clip;
            [Range(0f, 1f)] public float normalizedTime = 0f;
        }

        [Header("Default Animation (optional fallback)")]
        public AnimationClip clip;

        [Range(0f, 1f)]
        public float normalizedTime = 0f;

        [Header("Pose Clips")]
        public PoseClip[] poses;

        [Tooltip("Automatically update pose when values change")]
        public bool autoUpdate = true;

        [Tooltip("Keep AnimationMode active")]
        public bool livePreview = true;

        private GameObject _target;

#if UNITY_EDITOR
        private void OnEnable()
        {
            _target = gameObject;

            if (!Application.isPlaying && livePreview)
            {
                ApplyDefaultPose();
            }
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                AnimationMode.StopAnimationMode();
            }
        }
        

        /// <summary>
        /// Apply a specific pose from the pose list
        /// </summary>
        public void ApplyPose(PoseClip pose)
        {
            if (pose == null || pose.clip == null || _target == null)
                return;

            if (!AnimationMode.InAnimationMode())
                AnimationMode.StartAnimationMode();

            DisableAnimator();

            float time = pose.clip.length * pose.normalizedTime;
            AnimationMode.SampleAnimationClip(_target, pose.clip, time);

            SceneView.RepaintAll();
        }

        /// <summary>
        /// Fallback pose using default clip + normalizedTime
        /// </summary>
        public void ApplyDefaultPose()
        {
            AnimationMode.StartAnimationMode();
            
            if (clip == null || _target == null)
                return;

            if (!AnimationMode.InAnimationMode())
                AnimationMode.StartAnimationMode();

            DisableAnimator();

            float time = clip.length * normalizedTime;
            AnimationMode.SampleAnimationClip(_target, clip, time);

            SceneView.RepaintAll();
            
            AnimationMode.StopAnimationMode();
        }

        private void DisableAnimator()
        {
            var animator = _target.GetComponent<Animator>();
            if (animator != null)
            {
                animator.enabled = false;
            }
        }

        [ContextMenu("▶ Preview Default Pose")]
        public void Preview()
        {
            if (clip == null) return;

            AnimationMode.StartAnimationMode();
            ApplyDefaultPose();
        }

        [ContextMenu("⏹ Stop Preview")]
        public void StopPreview()
        {
            AnimationMode.StopAnimationMode();
        }
#endif
    }
}

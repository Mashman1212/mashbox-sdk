#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.Maching
{
    public static class AnimationPoseUtility
    {
        public static void SampleAtTime(GameObject character, AnimationClip clip, float time)
        {
            if (character == null || clip == null)
                return;

            AnimationMode.StartAnimationMode();

            AnimationMode.SampleAnimationClip(character, clip, time);
        }

        public static void Stop()
        {
            AnimationMode.StopAnimationMode();
        }
    }
}
#endif
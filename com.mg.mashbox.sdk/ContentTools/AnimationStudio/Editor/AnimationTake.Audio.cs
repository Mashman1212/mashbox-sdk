using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public enum StudioAudioSource { FmodEvent, AudioClip }

    [Serializable]
    public sealed class StudioAudioCue
    {
        public int frame;
        public bool enabled = true;
        public StudioAudioSource source;
        public string eventPath = "";
        public AudioClip clip;
        [Range(0, 1)] public float volume = 1;
        public string Label => source == StudioAudioSource.AudioClip ? (clip ? clip.name : "Choose an AudioClip") :
            (string.IsNullOrEmpty(eventPath) ? "Choose an FMOD event" : eventPath);
    }

    public sealed partial class AnimationTake
    {
        public List<StudioAudioCue> audioCues = new List<StudioAudioCue>();
    }

    // Frame crossings, rather than rounded playhead frames, prevent missed or repeated cues.
    internal static class StudioAudioTiming
    {
        internal static IEnumerable<StudioAudioCue> Crossed(AnimationTake take, double fromSeconds, double toSeconds, bool includeStart)
        {
            if (!take || take.audioCues == null || toSeconds < fromSeconds) yield break;
            foreach (var cue in take.audioCues)
            {
                if (cue == null || !cue.enabled || cue.frame < 0 || cue.frame > take.lastFrame) continue;
                double time = cue.frame / (double)Math.Max(1, take.frameRate);
                if ((time > fromSeconds || (includeStart && time == fromSeconds)) && time <= toSeconds) yield return cue;
            }
        }
    }
}

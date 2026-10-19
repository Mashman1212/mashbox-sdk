using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // No Unity object references: opening another RAM clip destroys its source take.
    [Serializable]
    internal sealed class StudioKeyClipboard
    {
        public string[] paths;
        public PoseKey[] keys;
        public int frameRate;
        public float originSeconds;
        public bool additive;
        private static StudioKeyClipboard cached;
        private static string CacheKey => "MashBox.StudioKeyClipboard." + Application.dataPath;
        internal static StudioKeyClipboard Current
        {
            get
            {
                if (cached == null)
                {
                    string json = SessionState.GetString(CacheKey, "");
                    if (!string.IsNullOrEmpty(json)) cached = JsonUtility.FromJson<StudioKeyClipboard>(json);
                }
                return cached;
            }
        }
        internal void Store()
        {
            SessionState.SetString(CacheKey, JsonUtility.ToJson(this));
            cached = this;
        }
        internal static int Mask(int[] mask, int index) => mask != null && index < mask.Length ? mask[index] : 0;
        internal static void ExplicitMasks(PoseKey key, bool baseTrack)
        {
            key.boneChannels = !key.selective && baseTrack ? Enumerable.Repeat(7, key.bones.Length).ToArray()
                : Enumerable.Range(0, key.bones.Length).Select(i => Mask(key.boneChannels, i)).ToArray();
            key.effectorChannels = !key.selective && baseTrack ? Enumerable.Repeat(15, key.effectors.Length).ToArray()
                : Enumerable.Range(0, key.effectors.Length).Select(i => Mask(key.effectorChannels, i)).ToArray();
            key.selective = true;
        }
        internal static StudioAnimationLayer Layer(AnimationTake actor, List<PoseKey> track) => actor.layers.FirstOrDefault(l => l.keys == track);
        internal static StudioKeyClipboard Capture(AnimationTake actor, List<PoseKey> track,
            IEnumerable<PoseKey> selected, int[] bones, int[] effectors, float origin, bool includeEmpty = false)
        {
            var copies = new List<PoseKey>();
            foreach (var source in selected.OrderBy(k => k.frame))
            {
                var key = source.Copy(source.frame);
                ExplicitMasks(key, track == actor.keys);
                for (int i = 0; i < key.boneChannels.Length; i++) key.boneChannels[i] &= Mask(bones, i);
                for (int i = 0; i < key.effectorChannels.Length; i++) key.effectorChannels[i] &= Mask(effectors, i);
                if (includeEmpty || key.boneChannels.Any(m => m != 0) || key.effectorChannels.Any(m => m != 0)) copies.Add(key);
            }
            return new StudioKeyClipboard { paths = (string[])actor.bonePaths.Clone(), keys = copies.ToArray(),
                frameRate = actor.frameRate, originSeconds = origin, additive = Layer(actor, track)?.mode == StudioLayerMode.Additive };
        }
        internal string Incompatibility(AnimationTake actor, List<PoseKey> track)
        {
            if (keys == null || keys.Length == 0) return "Copy selected keys first";
            if (additive != (Layer(actor, track)?.mode == StudioLayerMode.Additive))
                return additive ? "Choose an additive layer for these keys" : "Choose Base motion or an override layer for these keys";
            for (int i = 0; i < paths.Length; i++)
                if (keys.Any(k => Mask(k.boneChannels, i) != 0) && !actor.bonePaths.Contains(paths[i]))
                    return "The destination skeleton is missing copied joints";
            return null;
        }
        internal List<PoseKey> Remap(AnimationTake actor, List<PoseKey> track, PoseKey rest, float destinationSeconds)
        {
            string error = Incompatibility(actor, track);
            if (error != null) throw new InvalidOperationException(error + ".");
            var map = paths.Select(p => Array.IndexOf(actor.bonePaths, p)).ToArray();
            var result = new List<PoseKey>();
            foreach (var source in keys)
            {
                int at = Mathf.Max(0, Mathf.RoundToInt((destinationSeconds + source.frame / (float)frameRate - originSeconds) * actor.frameRate));
                var key = rest.Copy(at); key.selective = true;
                key.boneChannels = new int[key.bones.Length]; key.effectorChannels = new int[key.effectors.Length];
                for (int i = 0; i < map.Length; i++)
                    if (map[i] >= 0 && Mask(source.boneChannels, i) != 0)
                    { key.bones[map[i]] = source.bones[i]; key.boneChannels[map[i]] = source.boneChannels[i]; }
                for (int i = 0; i < key.effectors.Length && i < source.effectors.Length; i++)
                { key.effectors[i] = source.effectors[i]; key.effectorChannels[i] = Mask(source.effectorChannels, i); }
                result.Add(key);
            }
            return result;
        }
        internal static void Merge(AnimationTake actor, List<PoseKey> track, IEnumerable<PoseKey> samples, PoseKey rest)
        {
            var input = samples.ToArray();
            var byFrame = track.ToDictionary(k => k.frame);
            // Capture fallback poses before inserting anything, including before the first key.
            var fallback = input.Select(k => k.frame).Where(f => !byFrame.ContainsKey(f)).Distinct()
                .ToDictionary(f => f, f => (actor.EvaluateBase(f) ?? rest).Copy(f));
            var layer = Layer(actor, track);
            foreach (var source in input)
            {
                if (!byFrame.TryGetValue(source.frame, out var key))
                {
                    key = fallback[source.frame].Copy(source.frame); key.selective = true;
                    key.boneChannels = new int[key.bones.Length]; key.effectorChannels = new int[key.effectors.Length];
                    track.Add(key);
                    byFrame.Add(key.frame, key);
                }
                ExplicitMasks(key, track == actor.keys);
                for (int i = 0; i < key.bones.Length; i++)
                {
                    int mask = Mask(source.boneChannels, i); var value = source.bones[i];
                    if ((mask & 1) != 0) key.bones[i].position = value.position;
                    if ((mask & 2) != 0) key.bones[i].rotation = value.rotation;
                    if ((mask & 4) != 0) key.bones[i].scale = value.scale;
                    key.boneChannels[i] |= mask;
                    if (layer != null && mask != 0 && !layer.bones.Contains(actor.bonePaths[i])) layer.bones.Add(actor.bonePaths[i]);
                }
                for (int i = 0; i < key.effectors.Length; i++)
                {
                    int mask = Mask(source.effectorChannels, i); var value = source.effectors[i];
                    if ((mask & 1) != 0) key.effectors[i].position = value.position;
                    if ((mask & 2) != 0) key.effectors[i].rotation = value.rotation;
                    if ((mask & 4) != 0) key.effectors[i].pole = value.pole;
                    if ((mask & 8) != 0) key.effectors[i].weight = value.weight;
                    key.effectorChannels[i] |= mask;
                    if (layer != null) layer.effectors[i] |= mask;
                }
            }
            track.Sort((a, b) => a.frame.CompareTo(b.frame));
        }
    }
}

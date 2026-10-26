using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Optional integration: the SDK also loads in projects without FMOD installed.
    internal sealed class StudioAudioPreview : IDisposable
    {
        private static readonly Type EventManager = FindType("FMODUnity.EventManager");
        private static readonly Type EditorUtils = FindType("FMODUnity.EditorUtils");
        private static readonly Type AudioUtil = typeof(Editor).Assembly.GetType("UnityEditor.AudioUtil");
        private sealed class Voice
        {
            internal object Instance;
            internal bool OneShot;
        }
        private readonly List<Voice> instances = new List<Voice>();
        private bool ownsClipPreview;
        internal static bool HasFmod => EventManager != null && EditorUtils != null;
        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).FirstOrDefault(t => t != null);

        internal static string[] EventPaths()
        {
            if (!HasFmod) return Array.Empty<string>();
            var events = EventManager.GetProperty("Events")?.GetValue(null) as IEnumerable;
            if (events == null) return Array.Empty<string>();
            return events.Cast<object>().Select(e => e.GetType().GetField("Path")?.GetValue(e) as string)
                .Where(p => !string.IsNullOrEmpty(p)).Distinct().OrderBy(p => p).ToArray();
        }

        internal void Play(StudioAudioCue cue)
        {
            Prune();
            if (cue.source == StudioAudioSource.AudioClip)
            {
                if (!cue.clip) throw new InvalidOperationException("Assign an AudioClip to this audio key.");
                var play = AudioUtil?.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
                if (play == null) throw new InvalidOperationException("AudioClip preview is unavailable in this Unity editor.");
                play.Invoke(null, new object[] { cue.clip, 0, false });
                ownsClipPreview = true;
                return;
            }
            if (!HasFmod) throw new InvalidOperationException("Install the FMOD Unity integration to preview FMOD events.");
            var eventRef = EventManager.GetMethod("EventFromPath").Invoke(null, new object[] { cue.eventPath });
            if (eventRef == null) throw new InvalidOperationException("FMOD event not found: " + cue.eventPath + ". Build and refresh your FMOD banks.");
            EditorUtils.GetMethod("LoadPreviewBanks").Invoke(null, null);
            var instance = EditorUtils.GetMethod("PreviewEvent").Invoke(null,
                new object[] { eventRef, new Dictionary<string, float>(), Mathf.Clamp01(cue.volume), 0f });
            instances.Add(new Voice { Instance = instance, OneShot = (bool)eventRef.GetType().GetField("IsOneShot").GetValue(eventRef) });
        }

        private void Prune()
        {
            // FMOD's editor system releases completed one-shots. Drop our stale handles too.
            instances.RemoveAll(v => !(bool)v.Instance.GetType().GetMethod("isValid").Invoke(v.Instance, null));
        }

        internal Action<string> ReportError;

        private void Stop(object instance)
        {
            try
            {
                var stop = EditorUtils.GetMethod("PreviewStop");
                var mode = Enum.Parse(stop.GetParameters()[1].ParameterType, "IMMEDIATE");
                stop.Invoke(null, new[] { instance, mode });
            }
            catch (Exception ex) { ReportError?.Invoke("Could not stop studio audio: " + ex.GetBaseException().Message); }
        }

        internal void StopLoops()
        {
            foreach (var voice in instances.Where(v => !v.OneShot)) Stop(voice.Instance);
            instances.RemoveAll(v => !v.OneShot);
        }

        public void Dispose()
        {
            foreach (var voice in instances) Stop(voice.Instance);
            instances.Clear();
            if (ownsClipPreview)
            {
                AudioUtil?.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
                ownsClipPreview = false;
            }
        }
    }
}

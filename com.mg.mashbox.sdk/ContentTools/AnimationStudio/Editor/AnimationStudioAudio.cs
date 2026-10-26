using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private readonly StudioAudioPreview audioPreview = new StudioAudioPreview();
        private readonly HashSet<string> audioErrors = new HashSet<string>();
        [SerializeField] private bool muteAudio;
        private bool isPlaying;
        private double audioPlaybackFrame;
        private bool playing
        {
            get => isPlaying;
            set { isPlaying = value; if (!value) StopStudioAudio(); }
        }

        private void StopStudioAudio()
        {
            audioPreview.Dispose();
            StudioAudioKeysWindow.StopAuditions();
        }
        private IEnumerable<AnimationTake> AudioActors() => new[] { take }
            .Concat(actorPreviews.Select(a => a.Take)).Where(a => a).Distinct();

        private void PlayAudioRange(double fromFrame, double toFrame, bool includeStart = false)
        {
            if (muteAudio || !take) return;
            double fps = Math.Max(1, take.frameRate);
            var cues = AudioActors().SelectMany(a => StudioAudioTiming.Crossed(a, fromFrame / fps, toFrame / fps, includeStart)
                .Select(c => new { Cue = c, Time = c.frame / (double)Math.Max(1, a.frameRate) })).OrderBy(c => c.Time);
            foreach (var item in cues)
                try { audioPreview.Play(item.Cue); }
                catch (Exception ex)
                {
                    string error = ex.GetBaseException().Message;
                    if (audioErrors.Add(error)) message = "Audio: " + error;
                    viewport?.ShowNotification(new GUIContent("Audio: " + error));
                }
        }

        private void AddAudioMenu(GenericMenu menu, AnimationTake actor, int atFrame)
        {
            int localFrame = Mathf.RoundToInt(atFrame / (float)Math.Max(1, take.frameRate) * actor.frameRate);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Add audio key at frame " + atFrame), false, () =>
            {
                playing = false; FlushEdits();
                Undo.RegisterCompleteObjectUndo(actor, "Add audio key");
                if (actor.audioCues == null) actor.audioCues = new List<StudioAudioCue>();
                actor.audioCues.Add(new StudioAudioCue { frame = localFrame });
                actor.lastFrame = Mathf.Max(actor.lastFrame, localFrame);
                EditorUtility.SetDirty(actor); AssetDatabase.SaveAssetIfDirty(actor);
                OpenAudioKeys(actor, actor.audioCues.Count - 1);
            });
            menu.AddItem(new GUIContent("Edit audio keys…"), false, () => OpenAudioKeys(actor,
                actor.audioCues?.FindIndex(c => c != null && c.frame == localFrame) ?? -1));
        }

        private void OpenAudioKeys(AnimationTake actor, int selected = -1)
        {
            playing = false;
            StudioAudioKeysWindow.Open(actor, selected);
            viewport?.Repaint(); Repaint();
        }

        private void DrawAudioMarkers(Rect ruler)
        {
            foreach (var actor in AudioActors())
            {
                if (actor.audioCues == null) continue;
                foreach (var cue in actor.audioCues)
                {
                    if (cue == null) continue;
                    int shared = TimelineSharedFrame(actor, cue.frame);
                    if (shared < timelineViewFirst || shared > timelineViewLast) continue;
                    var rect = new Rect(TimelineFrameX(ruler, shared) - 5, ruler.y + 20, 10, 11);
                    EditorGUI.DrawRect(rect, cue.enabled ? new Color(.25f, .8f, .65f) : Color.gray);
                    GUI.Label(rect, new GUIContent("", actor.name + " · " + cue.Label + " · frame " + shared));
                }
            }
        }

        private bool HandleAudioMarker(Event evt, Rect ruler)
        {
            bool context = (evt.type == EventType.MouseDown && evt.button == 1) || evt.type == EventType.ContextClick;
            bool open = evt.type == EventType.MouseDown && evt.button == 0 && evt.clickCount == 2;
            if (!context && !open) return false;
            foreach (var actor in AudioActors())
            {
                if (actor.audioCues == null) continue;
                for (int i = 0; i < actor.audioCues.Count; i++)
                {
                    var cue = actor.audioCues[i];
                    if (cue == null) continue;
                    int shared = TimelineSharedFrame(actor, cue.frame);
                    if (shared < timelineViewFirst || shared > timelineViewLast) continue;
                    if (!new Rect(TimelineFrameX(ruler, shared) - 6, ruler.y + 20, 12, 12).Contains(evt.mousePosition)) continue;
                    int index = i;
                    if (open) OpenAudioKeys(actor, index);
                    else
                    {
                        var menu = new GenericMenu();
                        menu.AddItem(new GUIContent("Edit audio key…"), false, () => OpenAudioKeys(actor, index));
                        menu.AddItem(new GUIContent("Delete audio key"), false, () =>
                        {
                            playing = false;
                            Undo.RegisterCompleteObjectUndo(actor, "Delete audio key");
                            actor.audioCues.Remove(cue);
                            EditorUtility.SetDirty(actor); AssetDatabase.SaveAssetIfDirty(actor);
                            viewport?.Repaint();
                        });
                        AddAudioMenu(menu, actor, shared);
                        menu.ShowAsContext();
                    }
                    evt.Use(); return true;
                }
            }
            return false;
        }
    }
}

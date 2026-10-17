using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed class StudioAudioKeysWindow : EditorWindow
    {
        [SerializeField] private AnimationTake take;
        [SerializeField] private int selected = -1;
        private Vector2 scroll, eventScroll;
        private string search = "", error;
        private string[] paths;
        private readonly StudioAudioPreview preview = new StudioAudioPreview();

        internal static void StopAuditions()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<StudioAudioKeysWindow>()) window.preview.Dispose();
        }

        internal static void Open(AnimationTake actor, int index)
        {
            var window = GetWindow<StudioAudioKeysWindow>("Audio Keys");
            window.preview.Dispose(); window.take = actor; window.selected = index;
            window.minSize = new Vector2(440, 380); window.RefreshEvents(); window.Show();
        }
        private void OnEnable()
        {
            Undo.undoRedoPerformed += Changed;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }
        private void OnDisable()
        {
            preview.Dispose();
            Undo.undoRedoPerformed -= Changed;
            EditorApplication.playModeStateChanged -= OnPlayMode;
        }
        private void OnPlayMode(PlayModeStateChange state) => preview.Dispose();
        private void Changed() { preview.Dispose(); Repaint(); SceneView.RepaintAll(); }
        private void RefreshEvents()
        {
            try { paths = StudioAudioPreview.EventPaths(); error = null; }
            catch (Exception ex) { paths = Array.Empty<string>(); error = ex.GetBaseException().Message; }
        }
        private void Save()
        {
            EditorUtility.SetDirty(take); AssetDatabase.SaveAssetIfDirty(take); SceneView.RepaintAll();
        }
        private void Audition(StudioAudioCue cue)
        {
            try { preview.Dispose(); preview.Play(cue); error = null; }
            catch (Exception ex) { error = ex.GetBaseException().Message; }
        }
        private void OnGUI()
        {
            if (!take) { EditorGUILayout.HelpBox("Open audio keys from an Animation Studio timeline.", MessageType.Info); return; }
            EditorGUILayout.LabelField(take.name + " · Audio keys", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Audio keys play when the studio playhead crosses their frame. Scrubbing is silent. These cues are saved with the take and previewed in the studio; baked animation clips do not include them.", MessageType.Info);
            var cues = take.audioCues;
            if (cues == null) return;
            using (var area = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.Height(120)))
            {
                scroll = area.scrollPosition;
                foreach (int i in Enumerable.Range(0, cues.Count).Where(i => cues[i] != null).OrderBy(i => cues[i].frame))
                {
                    var cue = cues[i];
                    if (GUILayout.Toggle(selected == i, "Frame " + cue.frame + "  ·  " + (cue.enabled ? "" : "[Muted] ") + cue.Label, "Button")) selected = i;
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add audio key"))
                {
                    Undo.RegisterCompleteObjectUndo(take, "Add audio key");
                    cues.Add(new StudioAudioCue { frame = selected >= 0 && selected < cues.Count ? cues[selected].frame : 0 });
                    selected = cues.Count - 1; Save();
                }
                if (GUILayout.Button("Stop preview")) preview.Dispose();
            }
            if (selected >= 0 && selected < cues.Count && cues[selected] != null)
            {
                var cue = cues[selected];
                EditorGUI.BeginChangeCheck();
                int nextFrame = Mathf.Clamp(EditorGUILayout.DelayedIntField("Frame (" + take.frameRate + " fps)", cue.frame), 0, 18000);
                bool enabled = EditorGUILayout.Toggle("Enabled", cue.enabled);
                var source = (StudioAudioSource)EditorGUILayout.EnumPopup("Sound source", cue.source);
                var clip = cue.clip; string path = cue.eventPath; float volume = cue.volume;
                if (source == StudioAudioSource.AudioClip)
                {
                    clip = (AudioClip)EditorGUILayout.ObjectField("AudioClip", clip, typeof(AudioClip), false);
                    EditorGUILayout.HelpBox("Unity's AudioClip preview uses its shared preview channel and default volume. Use FMOD events for overlapping sounds and per-key volume.", MessageType.Info);
                }
                else
                {
                    path = EditorGUILayout.TextField("FMOD event", path);
                    volume = EditorGUILayout.Slider("Volume", volume, 0, 1);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    preview.Dispose(); Undo.RegisterCompleteObjectUndo(take, "Edit audio key");
                    cue.frame = nextFrame; cue.enabled = enabled; cue.source = source; cue.clip = clip; cue.eventPath = path; cue.volume = volume;
                    Save();
                }
                if (cue.frame > take.lastFrame) EditorGUILayout.HelpBox("This key is beyond the clip end and will not play. Extend the clip end or move the key earlier.", MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Preview sound")) Audition(cue);
                    if (GUILayout.Button("Delete audio key"))
                    {
                        preview.Dispose(); Undo.RegisterCompleteObjectUndo(take, "Delete audio key");
                        cues.RemoveAt(selected); selected = -1; Save(); GUIUtility.ExitGUI();
                    }
                }
                if (source == StudioAudioSource.FmodEvent) DrawEventPicker(cue);
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
        }
        private void DrawEventPicker(StudioAudioCue cue)
        {
            if (!StudioAudioPreview.HasFmod)
            {
                EditorGUILayout.HelpBox("FMOD is not installed in this project. You can still assign an AudioClip or save an event path for later.", MessageType.Info); return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                search = EditorGUILayout.TextField("Find event", search);
                if (GUILayout.Button("Refresh list", GUILayout.Width(90))) RefreshEvents();
            }
            if (paths == null) RefreshEvents();
            if (paths.Length == 0) EditorGUILayout.HelpBox("No FMOD events found. Build your banks and refresh them in FMOD's Event Browser, then refresh this list.", MessageType.Info);
            using (var area = new EditorGUILayout.ScrollViewScope(eventScroll))
            {
                eventScroll = area.scrollPosition;
                foreach (string path in paths.Where(p => p.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(new GUIContent(path, path), EditorStyles.miniButton))
                        {
                            Undo.RegisterCompleteObjectUndo(take, "Assign FMOD audio event"); cue.eventPath = path; Save();
                        }
                        if (GUILayout.Button("Hear", GUILayout.Width(45))) Audition(new StudioAudioCue { eventPath = path, volume = cue.volume });
                    }
            }
        }
    }
}

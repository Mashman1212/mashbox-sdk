using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationTake
    {
        public bool singleFramePose;

        public void SetSingleFramePose(bool enabled)
        {
            if (singleFramePose == enabled) return;
            var first = enabled ? Evaluate(0) : null;
            singleFramePose = enabled;
            if (!enabled) return;
            layers.Clear(); activeLayer = -1;
            if (first != null) SetKey(first, 0);
        }

        // Keep separate copies: editing either endpoint must never mutate the other by aliasing.
        public void SyncSingleFramePose()
        {
            if (!singleFramePose || keys.Count == 0) return;
            var first = keys.OrderBy(k => k.frame).First().Copy(0);
            first.selective = false; first.boneChannels = null; first.effectorChannels = null;
            keys.Clear(); keys.Add(first);
            if (lastFrame > 0) keys.Add(first.Copy(lastFrame));
        }
    }

    public sealed partial class AnimationStudioWindow
    {
        private const string SinglePoseLabel = "MashBoxSingleFramePose";
        private static readonly Color SinglePoseColor = new Color(.77f, .52f, 1f);

        private void DrawSingleFramePose()
        {
            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.Toggle(new GUIContent("Single Frame Pose",
                "Author one pose at frame 0. The final key mirrors it automatically."), take.singleFramePose);
            if (EditorGUI.EndChangeCheck()) SetSingleFramePoseMode(enabled);
            EditorGUILayout.HelpBox(take.singleFramePose
                ? "Edit and key the pose at frame 0. Both purple endpoint keys update together. Clip duration is preserved; attachment constraints are suspended. Save animation to keep this mode with the clip."
                : "Single Frame Pose replaces motion and correction layers with the evaluated frame 0 pose. Undo restores the previous motion.", MessageType.None);
        }

        private void DrawSingleFramePoseButton()
        {
            using (new EditorGUI.DisabledScope(!take))
            {
                bool enabled = take && take.singleFramePose;
                var previousColor = GUI.backgroundColor;
                if (enabled) GUI.backgroundColor = SinglePoseColor;
                bool next = GUILayout.Toggle(enabled,
                    RigIcon("AnimationClip Icon", "Single Frame Pose — author frame 0 and mirror the final key. Enabling replaces the current clip's motion with its first pose; Undo restores it."),
                    StudioTheme.Styles.overlayButton, GUILayout.Width(26), GUILayout.Height(24));
                GUI.backgroundColor = previousColor;
                if (next != enabled) Guard(() => SetSingleFramePoseMode(next));
            }
        }

        private void SetSingleFramePoseMode(bool enabled)
        {
            if (!take || take.singleFramePose == enabled) return;
            FlushEdits();
            Undo.RegisterCompleteObjectUndo(take, "Toggle Single Frame Pose");
            take.SetSingleFramePose(enabled);
            Save(); Seek(0);
            viewport?.Repaint(); Repaint();
        }

        private static void SaveSinglePoseLabel(AnimationClip clip, bool enabled)
        {
            var labels = AssetDatabase.GetLabels(clip).Where(l => l != SinglePoseLabel).ToList();
            if (enabled) labels.Add(SinglePoseLabel);
            AssetDatabase.SetLabels(clip, labels.ToArray());
        }
    }
}

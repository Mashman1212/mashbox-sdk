using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        internal static AnimationClip[] ResolveDroppedAnimationClips(UnityEngine.Object[] objects)
        {
            // A selected FBX child is the exact clip requested, not the whole model.
            var direct = objects.OfType<AnimationClip>().Where(IsEditableDroppedClip).Distinct().ToArray();
            var explicitPaths = direct.Select(AssetDatabase.GetAssetPath).ToArray();
            return direct.Concat(objects.OfType<GameObject>()
                .Select(AssetDatabase.GetAssetPath).Where(path => !string.IsNullOrEmpty(path))
                .Where(path => !explicitPaths.Contains(path))
                .Distinct().SelectMany(AssetDatabase.LoadAllAssetsAtPath)
                .OfType<AnimationClip>().Where(IsEditableDroppedClip)).Distinct().ToArray();
        }

        private static bool IsEditableDroppedClip(AnimationClip clip) =>
            clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal);

        private bool HandleAnimationClipDrop()
        {
            var evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return false;
            if (viewport.IsOverViewportControls(evt.mousePosition)) return false;
            var clips = ResolveDroppedAnimationClips(DragAndDrop.objectReferences);
            if (clips.Length == 0) return false;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                // Rebuilding a preview scene during SceneView's GUI pass is unsafe.
                EditorApplication.delayCall += () =>
                {
                    if (!this || !viewport) return;
                    ChooseActorClipDrop(clips, null, null);
                };
            }
            evt.Use();
            return true;
        }

        // 0 = human, 1 = vehicle, -1 = ambiguous: let the user choose an actor.
        internal static int DroppedClipRole(AnimationClip clip)
        {
            if (clip.isHumanMotion) return 0;
            var paths = AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Transform)).Select(b => b.path).ToArray();
            if (paths.Any(p => p.Contains("Frame_Joint") || p.Contains("DriveTrain_Joint") || p.Contains("Bars_Joint"))) return 1;
            if (paths.Any(p => p.Contains("Character_Reference") || p.Contains("Hips") || p.Contains("LeftHand"))) return 0;
            if (clip.name.StartsWith("Player@", StringComparison.OrdinalIgnoreCase)) return 0;
            if (clip.name.StartsWith("BMX@", StringComparison.OrdinalIgnoreCase)) return 1;
            return -1;
        }

        private void ChooseActorClipDrop(AnimationClip[] clips, AnimationTake target, bool? human)
        {
            if (clips.Length == 1 && (human.HasValue || DroppedClipRole(clips[0]) >= 0))
            { Guard(() => LoadDroppedActorClip(clips[0], target, human ?? DroppedClipRole(clips[0]) == 0)); return; }
            if (!human.HasValue && clips.Length == 2 && clips.Select(DroppedClipRole).OrderBy(r => r).SequenceEqual(new[] { 0, 1 }))
            {
                Guard(() => { foreach (var clip in clips.OrderBy(DroppedClipRole)) LoadDroppedActorClip(clip, null, DroppedClipRole(clip) == 0); });
                return;
            }
            var menu = new GenericMenu();
            for (int i = 0; i < clips.Length; i++)
            {
                var clip = clips[i]; string label = (i + 1) + ". " + clip.name.Replace('/', '_');
                foreach (bool asHuman in human.HasValue ? new[] { human.Value } : new[] { true, false })
                {
                    bool lane = asHuman;
                    menu.AddItem(new GUIContent((human.HasValue ? "" : lane ? "Human/" : "Vehicle/") + label), false,
                        () => Guard(() => LoadDroppedActorClip(clip, target, lane)));
                }
            }
            menu.ShowAsContext();
        }

        private void LoadDroppedActorClip(AnimationClip clip, AnimationTake target, bool human)
        {
            if (!clip) return;
            if (!take || rig == null) NewTake();
            if (!take || rig == null) return;
            EnsureActors();
            if (!target)
            {
                if ((rig.Animator && rig.Animator.isHuman) == human) target = take;
                else target = actorPreviews.FirstOrDefault(a => (a.Rig.Animator && a.Rig.Animator.isHuman) == human)?.Take;
            }
            AnimationTake added = null;
            try
            {
                if (!target)
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(clip));
                    var source = model ? model : AssetDatabase.LoadAssetAtPath<GameObject>(human ? SkeletonPath : BikePath);
                    AddActor(source); target = added = OutfitActor;
                    target.name = human ? "Human" : "BMX";
                }
                SelectClipActor(target);
                actorClip = clip;
                ImportActorClip();
                hiddenAttachments.Remove(target); ApplyAttachmentVisibility();
                if (!human) { showVehicleControls = true; viewport?.Repaint(); Repaint(); }
            }
            catch
            {
                if (added) { take.actors.Remove(added); ReleaseActors(); Undo.DestroyObjectImmediate(added); activeActor = -1; EnsureActors(); }
                throw;
            }
        }

        private void SelectClipActor(AnimationTake actor)
        {
            FinishSceneEdit(); FinishActorEdits();
            activeActor = actor == take ? -1 : actorPreviews.FindIndex(a => a.Take == actor);
            rigSelection.Clear(); rigSelectionExplicit = false;
            selectedRigChannelActor = null; selectedRigChannelBone = selectedRigChannelAxis = -1;
            timelineRangeStart = timelineRangeEnd = -1;
            viewport?.Repaint(); Repaint();
        }
    }
}

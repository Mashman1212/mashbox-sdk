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
            if (direct.Length > 0) return direct;
            return objects.OfType<GameObject>()
                .Select(AssetDatabase.GetAssetPath).Where(path => !string.IsNullOrEmpty(path))
                .Distinct().SelectMany(AssetDatabase.LoadAllAssetsAtPath)
                .OfType<AnimationClip>().Where(IsEditableDroppedClip).Distinct().ToArray();
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
                    if (clips.Length == 1) { if (clips[0]) OpenClipForEditing(clips[0]); return; }
                    var menu = new GenericMenu();
                    for (int i = 0; i < clips.Length; i++)
                    {
                        var clip = clips[i];
                        menu.AddItem(new GUIContent((i + 1) + ". " + clip.name.Replace('/', '_')), false,
                            () => { if (clip) OpenClipForEditing(clip); });
                    }
                    menu.ShowAsContext();
                };
            }
            evt.Use();
            return true;
        }
    }
}

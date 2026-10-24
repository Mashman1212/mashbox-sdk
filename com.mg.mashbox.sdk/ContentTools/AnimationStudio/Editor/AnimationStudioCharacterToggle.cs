using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private bool HasDefaultCharacterAppearance()
        {
            if (!defaultCharacter || OutfitRig == null) return false;
            var actor = OutfitActor;
            var currentBody = actor ? actor.body : body;
            var currentClothing = actor ? actor.clothing : clothing;
            var currentOptions = actor ? actor.outfitOptions : outfitOptions;
            var source = actor && actor.character ? actor.character : character;
            var expectedBody = defaultCharacter.body ? defaultCharacter.body : AssetDatabase.GetAssetPath(source) == SkeletonPath ? AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath) : null;
            var pieces = (defaultCharacter.outfit ?? Array.Empty<StudioDefaultCharacter.OutfitPiece>()).Where(p => p != null && p.enabled && p.prefab).ToArray();
            return (expectedBody || pieces.Length > 0) && currentBody == expectedBody &&
                (currentClothing ?? Array.Empty<GameObject>()).SequenceEqual(pieces.Select(p => p.prefab)) &&
                (currentOptions ?? Array.Empty<OutfitOptions>()).SequenceEqual(pieces.Select(p => p.options));
        }

        private void DrawDefaultCharacterToggle(Rect rect)
        {
            if (!defaultCharacter) defaultCharacter = AssetDatabase.LoadAssetAtPath<StudioDefaultCharacter>(DefaultCharacterPath);
            bool active = HasDefaultCharacterAppearance();
            var icon = new GUIContent(EditorGUIUtility.IconContent("Avatar Icon").image,
                active ? "Hide default character — show the bare rig" : "Show default character on the current rig");
            var old = GUI.backgroundColor;
            try
            {
                if (active) GUI.backgroundColor = StudioTheme.Current.Accent;
                using (new EditorGUI.DisabledScope(!defaultCharacter || OutfitRig == null || !OutfitRig.Animator))
                    if (GUI.Toggle(rect, active, icon, StudioTheme.Styles.overlayButton) != active)
                        Guard(() => ToggleDefaultCharacterAppearance(!active));
            }
            finally { GUI.backgroundColor = old; }
        }

        private void ToggleDefaultCharacterAppearance(bool enabled)
        {
            var pivot = viewport.pivot; var rotation = viewport.rotation;
            float size = viewport.size; bool orthographic = viewport.orthographic;
            try { SetDefaultCharacterAppearance(enabled, true); message = null; }
            finally
            {
                viewport.pivot = pivot; viewport.rotation = rotation;
                viewport.size = size; viewport.orthographic = orthographic;
                viewport.Repaint();
            }
        }
    }
}

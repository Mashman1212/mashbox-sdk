using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private const string DefaultCharacterPath = "Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Male Character.asset";
        [SerializeField] private StudioDefaultCharacter defaultCharacter;

        private void DrawDefaultCharacter()
        {
            if (!defaultCharacter) defaultCharacter = StudioDefaultReferences.LoadCharacter(DefaultCharacterPath);
            defaultCharacter = (StudioDefaultCharacter)EditorGUILayout.ObjectField("Default character", defaultCharacter, typeof(StudioDefaultCharacter), false);
            using (new EditorGUI.DisabledScope(!defaultCharacter))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply Default Character")) Guard(ApplyDefaultCharacter);
                if (GUILayout.Button("Edit Default Character"))
                { Selection.activeObject = defaultCharacter; EditorGUIUtility.PingObject(defaultCharacter); }
            }
        }

        private void ApplyDefaultCharacter() => SetDefaultCharacterAppearance(true, false);
        private void SetDefaultCharacterAppearance(bool enabled, bool keepCurrentRig)
        {
            if (!defaultCharacter) throw new InvalidOperationException("Assign a default character asset first.");
            defaultCharacter.ResolveReferences();
            FlushEdits(); FinishActorEdits();
            var source = defaultCharacter.character ? defaultCharacter.character : AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonPath);
            if (keepCurrentRig) source = OutfitActor && OutfitActor.character ? OutfitActor.character : character ? character : source;
            var skin = defaultCharacter.body ? defaultCharacter.body : AssetDatabase.GetAssetPath(source) == SkeletonPath ? AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath) : null;
            if (!source) throw new InvalidOperationException("Set Skeleton / Character in the Default Male Character asset. The SDK character models have not been assigned yet.");
            var pieces = (defaultCharacter.outfit ?? Array.Empty<StudioDefaultCharacter.OutfitPiece>()).Where(p => p != null && p.enabled && p.prefab).ToArray();
            var garments = pieces.Select(p => p.prefab).ToArray();
            var options = pieces.Select(p => p.options).ToArray();
            if (!enabled) { skin = null; garments = Array.Empty<GameObject>(); options = Array.Empty<OutfitOptions>(); }
            var actor = OutfitActor;
            // Validate the entire replacement before changing the document or disposing its preview.
            var replacement = new AuthoringRig(source, skin, garments, options);
            try
            {
                if (actor && actor.bonePaths.Length > 0 && !actor.bonePaths.SequenceEqual(replacement.Paths))
                    throw new InvalidOperationException("The default character's skeleton does not match this animation. Apply it in a new session, or assign the matching skeleton to the default asset.");
                if (actor && !replacement.HasIK && actor.keys.Any(k => k.effectors.Any(e => e.weight > 0)))
                    throw new InvalidOperationException("This animation needs IK: " + replacement.SolverStatus);
                replacement.SetNeutralShading(false);
                var current = activeActor >= 0 && activeActor < actorPreviews.Count ? actorPreviews[activeActor].Pose : pose;
                replacement.Apply(actor || keepCurrentRig ? current ?? replacement.Rest.Copy(0) : replacement.Rest.Copy(0));
                if (actor) replacement.BuildVehicleParts(actor.vehicleParts);
            }
            catch { replacement.Dispose(); throw; }

            if (actor)
            {
                Undo.IncrementCurrentGroup();
                Undo.RegisterCompleteObjectUndo(actor, "Apply Default Character");
                actor.character = source; actor.body = skin; actor.clothing = garments; actor.outfitOptions = options;
            }
            if (activeActor >= 0 && activeActor < actorPreviews.Count)
            {
                var preview = actorPreviews[activeActor];
                SceneManager.MoveGameObjectToScene(replacement.Root, rig.Scene);
                var previous = preview.Rig; preview.Rig = replacement; previous.Dispose();
                InActor(preview, Save);
            }
            else
            {
                var previous = rig;
                character = source; body = skin; clothing = garments; outfitOptions = options;
                rig = replacement; pose = actor || keepCurrentRig ? pose ?? rig.Rest.Copy(0) : rig.Rest.Copy(0); neutralShading = false;
                cachedBoneSearch = null; boneIndices = null;
                ReleaseActors();
                if (viewport) viewport.Bind(rig.Scene);
                previous?.Dispose();
                if (actor) Save();
                OpenViewport(); EnsureActors();
            }
            EvaluateConstraints(); viewport?.Repaint(); Repaint();
            Undo.IncrementCurrentGroup();
        }
    }
}

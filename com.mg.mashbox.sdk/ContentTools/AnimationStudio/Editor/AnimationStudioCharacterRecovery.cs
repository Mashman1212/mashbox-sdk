using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private static GameObject ResolveSdkSkeleton()
        {
            const string guid = "a4486624e11917747b51b875ff806652";
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return AssetDatabase.LoadAssetAtPath<GameObject>(string.IsNullOrEmpty(path) ? SkeletonPath : path);
        }

        private void RecoverMissingCharacter()
        {
            if (character) return;
            if (take && take.character) { character = take.character; return; }
            // A stale preview reference must not discard the document's animation or outfit.
            var candidate = ResolveSdkSkeleton();
            if (!candidate)
                throw new InvalidOperationException("The SDK Skeleton is unavailable. Assign a character using Session → Apply Default Character.");
            using (var check = new AuthoringRig(candidate, null))
            {
                if (take && take.bonePaths != null && take.bonePaths.Length > 0 && !take.bonePaths.SequenceEqual(check.Paths))
                    throw new InvalidOperationException("This session's character is missing and its bone paths do not match the SDK Skeleton. Assign its original character; the animation has been kept intact.");
            }
            character = candidate;
        }

        [MenuItem("MashBox/Dev/Studio Checks/Animation/Validate Studio character recovery")]
        private static void ValidateCharacterRecovery()
        {
            var window = CreateInstance<AnimationStudioWindow>();
            var document = CreateInstance<AnimationTake>();
            try
            {
                using (var expected = new AuthoringRig(ResolveSdkSkeleton(), null))
                {
                    document.bonePaths = expected.Paths;
                    document.SetKey(expected.Rest, 0);
                    window.take = document;
                    var original = JsonUtility.ToJson(document);
                    window.RecoverMissingCharacter();
                    if (!window.character || JsonUtility.ToJson(document) != original)
                        throw new InvalidOperationException("Recovery failed or changed animation data.");
                    window.character = null; document.bonePaths = new[] { "IncompatibleRig" };
                    bool rejected = false;
                    try { window.RecoverMissingCharacter(); } catch (InvalidOperationException) { rejected = true; }
                    if (!rejected || window.character) throw new InvalidOperationException("An incompatible rig was accepted.");
                }
                Debug.Log("StudioCharacterRecovery: PASS — SDK GUID resolves, missing source recovered, keys preserved, incompatible rig rejected.");
            }
            finally { window.take = null; DestroyImmediate(window); DestroyImmediate(document); }
        }
    }
}

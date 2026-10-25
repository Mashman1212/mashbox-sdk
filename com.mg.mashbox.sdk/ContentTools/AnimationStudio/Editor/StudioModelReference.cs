using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // FBX root/local IDs can change on import. Remember the source asset separately
    // so a lost imported-object reference can be resolved again without name searches.
    [Serializable]
    public sealed class StudioModelReference
    {
        [SerializeField] private string guid;
        [SerializeField] private string assetPath;

        internal void Remember(GameObject model)
        {
            assetPath = model ? AssetDatabase.GetAssetPath(model) : "";
            guid = string.IsNullOrEmpty(assetPath) ? "" : AssetDatabase.AssetPathToGUID(assetPath);
        }

        internal GameObject Resolve(GameObject model)
        {
            if (model) { Remember(model); return model; }
            string path = string.IsNullOrEmpty(guid) ? "" : AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) path = assetPath;
            if (string.IsNullOrEmpty(path)) return null;
            model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model) Remember(model);
            return model;
        }
    }

    [InitializeOnLoad]
    internal static class StudioDefaultReferences
    {
        internal const string CharacterPath = "Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Male Character.asset";
        internal const string SkinningPath = "Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Skinning Body.asset";

        static StudioDefaultReferences() => Queue();
        internal static StudioDefaultCharacter LoadCharacter(string path)
        {
            var preset = AssetDatabase.LoadAssetAtPath<StudioDefaultCharacter>(path);
            if (preset) preset.ResolveReferences();
            return preset;
        }
        internal static StudioSkinningDefaults LoadSkinning(string path)
        {
            var preset = AssetDatabase.LoadAssetAtPath<StudioSkinningDefaults>(path);
            if (preset) preset.ResolveReferences();
            return preset;
        }
        internal static void Queue()
        {
            EditorApplication.update -= RepairWhenReady;
            EditorApplication.update += RepairWhenReady;
        }
        private static void RepairWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= RepairWhenReady;
            LoadCharacter(CharacterPath);
            LoadSkinning(SkinningPath);
            foreach (var preset in Resources.FindObjectsOfTypeAll<StudioDefaultCharacter>()) preset.ResolveReferences();
            foreach (var preset in Resources.FindObjectsOfTypeAll<StudioSkinningDefaults>()) preset.ResolveReferences();
        }
    }

    internal sealed class StudioDefaultReferenceImports : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] previous)
        {
            // Resolve after import completes, never from a model import worker.
            if (!AssetDatabase.IsAssetImportWorkerProcess()) StudioDefaultReferences.Queue();
        }
    }
}

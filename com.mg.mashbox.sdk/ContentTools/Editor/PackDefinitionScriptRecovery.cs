#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.ContentTools.Editor
{
    /// <summary>Repairs stale MonoScript imports before a pack can be serialized without its script.</summary>
    public static class PackDefinitionScriptRecovery
    {
        private static readonly Dictionary<Type, string> ScriptGuids = new Dictionary<Type, string>
        {
            { typeof(ContentPackDefinition), "81f4548504065ce4390701f52bdd5d5e" },
            { typeof(MapContentPackDefinition), "89c06a0e0af71304b827792b0ef9a410" }
        };
        private const string AttemptKeyPrefix = "MashBox.PackDefinitionScriptRepair.";

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            EditorApplication.projectChanged += QueueCheck;
            QueueCheck();
        }

        private static bool IsBusy => AssetDatabase.IsAssetImportWorkerProcess() ||
            EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer;

        private static void QueueCheck()
        {
            EditorApplication.update -= CheckWhenReady;
            EditorApplication.update += CheckWhenReady;
        }

        private static void CheckWhenReady()
        {
            if (IsBusy) return;
            EditorApplication.update -= CheckWhenReady;
            foreach (var type in ScriptGuids.Keys)
                EnsureScript(type);
        }

        [MenuItem("Tools/MashBox/Repair Pack Definition Scripts")]
        public static void RepairScripts()
        {
            foreach (var guid in ScriptGuids.Values)
                SessionState.EraseBool(AttemptKeyPrefix + guid);
            QueueCheck();
        }

        private static bool EnsureScript(Type type)
        {
            var path = AssetDatabase.GUIDToAssetPath(ScriptGuids[type]);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null && script.GetClass() == type) return true;
            var attemptKey = AttemptKeyPrefix + ScriptGuids[type];
            if (IsBusy || string.IsNullOrEmpty(path) || SessionState.GetBool(attemptKey, false)) return false;
            SessionState.SetBool(attemptKey, true);

            // SessionState survives domain reloads. Reimport only this script, once per editor
            // session, so projectChanged cannot start a reimport/compilation loop.
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null && script.GetClass() == type)
            {
                Debug.Log($"[MashBox SDK] Repaired the imported script for {type.Name}.");
                return true;
            }

            Debug.LogWarning($"[MashBox SDK] {type.Name} still has no matching imported script at '{path}'. " +
                "Resolve any Console compilation errors, then use Tools > MashBox > Repair Pack Definition Scripts.");
            return false;
        }

        public static T CreatePack<T>() where T : ScriptableObject
        {
            var type = typeof(T);
            if (!ScriptGuids.ContainsKey(type)) throw new ArgumentException("Unsupported pack definition type.");
            // Healthy definitions may also be used by the manifest asset postprocessor.
            // EnsureScript itself refuses reimports while the AssetDatabase is updating.
            if (AssetDatabase.IsAssetImportWorkerProcess() || EditorApplication.isCompiling || !EnsureScript(type))
            {
                Debug.LogError($"[MashBox SDK] Cannot create {type.Name}: its script is not ready. " +
                    "Wait for importing/compilation to finish and resolve any Console errors, then retry. No pack was saved.");
                return null;
            }

            var pack = ScriptableObject.CreateInstance<T>();
            var script = MonoScript.FromScriptableObject(pack);
            if (script != null && script.GetClass() == type &&
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script)) == ScriptGuids[type])
                return pack;

            UnityEngine.Object.DestroyImmediate(pack);
            Debug.LogError($"[MashBox SDK] Cannot create {type.Name}: Unity did not attach its definition script. No pack was saved.");
            return null;
        }
    }
}
#endif

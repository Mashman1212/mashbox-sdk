#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.Shaders.Editor
{
    /// <summary>Normalizes template-controlled shader keywords after materials are imported.</summary>
    internal sealed class MGShaderTemplateEnforcerPostprocessor : AssetPostprocessor
    {
        private static readonly HashSet<string> PendingMaterialPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool updateScheduled;
        private static bool processing;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (processing || importedAssets == null)
                return;

            foreach (var path in importedAssets)
            {
                if (path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    PendingMaterialPaths.Add(path);
            }

            if (PendingMaterialPaths.Count == 0 || updateScheduled)
                return;

            updateScheduled = true;
            EditorApplication.delayCall += ProcessPendingMaterials;
        }

        private static void ProcessPendingMaterials()
        {
            updateScheduled = false;
            if (processing || PendingMaterialPaths.Count == 0)
                return;

            var paths = PendingMaterialPaths.ToArray();
            PendingMaterialPaths.Clear();
            var changed = false;
            processing = true;
            try
            {
                foreach (var path in paths)
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    changed |= MashBoxSDK.Shaders.ShaderEnforcer.SynchronizeTemplateState(material);
                }

                if (changed)
                    AssetDatabase.SaveAssets();
            }
            finally
            {
                processing = false;
            }
        }
    }

    /// <summary>Also enforces template state when an edited material is saved.</summary>
    internal sealed class MGShaderTemplateSaveProcessor : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            if (paths == null)
                return Array.Empty<string>();

            foreach (var path in paths)
            {
                if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    continue;

                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                MashBoxSDK.Shaders.ShaderEnforcer.SynchronizeTemplateState(material);
            }

            return paths;
        }
    }
}

#endif

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.ContentTools.Editor
{
    [Serializable]
    public class ContentPackGroupInfo
    {
        public string Name = "Ungrouped";
        [Tooltip("Optional parent group used by the Content Builder hierarchy.")]
        public string ParentName = string.Empty;
        public Color Color = new Color(0f, 0f, 0f, 0.35f);
    }

    // Unity needs a matching script filename to restore this asset after an editor restart.
    public class ContentPackGroupSettings : ScriptableObject
    {
        public List<ContentPackGroupInfo> Groups = new List<ContentPackGroupInfo>();

        internal void QueueSave()
        {
            EditorUtility.SetDirty(this);
            // Coalesce edits and save outside GUI/serialization callbacks.
            EditorApplication.delayCall -= SavePendingChanges;
            EditorApplication.delayCall += SavePendingChanges;
        }

        internal void SavePendingChanges()
        {
            EditorApplication.delayCall -= SavePendingChanges;
            if (this != null)
                AssetDatabase.SaveAssetIfDirty(this);
        }

        internal static void RepairLegacyScriptReference(string assetPath)
        {
            if (!File.Exists(assetPath))
                return;

            string yaml = File.ReadAllText(assetPath);
            const string legacyReference = "m_Script: {fileID: 11500000, guid: baa705a8ca4f4d74f8f8d8b8a6ba930b, type: 3}";
            // The old settings type shared ContentPackBuilderWindow.cs. Also handle
            // settings assets whose script reference was saved as null.
            const string missingReference = "m_Script: {fileID: 0}";
            if (!yaml.StartsWith("%YAML", StringComparison.Ordinal) ||
                !yaml.Contains("\n  Groups:"))
                return;

            string oldReference = yaml.Contains(legacyReference) ? legacyReference : missingReference;
            if (!yaml.Contains(oldReference))
                return;

            var settings = CreateInstance<ContentPackGroupSettings>();
            string scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(settings));
            DestroyImmediate(settings);
            string scriptGuid = AssetDatabase.AssetPathToGUID(scriptPath);
            if (string.IsNullOrEmpty(scriptGuid))
                throw new InvalidOperationException("Cannot locate the ContentPackGroupSettings script for migration.");

            // Only replace the script reference; preserve hierarchy, colors and asset GUID.
            File.WriteAllText(assetPath, yaml.Replace(oldReference,
                $"m_Script: {{fileID: 11500000, guid: {scriptGuid}, type: 3}}"));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }
    }
}

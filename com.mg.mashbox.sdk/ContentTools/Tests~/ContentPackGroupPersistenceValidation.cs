using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.ContentTools.Editor
{
    // Run only in an isolated project: these entry points exit Unity.
    public static class ContentPackGroupPersistenceValidation
    {
        private const string Path = "Assets/ContentPackGroups.asset";
        private const string ScriptReference = "m_Script: {fileID: 11500000, guid: 2c3996ef3dac4d49b75139444db98de9, type: 3}";

        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            Debug.Log("PASS: " + message);
        }

        public static void Write()
        {
            try
            {
                var settings = ScriptableObject.CreateInstance<ContentPackGroupSettings>();
                Check(MonoScript.FromScriptableObject(settings).GetClass() == typeof(ContentPackGroupSettings), "Settings script resolves to its own class");
                settings.Groups.Add(new ContentPackGroupInfo { Name = "Brand" });
                settings.Groups.Add(new ContentPackGroupInfo { Name = "Brand Bikes", ParentName = "Brand", Color = Color.red });
                settings.Groups.Add(new ContentPackGroupInfo { Name = "Manual", ParentName = "Brand Bikes" });
                settings.Groups.Add(new ContentPackGroupInfo { Name = "Empty" });
                AssetDatabase.CreateAsset(settings, Path);
                settings.QueueSave();
                EditorApplication.delayCall += () => ValidateSaved(settings);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void ValidateSaved(ContentPackGroupSettings settings)
        {
            try
            {
                Check(!EditorUtility.IsDirty(settings), "Queued save writes to disk on the next editor tick");
                Check(File.ReadAllText(Path).Contains("ParentName: Brand Bikes"), "Nested parent is serialized");
                Undo.RecordObject(settings, "Move subgroup");
                settings.Groups[2].ParentName = "Brand";
                settings.QueueSave();
                settings.SavePendingChanges();
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Check(settings.Groups[2].ParentName == "Brand Bikes", "Subgroup move supports undo");
                settings.QueueSave();
                settings.SavePendingChanges();
                Check(File.ReadAllText(Path).Contains("ParentName: Brand Bikes"), "Undone hierarchy is saved");

                foreach (string oldReference in new[]
                {
                    "m_Script: {fileID: 11500000, guid: baa705a8ca4f4d74f8f8d8b8a6ba930b, type: 3}",
                    "m_Script: {fileID: 0}"
                })
                {
                    string legacyPath = oldReference.Contains("guid:") ? "Assets/LegacyGroups.asset" : "Assets/MissingScriptGroups.asset";
                    string yaml = File.ReadAllText(Path);
                    Check(yaml.Contains(ScriptReference), "New asset references the dedicated settings script");
                    File.WriteAllText(legacyPath, yaml.Replace(ScriptReference, oldReference));
                    // Repair before importing: importing a missing MonoScript can crash Unity 2022.
                    string assetGuid = Guid.NewGuid().ToString("N");
                    File.WriteAllText(legacyPath + ".meta", "fileFormatVersion: 2\nguid: " + assetGuid + "\n");
                    ContentPackGroupSettings.RepairLegacyScriptReference(legacyPath);
                    var repaired = AssetDatabase.LoadAssetAtPath<ContentPackGroupSettings>(legacyPath);
                    Check(repaired != null && repaired.Groups.Count == 4 && repaired.Groups[2].ParentName == "Brand Bikes", "Legacy hierarchy survives script-reference repair");
                    Check(repaired.Groups[1].Color == Color.red && assetGuid == AssetDatabase.AssetPathToGUID(legacyPath), "Migration preserves colors and asset identity");
                    string migrated = File.ReadAllText(legacyPath);
                    ContentPackGroupSettings.RepairLegacyScriptReference(legacyPath);
                    Check(migrated == File.ReadAllText(legacyPath), "Migration is idempotent");
                }
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        public static void ReadAfterRestart()
        {
            try
            {
                var settings = AssetDatabase.LoadAssetAtPath<ContentPackGroupSettings>(Path);
                Check(settings != null && settings.Groups.Count == 4, "All groups survive a fresh Unity process");
                Check(settings.Groups.Single(group => group.Name == "Brand Bikes").ParentName == "Brand", "Prefix-style nesting survives restart");
                Check(settings.Groups.Single(group => group.Name == "Manual").ParentName == "Brand Bikes", "Multiple subgroup levels survive restart");
                Check(settings.Groups.Single(group => group.Name == "Empty").ParentName == string.Empty, "Empty groups survive restart");
                Check(settings.Groups[1].Color == Color.red, "Group colors survive restart");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}

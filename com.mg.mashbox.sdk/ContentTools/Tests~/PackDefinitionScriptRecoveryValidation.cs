using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.ContentTools.Editor;

namespace MashBoxSDK.ContentTools
{
    // Run only in an isolated project with the definition dependency stubs described in README.
    public static class PackDefinitionScriptRecoveryValidation
    {
        private static int assertions;
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            assertions++;
            Debug.Log("PASS: " + message);
        }

        public static void Run()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    AssetDatabase.DeleteAsset("Assets/Content.asset");
                    AssetDatabase.DeleteAsset("Assets/Map.asset");
                    var content = PackDefinitionScriptRecovery.CreatePack<ContentPackDefinition>();
                    var map = PackDefinitionScriptRecovery.CreatePack<MapContentPackDefinition>();
                    Check(content != null && map != null, "Both healthy definitions can be created");
                    map.MapName = "Recovery fixture";
                    AssetDatabase.CreateAsset(content, "Assets/Content.asset");
                    AssetDatabase.CreateAsset(map, "Assets/Map.asset");
                    AssetDatabase.SaveAssets();
                    Check(File.ReadAllText("Assets/Content.asset").Contains("81f4548504065ce4390701f52bdd5d5e"), "Content pack serializes the original script GUID");
                    Check(File.ReadAllText("Assets/Map.asset").Contains("89c06a0e0af71304b827792b0ef9a410"), "Map pack serializes the original script GUID");

                    var guids = (Dictionary<Type, string>)typeof(PackDefinitionScriptRecovery)
                        .GetField("ScriptGuids", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                    string original = guids[typeof(ContentPackDefinition)];
                    string before = File.ReadAllText("Assets/Content.asset");
                    string mapGuid = guids[typeof(MapContentPackDefinition)];
                    string attemptKey = "MashBox.PackDefinitionScriptRepair." + mapGuid;
                    try
                    {
                        // Model an imported script whose class association does not match the type.
                        guids[typeof(ContentPackDefinition)] = mapGuid;
                        SessionState.EraseBool(attemptKey);
                        Check(PackDefinitionScriptRecovery.CreatePack<ContentPackDefinition>() == null, "Wrong class association blocks creation");
                        Check(SessionState.GetBool(attemptKey, false), "Failed association gets one targeted repair attempt");
                        Check(PackDefinitionScriptRecovery.CreatePack<ContentPackDefinition>() == null, "Repeated failure stays blocked without another reimport");
                        guids[typeof(ContentPackDefinition)] = "00000000000000000000000000000000";
                        Check(PackDefinitionScriptRecovery.CreatePack<ContentPackDefinition>() == null, "Missing script blocks creation");
                        Check(File.ReadAllText("Assets/Content.asset") == before, "Failed recovery preserves existing pack bytes");
                    }
                    finally
                    {
                        guids[typeof(ContentPackDefinition)] = original;
                        SessionState.EraseBool(attemptKey);
                    }
                    // Reimporting a .cs file can schedule compilation; verify the restored
                    // association in the fresh-process test after compilation has settled.
                    Debug.Log($"PASS: {assertions} assertions; expected warnings/errors exercised recovery failures.");
                    EditorApplication.Exit(0);
                }
                catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
            };
        }

        public static void AfterRestart()
        {
            try
            {
                var content = AssetDatabase.LoadAssetAtPath<ContentPackDefinition>("Assets/Content.asset");
                var map = AssetDatabase.LoadAssetAtPath<MapContentPackDefinition>("Assets/Map.asset");
                Check(content != null && map != null, "Both packs load after an editor restart");
                Check(map.MapName == "Recovery fixture", "Map metadata survives restart");
                Check(MonoScript.FromScriptableObject(content).GetClass() == typeof(ContentPackDefinition), "Content pack resolves its script after restart");
                Check(MonoScript.FromScriptableObject(map).GetClass() == typeof(MapContentPackDefinition), "Map pack resolves its script after restart");
                var retry = PackDefinitionScriptRecovery.CreatePack<ContentPackDefinition>();
                Check(retry != null, "Creation succeeds after restoring the association and finishing compilation");
                UnityEngine.Object.DestroyImmediate(retry);
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}

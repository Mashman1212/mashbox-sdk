using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;

public static class StudioDefaultReferenceValidation
{
    const string Presets = "Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/";
    const string Human = "Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/";
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly List<string> Results = new List<string>();
    static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Flags).Invoke(value, args);
    static object Get(object value, string field) => value.GetType().GetField(field, Flags).GetValue(value);
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); Results.Add("PASS: " + label); }
    public static void Run()
    {
        int exit = 1;
        try
        {
            var body = AssetDatabase.LoadAssetAtPath<GameObject>(Human + "SM_Body_01.fbx");
            var bust = AssetDatabase.LoadAssetAtPath<GameObject>(Human + "Matt_Bust.fbx");
            Check(body && bust, "Both SDK models load");
            var skin = AssetDatabase.LoadAssetAtPath<StudioSkinningDefaults>(Presets + "Default Skinning Body.asset");
            Call(skin, "ResolveReferences");
            Check(skin.body == body && skin.bust == bust, "Skinning defaults assign body and bust to correct slots");
            const string broken = "Assets/DefaultReferenceValidation.asset";
            AssetDatabase.CopyAsset(Presets + "Default Skinning Body.asset", broken);
            File.WriteAllText(broken, Regex.Replace(File.ReadAllText(broken), @"(  (?:body|bust): \{fileID: )\d+", "${1}424242"));
            AssetDatabase.ImportAsset(broken, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var damaged = AssetDatabase.LoadAssetAtPath<StudioSkinningDefaults>(broken);
            Call(damaged, "ResolveReferences");
            Check(damaged.body == body && damaged.bust == bust, "Stale FBX local IDs recover both references by GUID");
            var preset = AssetDatabase.LoadAssetAtPath<StudioDefaultCharacter>(Presets + "Default Male Character.asset");
            var animated = UnityEngine.Object.Instantiate(preset);
            var bustPiece = animated.outfit.First(p => p.prefab == bust);
            animated.body = null; bustPiece.prefab = null;
            Call(animated, "ResolveReferences");
            Check(animated.body == body && bustPiece.prefab == bust, "Animation body and bust outfit recover missing references");
            bustPiece.enabled = false; bustPiece.prefab = null; Call(animated, "ResolveReferences");
            Check(bustPiece.prefab == bust && !bustPiece.enabled, "Recovery preserves outfit visibility");
            var custom = UnityEngine.Object.Instantiate(skin);
            custom.body = bust; Call(custom, "ResolveReferences");
            Check(custom.body == bust, "Valid custom assignments are preserved");
            custom.body = null;
            var serialized = new SerializedObject(custom);
            serialized.FindProperty("bodyReference").FindPropertyRelative("guid").stringValue = "00000000000000000000000000000000";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Call(custom, "ResolveReferences");
            Check(custom.body == bust, "Last known asset path recovers a replaced GUID");
            Call(Get(custom, "bodyReference"), "Remember", new object[] { null });
            custom.body = null; Call(custom, "ResolveReferences");
            Check(!custom.body, "Explicitly cleared references stay empty");
            var roundTrip = UnityEngine.Object.Instantiate(custom);
            Call(roundTrip, "ResolveReferences");
            Check(!roundTrip.body, "Explicit None survives serialization");
            AnimationStudioWindow.Open();
            var window = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            Call(window, "NewTake");
            var take = (AnimationTake)Get(window, "take");
            Check(take.body == body && take.clothing.Contains(bust), "New animation includes the intended body and bust");
            Call(window, "OpenSkinningMode");
            var doc = (StudioSkinningDocument)Get(window, "skinningDocument");
            var bodyMeshes = body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.sharedMesh).ToArray();
            var bustMeshes = bust.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.sharedMesh).ToArray();
            Check(doc.parts.Any(p => p.referenceOnly && p.role == StudioSkinningDocument.MeshRole.Body && bodyMeshes.Contains(p.sourceMesh)), "Skinning loads locked Default Body from SM_Body_01");
            Check(doc.parts.Any(p => p.referenceOnly && p.role == StudioSkinningDocument.MeshRole.Bust && bustMeshes.Contains(p.sourceMesh)), "Skinning loads locked Default Bust from Matt_Bust");
            AssetDatabase.DeleteAsset(broken);
            Results.Add("SUCCESS"); exit = 0;
        }
        catch (Exception e) { Results.Add(e.ToString()); Debug.LogException(e); }
        finally { File.WriteAllLines("default-reference-results.txt", Results); EditorApplication.Exit(exit); }
    }
}

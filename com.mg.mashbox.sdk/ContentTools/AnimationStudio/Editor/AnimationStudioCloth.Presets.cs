using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private MashBoxClothSettingsPreset clothSettingsPreset;

        private void DrawClothSettingsPresets()
        {
            GUILayout.Space(6);
            GUILayout.Label("SETTINGS PRESET", EditorStyles.boldLabel);
            clothSettingsPreset = (MashBoxClothSettingsPreset)EditorGUILayout.ObjectField(
                "Preset", clothSettingsPreset, typeof(MashBoxClothSettingsPreset), false);
            if (GUILayout.Button("Browse saved presets…"))
            {
                var menu = new GenericMenu();
                var ids = AssetDatabase.FindAssets("t:MashBoxClothSettingsPreset");
                foreach (var id in ids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(id);
                    var preset = AssetDatabase.LoadAssetAtPath<MashBoxClothSettingsPreset>(path);
                    menu.AddItem(new GUIContent(preset.name + " (" + path.Replace('/', '∕') + ")"),
                        clothSettingsPreset == preset, () => { clothSettingsPreset = preset; Repaint(); });
                }
                if (ids.Length == 0) menu.AddDisabledItem(new GUIContent("No saved presets yet"));
                menu.ShowAsContext();
            }
            bool valid = ClothEditable && clothProfile && clothProfile.Matches(MashBoxSDK.Cloth.MashBoxClothSession.GetMesh(ClothRenderer));
            using (new EditorGUI.DisabledScope(!valid))
            {
                using (new GUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!clothSettingsPreset))
                        if (GUILayout.Button("Apply preset")) ClothAction(() =>
                        {
                            EndClothStroke();
                            Undo.RecordObject(clothProfile, "Apply cloth settings preset");
                            clothSettingsPreset.ApplyTo(clothProfile);
                            EditorUtility.SetDirty(clothProfile);
                            viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
                            Repaint();
                        });
                    if (GUILayout.Button("Save current as preset…")) ClothAction(() =>
                    {
                        EndClothStroke();
                        var path = EditorUtility.SaveFilePanelInProject("Save cloth settings preset",
                            ClothRenderer.name + " Settings", "asset", "Save reusable simulation settings for other garments.");
                        if (string.IsNullOrEmpty(path)) return;
                        SaveClothSettingsPreset(path);
                    });
                }
            }
#if MashBoxDev
            using (new EditorGUI.DisabledScope(!valid))
                if (GUILayout.Button("Save current as preset to SDK…")) ClothAction(SaveClothSettingsPresetToSdk);
#endif
            GUILayout.Label("Reusable fabric, motion, solver and collision settings. Painting, mesh and fitted body shapes stay with each garment. Apply copies the settings; later tuning leaves the preset unchanged.", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(6);
        }
        private void SaveClothSettingsPreset(string path)
        {
            EndClothStroke();
                        path = AssetDatabase.GenerateUniqueAssetPath(path);
                        var preset = CreateInstance<MashBoxClothSettingsPreset>();
                        try
                        {
                            preset.Capture(clothProfile);
                            AssetDatabase.CreateAsset(preset, path);
                            AssetDatabase.SaveAssetIfDirty(preset);
                            clothSettingsPreset = preset;
                        }
                        catch { if (!AssetDatabase.Contains(preset)) DestroyImmediate(preset); throw; }
        }

#if MashBoxDev
        private void SaveClothSettingsPresetToSdk()
        {
            const string packageRoot = "Packages/com.mg.mashbox.sdk";
            const string relativeFolder = "ContentTools/AnimationStudio/ClothPresets";
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packageRoot + "/package.json");
            if (package == null || (package.source != UnityEditor.PackageManager.PackageSource.Local &&
                package.source != UnityEditor.PackageManager.PackageSource.Embedded))
                throw new System.InvalidOperationException("Saving SDK presets requires a local or embedded, writable MashBox SDK package.");
            string folder = packageRoot;
            foreach (var segment in relativeFolder.Split('/'))
            {
                string next = folder + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next))
                {
                    if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(folder, segment)))
                        throw new System.IO.IOException("Could not create SDK preset folder: " + next);
                }
                folder = next;
            }
            string physicalFolder = System.IO.Path.GetFullPath(System.IO.Path.Combine(package.resolvedPath, relativeFolder));
            string path = EditorUtility.SaveFilePanel("Save cloth settings preset to SDK", physicalFolder,
                ClothRenderer.name + " Settings", "asset");
            if (string.IsNullOrEmpty(path)) return;
            path = System.IO.Path.GetFullPath(path);
            string prefix = physicalFolder.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(System.IO.Path.GetExtension(path), ".asset", System.StringComparison.OrdinalIgnoreCase))
                throw new System.InvalidOperationException("Choose an .asset file inside the SDK's ContentTools/AnimationStudio/ClothPresets folder.");
            SaveClothSettingsPreset(folder + "/" + path.Substring(prefix.Length).Replace(System.IO.Path.DirectorySeparatorChar, '/'));
        }
#endif
    }
}

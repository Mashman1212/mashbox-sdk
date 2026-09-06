#if UNITY_EDITOR

using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    [CustomEditor(typeof(MGDetailFoliagePalette))]
    public sealed class MGDetailFoliagePaletteEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var palette = (MGDetailFoliagePalette)target;
            EditorGUILayout.HelpBox(
                "A palette distributes one painted MG Terrain density mask into deterministic grass variants and natural clumps. Assign a Prefab, or both Mesh and Material, to every slot you want baked.",
                MessageType.Info);
            DrawDefaultInspector();

            int valid = 0;
            for (int index = 0; index < palette.Entries.Count; index++)
            {
                MGDetailFoliagePalette.Entry entry = palette.Entries[index];
                if (entry != null && entry.Enabled && entry.HasRenderablePrototype && entry.Weight > 0f)
                    valid++;
            }
            EditorGUILayout.LabelField("Renderable Entries", $"{valid:N0} / {palette.Entries.Count:N0}");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Randomize Seed"))
                {
                    Undo.RecordObject(palette, "Randomize Detail Foliage Palette Seed");
                    palette.RandomizeSeed();
                    EditorUtility.SetDirty(palette);
                }
                if (GUILayout.Button("Reset Natural Slots...")
                    && EditorUtility.DisplayDialog(
                        "Reset Natural Foliage Slots?",
                        "Replace this palette's entries with six tuned starter roles? Existing prefab assignments and per-entry settings will be removed. This can be undone.",
                        "Reset Slots",
                        "Cancel"))
                {
                    Undo.RecordObject(palette, "Reset Detail Foliage Palette Slots");
                    palette.ConfigureNaturalStarterSet();
                    EditorUtility.SetDirty(palette);
                }
            }
        }
    }
}

#endif

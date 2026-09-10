#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    internal static partial class MGGrassDetailLayers
    {
        internal static int SharedIndex(MGTerrain terrain, int prototype, int population)
        {
            for (int i = 0; i < terrain.DensityDetailLayerCount; i++)
            {
                var layer = terrain.DensityDetailLayers[i];
                if (layer.PrototypeIndex == prototype && layer.UsesGrassArray && layer.GrassIdMap != null && layer.GrassPopulation == population) return i;
            }
            return -1;
        }

        // Explicit authoring conversion: one density/ID pair per population, at most two.
        // Source map assets are never modified. Full component authoring data is backed up.
        internal static int EnsureShared(MGTerrain terrain, int prototype, int population)
        {
            population = Mathf.Clamp(population, 0, 1);
            int current = SharedIndex(terrain, prototype, population);
            if (current >= 0) return current;
            var sources = new List<int>();
            bool hasShared = false;
            int width = 512, height = 512;
            for (int i = 0; i < terrain.DensityDetailLayerCount; i++)
            {
                var layer = terrain.DensityDetailLayers[i];
                if (layer.PrototypeIndex != prototype || layer.PaletteSourceOnly) continue;
                if (layer.GrassIdMap != null) { hasShared = true; width = layer.DensityMap.width; height = layer.DensityMap.height; continue; }
                if (layer.GeneratedByPalette != null) throw new InvalidOperationException("Convert palette-generated grass to standalone paint before using shared populations.");
                sources.Add(i);
                if (layer.DensityMap != null)
                {
                    if (!layer.DensityMap.isReadable || layer.DensityMap.format != TextureFormat.R16) throw new InvalidOperationException("Shared grass migration requires readable R16 density maps.");
                    width = Mathf.Max(width, layer.DensityMap.width); height = Mathf.Max(height, layer.DensityMap.height);
                }
            }
            if (hasShared && sources.Count > 0) throw new InvalidOperationException("This prototype mixes shared and legacy layers. Restore its migration backup before converting again.");
            var counts = new[] { new ushort[width * height], new ushort[width * height] };
            var ids = new[] { new byte[width * height], new byte[width * height] };
            var sizes = new[] { new ushort[width * height], new ushort[width * height] };
            ushort one = Mathf.FloatToHalf(1);
            for (int i = 0; i < width * height; i++) { sizes[0][i] = one; sizes[1][i] = one; }
            foreach (int source in sources)
            {
                var layer = terrain.DensityDetailLayers[source];
                if (!layer.RenderingEnabled || layer.DensityMap == null) continue;
                var density = layer.DensityMap.GetPixelData<ushort>(0);
                for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
                {
                    int index = z * width + x;
                    int sx = x * layer.DensityMap.width / width, sz = z * layer.DensityMap.height / height;
                    ushort value = density[sz * layer.DensityMap.width + sx];
                    if (value == 0) continue;
                    ushort size = layer.SizeMap != null && layer.SizeMap.isReadable
                        ? Mathf.FloatToHalf(Mathf.Clamp(layer.SizeMap.GetPixelBilinear((x + .5f) / width, (z + .5f) / height).r, .05f, 4f)) : one;
                    MGGrassPopulationMerge.Add(index, value, (byte)layer.TextureSlice, size, counts, ids, sizes);
                }
            }
            const string backupFolder = "Assets/Editor/MGGrassMigrationBackups";
            if (!AssetDatabase.IsValidFolder("Assets/Editor")) AssetDatabase.CreateFolder("Assets", "Editor");
            if (!AssetDatabase.IsValidFolder(backupFolder)) AssetDatabase.CreateFolder("Assets/Editor", "MGGrassMigrationBackups");
            string backup = AssetDatabase.GenerateUniqueAssetPath(backupFolder + "/GrassPopulation_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".json");
            File.WriteAllText(backup, EditorJsonUtility.ToJson(terrain, true));
            AssetDatabase.ImportAsset(backup);
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Convert to Shared Grass Populations");
            Undo.RegisterCompleteObjectUndo(terrain, "Convert to Shared Grass Populations");
            var serialized = new SerializedObject(terrain);
            var layers = serialized.FindProperty("m_DensityDetailLayers");
            int template = sources.Count > 0 ? sources[0] : SharedIndex(terrain, prototype, 0);
            bool secondOccupied = Array.Exists(counts[1], count => count > 0);
            for (int group = 0; group < 2; group++)
            {
                if (hasShared && group != population || !hasShared && group == 1 && population != 1) continue;
                int index = layers.arraySize++;
                var layer = layers.GetArrayElementAtIndex(index);
                if (template >= 0) MGTerrainSettingsCopy.CopyValue(layers.GetArrayElementAtIndex(template), layer);
                else foreach (string field in new[] { "m_MinWidth", "m_MaxWidth", "m_MinHeight", "m_MaxHeight", "m_SizeMultiplier", "m_WidthMultiplier", "m_HeightMultiplier", "m_WindMultiplier" }) layer.FindPropertyRelative(field).floatValue = 1;
                layer.FindPropertyRelative("m_PrototypeIndex").intValue = prototype;
                layer.FindPropertyRelative("m_UseGrassArray").boolValue = true;
                layer.FindPropertyRelative("m_GrassPopulation").intValue = group;
                layer.FindPropertyRelative("m_RenderDisabled").boolValue = false;
                layer.FindPropertyRelative("m_PaletteSourceOnly").boolValue = false;
                layer.FindPropertyRelative("m_GeneratedByPalette").objectReferenceValue = null;
                layer.FindPropertyRelative("m_PaletteSourceMap").objectReferenceValue = null;
                layer.FindPropertyRelative("m_PaletteEntryIndex").intValue = -1;
                layer.FindPropertyRelative("m_Seed").intValue = UnityEngine.Random.Range(1, int.MaxValue);
                layer.FindPropertyRelative("m_DensityMap").objectReferenceValue = SaveSharedMap(width, height, TextureFormat.R16, "GrassDensity" + group, counts[group]);
                layer.FindPropertyRelative("m_GrassIdMap").objectReferenceValue = SaveSharedMap(width, height, TextureFormat.R8, "GrassIDs" + group, ids[group]);
                layer.FindPropertyRelative("m_SizeMap").objectReferenceValue = SaveSharedMap(width, height, TextureFormat.RHalf, "GrassSizes" + group, sizes[group]);
                long total = 0; foreach (ushort count in counts[group]) total += count;
                layer.FindPropertyRelative("m_RepresentedInstanceCount").longValue = total;
            }
            for (int i = sources.Count - 1; i >= 0; i--) layers.DeleteArrayElementAtIndex(sources[i]);
            serialized.ApplyModifiedProperties();
            terrain.InvalidateRenderCache();
            EditorUtility.SetDirty(terrain);
            Undo.CollapseUndoOperations(undo);
            return SharedIndex(terrain, prototype, population);
        }

        static Texture2D SaveSharedMap<T>(int width, int height, TextureFormat format, string name, T[] data) where T : struct
        {
            const string folder = "Assets/MGTerrainDetailPaint";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "MGTerrainDetailPaint");
            var map = new Texture2D(width, height, format, false, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            map.SetPixelData(data, 0); map.Apply(false, false);
            AssetDatabase.CreateAsset(map, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".asset"));
            Undo.RegisterCreatedObjectUndo(map, "Create Shared Grass Map");
            return map;
        }
    }
}
#endif
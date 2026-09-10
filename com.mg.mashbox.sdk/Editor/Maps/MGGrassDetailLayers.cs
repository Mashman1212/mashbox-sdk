#if UNITY_EDITOR
using System;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    internal static partial class MGGrassDetailLayers
    {
        [Serializable] sealed class ArraySources { public SourceLayer[] layers; }
        [Serializable] sealed class SourceLayer { public string name; public string[] textures; }

        internal static bool SlotInfo(Material material, int id, out string name, out Texture2D texture)
        {
            name = "Sub-ID " + id;
            texture = null;
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(material));
            if (importer == null) return false;
            const string marker = "MGGrassArrays=";
            foreach (string line in importer.userData.Split('\n'))
            {
                if (!line.StartsWith(marker, StringComparison.Ordinal)) continue;
                ArraySources data;
                try { data = JsonUtility.FromJson<ArraySources>(line.Substring(marker.Length)); }
                catch (ArgumentException) { continue; }
                if (data?.layers == null || id >= data.layers.Length) return false;
                var slot = data.layers[id];
                if (slot == null) return false;
                if (!string.IsNullOrEmpty(slot.name)) name = slot.name;
                if (slot.textures != null && slot.textures.Length > 0 && !string.IsNullOrEmpty(slot.textures[0]))
                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(slot.textures[0]));
                return texture != null;
            }
            return false;
        }
        internal static int ConfiguredMask(Material material)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(material));
            if (importer == null) return 0;
            const string marker = "MGGrassArrays=";
            foreach (string line in importer.userData.Split('\n'))
            {
                if (!line.StartsWith(marker, StringComparison.Ordinal)) continue;
                ArraySources data;
                try { data = JsonUtility.FromJson<ArraySources>(line.Substring(marker.Length)); }
                catch (ArgumentException) { continue; }
                int mask = 0;
                if (data?.layers == null) return mask;
                for (int id = 0; id < Mathf.Min(8, data.layers.Length); id++)
                {
                    var textures = data.layers[id]?.textures;
                    if (textures != null && textures.Length > 0 && !string.IsNullOrEmpty(textures[0])
                        && !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(textures[0]))) mask |= 1 << id;
                }
                return mask;
            }
            return 0;
        }

        internal static int MissingMask(MGTerrain terrain, int prototype, int configured)
        {
            foreach (var layer in terrain.DensityDetailLayers)
                if (layer != null && layer.PrototypeIndex == prototype && layer.UsesGrassArray)
                    configured &= ~(1 << layer.TextureSlice);
            return configured;
        }
        internal static int FirstUnused(MGTerrain terrain, int prototype)
        {
            for (int id = 0; id < 8; id++)
            {
                bool used = false;
                foreach (var layer in terrain.DensityDetailLayers)
                    used |= layer.PrototypeIndex == prototype && layer.UsesGrassArray && layer.TextureSlice == id;
                if (!used) return id;
            }
            return -1;
        }

        internal static int Create(MGTerrain terrain, int prototype, int subId)
        {
            subId = Mathf.Clamp(subId, 0, 7);
            for (int i = 0; i < terrain.DensityDetailLayerCount; i++)
                if (terrain.DensityDetailLayers[i].PrototypeIndex == prototype && terrain.DensityDetailLayers[i].UsesGrassArray && terrain.DensityDetailLayers[i].TextureSlice == subId) return i;
            var serialized = new SerializedObject(terrain);
            var layers = serialized.FindProperty("m_DensityDetailLayers");
            int sourceIndex = -1;
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).FindPropertyRelative("m_PrototypeIndex").intValue == prototype) { sourceIndex = i; break; }
            Undo.RegisterCompleteObjectUndo(terrain, "Add Grass Sub-ID");
            int index = layers.arraySize++;
            var layer = layers.GetArrayElementAtIndex(index);
            if (sourceIndex >= 0) MGTerrainSettingsCopy.CopyValue(layers.GetArrayElementAtIndex(sourceIndex), layer);
            layer.FindPropertyRelative("m_PrototypeIndex").intValue = prototype;
            layer.FindPropertyRelative("m_UseGrassArray").boolValue = true;
            layer.FindPropertyRelative("m_TextureSlice").intValue = subId;
            layer.FindPropertyRelative("m_SizeMap").objectReferenceValue = null;
            layer.FindPropertyRelative("m_RenderDisabled").boolValue = false;
            layer.FindPropertyRelative("m_PaletteSourceOnly").boolValue = false;
            layer.FindPropertyRelative("m_GeneratedByPalette").objectReferenceValue = null;
            layer.FindPropertyRelative("m_PaletteSourceMap").objectReferenceValue = null;
            layer.FindPropertyRelative("m_PaletteEntryIndex").intValue = -1;
            layer.FindPropertyRelative("m_RepresentedInstanceCount").longValue = 0;
            layer.FindPropertyRelative("m_Seed").intValue = UnityEngine.Random.Range(1, int.MaxValue);
            if (sourceIndex < 0)
            {
                foreach (string field in new[] { "m_MinWidth", "m_MaxWidth", "m_MinHeight", "m_MaxHeight", "m_SizeMultiplier", "m_WidthMultiplier", "m_HeightMultiplier", "m_WindMultiplier" }) layer.FindPropertyRelative(field).floatValue = 1;
                layer.FindPropertyRelative("m_ShaderTint").colorValue = Color.white;
                layer.FindPropertyRelative("m_YOffset").floatValue = 0;
            }
            var sourceMap = layer.FindPropertyRelative("m_DensityMap").objectReferenceValue as Texture2D;
            var map = new Texture2D(sourceMap != null ? sourceMap.width : 512, sourceMap != null ? sourceMap.height : 512, TextureFormat.R16, false, true)
            { name = terrain.name + "_GrassSubID_" + subId, wrapMode = TextureWrapMode.Clamp };
            var values = map.GetPixelData<ushort>(0);
            for (int i = 0; i < values.Length; i++) values[i] = 0;
            map.Apply(false, false);
            const string folder = "Assets/MGTerrainDetailPaint";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "MGTerrainDetailPaint");
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/GrassSubID_" + subId + ".asset");
            try { AssetDatabase.CreateAsset(map, path); }
            catch { UnityEngine.Object.DestroyImmediate(map); throw; }
            Undo.RegisterCreatedObjectUndo(map, "Create Grass Sub-ID Density Map");
            layer.FindPropertyRelative("m_DensityMap").objectReferenceValue = map;
            serialized.ApplyModifiedProperties();
            terrain.InvalidateRenderCache();
            EditorUtility.SetDirty(terrain);
            SceneView.RepaintAll();
            return index;
        }
    }
}
#endif

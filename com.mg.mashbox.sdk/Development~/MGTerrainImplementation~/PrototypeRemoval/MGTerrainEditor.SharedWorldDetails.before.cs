#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        string m_SharedDefinitionSignature;
        readonly HashSet<string> m_SharedLayerIds = new HashSet<string>();
        readonly HashSet<MGTerrain> m_SharedTiles = new HashSet<MGTerrain>();

        void PrepareSharedWorldDetails(MGTerrain source)
        {
            if (source.World == null) return;
            var tiles = SharedTiles(source).ToArray();
            if (m_SharedDefinitionSignature != null && m_SharedTiles.SetEquals(tiles)) return;
            FinishDetailStroke();
            // Merge the existing definitions before choosing a common setup. Painted maps never move.
            SharedIds(source);
            foreach (var tile in tiles)
                if (tile != source) MergeSharedDefinitions(tile, source, false, null);
            SharedIds(source);
            foreach (var tile in tiles)
                if (tile != source) MergeSharedDefinitions(source, tile, true, null);
            m_SharedTiles.Clear();
            foreach (var tile in tiles) m_SharedTiles.Add(tile);
            RememberSharedDefinitions(source);
        }

        void CommitSharedWorldDetails(MGTerrain source)
        {
            if (source.World == null) return;
            SharedIds(source);
            string signature = SharedSignature(source);
            if (signature == m_SharedDefinitionSignature) return;
            var remaining = new HashSet<string>(source.DensityDetailLayers.Select(l => l.WorldDetailId));
            var removed = new HashSet<string>(m_SharedLayerIds.Where(id => !remaining.Contains(id)));
            foreach (var tile in SharedTiles(source))
                if (tile != source) MergeSharedDefinitions(source, tile, true, removed);
            RememberSharedDefinitions(source);
            SceneView.RepaintAll();
        }

        static void ResetSharedWorldPainting(MGTerrain source, int index)
        {
            foreach (var tile in SharedTiles(source))
            {
                if (tile == source) continue;
                int match = FindWorldPaintLayer(source, index, tile);
                if (match < 0) continue;
                using var data = new SerializedObject(tile);
                ResetDetailPainting(data.FindProperty("m_DensityDetailLayers").GetArrayElementAtIndex(match));
                data.ApplyModifiedProperties();
                tile.InvalidateRenderCache();
            }
        }

        void RememberSharedDefinitions(MGTerrain source)
        {
            m_SharedDefinitionSignature = SharedSignature(source);
            m_SharedLayerIds.Clear();
            foreach (var layer in source.DensityDetailLayers) m_SharedLayerIds.Add(layer.WorldDetailId);
        }

        static IEnumerable<MGTerrain> SharedTiles(MGTerrain source) =>
            source.World.GetComponentsInChildren<MGTerrain>(true)
                .Where(t => t.GetComponentInParent<MGTerrainWorld>(true) == source.World);

        static readonly HashSet<string> LocalDetailData = new HashSet<string>
        { "m_PrototypeIndex", "m_DensityMap", "m_SizeMap", "m_GrassIdMap", "m_PaletteSourceMap", "m_RepresentedInstanceCount" };

        static void SharedIds(MGTerrain tile)
        {
            using var data = new SerializedObject(tile);
            foreach (string list in new[] { "m_Prototypes", "m_DensityDetailLayers" })
            {
                var array = data.FindProperty(list);
                var used = new HashSet<string>();
                for (int i = 0; i < array.arraySize; i++)
                {
                    var id = array.GetArrayElementAtIndex(i).FindPropertyRelative("m_WorldDetailId");
                    if (string.IsNullOrEmpty(id.stringValue) || !used.Add(id.stringValue))
                    { id.stringValue = Guid.NewGuid().ToString("N"); used.Add(id.stringValue); }
                }
            }
            data.ApplyModifiedProperties();
        }

        static int SharedFind(SerializedProperty array, HashSet<int> used, Func<SerializedProperty, bool> predicate)
        {
            for (int i = 0; i < array.arraySize; i++)
                if (!used.Contains(i) && predicate(array.GetArrayElementAtIndex(i))) return i;
            return -1;
        }
        static string SharedId(SerializedProperty entry) => entry.FindPropertyRelative("m_WorldDetailId").stringValue;
        static bool SharedSame(SerializedProperty a, SerializedProperty b, string field)
        { return SerializedProperty.DataEquals(a.FindPropertyRelative(field), b.FindPropertyRelative(field)); }

        static void MergeSharedDefinitions(MGTerrain source, MGTerrain destination, bool overwrite, HashSet<string> removed)
        {
            using var from = new SerializedObject(source);
            using var to = new SerializedObject(destination);
            var prototypes = to.FindProperty("m_Prototypes");
            var sourcePrototypes = from.FindProperty("m_Prototypes");
            var used = new HashSet<int>();
            var remap = new Dictionary<int, int>();
            for (int i = 0; i < sourcePrototypes.arraySize; i++)
            {
                var a = sourcePrototypes.GetArrayElementAtIndex(i);
                int match = SharedFind(prototypes, used, b => !string.IsNullOrEmpty(SharedId(a)) && SharedId(a) == SharedId(b));
                if (match < 0) match = SharedFind(prototypes, used, b =>
                    SharedSame(a,b,"m_Prefab") && SharedSame(a,b,"m_Mesh") && SharedSame(a,b,"m_Material") && SharedSame(a,b,"m_Kind"));
                bool added = match < 0;
                if (added) match = prototypes.arraySize++;
                if (added || overwrite) MGTerrainSettingsCopy.CopyValue(a, prototypes.GetArrayElementAtIndex(match));
                used.Add(match); remap[i] = match;
            }
            var layers = to.FindProperty("m_DensityDetailLayers");
            if (removed != null)
                for (int i = layers.arraySize - 1; i >= 0; i--)
                    if (removed.Contains(SharedId(layers.GetArrayElementAtIndex(i)))) layers.DeleteArrayElementAtIndex(i);
            used.Clear();
            var sourceLayers = from.FindProperty("m_DensityDetailLayers");
            for (int i = 0; i < sourceLayers.arraySize; i++)
            {
                var a = sourceLayers.GetArrayElementAtIndex(i);
                if (!remap.TryGetValue(a.FindPropertyRelative("m_PrototypeIndex").intValue, out int prototype)) continue;
                int match = SharedFind(layers, used, b => !string.IsNullOrEmpty(SharedId(a)) && SharedId(a) == SharedId(b));
                if (match < 0) match = SharedFind(layers, used, b =>
                    b.FindPropertyRelative("m_PrototypeIndex").intValue == prototype
                    && SharedSame(a,b,"m_GeneratedByPalette") && SharedSame(a,b,"m_PaletteEntryIndex")
                    && SharedSame(a,b,"m_PaletteSourceOnly") && SharedSame(a,b,"m_UseGrassArray")
                    && SharedSame(a,b,"m_GrassPopulation") && SharedSame(a,b,"m_TextureSlice"));
                bool added = match < 0;
                if (added) match = layers.arraySize++;
                var b = layers.GetArrayElementAtIndex(match);
                if (added || overwrite)
                {
                    if (added)
                    {
                        foreach (var field in LocalDetailData)
                        {
                            var local = b.FindPropertyRelative(field);
                            if (local.propertyType == SerializedPropertyType.ObjectReference) local.objectReferenceValue = null;
                            else local.longValue = 0;
                        }
                    }
                    var child = a.Copy();
                    if (child.Next(true)) do
                    {
                        if (child.depth != a.depth + 1) break;
                        if (!LocalDetailData.Contains(child.name))
                            MGTerrainSettingsCopy.CopyValue(child, b.FindPropertyRelative(child.name));
                    } while (child.Next(false));
                    b.FindPropertyRelative("m_PrototypeIndex").intValue = prototype;
                    if (b.FindPropertyRelative("m_GrassIdMap").objectReferenceValue == null
                        && a.FindPropertyRelative("m_GrassIdMap").objectReferenceValue is Texture2D sourceIds)
                    {
                        var ids = new Texture2D(sourceIds.width, sourceIds.height, TextureFormat.R8, false, true)
                        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                        var pixels = ids.GetPixelData<byte>(0);
                        for (int pixel = 0; pixel < pixels.Length; pixel++) pixels[pixel] = 0;
                        ids.Apply(false, false);
                        MGTerrainSceneAssets.Create(ids, destination, "Layer_" + match + "_GrassIDs");
                        b.FindPropertyRelative("m_GrassIdMap").objectReferenceValue = ids;
                    }
                }
                used.Add(match);
            }
            // Share palette definitions while preserving each area's source painting and bake state.
            var palettes = to.FindProperty("m_DetailFoliagePalettes");
            var sourcePalettes = from.FindProperty("m_DetailFoliagePalettes");
            used.Clear();
            for (int i = 0; i < sourcePalettes.arraySize; i++)
            {
                var a = sourcePalettes.GetArrayElementAtIndex(i);
                int match = SharedFind(palettes, used, b => SharedSame(a,b,"m_Palette"));
                bool added = match < 0;
                if (added) match = palettes.arraySize++;
                var b = palettes.GetArrayElementAtIndex(match);
                if (added || overwrite)
                {
                    var map = added ? null : b.FindPropertyRelative("m_SourceDensityMap").objectReferenceValue;
                    bool needsBake = added || b.FindPropertyRelative("m_NeedsBake").boolValue;
                    MGTerrainSettingsCopy.CopyValue(a,b);
                    b.FindPropertyRelative("m_SourceDensityMap").objectReferenceValue = map;
                    b.FindPropertyRelative("m_NeedsBake").boolValue = needsBake;
                }
                used.Add(match);
            }
            if (to.ApplyModifiedProperties())
            {
                destination.InvalidateRenderCache();
                EditorUtility.SetDirty(destination);
                if (destination.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(destination.gameObject.scene);
            }
        }

        static string SharedSignature(MGTerrain terrain)
        {
            using var data = new SerializedObject(terrain);
            var text = new StringBuilder();
            foreach (string name in new[] { "m_Prototypes", "m_DensityDetailLayers", "m_DetailFoliagePalettes" })
            {
                var array = data.FindProperty(name);
                text.Append(name).Append(array.arraySize);
                for (int i = 0; i < array.arraySize; i++)
                {
                    var entry = array.GetArrayElementAtIndex(i);
                    var child = entry.Copy();
                    if (child.Next(true)) do
                    {
                        if (child.depth != entry.depth + 1) break;
                        if (LocalDetailData.Contains(child.name) && child.name != "m_PrototypeIndex"
                            || child.name == "m_SourceDensityMap" || child.name == "m_NeedsBake") continue;
                        text.Append(child.name).Append(':');
                        if (child.propertyType == SerializedPropertyType.ObjectReference)
                            text.Append(child.objectReferenceInstanceIDValue);
                        else text.Append(child.boxedValue);
                        text.Append('|');
                    } while (child.Next(false));
                }
            }
            return text.ToString();
        }
    }
}
#endif

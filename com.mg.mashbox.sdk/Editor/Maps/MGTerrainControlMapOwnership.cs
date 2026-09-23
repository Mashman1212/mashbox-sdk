using System;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainControlMapOwnership
    {
        internal static Texture2D Source(MGTerrain tile, int channel)
        {
            var map = channel == 1 ? tile.ControlMap1 : tile.ControlMap2;
            if (map != null) return map;
            var material = tile.MeshRenderer != null ? tile.MeshRenderer.sharedMaterial : null;
            string property = "_ControlMap" + channel;
            return material != null && material.HasProperty(property) ? material.GetTexture(property) as Texture2D : null;
        }
        internal static void Ensure(MGTerrain tile, Material material, MGTerrainAssetTransaction transaction)
        {
            var original1 = tile.ControlMap1; var original2 = tile.ControlMap2;
            var source1 = Source(tile, 1); var source2 = Source(tile, 2);
            transaction.OnRollback(() => { if (tile != null) tile.SetControlMaps(original1, original2); });
            Texture2D Own(Texture2D source, int channel)
            {
                if (source == null) return null;
                string path = MGTerrainAssetStore.MapPath(tile, "ControlMap" + channel);
                if (AssetDatabase.GetAssetPath(source) == path) return source;
                // A native clone preserves pixels/format without requiring readable imported maps.
                path = MGTerrainAssetStore.PathFor(tile, "ControlMap" + channel);
                var copy = UnityEngine.Object.Instantiate(source);
                try { return MGTerrainAssetStore.Save(copy, path, transaction, true); }
                finally { if (!EditorUtility.IsPersistent(copy)) UnityEngine.Object.DestroyImmediate(copy); }
            }
            var first = Own(source1, 1); var second = Own(source2, 2);
            Undo.RecordObject(tile, "Repair Terrain Control Maps");
            Undo.RecordObject(material, "Repair Terrain Control Maps");
            tile.SetControlMaps(first, second);
            if (material.HasProperty("_ControlMap1")) material.SetTexture("_ControlMap1", first);
            if (material.HasProperty("_ControlMap2")) material.SetTexture("_ControlMap2", second);
            EditorUtility.SetDirty(tile); EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }
        internal static void Repair(MGTerrainWorld world)
        {
            if (Application.isPlaying || string.IsNullOrEmpty(world.gameObject.scene.path))
                throw new InvalidOperationException("Repair control maps in a saved scene outside Play Mode.");
            var tiles = world.GetComponentsInChildren<MGTerrain>(true)
                .Where(t => t.GetComponentInParent<MGTerrainWorld>(true) == world && t.MeshRenderer != null && t.MeshRenderer.sharedMaterial != null).ToArray();
            if (tiles.GroupBy(MGTerrainAssetStore.Prefix).Any(g => g.Count() > 1))
                throw new InvalidOperationException("Two tiles occupy the same grid coordinate. Resolve their overlap before repairing control maps.");
            using var transaction = new MGTerrainAssetTransaction();
            foreach (var tile in tiles)
            {
                var renderer = tile.MeshRenderer; var originals = renderer.sharedMaterials;
                transaction.OnRollback(() => { if(renderer != null) renderer.sharedMaterials = originals; });
                MGTerrainAssetStore.Material(tile, transaction);
            }
            transaction.Commit();
            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            Debug.Log($"Validated independent control maps and matching material bindings on {tiles.Length} terrain tiles.", world);
        }
    }
}

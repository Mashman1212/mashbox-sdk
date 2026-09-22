using System;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainStampMapSetup
    {
        internal static bool NeedsMaps(MGTerrain tile)
        {
            var material = tile != null && tile.MeshRenderer != null ? tile.MeshRenderer.sharedMaterial : null;
            return material == null || !material.HasProperty(MGTerrainAppearanceCaptureAssets.ColourProperty)
                || !material.HasProperty(MGTerrainAppearanceCaptureAssets.NormalProperty)
                || material.GetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty) == null
                || material.GetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty) == null;
        }

        // Blank maps have no dependency on cameras, HDRP or detail rendering.
        static Texture2D CreateBlank(MGTerrain tile, int width, int height, bool normal)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, normal)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            try
            {
                var pixels = new Color32[width * height];
                var fill = normal ? new Color32(128, 255, 128, 255) : new Color32(128, 128, 128, 255);
                for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
                texture.SetPixels32(pixels);
                texture.Apply(true, false);
                using var transaction = new MGTerrainAssetTransaction();
                var saved = MGTerrainAssetStore.SaveMap(texture, MGTerrainAssetStore.MapPath(tile, normal ? "Appearance_NormalWS" : "Appearance"), normal, transaction);
                transaction.Commit();
                if (saved != texture) UnityEngine.Object.DestroyImmediate(texture);
                return saved;
            }
            catch { if (!EditorUtility.IsPersistent(texture)) UnityEngine.Object.DestroyImmediate(texture); throw; }
        }
        internal static void Generate(MGTerrain tile, bool blank = false)
        {
            if (!NeedsMaps(tile)) return;
            var renderer = tile != null ? tile.MeshRenderer : null;
            var original = renderer != null ? renderer.sharedMaterial : null;
            if (original == null || !original.HasProperty(MGTerrainAppearanceCaptureAssets.ColourProperty)
                || !original.HasProperty(MGTerrainAppearanceCaptureAssets.NormalProperty))
                throw new InvalidOperationException("The terrain shader needs far-range RGB and world-normal slots (for example MG_Lit_Trail).");
            MGTerrainTileAuthoring.Validate(tile);
            var colour = original.GetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty);
            var normal = original.GetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty);
            using var transaction = new MGTerrainAssetTransaction();
            var slots = renderer.sharedMaterials;
            transaction.OnRollback(() => renderer.sharedMaterials = slots);
            var working = MGTerrainAssetStore.Material(tile, transaction);
            if (blank)
            {
                int resolution = tile.World != null ? tile.World.CaptureResolution : 2048;
                var existing = colour != null ? colour : normal;
                int width = existing != null ? existing.width : resolution, height = existing != null ? existing.height : resolution;
                if (colour == null) working.SetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty, CreateBlank(tile, width, height, false));
                if (normal == null) working.SetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty, CreateBlank(tile, width, height, true));
            }
            else
            {
                var editor = (MGTerrainEditor)Editor.CreateEditor(tile);
                try { editor.BakeStampBaseMaps(tile.World, MGTerrainAssetStore.MapPath(tile, "Appearance")); }
                finally { UnityEngine.Object.DestroyImmediate(editor); }
                if (colour != null) working.SetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty, colour);
                if (normal != null) working.SetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty, normal);
            }
            if (NeedsMaps(tile)) throw new InvalidOperationException("Appearance generation did not complete.");
            EditorUtility.SetDirty(working); AssetDatabase.SaveAssetIfDirty(working);
            EditorSceneManager.MarkSceneDirty(tile.gameObject.scene); transaction.Commit();
        }
    }

    public sealed partial class MGTerrainEditor
    {
        internal void BakeStampBaseMaps(MGTerrainWorld world, string path)
        {
            m_FarBakeResolution = world != null ? world.CaptureResolution : 2048;
            m_CaptureExposure = world != null ? world.CaptureExposure : 10;
            if (world != null && world.CaptureSyncSceneExposure && !MGTerrainSceneExposure.TryGet(out m_CaptureExposure, out string error))
                throw new InvalidOperationException(error);
            m_CaptureDetailTilt = world != null ? world.CaptureDetailTilt : 0;
            m_CaptureSyncTimeOfDay = true;
            m_CaptureNormalMap = true;
            CaptureTerrainAppearance((MGTerrain)target, false, path);
        }
    }
}

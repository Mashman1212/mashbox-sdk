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

        internal static void Generate(MGTerrain tile)
        {
            if (!NeedsMaps(tile)) return;
            var renderer = tile != null ? tile.MeshRenderer : null;
            var original = renderer != null ? renderer.sharedMaterial : null;
            if (original == null || !original.HasProperty(MGTerrainAppearanceCaptureAssets.ColourProperty)
                || !original.HasProperty(MGTerrainAppearanceCaptureAssets.NormalProperty))
                throw new InvalidOperationException("The terrain shader needs far-range RGB and world-normal slots (for example MG_Lit_Trail).");
            MGTerrainTileAuthoring.Validate(tile);
            const string folder = "Assets/MG Terrain Stamp Maps";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "MG Terrain Stamp Maps");
            var colour = original.GetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty);
            var normal = original.GetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty);
            var slots = renderer.sharedMaterials;
            var working = new Material(original) { name = tile.name + " Appearance" };
            var temporarySlots = (Material[])slots.Clone(); temporarySlots[0] = working;
            MGTerrainEditor editor = null;
            try
            {
                renderer.sharedMaterials = temporarySlots;
                editor = (MGTerrainEditor)Editor.CreateEditor(tile);
                string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + MGTerrainAppearanceCaptureAssets.TerrainPrefix(tile.name) + "BaseAppearance.png");
                editor.BakeStampBaseMaps(tile.World, path);
                if (working.GetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty) == null
                    || working.GetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty) == null)
                    throw new InvalidOperationException("Appearance-map generation did not complete. Check the terrain capture message and try again.");
                // The capture produces a matched pair, but never replace an existing authored map.
                if (colour != null) working.SetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty, colour);
                if (normal != null) working.SetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty, normal);
                AssetDatabase.CreateAsset(working, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + MGTerrainAppearanceCaptureAssets.TerrainPrefix(tile.name) + "Appearance.mat"));
                renderer.sharedMaterials = slots;
                Undo.RecordObject(renderer, "Generate Terrain Appearance Maps");
                renderer.sharedMaterials = temporarySlots;
                EditorUtility.SetDirty(renderer);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
            }
            catch { renderer.sharedMaterials = slots; throw; }
            finally
            {
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
                if (!EditorUtility.IsPersistent(working)) UnityEngine.Object.DestroyImmediate(working);
            }
        }
    }

    public sealed partial class MGTerrainEditor
    {
        internal void BakeStampBaseMaps(MGTerrainWorld world, string path)
        {
            m_FarBakeResolution = world != null ? world.CaptureResolution : 2048;
            m_CaptureExposure = world != null ? world.CaptureExposure : 10;
            m_CaptureDetailTilt = world != null ? world.CaptureDetailTilt : 0;
            m_CaptureSyncTimeOfDay = true;
            m_CaptureNormalMap = true;
            CaptureTerrainAppearance((MGTerrain)target, false, path);
        }
    }
}

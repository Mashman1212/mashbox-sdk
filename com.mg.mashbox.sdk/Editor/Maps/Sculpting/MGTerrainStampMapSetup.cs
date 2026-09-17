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
                string path = MGTerrainSceneAssets.UniquePath(tile, normal ? "BlankNormalWS" : "BlankRGB");
                texture.name = System.IO.Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(texture, path);
                return texture;
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
            var slots = renderer.sharedMaterials;
            var working = new Material(original) { name = tile.name + " Appearance" };
            var temporarySlots = (Material[])slots.Clone(); temporarySlots[0] = working;
            MGTerrainEditor editor = null;
            try
            {
                renderer.sharedMaterials = temporarySlots;
                if (blank)
                {
                    int resolution = tile.World != null ? Mathf.Clamp(tile.World.CaptureResolution, 32, 4096) : 2048;
                    var existing = colour != null ? colour : normal;
                    int width = existing != null ? existing.width : resolution;
                    int height = existing != null ? existing.height : resolution;
                    if (colour == null) working.SetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty,
                        CreateBlank(tile, width, height, false));
                    if (normal == null) working.SetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty,
                        CreateBlank(tile, width, height, true));
                }
                else
                {
                    editor = (MGTerrainEditor)Editor.CreateEditor(tile);
                    string path = MGTerrainSceneAssets.UniquePath(tile, "BaseAppearance", ".png");
                    editor.BakeStampBaseMaps(tile.World, path);
                }
                if (working.GetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty) == null
                    || working.GetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty) == null)
                    throw new InvalidOperationException("Appearance-map generation did not complete. Check the terrain capture message and try again.");
                // The capture produces a matched pair, but never replace an existing authored map.
                if (colour != null) working.SetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty, colour);
                if (normal != null) working.SetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty, normal);
                AssetDatabase.CreateAsset(working, MGTerrainSceneAssets.UniquePath(tile, "Appearance", ".mat"));
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

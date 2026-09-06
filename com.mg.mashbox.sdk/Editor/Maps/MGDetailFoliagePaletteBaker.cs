using System;
using System.Collections.Generic;
using System.IO;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    static class MGDetailFoliagePaletteBaker
    {
        internal readonly struct BakeResult
        {
            internal readonly int paletteCount;
            internal readonly int layerCount;
            internal readonly long instanceCount;
            internal readonly string error;

            internal BakeResult(int paletteCount, int layerCount, long instanceCount, string error)
            {
                this.paletteCount = paletteCount;
                this.layerCount = layerCount;
                this.instanceCount = instanceCount;
                this.error = error;
            }
        }

        internal static BakeResult BakeAll(MGTerrain terrain, bool randomizeSeeds)
        {
            if (terrain == null)
                return new BakeResult(0, 0, 0L, "No MG Terrain was supplied.");

            int palettes = 0;
            int layers = 0;
            long instances = 0L;
            var errors = new List<string>();
            Undo.RecordObject(terrain, randomizeSeeds ? "Reseed MG Detail Foliage Palettes" : "Bake MG Detail Foliage Palettes");
            try
            {
                AssetDatabase.StartAssetEditing();
                IReadOnlyList<MGTerrain.DetailFoliagePaletteBinding> bindings = terrain.DetailFoliagePalettes;
                for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                {
                    MGTerrain.DetailFoliagePaletteBinding binding = bindings[bindingIndex];
                    if (binding == null || !binding.Enabled)
                        continue;
                    try
                    {
                        if (binding.SourceDensityMap == null
                            && !terrain.TryAssignAutomaticDetailFoliageSource(binding, out int sourceChoiceCount))
                        {
                            errors.Add(sourceChoiceCount > 1
                                ? $"Palette binding {bindingIndex + 1} has no Source Density Map. This terrain has {sourceChoiceCount:N0} painted density maps, so choose the intended master mask in the binding."
                                : $"Palette binding {bindingIndex + 1} has no Source Density Map and this terrain has no existing painted density layer to use automatically.");
                            continue;
                        }
                        if (randomizeSeeds)
                            terrain.RandomizeDetailFoliagePaletteSeed(binding);
                        string error = BakeBinding(terrain, binding, bindingIndex, out int createdLayers, out long represented);
                        if (!string.IsNullOrEmpty(error))
                        {
                            errors.Add(error);
                            continue;
                        }
                        palettes++;
                        layers += createdLayers;
                        instances += represented;
                    }
                    catch (Exception exception)
                    {
                        if (binding.SourceDensityMap != null)
                            terrain.SetDensityMapPaletteSourceOnly(binding.SourceDensityMap, false);
                        errors.Add($"Palette binding {bindingIndex + 1}: {exception.Message}");
                    }
                }
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                terrain.InvalidateRenderCache();
                EditorUtility.SetDirty(terrain);
                SceneView.RepaintAll();
            }

            return new BakeResult(palettes, layers, instances, string.Join("\n", errors));
        }

        internal static int CountExistingGeneratedLayers(MGTerrain terrain)
        {
            int count = 0;
            IReadOnlyList<MGTerrain.DetailFoliagePaletteBinding> bindings = terrain.DetailFoliagePalettes;
            for (int index = 0; index < bindings.Count; index++)
            {
                MGTerrain.DetailFoliagePaletteBinding binding = bindings[index];
                if (binding != null && binding.Palette != null && binding.SourceDensityMap != null)
                    count += terrain.CountGeneratedDensityDetailLayers(binding.Palette, binding.SourceDensityMap);
            }
            return count;
        }

        internal static int ClearGeneratedLayers(MGTerrain terrain)
        {
            if (terrain == null)
                return 0;
            Undo.RecordObject(terrain, "Clear MG Detail Foliage Palette Bake");
            int removed = 0;
            IReadOnlyList<MGTerrain.DetailFoliagePaletteBinding> bindings = terrain.DetailFoliagePalettes;
            for (int index = 0; index < bindings.Count; index++)
            {
                MGTerrain.DetailFoliagePaletteBinding binding = bindings[index];
                if (binding == null || binding.Palette == null || binding.SourceDensityMap == null)
                    continue;
                removed += terrain.RemoveGeneratedDensityDetailLayers(binding.Palette, binding.SourceDensityMap);
                terrain.SetDensityMapPaletteSourceOnly(binding.SourceDensityMap, false);
            }
            terrain.InvalidateRenderCache();
            EditorUtility.SetDirty(terrain);
            SceneView.RepaintAll();
            return removed;
        }

        static string BakeBinding(
            MGTerrain terrain,
            MGTerrain.DetailFoliagePaletteBinding binding,
            int bindingIndex,
            out int createdLayers,
            out long representedInstances)
        {
            createdLayers = 0;
            representedInstances = 0L;
            MGDetailFoliagePalette palette = binding.Palette;
            Texture2D source = binding.SourceDensityMap;
            if (palette == null)
                return $"Palette binding {bindingIndex + 1} has no palette asset.";
            if (source == null)
                return $"Palette '{palette.name}' has no Source Density Map.";
            if (source.format != TextureFormat.R16 || !source.isReadable)
                return $"Palette '{palette.name}' requires a readable R16 Source Density Map. '{source.name}' is {source.format}.";
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath))
                return $"Source map '{source.name}' must be saved as a project asset before it can be baked.";

            IReadOnlyList<MGDetailFoliagePalette.Entry> entries = palette.Entries;
            int validEntries = 0;
            for (int index = 0; index < entries.Count; index++)
                if (entries[index] != null && entries[index].Enabled && entries[index].HasRenderablePrototype && entries[index].Weight > 0f)
                    validEntries++;
            if (validEntries == 0 && terrain.TryGetDensityDetailPrototype(source, out MGTerrain.Prototype sourcePrototype))
            {
                Undo.RecordObject(palette, "Seed MG Detail Foliage Palette Prototype");
                if (palette.TrySeedPrimaryPrototype(sourcePrototype))
                {
                    EditorUtility.SetDirty(palette);
                    validEntries = 1;
                }
            }
            if (validEntries == 0)
                return $"Palette '{palette.name}' has no enabled entries with a prefab or mesh/material prototype.";

            var sourceNative = source.GetRawTextureData<ushort>();
            var sourceValues = new ushort[sourceNative.Length];
            for (int index = 0; index < sourceValues.Length; index++)
                sourceValues[index] = sourceNative[index];

            terrain.RemoveGeneratedDensityDetailLayers(palette, source);
            terrain.SetDensityMapPaletteSourceOnly(source, true);
            var outputValues = new ushort[entries.Count][];
            var outputCounts = new long[entries.Count];
            var weights = new float[entries.Count];
            for (int index = 0; index < entries.Count; index++)
                if (entries[index] != null && entries[index].Enabled && entries[index].HasRenderablePrototype && entries[index].Weight > 0f)
                    outputValues[index] = new ushort[sourceValues.Length];

            SurfaceSampler surface = new SurfaceSampler(terrain);
            int width = source.width;
            int height = source.height;
            int[] occupancyIntegral = BuildOccupancyIntegral(sourceValues, width, height);
            int seed = unchecked(palette.Seed * 397 ^ binding.SeedOffset);
            for (int z = 0; z < height; z++)
            {
                if ((z & 63) == 0)
                    EditorUtility.DisplayProgressBar("Baking Detail Foliage Palette", $"{terrain.name} / {palette.name}", (float)z / height);
                for (int x = 0; x < width; x++)
                {
                    int valueIndex = z * width + x;
                    int sourceCount = sourceValues[valueIndex];
                    if (sourceCount <= 0)
                        continue;

                    float interior = CalculateInterior(
                        occupancyIntegral,
                        width,
                        height,
                        x,
                        z,
                        palette.EdgeFeatherCells);
                    float edgeAmount = 1f - interior;
                    float breakupNoise = Mathf.Pow(
                        FractalNoise((x + 0.5f) / palette.BreakupScale, (z + 0.5f) / palette.BreakupScale, seed),
                        palette.BreakupContrast);
                    float breakup = Mathf.Lerp(
                        1f,
                        Mathf.Lerp(palette.MinimumBreakupDensity, 1f, breakupNoise),
                        palette.BreakupStrength);
                    float edgeFeather = Mathf.Lerp(1f, Mathf.SmoothStep(0.15f, 1f, interior), palette.EdgeFeather);
                    float targetCount = sourceCount * palette.OverallDensity * breakup * edgeFeather;
                    if (targetCount <= 0f)
                        continue;

                    surface.Sample(x, z, width, height, out float slope, out float worldHeight);
                    float weightSum = 0f;
                    for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                    {
                        MGDetailFoliagePalette.Entry entry = entries[entryIndex];
                        if (outputValues[entryIndex] == null
                            || slope < entry.MinSlope || slope > entry.MaxSlope
                            || worldHeight < entry.MinWorldHeight || worldHeight > entry.MaxWorldHeight)
                        {
                            weights[entryIndex] = 0f;
                            continue;
                        }

                        int entrySeed = unchecked(seed ^ entry.SeedOffset ^ (entryIndex + 1) * 92821);
                        float clump = Mathf.Pow(
                            FractalNoise((x + 0.5f) / entry.ClumpSize, (z + 0.5f) / entry.ClumpSize, entrySeed),
                            entry.ClumpContrast);
                        float spatial = Mathf.Lerp(1f, Mathf.Lerp(0.02f, 2f, clump), entry.ClumpStrength);
                        if (entry.EdgeBias > 0f)
                            spatial *= Mathf.Lerp(1f, Mathf.Lerp(0.15f, 2.25f, edgeAmount), entry.EdgeBias);
                        else if (entry.EdgeBias < 0f)
                            spatial *= Mathf.Lerp(1f, Mathf.Lerp(0.15f, 1.75f, interior), -entry.EdgeBias);
                        float weight = entry.Weight * spatial;
                        weights[entryIndex] = weight;
                        weightSum += weight;
                    }
                    if (weightSum <= 0f)
                        continue;

                    for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                    {
                        if (weights[entryIndex] <= 0f)
                            continue;
                        MGDetailFoliagePalette.Entry entry = entries[entryIndex];
                        float exact = targetCount * (weights[entryIndex] / weightSum) * entry.DensityMultiplier;
                        int count = Mathf.FloorToInt(exact);
                        float fraction = exact - count;
                        uint stochastic = Hash((uint)(seed ^ valueIndex * 19349663 ^ entryIndex * 73856093));
                        if ((stochastic & 0x00ffffffu) / 16777216f < fraction)
                            count++;
                        count = Mathf.Clamp(count, 0, ushort.MaxValue);
                        outputValues[entryIndex][valueIndex] = (ushort)count;
                        outputCounts[entryIndex] += count;
                    }
                }
            }

            string folder = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                MGDetailFoliagePalette.Entry entry = entries[entryIndex];
                if (outputValues[entryIndex] == null || outputCounts[entryIndex] <= 0L)
                    continue;
                int prototypeIndex = entry.Prefab != null
                    ? terrain.FindOrAddPrototype(entry.Prefab, MGTerrain.InstanceKind.Detail)
                    : terrain.FindOrAddPrototype(entry.Mesh, entry.Material, MGTerrain.InstanceKind.Detail);
                if (prototypeIndex < 0)
                    continue;

                string assetName = $"{Sanitize(source.name)}__{Sanitize(palette.name)}__{entryIndex:00}_{Sanitize(entry.Name)}";
                string assetPath = $"{folder}/{assetName}.asset";
                Texture2D output = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (output == null || output.format != TextureFormat.R16 || output.width != width || output.height != height)
                {
                    if (output != null)
                        assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
                    output = new Texture2D(width, height, TextureFormat.R16, false, true) { name = assetName };
                    output.SetPixelData(outputValues[entryIndex], 0);
                    output.Apply(false, false);
                    AssetDatabase.CreateAsset(output, assetPath);
                }
                else
                {
                    output.SetPixelData(outputValues[entryIndex], 0);
                    output.Apply(false, false);
                    EditorUtility.SetDirty(output);
                }

                terrain.AddGeneratedDensityDetailLayer(
                    prototypeIndex,
                    output,
                    entry.MinWidth,
                    entry.MaxWidth,
                    entry.MinHeight,
                    entry.MaxHeight,
                    unchecked(seed ^ entry.SeedOffset ^ entryIndex * 486187739),
                    outputCounts[entryIndex],
                    entry.YOffset,
                    palette,
                    source,
                    entryIndex);
                createdLayers++;
                representedInstances += outputCounts[entryIndex];
            }

            if (createdLayers <= 0)
            {
                terrain.SetDensityMapPaletteSourceOnly(source, false);
                return $"Palette '{palette.name}' baked no instances. Check its filters and source density.";
            }
            terrain.MarkDetailFoliagePaletteBaked(binding);
            EditorUtility.SetDirty(palette);
            return null;
        }

        static int[] BuildOccupancyIntegral(ushort[] values, int width, int height)
        {
            int stride = width + 1;
            var integral = new int[stride * (height + 1)];
            for (int z = 0; z < height; z++)
            {
                int rowTotal = 0;
                int sourceRow = z * width;
                int destinationRow = (z + 1) * stride;
                int previousRow = z * stride;
                for (int x = 0; x < width; x++)
                {
                    if (values[sourceRow + x] > 0)
                        rowTotal++;
                    integral[destinationRow + x + 1] = integral[previousRow + x + 1] + rowTotal;
                }
            }
            return integral;
        }

        static float CalculateInterior(int[] integral, int width, int height, int x, int z, int radius)
        {
            int minimumX = Mathf.Max(0, x - radius);
            int maximumX = Mathf.Min(width - 1, x + radius);
            int minimumZ = Mathf.Max(0, z - radius);
            int maximumZ = Mathf.Min(height - 1, z + radius);
            int stride = width + 1;
            int x0 = minimumX;
            int x1 = maximumX + 1;
            int z0 = minimumZ;
            int z1 = maximumZ + 1;
            int occupied = integral[z1 * stride + x1]
                - integral[z0 * stride + x1]
                - integral[z1 * stride + x0]
                + integral[z0 * stride + x0];
            int samples = (x1 - x0) * (z1 - z0);
            return samples > 0 ? (float)occupied / samples : 0f;
        }

        static float FractalNoise(float x, float z, int seed)
        {
            float value = 0f;
            float amplitude = 0.5714286f;
            float sum = 0f;
            for (int octave = 0; octave < 3; octave++)
            {
                value += ValueNoise(x, z, seed + octave * 1013) * amplitude;
                sum += amplitude;
                x = x * 2.03f + 19.17f;
                z = z * 2.03f - 7.31f;
                amplitude *= 0.5f;
            }
            return sum > 0f ? Mathf.Clamp01(value / sum) : 0.5f;
        }

        static float ValueNoise(float x, float z, int seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int z0 = Mathf.FloorToInt(z);
            float tx = Smooth(x - x0);
            float tz = Smooth(z - z0);
            float a = Hash01(x0, z0, seed);
            float b = Hash01(x0 + 1, z0, seed);
            float c = Hash01(x0, z0 + 1, seed);
            float d = Hash01(x0 + 1, z0 + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        static float Smooth(float value) => value * value * (3f - 2f * value);

        static float Hash01(int x, int z, int seed) =>
            (Hash((uint)(x * 73856093 ^ z * 19349663 ^ seed)) & 0x00ffffffu) / 16777216f;

        static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            return value ^ (value >> 16);
        }

        static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Foliage";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value.Replace(' ', '_');
        }

        sealed class SurfaceSampler
        {
            readonly MGTerrain m_Terrain;
            readonly Vector3[] m_Vertices;
            readonly Vector3[] m_Normals;
            readonly int m_Grid;

            internal SurfaceSampler(MGTerrain terrain)
            {
                m_Terrain = terrain;
                Mesh mesh = terrain.MeshFilter != null ? terrain.MeshFilter.sharedMesh : null;
                m_Vertices = mesh != null ? mesh.vertices : Array.Empty<Vector3>();
                m_Normals = mesh != null ? mesh.normals : Array.Empty<Vector3>();
                int grid = Mathf.RoundToInt(Mathf.Sqrt(m_Vertices.Length));
                m_Grid = grid * grid == m_Vertices.Length ? grid : 0;
            }

            internal void Sample(int x, int z, int width, int height, out float slope, out float worldHeight)
            {
                if (m_Grid <= 1)
                {
                    slope = 0f;
                    worldHeight = m_Terrain.transform.position.y;
                    return;
                }
                int gx = Mathf.Clamp(Mathf.RoundToInt((x + 0.5f) / width * (m_Grid - 1)), 0, m_Grid - 1);
                int gz = Mathf.Clamp(Mathf.RoundToInt((z + 0.5f) / height * (m_Grid - 1)), 0, m_Grid - 1);
                int index = gz * m_Grid + gx;
                Vector3 localNormal = index < m_Normals.Length ? m_Normals[index] : Vector3.up;
                Vector3 worldNormal = m_Terrain.transform.TransformDirection(localNormal).normalized;
                slope = Vector3.Angle(worldNormal, Vector3.up);
                worldHeight = m_Terrain.transform.TransformPoint(m_Vertices[index]).y;
            }
        }
    }
}

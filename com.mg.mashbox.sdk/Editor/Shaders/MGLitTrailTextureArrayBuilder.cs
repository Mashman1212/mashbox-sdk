#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.Shaders.HDRP.Lit.Editor.EditorGui
{
    /// <summary>
    /// Builds the material-local texture arrays used by MG_Lit_Trail while keeping
    /// TerrainLayer assets as the inspector's authoring source.
    /// </summary>
    internal static class MGLitTrailTextureArrayBuilder
    {
        internal static readonly string[] BaseMapArrayPropertyNames =
        {
            "_BaseMapArray", "_TrailAlbedoArray", "_AlbedoArray"
        };

        internal static readonly string[] HeightArrayPropertyNames =
        {
            "_HeightMapArray", "_TrailHeightArray", "_HeightArray"
        };

        internal static readonly string[] SurfaceArrayPropertyNames =
        {
            "_SurfaceMapArray", "_TrailSurfaceArray", "_SurfaceArray"
        };

        private static readonly string[] ObsoleteArrayPropertyNames =
        {
            "_NormalMapArray", "_TrailNormalArray", "_NormalArray",
            "_MaskMapArray", "_TrailMaskArray", "_MaskArray"
        };

        private const int LayerCount = 8;
        private const string TerrainLayerTagPrefix = "MashBox.MGLitTrail.TerrainLayer.";
        private const string ArrayResolutionTagName = "MashBox.MGLitTrail.ArrayResolution";
        private const string SharedArraysTagName = "MashBox.MGLitTrail.UseSharedArrays";
        private const string PackingShaderName = "Hidden/MashBox/MGLitTrailArrayPack";
        private static readonly HashSet<string> LegacyLayerTexturePropertyNames =
            CreateLegacyLayerTexturePropertyNames();
        private static readonly Dictionary<string, int> PendingBuilds =
            new(StringComparer.OrdinalIgnoreCase);
        private static bool buildQueued;
        private static bool isBuilding;

        internal static bool IsBuilding => isBuilding;

        internal enum ArrayKind
        {
            BaseMap,
            Height,
            Surface
        }

        internal static void QueueBuild(Material material, int resolution)
        {
            if (material == null || UsesExternalArraySource(material))
                return;

            string materialPath = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(materialPath))
                return;

            PendingBuilds[materialPath] = SanitizeResolution(resolution);
            if (buildQueued)
                return;

            buildQueued = true;
            EditorApplication.delayCall += BuildPending;
        }

        internal static bool Build(Material material, int resolution, bool showErrors)
        {
            if (material == null || isBuilding)
                return false;

            if (UsesExternalArraySource(material))
            {
                if (showErrors)
                {
                    EditorUtility.DisplayDialog(
                        "Linked Trail Material",
                        "This material gets its texture arrays from another material. Disable 'Use Linked Material' before rebuilding material-local arrays.",
                        "OK");
                }
                return false;
            }

            string materialPath = AssetDatabase.GetAssetPath(material);
            string materialDirectory = Path.GetDirectoryName(materialPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(materialDirectory))
            {
                if (showErrors)
                    EditorUtility.DisplayDialog("Generate Trail Texture Arrays", "Save this material as an asset first.", "OK");
                return false;
            }

            resolution = SanitizeResolution(resolution);
            material.SetOverrideTag(ArrayResolutionTagName, resolution.ToString());
            string materialName = Path.GetFileNameWithoutExtension(materialPath);

            isBuilding = true;
            Material packingMaterial = null;
            try
            {
                Shader packingShader = Shader.Find(PackingShaderName);
                if (packingShader == null)
                    throw new InvalidOperationException($"Could not find editor packing shader '{PackingShaderName}'.");
                packingMaterial = new Material(packingShader) { hideFlags = HideFlags.HideAndDontSave };

                Texture2DArray baseMapArray = EnsureArray(
                    $"{materialDirectory}/{materialName}_BaseMapArray.asset",
                    $"{materialName} Base Map Array", resolution, TextureFormat.BC7, false);
                Texture2DArray heightArray = EnsureArray(
                    $"{materialDirectory}/{materialName}_HeightMapArray.asset",
                    $"{materialName} Height Map Array", resolution, TextureFormat.BC4, true);
                Texture2DArray surfaceArray = EnsureArray(
                    $"{materialDirectory}/{materialName}_SurfaceMapArray.asset",
                    $"{materialName} Surface Map Array", resolution, TextureFormat.BC7, true);

                for (int index = 0; index < LayerCount; index++)
                {
                    TerrainLayer layer = GetTerrainLayer(material, index);
                    BakeBaseMapSlice(
                        baseMapArray, index, layer != null ? layer.diffuseTexture : null);
                    BakeHeightSlice(
                        heightArray, index, layer != null ? layer.maskMapTexture : null, packingMaterial);
                    BakeSurfaceSlice(
                        surfaceArray,
                        index,
                        layer != null ? layer.normalMapTexture : null,
                        layer != null ? layer.maskMapTexture : null,
                        packingMaterial);
                }

                ApplySettings(baseMapArray);
                ApplySettings(heightArray);
                ApplySettings(surfaceArray);
                AssignArray(material, baseMapArray, BaseMapArrayPropertyNames);
                AssignArray(material, heightArray, HeightArrayPropertyNames);
                AssignArray(material, surfaceArray, SurfaceArrayPropertyNames);
                StripLegacySourceTextureReferences(material);
                DeleteObsoleteGeneratedArrays(material, materialDirectory, materialName);

                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, material);
                if (showErrors)
                    EditorUtility.DisplayDialog("Trail Texture Array Generation Failed", exception.Message, "OK");
                return false;
            }
            finally
            {
                if (packingMaterial != null)
                    UnityEngine.Object.DestroyImmediate(packingMaterial);
                isBuilding = false;
            }
        }

        internal static void AssignExistingArrays(Material material)
        {
            if (material == null)
                return;

            if (MGLitTrailLinkedMaterialUtility.UsesLinkedMaterial(material))
            {
                MGLitTrailLinkedMaterialUtility.Synchronize(material);
                StripLegacySourceTextureReferences(material);
                return;
            }

            if (UsesSharedArrays(material))
            {
                StripLegacySourceTextureReferences(material);
                return;
            }

            AssignArray(material, LoadGeneratedArray(material, ArrayKind.BaseMap), BaseMapArrayPropertyNames);
            AssignArray(material, LoadGeneratedArray(material, ArrayKind.Height), HeightArrayPropertyNames);
            AssignArray(material, LoadGeneratedArray(material, ArrayKind.Surface), SurfaceArrayPropertyNames);
            StripLegacySourceTextureReferences(material);
        }

        internal static bool UsesSharedArrays(Material material)
        {
            return material != null &&
                   string.Equals(
                       material.GetTag(SharedArraysTagName, false, string.Empty),
                       "True",
                       StringComparison.OrdinalIgnoreCase);
        }

        internal static bool UsesExternalArraySource(Material material)
        {
            return UsesSharedArrays(material) ||
                   MGLitTrailLinkedMaterialUtility.UsesLinkedMaterial(material);
        }

        internal static void SetUsesSharedArrays(Material material, bool enabled)
        {
            if (material == null || UsesSharedArrays(material) == enabled)
                return;

            material.SetOverrideTag(SharedArraysTagName, enabled ? "True" : string.Empty);
            if (!enabled)
            {
                AssignGeneratedArrayOrClear(material, ArrayKind.BaseMap, BaseMapArrayPropertyNames);
                AssignGeneratedArrayOrClear(material, ArrayKind.Height, HeightArrayPropertyNames);
                AssignGeneratedArrayOrClear(material, ArrayKind.Surface, SurfaceArrayPropertyNames);
                StripLegacySourceTextureReferences(material);
            }
            else
                StripLegacySourceTextureReferences(material);
            EditorUtility.SetDirty(material);
        }

        internal static Texture2DArray GetAssignedArray(Material material, string[] propertyNames)
        {
            if (material == null)
                return null;

            foreach (string propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName) && material.GetTexture(propertyName) is Texture2DArray array)
                    return array;
            }

            return null;
        }

        internal static bool HasAnyArrayProperty(Material material)
        {
            return HasAnyProperty(material, BaseMapArrayPropertyNames) ||
                   HasAnyProperty(material, HeightArrayPropertyNames) ||
                   HasAnyProperty(material, SurfaceArrayPropertyNames);
        }

        /// <summary>
        /// Removes obsolete serialized texture references after the shader has fully
        /// migrated to arrays. TerrainLayer GUID tags remain as editor authoring data,
        /// but strings do not create Unity asset dependencies.
        /// </summary>
        internal static bool StripLegacySourceTextureReferences(Material material)
        {
            if (!CanStripLegacySourceTextures(material))
                return false;

            var serializedMaterial = new SerializedObject(material);
            SerializedProperty textureEnvironments =
                serializedMaterial.FindProperty("m_SavedProperties.m_TexEnvs");
            if (textureEnvironments == null || !textureEnvironments.isArray)
                return false;

            bool changed = false;
            for (int index = textureEnvironments.arraySize - 1; index >= 0; index--)
            {
                SerializedProperty entry = textureEnvironments.GetArrayElementAtIndex(index);
                SerializedProperty propertyName = entry.FindPropertyRelative("first");
                if (propertyName == null ||
                    (!LegacyLayerTexturePropertyNames.Contains(propertyName.stringValue) &&
                     Array.IndexOf(ObsoleteArrayPropertyNames, propertyName.stringValue) < 0))
                    continue;

                int oldSize = textureEnvironments.arraySize;
                textureEnvironments.DeleteArrayElementAtIndex(index);
                if (textureEnvironments.arraySize == oldSize)
                    textureEnvironments.DeleteArrayElementAtIndex(index);
                changed = true;
            }

            if (!changed)
                return false;

            serializedMaterial.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(material);
            return true;
        }

        internal static int GetStoredResolution(Material material)
        {
            string stored = material != null
                ? material.GetTag(ArrayResolutionTagName, false, string.Empty)
                : string.Empty;
            return int.TryParse(stored, out int resolution) ? SanitizeResolution(resolution) : 1024;
        }

        internal static bool UsesAnySource(Material material, HashSet<string> assetPaths)
        {
            if (material == null || assetPaths == null || assetPaths.Count == 0)
                return false;

            for (int index = 0; index < LayerCount; index++)
            {
                TerrainLayer layer = GetTerrainLayer(material, index);
                if (layer == null)
                    continue;

                if (assetPaths.Contains(AssetDatabase.GetAssetPath(layer)) ||
                    UsesTexture(layer.diffuseTexture, assetPaths) ||
                    UsesTexture(layer.normalMapTexture, assetPaths) ||
                    UsesTexture(layer.maskMapTexture, assetPaths))
                    return true;
            }

            return false;
        }

        internal static Texture2DArray LoadGeneratedArray(Material material, ArrayKind kind)
        {
            string materialPath = material != null ? AssetDatabase.GetAssetPath(material) : string.Empty;
            string directory = Path.GetDirectoryName(materialPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory))
                return null;

            string materialName = Path.GetFileNameWithoutExtension(materialPath);
            string suffix = kind switch
            {
                ArrayKind.BaseMap => "BaseMapArray",
                ArrayKind.Height => "HeightMapArray",
                _ => "SurfaceMapArray"
            };
            return AssetDatabase.LoadAssetAtPath<Texture2DArray>($"{directory}/{materialName}_{suffix}.asset");
        }

        private static void BuildPending()
        {
            buildQueued = false;
            var builds = new KeyValuePair<string, int>[PendingBuilds.Count];
            int index = 0;
            foreach (KeyValuePair<string, int> build in PendingBuilds)
                builds[index++] = build;
            PendingBuilds.Clear();

            foreach (KeyValuePair<string, int> build in builds)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(build.Key);
                if (material != null)
                    Build(material, build.Value, false);
            }
        }

        private static TerrainLayer GetTerrainLayer(Material material, int index)
        {
            string guid = material.GetTag(TerrainLayerTagPrefix + index.ToString("00"), false, string.Empty);
            if (string.IsNullOrEmpty(guid))
                return null;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            return AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        }

        private static Texture2DArray EnsureArray(
            string assetPath,
            string displayName,
            int resolution,
            TextureFormat format,
            bool linear)
        {
            Texture2DArray current = AssetDatabase.LoadAssetAtPath<Texture2DArray>(assetPath);
            if (current != null &&
                current.width == resolution &&
                current.height == resolution &&
                current.depth == LayerCount &&
                current.format == format)
            {
                current.name = displayName;
                return current;
            }

            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (existing != null)
            {
                if (!AssetDatabase.MakeEditable(assetPath))
                    throw new IOException($"The generated array is read-only: {assetPath}");
                if (!AssetDatabase.DeleteAsset(assetPath))
                    throw new IOException($"Could not replace the generated array: {assetPath}");
            }

            var replacement = new Texture2DArray(
                resolution, resolution, LayerCount, format, true, linear)
            {
                name = displayName
            };
            AssetDatabase.CreateAsset(replacement, assetPath);
            return replacement;
        }

        private static void BakeBaseMapSlice(
            Texture2DArray destination,
            int slice,
            Texture2D baseMap)
        {
            Texture2D fallbackBaseMap = null;
            if (baseMap == null)
            {
                fallbackBaseMap = CreateFallbackTexture(Color.white, false);
                baseMap = fallbackBaseMap;
            }

            int resolution = destination.width;
            var readback = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            RenderTexture temporary = RenderTexture.GetTemporary(
                resolution,
                resolution,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;

            try
            {
                Graphics.Blit(baseMap, temporary);
                RenderTexture.active = temporary;
                readback.ReadPixels(new Rect(0f, 0f, resolution, resolution), 0, 0, false);
                readback.Apply(true, false);
                EditorUtility.CompressTexture(
                    readback,
                    TextureFormat.BC7,
                    TextureCompressionQuality.Best);
                for (int mip = 0; mip < destination.mipmapCount; mip++)
                    destination.SetPixelData(readback.GetPixelData<byte>(mip), mip, slice);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                UnityEngine.Object.DestroyImmediate(readback);
                if (fallbackBaseMap != null)
                    UnityEngine.Object.DestroyImmediate(fallbackBaseMap);
            }
        }

        private static void BakeHeightSlice(
            Texture2DArray destination,
            int slice,
            Texture2D maskMap,
            Material packingMaterial)
        {
            Texture2D fallbackMask = null;
            try
            {
                if (maskMap == null)
                {
                    fallbackMask = CreateFallbackTexture(Color.white, true);
                    maskMap = fallbackMask;
                }
                packingMaterial.SetTexture("_MaskMap", maskMap);
                BakePackedSlice(destination, slice, packingMaterial, 0, TextureFormat.BC4);
            }
            finally
            {
                if (fallbackMask != null)
                    UnityEngine.Object.DestroyImmediate(fallbackMask);
            }
        }

        private static void BakeSurfaceSlice(
            Texture2DArray destination,
            int slice,
            Texture2D normalMap,
            Texture2D maskMap,
            Material packingMaterial)
        {
            Texture2D fallbackNormal = null;
            Texture2D fallbackMask = null;
            try
            {
                if (normalMap == null)
                {
                    fallbackNormal = CreateFallbackTexture(new Color(0.5f, 0.5f, 1f, 1f), true);
                    normalMap = fallbackNormal;
                }
                if (maskMap == null)
                {
                    fallbackMask = CreateFallbackTexture(Color.white, true);
                    maskMap = fallbackMask;
                }
                packingMaterial.SetTexture("_NormalMap", normalMap);
                packingMaterial.SetTexture("_MaskMap", maskMap);
                BakePackedSlice(destination, slice, packingMaterial, 1, TextureFormat.BC7);
            }
            finally
            {
                if (fallbackNormal != null)
                    UnityEngine.Object.DestroyImmediate(fallbackNormal);
                if (fallbackMask != null)
                    UnityEngine.Object.DestroyImmediate(fallbackMask);
            }
        }

        private static void BakePackedSlice(
            Texture2DArray destination,
            int slice,
            Material packingMaterial,
            int shaderPass,
            TextureFormat compressedFormat)
        {
            int resolution = destination.width;
            var readback = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true, true)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            RenderTexture temporary = RenderTexture.GetTemporary(
                resolution,
                resolution,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;

            try
            {
                Graphics.Blit(Texture2D.blackTexture, temporary, packingMaterial, shaderPass);
                RenderTexture.active = temporary;
                readback.ReadPixels(new Rect(0f, 0f, resolution, resolution), 0, 0, false);
                readback.Apply(true, false);

                EditorUtility.CompressTexture(
                    readback,
                    compressedFormat,
                    TextureCompressionQuality.Best);
                for (int mip = 0; mip < destination.mipmapCount; mip++)
                    destination.SetPixelData(readback.GetPixelData<byte>(mip), mip, slice);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        private static Texture2D CreateFallbackTexture(Color color, bool linear)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, linear)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, false);
            return texture;
        }

        private static void ApplySettings(Texture2DArray array)
        {
            array.filterMode = FilterMode.Trilinear;
            array.wrapMode = TextureWrapMode.Repeat;
            array.anisoLevel = 4;
            array.Apply(false, false);
            EditorUtility.SetDirty(array);
        }

        private static void AssignArray(Material material, Texture2DArray array, string[] propertyNames)
        {
            if (material == null || array == null)
                return;

            foreach (string propertyName in propertyNames)
            {
                if (!material.HasProperty(propertyName) || material.GetTexture(propertyName) == array)
                    continue;

                material.SetTexture(propertyName, array);
                EditorUtility.SetDirty(material);
            }
        }

        private static void AssignGeneratedArrayOrClear(
            Material material,
            ArrayKind kind,
            string[] propertyNames)
        {
            Texture2DArray generatedArray = LoadGeneratedArray(material, kind);
            if (generatedArray != null)
            {
                AssignArray(material, generatedArray, propertyNames);
                return;
            }

            foreach (string propertyName in propertyNames)
            {
                if (!material.HasProperty(propertyName) || material.GetTexture(propertyName) == null)
                    continue;

                material.SetTexture(propertyName, null);
                EditorUtility.SetDirty(material);
            }
        }

        private static bool HasAnyProperty(Material material, string[] propertyNames)
        {
            if (material == null)
                return false;

            foreach (string propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName))
                    return true;
            }

            return false;
        }

        private static bool CanStripLegacySourceTextures(Material material)
        {
            if (material == null ||
                !HasAnyProperty(material, BaseMapArrayPropertyNames) ||
                !HasAnyProperty(material, HeightArrayPropertyNames) ||
                !HasAnyProperty(material, SurfaceArrayPropertyNames) ||
                GetAssignedArray(material, BaseMapArrayPropertyNames) == null ||
                GetAssignedArray(material, HeightArrayPropertyNames) == null ||
                GetAssignedArray(material, SurfaceArrayPropertyNames) == null)
                return false;

            // Do not strip during a transitional shader version that still declares
            // any of the old textures; those properties may still be sampled.
            foreach (string propertyName in LegacyLayerTexturePropertyNames)
            {
                if (material.HasProperty(propertyName))
                    return false;
            }

            return true;
        }

        private static void DeleteObsoleteGeneratedArrays(
            Material material,
            string materialDirectory,
            string materialName)
        {
            if (material == null ||
                HasAnyProperty(material, ObsoleteArrayPropertyNames) ||
                GetAssignedArray(material, BaseMapArrayPropertyNames) == null ||
                GetAssignedArray(material, HeightArrayPropertyNames) == null ||
                GetAssignedArray(material, SurfaceArrayPropertyNames) == null)
                return;

            string[] suffixes = { "NormalMapArray", "MaskMapArray" };
            foreach (string suffix in suffixes)
            {
                string path = $"{materialDirectory}/{materialName}_{suffix}.asset";
                if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                    continue;

                if (AssetDatabase.MakeEditable(path))
                    AssetDatabase.DeleteAsset(path);
                else
                    Debug.LogWarning($"Could not remove obsolete read-only trail array: {path}", material);
            }
        }

        private static HashSet<string> CreateLegacyLayerTexturePropertyNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < LayerCount; index++)
            {
                string suffix = index.ToString("00");
                names.Add("_BaseMap" + suffix);
                names.Add("_NormalMap" + suffix);
                names.Add("_MaskMap" + suffix);
            }

            return names;
        }

        private static bool UsesTexture(Texture2D texture, HashSet<string> assetPaths)
        {
            return texture != null && assetPaths.Contains(AssetDatabase.GetAssetPath(texture));
        }

        private static int SanitizeResolution(int resolution)
        {
            return Mathf.Clamp(Mathf.ClosestPowerOfTwo(resolution), 32, 8192);
        }
    }

    internal sealed class MGLitTrailMaterialArrayPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (MGLitTrailTextureArrayBuilder.IsBuilding || importedAssets == null)
                return;

            var relevantAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in importedAssets)
            {
                Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (type == typeof(TerrainLayer) || type == typeof(Texture2D))
                    relevantAssets.Add(path);
            }

            if (relevantAssets.Count == 0)
                return;

            foreach (string guid in AssetDatabase.FindAssets("t:Material"))
            {
                string materialPath = AssetDatabase.GUIDToAssetPath(guid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null ||
                    material.shader == null ||
                    material.shader.name.IndexOf("MG_Lit_Trail", StringComparison.OrdinalIgnoreCase) < 0 ||
                    MGLitTrailTextureArrayBuilder.UsesExternalArraySource(material) ||
                    (!MGLitTrailTextureArrayBuilder.HasAnyArrayProperty(material) &&
                     MGLitTrailTextureArrayBuilder.LoadGeneratedArray(
                         material, MGLitTrailTextureArrayBuilder.ArrayKind.Height) == null) ||
                    !MGLitTrailTextureArrayBuilder.UsesAnySource(material, relevantAssets))
                    continue;

                MGLitTrailTextureArrayBuilder.QueueBuild(
                    material,
                    MGLitTrailTextureArrayBuilder.GetStoredResolution(material));
            }
        }
    }
}

#endif

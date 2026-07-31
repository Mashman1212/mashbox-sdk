using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.Trail;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.Maps.Trail.Editor
{
    [CustomEditor(typeof(MGTrailLayerLibrary))]
    public sealed class MGTrailLayerLibraryEditor : UnityEditor.Editor
    {
        bool m_BuildQueued;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            var library = (MGTrailLayerLibrary)target;
            if (library.Layers.Count > library.Capacity)
            {
                EditorGUILayout.HelpBox(
                    $"Only the first {library.Capacity} of {library.Layers.Count} layers will be baked. Increase Capacity to include the remaining layers.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(!AssetDatabase.Contains(library)))
            {
                if (GUILayout.Button("Build Texture Arrays", GUILayout.Height(28f)))
                    MGTrailTextureArrayBuilder.Build(library, true);
            }

            DrawGeneratedArray("Albedo Array (sRGB)", library.AlbedoArray);
            DrawGeneratedArray("Normal Array (Linear)", library.NormalArray);
            DrawGeneratedArray("Mask Array (Linear)", library.MaskArray);

            EditorGUILayout.HelpBox(
                "All three arrays share the same resolution, capacity, mip count, filtering, and slice indices. In Shader Graph, sample them with Sample Texture 2D Array and use the layer index as the array Index input.",
                MessageType.Info);

            if (changed)
                QueueBuild();
        }

        void QueueBuild()
        {
            if (m_BuildQueued)
                return;

            m_BuildQueued = true;
            EditorApplication.delayCall += () =>
            {
                m_BuildQueued = false;
                if (target is MGTrailLayerLibrary library && library != null && AssetDatabase.Contains(library))
                    MGTrailTextureArrayBuilder.Build(library, false);
            };
        }

        static void DrawGeneratedArray(string label, Texture2DArray array)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(label, array, typeof(Texture2DArray), false);
        }
    }

    internal static class MGTrailTextureArrayBuilder
    {
        static bool s_IsBuilding;

        public static bool Build(MGTrailLayerLibrary library, bool logResult)
        {
            if (library == null || s_IsBuilding)
                return false;

            string libraryPath = AssetDatabase.GetAssetPath(library);
            if (string.IsNullOrEmpty(libraryPath))
            {
                Debug.LogWarning("Save the MG Trail Layer Library as an asset before building its texture arrays.", library);
                return false;
            }

            s_IsBuilding = true;
            try
            {
                int resolution = library.Resolution;
                int capacity = library.Capacity;
                Texture2DArray albedo = EnsureArray(
                    library.AlbedoArray, library, libraryPath, "Albedo Array", resolution, capacity, false);
                Texture2DArray normal = EnsureArray(
                    library.NormalArray, library, libraryPath, "Normal Array", resolution, capacity, true);
                Texture2DArray mask = EnsureArray(
                    library.MaskArray, library, libraryPath, "Mask Array", resolution, capacity, true);

                for (int slice = 0; slice < capacity; slice++)
                {
                    MGTrailLayerLibrary.Layer layer = slice < library.LayerCount
                        ? library.Layers[slice]
                        : null;

                    BakeSlice(albedo, slice, layer?.albedo, library.DefaultAlbedo, false);
                    BakeSlice(normal, slice, layer?.normal, library.DefaultNormal, true);
                    BakeSlice(mask, slice, layer?.mask, library.DefaultMask, true);
                }

                ApplySettings(albedo, library);
                ApplySettings(normal, library);
                ApplySettings(mask, library);
                library.SetGeneratedArrays(albedo, normal, mask);

                EditorUtility.SetDirty(albedo);
                EditorUtility.SetDirty(normal);
                EditorUtility.SetDirty(mask);
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssets();

                if (logResult)
                {
                    Debug.Log(
                        $"Built MG Trail arrays: {library.LayerCount} layer(s), {capacity} slice capacity, {resolution}x{resolution}.",
                        library);
                }
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, library);
                return false;
            }
            finally
            {
                s_IsBuilding = false;
            }
        }

        static Texture2DArray EnsureArray(
            Texture2DArray current,
            MGTrailLayerLibrary owner,
            string ownerPath,
            string displayName,
            int resolution,
            int capacity,
            bool linear)
        {
            if (current != null &&
                current.width == resolution &&
                current.height == resolution &&
                current.depth == capacity &&
                current.format == TextureFormat.RGBA32)
            {
                current.name = $"{owner.name} {displayName}";
                return current;
            }

            var replacement = new Texture2DArray(
                resolution,
                resolution,
                capacity,
                TextureFormat.RGBA32,
                true,
                linear)
            {
                name = $"{owner.name} {displayName}"
            };

            AssetDatabase.AddObjectToAsset(replacement, ownerPath);
            if (current != null && AssetDatabase.IsSubAsset(current))
                UnityEngine.Object.DestroyImmediate(current, true);
            return replacement;
        }

        static void BakeSlice(
            Texture2DArray destination,
            int slice,
            Texture2D source,
            Color fallback,
            bool linear)
        {
            int resolution = destination.width;
            Texture2D fallbackTexture = null;
            if (source == null)
            {
                fallbackTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false, linear)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                fallbackTexture.SetPixel(0, 0, fallback);
                fallbackTexture.Apply(false, false);
                source = fallbackTexture;
            }

            var readback = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true, linear)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            RenderTexture temporary = RenderTexture.GetTemporary(
                resolution,
                resolution,
                0,
                RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;

            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                readback.ReadPixels(new Rect(0f, 0f, resolution, resolution), 0, 0, false);
                readback.Apply(true, false);

                for (int mip = 0; mip < destination.mipmapCount; mip++)
                    destination.SetPixels(readback.GetPixels(mip), slice, mip);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                UnityEngine.Object.DestroyImmediate(readback);
                if (fallbackTexture != null)
                    UnityEngine.Object.DestroyImmediate(fallbackTexture);
            }
        }

        static void ApplySettings(Texture2DArray array, MGTrailLayerLibrary library)
        {
            array.filterMode = library.ArrayFilterMode;
            array.wrapMode = library.ArrayWrapMode;
            array.anisoLevel = library.ArrayAnisoLevel;
            array.Apply(false, false);
        }
    }

    internal sealed class MGTrailLayerTexturePostprocessor : AssetPostprocessor
    {
        static readonly HashSet<string> s_PendingLibraries =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool s_RebuildQueued;

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (importedAssets == null || importedAssets.Length == 0)
                return;

            var imported = new HashSet<string>(importedAssets, StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:MGTrailLayerLibrary"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var library = AssetDatabase.LoadAssetAtPath<MGTrailLayerLibrary>(path);
                if (library == null || !UsesAny(library, imported))
                    continue;

                s_PendingLibraries.Add(path);
            }

            if (s_PendingLibraries.Count > 0 && !s_RebuildQueued)
            {
                s_RebuildQueued = true;
                EditorApplication.delayCall += RebuildPendingLibraries;
            }
        }

        static void RebuildPendingLibraries()
        {
            s_RebuildQueued = false;
            string[] paths = new string[s_PendingLibraries.Count];
            s_PendingLibraries.CopyTo(paths);
            s_PendingLibraries.Clear();

            foreach (string path in paths)
            {
                var library = AssetDatabase.LoadAssetAtPath<MGTrailLayerLibrary>(path);
                if (library != null)
                    MGTrailTextureArrayBuilder.Build(library, false);
            }
        }

        static bool UsesAny(MGTrailLayerLibrary library, HashSet<string> imported)
        {
            foreach (MGTrailLayerLibrary.Layer layer in library.Layers)
            {
                if (IsImported(layer?.albedo, imported) ||
                    IsImported(layer?.normal, imported) ||
                    IsImported(layer?.mask, imported))
                {
                    return true;
                }
            }
            return false;
        }

        static bool IsImported(Texture2D texture, HashSet<string> imported)
        {
            return texture != null && imported.Contains(AssetDatabase.GetAssetPath(texture));
        }
    }
}

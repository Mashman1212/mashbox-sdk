using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    internal static class PrefabStampAppearance
    {
        internal struct Dab
        {
            public MGTerrain tile;
            public Vector3 center;
            public float radius, height, rotation, falloff;
            public bool invert;
        }

        // Material snapshots make map edits undoable without overwriting source textures.
        internal static void Apply(PrefabStampSource source, MeshStampBrush brush, List<Dab> dabs,
            bool colour, bool normals)
        {
            if ((!colour && !normals) || dabs.Count == 0) return;
            using var sampler = new PrefabStampSampler(source, colour, normals);
            var outputs = new Dictionary<MGTerrain, Material>();

            var resources = new List<Object>();
            try
            {
                foreach (var dab in dabs)
                {
                    if (dab.tile == null) continue;
                    var renderer = dab.tile.MeshRenderer;
                    var original = renderer != null ? renderer.sharedMaterial : null;
                    ValidateTarget(dab.tile, colour, normals);
                    if (!outputs.TryGetValue(dab.tile, out var material))
                    {
                        material = new Material(original) { name = dab.tile.name + " Stamped Appearance" }; resources.Add(material);
                        if (colour) material.SetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty,
                            CloneMap(original.GetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty), 1, false, Color.black, resources));
                        if (normals) material.SetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty,
                            CloneMap(original.GetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty), 1, true, new Color(.5f, 1, .5f), resources));
                        outputs.Add(dab.tile, material);
                    }
                    sampler.SetShape(dab);
                    if (colour) Composite((Texture2D)material.GetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty), sampler, dab, brush, false);
                    if (normals) Composite((Texture2D)material.GetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty), sampler, dab, brush, true);
                }
                const string folder = "Assets/MG Terrain Stamp Maps";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "MG Terrain Stamp Maps");
                // Publish assignments only after every texture transfer has succeeded.
                foreach (var pair in outputs)
                {
                    string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + MGTerrainAppearanceCaptureAssets.TerrainPrefix(pair.Key.name) + "Stamp.mat");

                    foreach (var property in new[] { MGTerrainAppearanceCaptureAssets.ColourProperty, MGTerrainAppearanceCaptureAssets.NormalProperty })
                    {
                        if (!pair.Value.HasProperty(property)) continue;
                        var texture = pair.Value.GetTexture(property);
                        if (texture != null && !EditorUtility.IsPersistent(texture))
                        {
                            texture.name = pair.Key.name + property;
                            AssetDatabase.CreateAsset(texture, AssetDatabase.GenerateUniqueAssetPath(path.Replace(".mat", property + ".asset")));
                            pair.Value.SetTexture(property, texture);
                        }
                    }
                    AssetDatabase.CreateAsset(pair.Value, path);
                    EditorUtility.SetDirty(pair.Value);
                    AssetDatabase.SaveAssetIfDirty(pair.Value);
                }
                foreach (var pair in outputs)
                {
                    var renderer = pair.Key.MeshRenderer;
                    Undo.RecordObject(renderer, "Stamp Terrain Appearance");
                    var slots = renderer.sharedMaterials; slots[0] = pair.Value; renderer.sharedMaterials = slots;
                    EditorUtility.SetDirty(renderer); PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
                }
            }
            finally { foreach (var resource in resources) if (resource != null && !EditorUtility.IsPersistent(resource)) Object.DestroyImmediate(resource); }
        }

        internal static void ValidateTarget(MGTerrain tile, bool colour, bool normals)
        {
            if (tile == null) throw new InvalidOperationException("Appearance stamping needs an MG Terrain tile or world.");
            var material = tile.MeshRenderer != null ? tile.MeshRenderer.sharedMaterial : null;
            if (material == null || colour && !material.HasProperty(MGTerrainAppearanceCaptureAssets.ColourProperty)
                || normals && !material.HasProperty(MGTerrainAppearanceCaptureAssets.NormalProperty))
                throw new InvalidOperationException("The terrain material must support the selected far-range appearance maps.");
            if (colour && material.GetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty) == null
                || normals && material.GetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty) == null)
                throw new InvalidOperationException("Bake the terrain appearance maps first, then stamp into them. Tile: " + tile.name);
            MGTerrainTileAuthoring.Validate(tile);
        }
        internal static Texture2D CloneMap(Texture source, int resolution, bool linear, Color fallback, List<Object> resources)
        {
            int width = source != null ? source.width : resolution, height = source != null ? source.height : resolution;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active; bool previousSRGB = GL.sRGBWrite;
            try
            {
                RenderTexture.active = rt; GL.Clear(false, true, fallback);
                if (source != null) { GL.sRGBWrite = !linear; Graphics.Blit(source, rt); }
                var texture = Read(rt, linear); texture.wrapMode = TextureWrapMode.Clamp; resources.Add(texture); return texture;
            }
            finally { GL.sRGBWrite = previousSRGB; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
        }

        internal static void Composite(Texture2D destination, PrefabStampSampler sampler, Dab dab, MeshStampBrush brush, bool normal)
        {
            var filter = dab.tile.MeshFilter;
            var bounds = filter.sharedMesh.bounds;
            var pixels = destination.GetPixels();
            var inverse = Quaternion.Euler(0, -dab.rotation, 0);
            var minimum = filter.transform.InverseTransformPoint(dab.center - new Vector3(dab.radius, 0, dab.radius));
            var maximum = filter.transform.InverseTransformPoint(dab.center + new Vector3(dab.radius, 0, dab.radius));
            int minX = Mathf.Clamp(Mathf.FloorToInt((minimum.x - bounds.min.x) / bounds.size.x * destination.width), 0, destination.width);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((maximum.x - bounds.min.x) / bounds.size.x * destination.width), 0, destination.width);
            int minY = Mathf.Clamp(Mathf.FloorToInt((minimum.z - bounds.min.z) / bounds.size.z * destination.height), 0, destination.height);
            int maxY = Mathf.Clamp(Mathf.CeilToInt((maximum.z - bounds.min.z) / bounds.size.z * destination.height), 0, destination.height);
            for (int y = minY; y < maxY; y++) for (int x = minX; x < maxX; x++)
            {
                Vector3 world = filter.transform.TransformPoint(new Vector3(bounds.min.x + (x + .5f) / destination.width * bounds.size.x,
                    0, bounds.min.z + (y + .5f) / destination.height * bounds.size.z));
                Vector3 offset = (world - dab.center) / dab.radius;
                Vector3 local = inverse * offset;
                float distance = new Vector2(local.x, local.z).magnitude;
                if (distance >= 1 || !brush.TrySurface(local.x, local.z, out _, out int triangle, out var barycentric)) continue;
                if (!sampler.Sample(triangle, barycentric, normal, out Color sample)) continue;
                float weight = Mathf.Pow(1 - distance, dab.falloff);

                int i = y * destination.width + x;
                if (normal)
                {
                    var a = new Vector3(pixels[i].r * 2 - 1, pixels[i].g * 2 - 1, pixels[i].b * 2 - 1).normalized;
                    var b = new Vector3(sample.r * 2 - 1, sample.g * 2 - 1, sample.b * 2 - 1).normalized;
                    var combined = Vector3.Lerp(a, (a + b - Vector3.up).normalized, weight).normalized;
                    pixels[i] = new Color(combined.x * .5f + .5f, combined.y * .5f + .5f, combined.z * .5f + .5f, 1);
                }
                else { float alpha = pixels[i].a; pixels[i] = Color.Lerp(pixels[i].linear, sample.linear, weight).gamma; pixels[i].a = alpha; }
            }
            destination.SetPixels(pixels); destination.Apply(true, false);
        }

        static Texture2D Read(RenderTexture source, bool linear)
        {
            var previous = RenderTexture.active; bool previousSRGB = GL.sRGBWrite;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, linear);
            try { RenderTexture.active = source; texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); texture.Apply(); return texture; }
            catch { Object.DestroyImmediate(texture); throw; }
            finally { RenderTexture.active = previous; }
        }

    }
}
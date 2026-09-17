using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
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
            bool colour, bool normals, int resolution, float exposure)
        {
            if ((!colour && !normals) || dabs.Count == 0) return;
            if (!(RenderPipelineManager.currentPipeline is HDRenderPipeline)) throw new InvalidOperationException("Prefab appearance stamping requires HDRP.");
            var outputs = new Dictionary<MGTerrain, Material>();
            var captures = new Dictionary<Vector4, (Texture2D colour, Texture2D normal)>();
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
                            CloneMap(original.GetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty), resolution, false, Color.black, resources));
                        if (normals) material.SetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty,
                            CloneMap(original.GetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty), resolution, true, new Color(.5f, 1, .5f), resources));
                        outputs.Add(dab.tile, material);
                    }
                    var key = new Vector4(dab.radius, dab.invert ? -dab.height : dab.height, dab.rotation, dab.falloff);
                    if (!captures.TryGetValue(key, out var capture))
                    {
                        capture = Capture(source, dab, resolution, normals, exposure);
                        captures.Add(key, capture); resources.Add(capture.colour);
                        if (capture.normal != null) resources.Add(capture.normal);
                    }
                    if (colour) Composite((Texture2D)material.GetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty), capture.colour, dab, brush, false);
                    if (normals) Composite((Texture2D)material.GetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty), capture.normal, dab, brush, true);
                }
                const string folder = "Assets/MG Terrain Stamp Maps";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "MG Terrain Stamp Maps");
                // Publish assignments only after every capture and composite has succeeded.
                foreach (var pair in outputs)
                {
                    string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + MGTerrainAppearanceCaptureAssets.TerrainPrefix(pair.Key.name) + "Stamp.mat");
                    AssetDatabase.CreateAsset(pair.Value, path);
                    foreach (var property in new[] { MGTerrainAppearanceCaptureAssets.ColourProperty, MGTerrainAppearanceCaptureAssets.NormalProperty })
                    {
                        if (!pair.Value.HasProperty(property)) continue;
                        var texture = pair.Value.GetTexture(property);
                        if (texture != null && !EditorUtility.IsPersistent(texture)) { texture.name = property; AssetDatabase.AddObjectToAsset(texture, pair.Value); }
                    }
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
        static Texture2D CloneMap(Texture source, int resolution, bool linear, Color fallback, List<Object> resources)
        {
            int width = source != null ? source.width : resolution, height = source != null ? source.height : resolution;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = rt; GL.Clear(false, true, fallback);
                if (source != null) Graphics.Blit(source, rt);
                var texture = Read(rt, linear); texture.wrapMode = TextureWrapMode.Clamp; resources.Add(texture); return texture;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
        }

        internal static void Composite(Texture2D destination, Texture2D capture, Dab dab, MeshStampBrush brush, bool normal)
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
                if (distance >= 1 || !brush.TryHeight(local.x, local.z, out _)) continue;
                float weight = Mathf.Pow(1 - distance, dab.falloff);
                Color sample = capture.GetPixelBilinear(offset.x * .5f + .5f, offset.z * .5f + .5f);
                int i = y * destination.width + x;
                if (normal)
                {
                    var a = new Vector3(pixels[i].r * 2 - 1, pixels[i].g * 2 - 1, pixels[i].b * 2 - 1).normalized;
                    var b = new Vector3(sample.r * 2 - 1, sample.g * 2 - 1, sample.b * 2 - 1).normalized;
                    var combined = Vector3.Lerp(a, (a + b - Vector3.up).normalized, weight).normalized;
                    pixels[i] = new Color(combined.x * .5f + .5f, combined.y * .5f + .5f, combined.z * .5f + .5f, 1);
                }
                else pixels[i] = Color.Lerp(pixels[i], sample, weight);
            }
            destination.SetPixels(pixels); destination.Apply(true, false);
        }

        static Texture2D Read(RenderTexture source, bool linear)
        {
            var previous = RenderTexture.active;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, linear);
            try { RenderTexture.active = source; texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); texture.Apply(); return texture; }
            catch { Object.DestroyImmediate(texture); throw; }
            finally { RenderTexture.active = previous; }
        }

        internal static (Texture2D colour, Texture2D normal) Capture(PrefabStampSource source, Dab dab, int resolution, bool normals, float exposure)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var resources = new List<Object>();
            Texture2D colour = null, normal = null;
            try
            {
                var root = new GameObject("Prefab Stamp Capture") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var mesh = source.Shape(Vector3.zero, dab.radius, dab.height, dab.rotation, dab.falloff, dab.invert); resources.Add(mesh);
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>().sharedMaterials = source.Materials;
                var cameraObject = new GameObject("Stamp Camera"); cameraObject.transform.SetParent(root.transform);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.allowHDR = true; camera.allowMSAA = false;
                camera.orthographic = true; camera.orthographicSize = dab.radius; camera.aspect = 1;
                camera.nearClipPlane = .1f; camera.farClipPlane = dab.height * 2 + 20;
                camera.transform.position = new Vector3(0, dab.height + 10, 0);
                camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                var hd = cameraObject.AddComponent<HDAdditionalCameraData>();
                hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color; hd.backgroundColorHDR = Color.black;
                hd.customRenderingSettings = true; hd.volumeLayerMask = ~0;
                foreach (var field in new[] { FrameSettingsField.AtmosphericScattering, FrameSettingsField.Volumetrics,
                    FrameSettingsField.Bloom, FrameSettingsField.MotionBlur, FrameSettingsField.DepthOfField,
                    FrameSettingsField.Tonemapping, FrameSettingsField.ColorGrading }) Set(field, false);
                Set(FrameSettingsField.CustomPass, true); Set(FrameSettingsField.ExposureControl, true); Set(FrameSettingsField.Postprocess, true);
                var volume = cameraObject.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = float.MaxValue;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>(); resources.Add(profile); volume.sharedProfile = profile;
                var exp = profile.Add<Exposure>(); resources.Add(exp); exp.mode.Override(ExposureMode.Fixed); exp.fixedExposure.Override(exposure); exp.compensation.Override(0);
                var sunObject = new GameObject("Stamp Light"); sunObject.transform.SetParent(root.transform);
                var sun = sunObject.AddComponent<Light>(); sun.type = LightType.Directional;
                sun.color = RenderSettings.sun != null ? RenderSettings.sun.color : Color.white;
                sun.intensity = RenderSettings.sun != null ? RenderSettings.sun.intensity : 100000;
                sun.transform.rotation = RenderSettings.sun != null ? RenderSettings.sun.transform.rotation : Quaternion.Euler(55, -30, 0);
                sunObject.AddComponent<HDAdditionalLightData>();
                var rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); resources.Add(rt); rt.Create();
                MGTerrainEditor.AppearanceNormalPass pass = null;
                RenderTexture normalRT = null;
                if (normals)
                {
                    var shader = Shader.Find("Hidden/MashBox/TerrainCaptureNormals");
                    if (shader == null || !shader.isSupported) throw new InvalidOperationException("Terrain normal capture shader is unavailable.");
                    var decoder = CoreUtils.CreateEngineMaterial(shader); resources.Add(decoder);
                    normalRT = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); resources.Add(normalRT); normalRT.Create();
                    var custom = cameraObject.AddComponent<CustomPassVolume>(); custom.isGlobal = true; custom.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
                    pass = new MGTerrainEditor.AppearanceNormalPass { captureCamera = camera, destination = normalRT, decoder = decoder,
                        targetColorBuffer = CustomPass.TargetBuffer.None, targetDepthBuffer = CustomPass.TargetBuffer.None };
                    custom.customPasses.Add(pass);
                }
                var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                if (!UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("HDRP camera capture is unavailable.");
                // HDRP initializes exposure history on the first render of a new camera.
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
                colour = Read(rt, false);
                if (normals)
                {
                    if (!pass.captured) throw new InvalidOperationException("HDRP did not capture prefab normals.");
                    normal = Read(normalRT, true);
                }
                return (colour, normal);
                void Set(FrameSettingsField field, bool enabled)
                { hd.renderingPathCustomFrameSettings.SetEnabled(field, enabled); hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true; }
            }
            catch { if (colour != null) Object.DestroyImmediate(colour); if (normal != null) Object.DestroyImmediate(normal); throw; }
            finally { EditorSceneManager.ClosePreviewScene(scene); foreach (var resource in resources) if (resource != null) Object.DestroyImmediate(resource); }
        }
    }
}

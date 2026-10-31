using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MG.GodSurface.Editor
{
    public static class MGSurfaceBaker
    {
        public const string SourceShader = "MGShaders/HDRP/Lit/MG_Lit_Basic";
        public static string OutputRoot(MGSurfaceWorld world)
        {
            if (!world) throw new ArgumentNullException(nameof(world));
            string scenePath = world.gameObject.scene.path.Replace('\\', '/');
            if (string.IsNullOrEmpty(scenePath) || !scenePath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("Save the prop scene inside Assets before cooking. GodSurface assets are created beside its scene file.");
            return Path.GetDirectoryName(scenePath).Replace('\\', '/') + "/GodSurface";
        }
        [Serializable] public sealed class Source { public string materialGuid, previewGuid, name; public int bank, surfaceID; public string[] textureGuids; }
        [Serializable] public sealed class Recipe { public int resolution; public bool compressed; public string libraryGuid; public Source[] sources; }
        public static string RecipePath(MGSurfaceLibrary library) => Path.GetDirectoryName(AssetDatabase.GetAssetPath(library)).Replace('\\', '/') + "/Editor/Sources.json";
        public static Recipe ReadRecipe(MGSurfaceLibrary library) => JsonUtility.FromJson<Recipe>(File.ReadAllText(RecipePath(library)));
        static string Guid(UnityEngine.Object value) => value ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value)) : "";
        static T Resolve<T>(string guid) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
        public static Material Original(Material material, MGSurfaceLibrary library)
        {
            if (!material || material.shader.name != MGSurfaceLibrary.ShaderName || !library) return material;
            var recipe = ReadRecipe(library); string guid = Guid(material);
            var record = recipe.sources.FirstOrDefault(s => s.previewGuid == guid);
            return record == null ? null : Resolve<Material>(record.materialGuid);
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        // Array sampling is deliberately consistent across props. Addressing remains a bank constraint:
        // changing Repeat to Clamp would change UVs, unlike the chosen filtering-quality policy.
        static string SamplerKey(Texture texture) => $"{(texture ? texture.wrapModeU : TextureWrapMode.Repeat)}/{(texture ? texture.wrapModeV : TextureWrapMode.Repeat)}";
        static bool InactiveSetting(Material source, string name)
        {
            if ((name == "_DoubleSidedNormalMode" || name == "_DoubleSidedConstants") &&
                source.HasProperty("_DoubleSidedEnable") && source.GetFloat("_DoubleSidedEnable") == 0 && !source.IsKeywordEnabled("_DOUBLESIDED_ON")) return true;
            return name == "_UseShadowThreshold" && source.HasProperty("_AlphaCutoffEnable") && source.GetFloat("_AlphaCutoffEnable") == 0 && !source.IsKeywordEnabled("_ALPHATEST_ON");
        }
        internal static string RenderKey(Material source, Shader target)
        {
            var key = new StringBuilder().Append(source.renderQueue).Append('|').Append(source.doubleSidedGI).Append('|').Append(source.globalIlluminationFlags);
            for (int i = 0; i < target.GetPropertyCount(); i++)
            {
                string name = target.GetPropertyName(i); if (name.StartsWith("_MGSurface", StringComparison.Ordinal) || !source.HasProperty(name) || InactiveSetting(source, name)) continue;
                var type = target.GetPropertyType(i); key.Append('|').Append(name).Append('=');
                if (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range) key.Append(source.GetFloat(name).ToString("R", CultureInfo.InvariantCulture));
                else if (type == ShaderPropertyType.Int) key.Append(source.GetInteger(name));
                else if (type == ShaderPropertyType.Color || type == ShaderPropertyType.Vector) key.Append(JsonUtility.ToJson(source.GetVector(name)));
            }
            foreach (string keyword in source.shaderKeywords.Where(k => target.keywordSpace.FindKeyword(k).isValid).OrderBy(x => x)) key.Append('|').Append(keyword);
            for (int p = 0; p < source.passCount; p++) key.Append('|').Append(source.GetPassName(p)).Append(':').Append(source.GetShaderPassEnabled(source.GetPassName(p)));
            foreach (string map in MGSurfaceSchema.Maps) key.Append('|').Append(SamplerKey(source.GetTexture(map)));
            return key.ToString();
        }
        static void CopyRenderState(Material source, Material target)
        {
            for (int i = 0; i < target.shader.GetPropertyCount(); i++)
            {
                string name = target.shader.GetPropertyName(i); if (name.StartsWith("_MGSurface", StringComparison.Ordinal) || !source.HasProperty(name) || InactiveSetting(source, name)) continue;
                switch (target.shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Float: case ShaderPropertyType.Range: target.SetFloat(name, source.GetFloat(name)); break;
                    case ShaderPropertyType.Int: target.SetInteger(name, source.GetInteger(name)); break;
                    case ShaderPropertyType.Color: target.SetColor(name, source.GetColor(name)); break;
                    case ShaderPropertyType.Vector: target.SetVector(name, source.GetVector(name)); break;
                }
            }
            target.shaderKeywords = source.shaderKeywords.Where(k => target.shader.keywordSpace.FindKeyword(k).isValid).ToArray();
            target.renderQueue = source.renderQueue; target.doubleSidedGI = source.doubleSidedGI;
            target.globalIlluminationFlags = source.globalIlluminationFlags; target.enableInstancing = true;
            for (int p = 0; p < target.passCount; p++) target.SetShaderPassEnabled(target.GetPassName(p), source.GetShaderPassEnabled(target.GetPassName(p)));
        }
        public static MGSurfaceLibrary Bake(MGSurfaceWorld world, int resolution = 1024, bool compress = true)
        {
            if (!world) throw new ArgumentNullException(nameof(world));
            var shader = Shader.Find(MGSurfaceLibrary.ShaderName);
            if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("God Surface shader is missing or has compilation errors.");
            if (!SystemInfo.supports2DArrayTextures || (compress && !SystemInfo.SupportsTextureFormat(TextureFormat.BC7))) throw new InvalidOperationException("The requested array format is not supported by this device.");
            resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(resolution), 128, 2048);
            string generated = OutputRoot(world);
            bool wasRendering = world.IsRendering; world.Release();
            string folder = null;
            try
            {
                var materials = world.EnumerateRenderers().Where(r => world.UnsupportedReason(r) == null)
                    .SelectMany(r => r.sharedMaterials).Distinct().Select(m => Original(m, world.surfaceLibrary)).Where(m => m && m.shader.name == SourceShader)
                    .Distinct().OrderBy(m => AssetDatabase.GetAssetPath(m), StringComparer.Ordinal).ThenBy(m => m.name, StringComparer.Ordinal).ToArray();
                if (materials.Length == 0) throw new InvalidOperationException("No eligible MG Lit Basic materials found in this world's roots.");
                if (materials.Any(m => string.IsNullOrEmpty(Guid(m)))) throw new InvalidOperationException("Save source materials as assets before baking.");
                if (materials.Any(m => Resolve<Material>(Guid(m)) != m)) throw new InvalidOperationException("Extract embedded source materials to standalone .mat assets before baking.");
                Folder(generated); folder = AssetDatabase.GenerateUniqueAssetPath(generated + "/SurfaceLibrary"); Folder(folder); Folder(folder + "/Editor");
                var library = ScriptableObject.CreateInstance<MGSurfaceLibrary>(); library.resolution = resolution; library.compressed = compress;
                AssetDatabase.CreateAsset(library, folder + "/Library.asset");
                var banks = new List<MGSurfaceLibrary.Bank>(); var sources = new List<Source>();
                foreach (var group in materials.GroupBy(m => RenderKey(m, shader)))
                {
                    var batch = group.ToArray(); int bankID = banks.Count; string bankFolder = folder + "/Bank" + bankID; Folder(bankFolder);
                    if (batch.Length > 2048) throw new InvalidOperationException("Split this library into smaller banks (maximum 2,048 surfaces per bank).");
                    var parameters = new Texture2D(2 + MGSurfaceSchema.Parameters.Length + 6, batch.Length, TextureFormat.RGBAFloat, false, true)
                        { name = "Surface parameters", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    var pixels = new Color[parameters.width * parameters.height];
                    var bank = new MGSurfaceLibrary.Bank { arrays = new Texture2DArray[6], surfaceNames = batch.Select(m => m.name).ToArray(), previewMaterials = new Material[batch.Length], parameters = parameters };
                    for (int row = 0; row < batch.Length; row++)
                    {
                        var source = batch[row];
                        for (int column = 0; column < MGSurfaceSchema.Parameters.Length; column++)
                        {
                            string name = MGSurfaceSchema.Parameters[column]; Color value;
                            if (MGSurfaceSchema.Kinds[column] == 1) { value = source.GetColor(name); if (QualitySettings.activeColorSpace == ColorSpace.Linear) value = value.linear; }
                            else value = new Color(source.GetFloat(name), 0, 0, 0);
                            pixels[row * parameters.width + 2 + column] = value;
                        }
                        for (int map = 0; map < 6; map++)
                        {
                            Vector2 scale = source.GetTextureScale(MGSurfaceSchema.Maps[map]), offset = source.GetTextureOffset(MGSurfaceSchema.Maps[map]);
                            pixels[row * parameters.width + 2 + MGSurfaceSchema.Parameters.Length + map] = new Color(scale.x, scale.y, offset.x, offset.y);
                        }
                    }
                    for (int map = 0; map < 6; map++)
                    {
                        EditorUtility.DisplayProgressBar("Bake God Surface", $"Bank {bankID + 1}: {MGSurfaceSchema.Maps[map]}", map / 6f);
                        var textures = new List<Texture>(); int[] slices = new int[batch.Length];
                        for (int row = 0; row < batch.Length; row++)
                        {
                            Texture texture = batch[row].GetTexture(MGSurfaceSchema.Maps[map]);
                            int slice = textures.IndexOf(texture); if (slice < 0) { slice = textures.Count; textures.Add(texture); }
                            slices[row] = slice;
                            int pixel = row * parameters.width + map / 4; Color value = pixels[pixel]; value[map % 4] = slice; pixels[pixel] = value;
                        }
                        bank.arrays[map] = BakeArray(textures, map, resolution, compress);
                        AssetDatabase.CreateAsset(bank.arrays[map], bankFolder + "/" + MGSurfaceSchema.Arrays[map] + ".asset");
                    }
                    parameters.SetPixels(pixels); parameters.Apply(false, true); AssetDatabase.CreateAsset(parameters, bankFolder + "/Parameters.asset");
                    var shared = new Material(shader) { name = "God Surface Bank " + bankID }; CopyRenderState(batch[0], shared);
                    shared.SetTexture("_MGSurfaceParameters", parameters);
                    for (int map = 0; map < 6; map++) shared.SetTexture(MGSurfaceSchema.Arrays[map], bank.arrays[map]);
                    bank.sharedMaterial = shared; AssetDatabase.CreateAsset(shared, bankFolder + "/Shared.mat");
                    for (int row = 0; row < batch.Length; row++)
                    {
                        var preview = new Material(shared) { name = batch[row].name + " [Surface " + row + "]" }; preview.SetFloat("_MGSurfaceID", row);
                        string path = bankFolder + "/Surface" + row + ".mat"; AssetDatabase.CreateAsset(preview, path); bank.previewMaterials[row] = preview;
                        sources.Add(new Source { materialGuid = Guid(batch[row]), previewGuid = Guid(preview), name = batch[row].name, bank = bankID, surfaceID = row,
                            textureGuids = MGSurfaceSchema.Maps.Select(n => Guid(batch[row].GetTexture(n))).ToArray() });
                    }
                    banks.Add(bank);
                }
                library.banks = banks.ToArray(); EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
                File.WriteAllText(folder + "/Editor/Sources.json", JsonUtility.ToJson(new Recipe { libraryGuid = Guid(library), resolution = resolution, compressed = compress, sources = sources.ToArray() }, true));
                AssetDatabase.ImportAsset(folder + "/Editor/Sources.json");
                string[] forbidden = sources.SelectMany(s => s.textureGuids.Concat(new[] { s.materialGuid })).Where(s => !string.IsNullOrEmpty(s)).Select(AssetDatabase.GUIDToAssetPath).Distinct().ToArray();
                var dependencies = new HashSet<string>(AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(library), true));
                var leaked = forbidden.Where(dependencies.Contains).ToArray();
                if (leaked.Length > 0) throw new InvalidOperationException("Bake retained source dependencies: " + string.Join(", ", leaked));
                Debug.Log($"God Surface: baked {sources.Count} surfaces into {banks.Count} banks; source texture/material dependency check passed.", library);
                return library;
            }
            catch { if (folder != null && AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder); throw; }
            finally { EditorUtility.ClearProgressBar(); if (wasRendering) world.RequestRebuild(); }
        }
        static Texture2DArray BakeArray(List<Texture> textures, int channel, int limit, bool compress)
        {
            int size = 4; foreach (var texture in textures) if (texture) size = Mathf.Max(size, Mathf.Min(limit, Mathf.NextPowerOfTwo(Mathf.Max(texture.width, texture.height))));
            bool linear = channel != 0 && channel != 3 && channel != 5;
            var array = new Texture2DArray(size, size, textures.Count, compress ? TextureFormat.BC7 : TextureFormat.RGBA32, true, linear)
                { name = MGSurfaceSchema.Arrays[channel], wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, mipMapBias = 0 };
            Texture sampler = textures.FirstOrDefault(t => t);
            if (sampler) { array.wrapModeU = sampler.wrapModeU; array.wrapModeV = sampler.wrapModeV; }
            var shader = Shader.Find("Hidden/MashBox/GodSurfaceArrayPack"); if (!shader) throw new InvalidOperationException("Array packing shader is missing.");
            var pack = new Material(shader) { hideFlags = HideFlags.HideAndDontSave }; pack.SetFloat("_DecodeNormal", channel == 1 ? 1 : 0);
            var previous = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
            var target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            try
            {
                for (int slice = 0; slice < textures.Count; slice++)
                {
                    var readback = new Texture2D(size, size, TextureFormat.RGBA32, true, linear);
                    try
                    {
                        RenderTexture.active = target; GL.sRGBWrite = !linear;
                        if (textures[slice]) Graphics.Blit(textures[slice], target, pack);
                        else GL.Clear(false, true, channel == 1 ? new Color(.5f, .5f, 1, 1) : Color.white);
                        RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, size, size), 0, 0); readback.Apply(true, false);
                        if (compress) EditorUtility.CompressTexture(readback, TextureFormat.BC7, TextureCompressionQuality.Best);
                        for (int mip = 0; mip < array.mipmapCount; mip++) array.SetPixelData(readback.GetPixelData<byte>(mip), mip, slice);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(readback); }
                }
                array.Apply(false, true); return array;
            }
            catch { UnityEngine.Object.DestroyImmediate(array); throw; }
            finally { RenderTexture.active = previous; GL.sRGBWrite = previousSrgb; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(pack); }
        }
        public static int Apply(MGSurfaceWorld world, MGSurfaceLibrary library)
        {
            if (!world || !library) throw new ArgumentNullException();
            Recipe recipe = ReadRecipe(library); var replacements = recipe.sources.ToDictionary(s => s.materialGuid, s => Resolve<Material>(s.previewGuid));
            var materialCache = new Dictionary<Material, Material>();
            world.Release(); Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Apply God Surface library"); int changed = 0;
            try
            {
                foreach (var renderer in world.EnumerateRenderers())
                {
                    var materials = renderer.sharedMaterials; bool replace = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var current = materials[i]; if (!current) continue;
                        if (!materialCache.TryGetValue(current, out var preview))
                        {
                            var original = Original(current, world.surfaceLibrary);
                            preview = original && replacements.TryGetValue(Guid(original), out var replacement) ? replacement : current;
                            materialCache.Add(current, preview);
                        }
                        if (preview && current != preview) { materials[i] = preview; replace = true; }
                    }
                    if (!replace) continue; Undo.RecordObject(renderer, "Apply God Surface material"); renderer.sharedMaterials = materials;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); changed++;
                }
                Undo.RecordObject(world, "Assign God Surface library"); world.surfaceLibrary = library; EditorUtility.SetDirty(world);
                PrefabUtility.RecordPrefabInstancePropertyModifications(world); EditorSceneManager.MarkSceneDirty(world.gameObject.scene); return changed;
            }
            finally { Undo.CollapseUndoOperations(undo); world.RequestRebuild(); }
        }
        public static int Restore(MGSurfaceWorld world)
        {
            if (!world || !world.surfaceLibrary) return 0;
            var originals = ReadRecipe(world.surfaceLibrary).sources.ToDictionary(s => s.previewGuid, s => Resolve<Material>(s.materialGuid));
            if (originals.Values.Any(m => !m)) throw new InvalidOperationException("A source material is missing. Restore it before reverting this library.");
            world.Release(); int changed = 0; Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
            foreach (var r in world.EnumerateRenderers())
            {
                var materials = r.sharedMaterials; bool replace = false;
                for (int i = 0; i < materials.Length; i++) if (originals.TryGetValue(Guid(materials[i]), out var original)) { materials[i] = original; replace = true; }
                if (!replace) continue; Undo.RecordObject(r, "Restore source surface material"); r.sharedMaterials = materials; PrefabUtility.RecordPrefabInstancePropertyModifications(r); changed++;
            }
            Undo.RecordObject(world, "Remove surface library"); world.surfaceLibrary = null; EditorUtility.SetDirty(world); EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            Undo.CollapseUndoOperations(undo); world.RequestRebuild(); return changed;
        }
    }
}

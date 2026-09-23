using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    // All mutations are enrolled before writing. A failed/cancelled operation restores
    // both data and .meta, including native subassets and importer metadata.
    internal sealed class MGTerrainAssetTransaction : IDisposable
    {
        readonly string backup = Path.Combine(Path.GetTempPath(), "MG-Terrain-Transaction-" + Guid.NewGuid().ToString("N"));
        readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool complete, disposed;
        readonly List<Action> rollback = new List<Action>();
        readonly List<Action> publish = new List<Action>();
        internal void OnRollback(Action action) => rollback.Add(action);
        internal void OnCommit(Action action) => publish.Add(action);
        internal void Track(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) throw new IOException("Terrain output must be inside Assets: " + path);
            if (files.ContainsKey(path)) return;
            Directory.CreateDirectory(backup);
            string copy = Path.Combine(backup, files.Count.ToString());
            if (File.Exists(path))
            {
                if (!AssetDatabase.MakeEditable(path)) throw new IOException("Terrain asset is not writable: " + path);
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
                File.Copy(path, copy);
            }
            if (File.Exists(path + ".meta")) File.Copy(path + ".meta", copy + ".meta");
            files.Add(path, copy);
        }
        internal void Commit() { foreach (var action in publish) action(); complete = true; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (!complete)
                {
                    foreach (var action in rollback.AsEnumerable().Reverse()) action();
                    foreach (var pair in files.Reverse())
                    {
                        if (File.Exists(pair.Value))
                        {
                            File.Copy(pair.Value, pair.Key, true);
                            if (File.Exists(pair.Value + ".meta")) File.Copy(pair.Value + ".meta", pair.Key + ".meta", true);
                            AssetDatabase.ImportAsset(pair.Key, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                        }
                        else if (File.Exists(pair.Key)) AssetDatabase.DeleteAsset(pair.Key);
                    }
                }
            }
            finally
            {
                // Only files created in this transaction's private temporary directory.
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
            }
        }
    }

    internal static class MGTerrainAssetStore
    {
        internal static string TileIdentity(MGTerrain tile)
        {
            if (MGTerrainTileNames.TryCoordinates(tile, out var coordinates))
                return "Tile_" + coordinates.x + "_" + coordinates.y;
            return MGTerrainSceneAssets.SafeName(tile.name);
        }
        internal static string Prefix(MGTerrain tile)
        {
            var world = tile.World != null ? tile.World : tile.GetComponentInParent<MGTerrainWorld>(true);
            string identity = TileIdentity(tile);
            return MGTerrainSceneAssets.SafeName(world != null ? world.name : "MG Terrain") + "_" + identity;
        }
        internal static string PathFor(MGTerrain tile, string role, string extension = ".asset")
            => MGTerrainSceneAssets.Folder(tile) + "/" + Prefix(tile) + "_" + MGTerrainSceneAssets.SafeName(role) + extension;
        internal static string MapPath(MGTerrain tile, string role)
        {
            string native = PathFor(tile, role);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(native) != null ? native : PathFor(tile, role, ".png");
        }
        internal static T Save<T>(T generated, string path, MGTerrainAssetTransaction transaction, bool undo = false) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == generated) return existing;
            if (existing == null && AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new IOException("Terrain asset type conflict: " + path);
            transaction.Track(path);
            if (existing == null)
            {
                generated.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            if (undo) Undo.RegisterCompleteObjectUndo(existing, "Update Terrain Data");
            EditorUtility.CopySerialized(generated, existing);
            if (existing is Mesh mesh && generated is Mesh source) { mesh.vertices = source.vertices; mesh.UploadMeshData(false); }
            existing.name = Path.GetFileNameWithoutExtension(path);
            if (existing is Texture2D texture && texture.isReadable) texture.Apply(false, false);
            if (existing is Texture2DArray array && array.isReadable) array.Apply(false, false);
            EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing);
            return existing;
        }
        internal static Texture2D SaveMap(Texture2D generated, string path, bool linear, MGTerrainAssetTransaction transaction, bool undo = false)
        {
            if (Path.GetExtension(path).Equals(".asset", StringComparison.OrdinalIgnoreCase)) return Save(generated, path, transaction, undo);
            if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsupported terrain map output: " + path);
            transaction.Track(path);
            byte[] png = generated.EncodeToPNG();
            if (undo && File.Exists(path)) MGTerrainMapUndo.Record(path, png);
            MGTerrainAppearanceCaptureAssets.WritePng(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = !linear;
            importer.convertToNormalmap = false; importer.flipGreenChannel = false; importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true; importer.fadeout = false; importer.mipMapsPreserveCoverage = false;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4; importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(generated.width, generated.height));
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = false; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        internal static T SaveGenerated<T>(T generated, MGTerrain tile, string role) where T : Object
        {
            using var transaction = new MGTerrainAssetTransaction();
            try { var saved = Save(generated, PathFor(tile, role), transaction, true); transaction.Commit(); return saved; }
            finally { if (!EditorUtility.IsPersistent(generated)) Object.DestroyImmediate(generated); }
        }
        internal static Texture2D SaveDetailMap(Texture2D generated, MGTerrain tile, int index, string channel)
        {
            string role = $"Layer_{index}_{channel}";
            // A canonical name may still belong to a deliberately shared source.
            // Never overwrite another layer's map when isolating a paint stroke.
            for (int suffix = 0; ; suffix++)
            {
                string candidate = suffix == 0 ? role : role + "_Local" + suffix;
                var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(PathFor(tile, candidate));
                bool shared = existing != null && Resources.FindObjectsOfTypeAll<MGTerrain>().Any(other =>
                    other != null && other.gameObject.scene.IsValid() && other.DensityDetailLayers.Where((layer, i) => other != tile || i != index)
                    .Any(layer => layer.DensityMap == existing || layer.SizeMap == existing || layer.GrassIdMap == existing));
                if (!shared) return SaveGenerated(generated, tile, candidate);
            }
        }
        internal static MGTerrain FindTile(Material material)
            => Resources.FindObjectsOfTypeAll<MGTerrain>().FirstOrDefault(t=>t.gameObject.scene.IsValid() && t.MeshRenderer != null && t.MeshRenderer.sharedMaterial == material);
        internal static string MaterialTexturePath(Material material, string role, string fallback, string extension)
        {
            var tile = FindTile(material);
            return tile != null ? (extension == ".png" ? MapPath(tile, role) : PathFor(tile, role, extension)) : fallback;
        }
        internal static Material Material(MGTerrain tile, MGTerrainAssetTransaction transaction)
        {
            var renderer = tile.MeshRenderer;
            if (renderer == null || renderer.sharedMaterial == null) throw new InvalidOperationException("Tile material is missing.");
            string path = PathFor(tile, "Material", ".mat");
            if (AssetDatabase.GetAssetPath(renderer.sharedMaterial) == path) { transaction.Track(path); MGTerrainControlMapOwnership.Ensure(tile, renderer.sharedMaterial, transaction); return renderer.sharedMaterial; }
            var copy = new Material(renderer.sharedMaterial);
            try
            {
                var result = Save(copy, path, transaction, true);
                Undo.RecordObject(renderer, "Assign Tile Material");
                var slots = renderer.sharedMaterials; slots[0] = result; renderer.sharedMaterials = slots;
                EditorUtility.SetDirty(renderer);
                MGTerrainControlMapOwnership.Ensure(tile, result, transaction);
                return result;
            }
            finally { if (!EditorUtility.IsPersistent(copy)) Object.DestroyImmediate(copy); }
        }
    }

    // PNG importer data is not serialized by Unity Undo. Keep history in memory,
    // rather than creating another material and texture asset for every stamp.
    internal sealed class MGTerrainMapUndo : ScriptableObject
    {
        [SerializeField] string assetPath;
        [SerializeField] byte[] png;
        [SerializeField] int revision;
        [NonSerialized] int publishedRevision;
        static readonly Dictionary<string, MGTerrainMapUndo> entries = new Dictionary<string, MGTerrainMapUndo>();
        [InitializeOnLoadMethod] static void Initialize()
        {
            Undo.undoRedoPerformed -= Restore; Undo.undoRedoPerformed += Restore;
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
        }
        internal static void Record(string path, byte[] next)
        {
            if (!entries.TryGetValue(path, out var state) || state == null)
            {
                state = CreateInstance<MGTerrainMapUndo>(); state.hideFlags = HideFlags.HideAndDontSave;
                state.assetPath = path; entries[path] = state;
            }
            state.png = File.ReadAllBytes(path);
            Undo.RegisterCompleteObjectUndo(state, "Stamp Terrain Map");
            state.png = next; state.revision++; state.publishedRevision = state.revision;
        }
        static void Restore()
        {
            foreach (var state in entries.Values)
                if (state != null && state.revision != state.publishedRevision)
                {
                    state.publishedRevision = state.revision;
                    if (state.png == null || !File.Exists(state.assetPath) || File.ReadAllBytes(state.assetPath).SequenceEqual(state.png)) continue;
                    MGTerrainAppearanceCaptureAssets.WritePng(state.assetPath, state.png);
                    AssetDatabase.ImportAsset(state.assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                }
        }
        static void Clear() { foreach (var state in entries.Values) if (state != null) DestroyImmediate(state); entries.Clear(); }
    }
}

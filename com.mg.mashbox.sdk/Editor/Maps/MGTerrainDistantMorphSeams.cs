#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    // The freshly captured tile owns its shared boundary. Copy decoded world-space
    // lift, never normalized R16 values (each map has its own height range).
    internal static class MGTerrainDistantMorphSeams
    {
        const float Epsilon = .001f;
        const string Property = "_DistantSurfaceHeightMap";
        sealed class Tile
        {
            internal MGTerrain terrain;
            internal Texture2D map;
            internal Bounds bounds;
            internal Vector4 decode;
            internal float scale;
            internal float[] lift;
            internal bool changed;
            internal int Index(int x, int z) => z * map.width + x;
        }

        internal static void Weld(MGTerrain source, Texture2D height, MGTerrainAssetTransaction transaction)
        {
            var world = source.GetComponentInParent<MGTerrainWorld>(true);
            var candidates = world != null ? world.GetComponentsInChildren<MGTerrain>(true)
                : Resources.FindObjectsOfTypeAll<MGTerrain>().Where(t => t.gameObject.scene == source.gameObject.scene
                    && t.GetComponentInParent<MGTerrainWorld>(true) == null).ToArray();
            Weld(source, height, candidates, transaction);
        }

        // Explicit candidates also allow isolated validation without an open authoring scene.
        internal static void Weld(MGTerrain source, Texture2D height, IEnumerable<MGTerrain> candidates,
            MGTerrainAssetTransaction transaction)
        {
            var origin = Read(source, height);
            var neighbours = new List<Tile>();
            foreach (var terrain in candidates.Distinct())
            {
                if (terrain == null || terrain == source || terrain.MeshFilter == null || terrain.MeshFilter.sharedMesh == null) continue;
                Bounds bounds = WorldBounds(terrain);
                if (!Touches(origin.bounds, bounds)) continue;
                var material = terrain.MeshRenderer != null ? terrain.MeshRenderer.sharedMaterial : null;
                var map = material != null && material.HasProperty(Property) ? material.GetTexture(Property) as Texture2D : null;
                if (map == null) continue; // A later first bake will join this tile to its existing neighbours.
                if (map == height || neighbours.Any(t => t.map == map))
                    throw new InvalidOperationException("Distant morph welding needs a separate height asset for each tile: " + terrain.name);
                var next = Read(terrain, map);
                CopyBoundary(origin, next);
                if (next.changed) neighbours.Add(next);
            }
            // Validate every neighbour before touching any assets. All writes belong to
            // the surrounding capture transaction, including material range changes.
            foreach (var neighbour in neighbours) Save(neighbour, transaction);
        }

        static Bounds WorldBounds(MGTerrain terrain)
        {
            var filter = terrain.MeshFilter;
            Bounds local = filter.sharedMesh.bounds;
            var result = new Bounds(filter.transform.TransformPoint(local.min), Vector3.zero);
            for (int i = 1; i < 8; i++) result.Encapsulate(filter.transform.TransformPoint(new Vector3(
                (i & 1) == 0 ? local.min.x : local.max.x, (i & 2) == 0 ? local.min.y : local.max.y,
                (i & 4) == 0 ? local.min.z : local.max.z)));
            return result;
        }

        static bool Near(float a, float b) => Mathf.Abs(a - b) <= Epsilon;
        static bool Touches(Bounds a, Bounds b)
        {
            bool overlapX = Mathf.Min(a.max.x, b.max.x) >= Mathf.Max(a.min.x, b.min.x) - Epsilon;
            bool overlapZ = Mathf.Min(a.max.z, b.max.z) >= Mathf.Max(a.min.z, b.min.z) - Epsilon;
            return overlapZ && (Near(a.max.x, b.min.x) || Near(a.min.x, b.max.x))
                || overlapX && (Near(a.max.z, b.min.z) || Near(a.min.z, b.max.z));
        }

        static Tile Read(MGTerrain terrain, Texture2D map)
        {
            var transform = terrain.MeshFilter.transform;
            Vector3 right = transform.TransformVector(Vector3.right), up = transform.TransformVector(Vector3.up),
                forward = transform.TransformVector(Vector3.forward);
            if (right.x <= 0 || up.y <= 0 || forward.z <= 0 ||
                Mathf.Abs(right.y) + Mathf.Abs(right.z) + Mathf.Abs(up.x) + Mathf.Abs(up.z)
                + Mathf.Abs(forward.x) + Mathf.Abs(forward.y) > .00001f)
                throw new InvalidOperationException("Distant morph welding requires upright, axis-aligned tiles with positive scale: " + terrain.name);
            Vector4 decode = MGTerrainHeightEncoding.ReadDecode(map);
            if (map.format != TextureFormat.R16 || !map.isReadable || decode.z < 1.5f || map.width < 2 || map.height < 2)
                throw new InvalidOperationException("Distant morph welding requires readable delta R16 height maps. Clear the legacy height assignment on "
                    + terrain.name + " and rebake its distant morph maps.");
            var tile = new Tile { terrain = terrain, map = map, bounds = WorldBounds(terrain), decode = decode, scale = up.y };
            var pixels = map.GetPixelData<ushort>(0);
            tile.lift = new float[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) tile.lift[i] = pixels[i] / 65535f * decode.y * tile.scale;
            return tile;
        }

        static void CopyBoundary(Tile source, Tile other)
        {
            Bounds a = source.bounds, b = other.bounds;
            bool xSide = Near(a.max.x, b.min.x) || Near(a.min.x, b.max.x);
            bool zSide = Near(a.max.z, b.min.z) || Near(a.min.z, b.max.z);
            void Copy(int sx, int sz, int dx, int dz)
            {
                other.lift[other.Index(dx, dz)] = source.lift[source.Index(sx, sz)];
                other.changed = true;
            }
            if (xSide && zSide)
            {
                // Diagonal neighbours share the same corner as both edge neighbours.
                Copy(Near(a.max.x, b.min.x) ? source.map.width - 1 : 0,
                    Near(a.max.z, b.min.z) ? source.map.height - 1 : 0,
                    Near(a.max.x, b.min.x) ? 0 : other.map.width - 1,
                    Near(a.max.z, b.min.z) ? 0 : other.map.height - 1);
                return;
            }
            if (xSide)
            {
                RequireMatchingEdge(Near(a.min.z, b.min.z) && Near(a.max.z, b.max.z)
                    && source.map.height == other.map.height, other);
                for (int z = 0; z < source.map.height; z++)
                    Copy(Near(a.max.x, b.min.x) ? source.map.width - 1 : 0, z,
                        Near(a.max.x, b.min.x) ? 0 : other.map.width - 1, z);
            }
            else if (zSide)
            {
                RequireMatchingEdge(Near(a.min.x, b.min.x) && Near(a.max.x, b.max.x)
                    && source.map.width == other.map.width, other);
                for (int x = 0; x < source.map.width; x++)
                    Copy(x, Near(a.max.z, b.min.z) ? source.map.height - 1 : 0,
                        x, Near(a.max.z, b.min.z) ? 0 : other.map.height - 1);
            }
        }

        static void RequireMatchingEdge(bool matches, Tile tile)
        {
            if (!matches) throw new InvalidOperationException("Cannot weld distant morph edge to " + tile.terrain.name
                + ": touching tiles must have matching edge extents and height-map resolution. Use matching tile sizes and capture resolutions;"
                + " clear outdated height assignments before rebaking. The previous bake has been kept.");
        }

        static void Save(Tile tile, MGTerrainAssetTransaction transaction)
        {
            string path = AssetDatabase.GetAssetPath(tile.map);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith("_Height.asset", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cannot weld a height map outside the generated _Height.asset files: " + path);
            float range = Mathf.Max(tile.decode.y, tile.lift.Max() / tile.scale);
            var pixels = new ushort[tile.lift.Length];
            // Keep the existing range whenever possible: untouched pixels then keep
            // their exact R16 values. Ranges can grow to avoid clipping a taller canopy.
            for (int i = 0; i < pixels.Length; i++) pixels[i] = range > 0
                ? (ushort)Mathf.RoundToInt(Mathf.Clamp01(tile.lift[i] / tile.scale / range) * 65535f) : (ushort)0;
            transaction.Track(path);
            Undo.RegisterCompleteObjectUndo(tile.map, "Weld Distant Morph Heights");
            tile.map.SetPixelData(pixels, 0); tile.map.Apply(false, false);
            tile.map.wrapMode = TextureWrapMode.Clamp; tile.map.filterMode = FilterMode.Bilinear;
            EditorUtility.SetDirty(tile.map); AssetDatabase.SaveAssetIfDirty(tile.map);
            var decode = new Vector4(0, range, 2, 0);
            if (range != tile.decode.y) MGTerrainHeightEncoding.SaveMetadata(path, decode);
            var renderer = tile.terrain.MeshRenderer;
            var slots = renderer.sharedMaterials;
            transaction.OnRollback(() => { if (renderer != null) renderer.sharedMaterials = slots; });
            var material = MGTerrainAssetStore.Material(tile.terrain, transaction);
            Undo.RecordObject(material, "Weld Distant Morph Heights");
            material.SetVector("_DistantSurfaceHeightDecode", decode);
            float top = tile.terrain.MeshFilter.sharedMesh.bounds.max.y + range;
            material.SetFloat("_DistantSurfaceMaxHeight", top);
            if (material.HasProperty("_TessellationMaxDisplacement"))
                material.SetFloat("_TessellationMaxDisplacement", Mathf.Max(material.GetFloat("_TessellationMaxDisplacement"),
                    (top - tile.terrain.MeshFilter.sharedMesh.bounds.min.y) * tile.scale + 1));
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        }
    }
}
#endif

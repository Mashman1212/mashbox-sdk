using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    // Snapshot component state once per draw, not once per source vertex.
    // Bounds only reject impossible hits; the collider supplies the exact height.
    internal sealed class PrefabStampSurfaceSampler
    {
        struct Entry { internal MeshCollider Collider; internal Bounds Bounds; internal int Tile; }
        readonly List<Entry> entries = new List<Entry>();
        public void Prepare(IReadOnlyList<MGTerrain> tiles)
        {
            entries.Clear();
            for (int i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i];
                if (tile == null) continue;
                Add(tile.MeshCollider, i);
                var chunks = tile.SurfaceColliderChunks;
                for (int j = 0; j < chunks.Count; j++) Add(chunks[j], i);
            }
        }
        void Add(MeshCollider collider, int tile)
        {
            if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
                entries.Add(new Entry { Collider = collider, Bounds = collider.bounds, Tile = tile });
        }
        public float? Sample(Vector3 point)
        {
            bool found = false;
            float highest = float.NegativeInfinity;
            int tile = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                // Match the existing first-hit-tile behaviour, including overlap/seams.
                if (found && entry.Tile != tile) break;
                var b = entry.Bounds;
                if (point.x < b.min.x || point.x > b.max.x || point.z < b.min.z || point.z > b.max.z) continue;
                var ray = new Ray(new Vector3(point.x, b.max.y + 1, point.z), Vector3.down);
                if (entry.Collider.Raycast(ray, out var hit, b.size.y + 2))
                { highest = Mathf.Max(highest, hit.point.y); found = true; tile = entry.Tile; }
            }
            return found ? highest + .02f : (float?)null;
        }
    }
}

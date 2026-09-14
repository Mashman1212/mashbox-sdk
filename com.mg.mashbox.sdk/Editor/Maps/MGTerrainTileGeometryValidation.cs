using System;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public static class MGTerrainTileGeometryValidation
    {
        [MenuItem("MashBox/Validation/Validate Terrain Tile Geometry")]
        public static void Run()
        {
            var mesh = new Mesh();
            try
            {
                var original = new[] { new Vector3(0, 1, 0), new Vector3(5, 2, 0), new Vector3(10, 3, 0),
                    new Vector3(0, 4, 10), new Vector3(10, 6, 10), new Vector3(0, 2, 5), new Vector3(10, 4, 5),
                    new Vector3(3.17f, 8, 4.63f), new Vector3(0, 1, 0), new Vector3(5, 2, 0) };
                mesh.vertices = original;
                mesh.RecalculateBounds();
                foreach (var direction in new[] { Vector2Int.down, Vector2Int.up, Vector2Int.left, Vector2Int.right })
                {
                    MGTerrainTileAuthoring.BuildTileVertices(mesh, direction, out var xs, out var zs, out var vertices, out var uv);
                    Check(xs.Length == 3 && zs.Length == 3 && vertices.Length == 9, "Interior and duplicate vertices must not change grid dimensions.");
                    Check(uv[0] == Vector2.zero && uv[8] == Vector2.one, "Tile UV coverage.");
                    for (int z = 0; z < 3; z++)
                        for (int x = 0; x < 3; x++)
                        {
                            float expected = direction == Vector2Int.down ? 1 + x
                                : direction == Vector2Int.up ? 4 + x
                                : direction == Vector2Int.left ? new[] { 1f, 2f, 4f }[z] : new[] { 3f, 4f, 6f }[z];
                            Check(Mathf.Abs(vertices[z * 3 + x].y - expected) < .0001f, "Shared edge height and interpolation: " + direction);
                        }
                }
                Check(mesh.vertices.SequenceEqual(original), "Source mesh must remain unchanged.");
                mesh.vertices = original.Concat(new[] { new Vector3(5, -5, 0) }).ToArray();
                mesh.RecalculateBounds();
                bool rejected = false;
                try { MGTerrainTileAuthoring.BuildTileVertices(mesh, Vector2Int.down, out _, out _, out _, out _); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Ambiguous overlapping edge heights must not silently produce a seam.");
                Debug.Log("Terrain tile geometry PASS: south/north/east/west, split vertices, irregular interior, edge interpolation, UVs, source preservation, ambiguous edge guard.");
                ValidateSurfaceTileReuse();
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        static void ValidateSurfaceTileReuse()
        {
            var owner = new GameObject("Surface tile reuse validation") { hideFlags = HideFlags.HideAndDontSave };
            var mesh = new Mesh();
            try
            {
                var filter = owner.AddComponent<MeshFilter>();
                owner.AddComponent<MeshRenderer>();
                mesh.vertices = new[] { Vector3.zero, new Vector3(64, 0, 0), new Vector3(0, 0, 64), new Vector3(64, 0, 64) };
                mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                mesh.colors = Enumerable.Repeat(Color.red, 4).ToArray();
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                filter.sharedMesh = mesh;
                var terrain = owner.AddComponent<MGTerrain>();
                terrain.RefreshSurfaceTiles();
                var tiles = owner.GetComponentsInChildren<MeshFilter>().Where(terrain.IsSurfaceRenderTile).ToArray();
                Check(tiles.Length > 0, "Validation must create render tiles.");
                var originals = tiles.Select(tile => tile.sharedMesh).ToArray();

                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i].y = 3f;
                mesh.vertices = vertices;
                mesh.colors = Enumerable.Repeat(Color.blue, 4).ToArray();
                mesh.RecalculateBounds();
                terrain.NotifySurfaceMeshChanged();
                terrain.RefreshSurfaceTiles();
                // Settings validation must update the cache without recreating it.
                owner.SendMessage("OnValidate", SendMessageOptions.DontRequireReceiver);
                terrain.RefreshSurfaceTiles();
                for (int i = 0; i < tiles.Length; i++)
                {
                    Check(tiles[i] != null && tiles[i].sharedMesh == originals[i], "Vertex edits and component validation must reuse tile meshes.");
                    Check(originals[i].vertices.All(vertex => Mathf.Abs(vertex.y - 3f) < .0001f), "Reused tiles must contain updated heights.");
                    Check(originals[i].colors.All(color => color == Color.blue), "Reused tiles must update colours.");
                    Check(originals[i].uv.Length == originals[i].vertexCount, "Tile UVs must survive refresh.");
                }
                mesh.colors = Array.Empty<Color>();
                mesh.uv = Array.Empty<Vector2>();
                terrain.NotifySurfaceMeshChanged();
                terrain.RefreshSurfaceTiles();
                Check(originals.All(tile => tile.colors.Length == 0 && tile.uv.Length == 0), "Removed channels must not retain stale buffer data.");
                terrain.NotifySurfaceMeshChanged(true);
                terrain.RefreshSurfaceTiles();
                Check(originals.All(tile => tile == null), "Explicit topology changes must rebuild render tiles.");
                Debug.Log("Surface tile reuse PASS: stable mesh identities, updated heights/colours, UV preservation, removed channels, topology rebuild.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}

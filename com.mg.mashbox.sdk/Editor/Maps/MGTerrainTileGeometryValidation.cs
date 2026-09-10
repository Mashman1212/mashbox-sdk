using System;
using System.Linq;
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
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}

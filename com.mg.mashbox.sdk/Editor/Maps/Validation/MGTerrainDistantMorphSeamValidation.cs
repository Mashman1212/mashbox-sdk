#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public static class MGTerrainDistantMorphSeamValidation
    {
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("RunBatch is for an isolated batch-mode project only.");
            string path = "Assets/MGDistantSeamBatch_" + Guid.NewGuid().ToString("N") + ".unity";
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), path);
            try { Run(); }
            finally { AssetDatabase.DeleteAsset(path); }
        }

        [MenuItem("Tools/MashBox/MG Terrain/Validation/Distant Morph Seams")]
        public static void Run()
        {
            string folder = "Assets/MGDistantSeamValidation_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            var previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
            var tiles = new List<MGTerrain>();
            string report = "FAIL: validation did not complete.";
            try
            {
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, folder + "/Validation.unity");
                string shaderPath = folder + "/Validation.shader";
                File.WriteAllText(shaderPath, "Shader \"Hidden/MGDistantSeamValidation\" { Properties { "
                    + "_DistantSurfaceHeightMap (\"Height\", 2D) = \"black\" {} "
                    + "_DistantSurfaceHeightDecode (\"Decode\", Vector) = (0,1,2,0) "
                    + "_DistantSurfaceMaxHeight (\"Max\", Float) = 1 "
                    + "_TessellationMaxDisplacement (\"Displacement\", Float) = 1 } SubShader { Pass {} } }");
                AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                MGTerrain Create(int x, int z, float scale, int resolution = 8)
                {
                    var go = new GameObject("Tile " + x + " " + z); go.SetActive(false);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                    go.transform.position = new Vector3(x * 10, 0, z * 10);
                    go.transform.localScale = new Vector3(1, scale, 1);
                    var mesh = new Mesh { vertices = new[] { Vector3.zero, new Vector3(10,0,0),
                        new Vector3(0,0,10), new Vector3(10,0,10) }, triangles = new[] { 0,2,1,1,2,3 } };
                    mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, folder + "/Mesh" + tiles.Count + ".asset");
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    var material = new Material(shader);
                    AssetDatabase.CreateAsset(material, folder + "/Material" + tiles.Count + ".mat");
                    renderer.sharedMaterial = material;
                    var tile = go.AddComponent<MGTerrain>(); tiles.Add(tile);
                    var texture = new Texture2D(resolution, resolution, TextureFormat.R16, false, true)
                        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    string path = folder + "/Tile" + tiles.Count + "_Height.asset";
                    AssetDatabase.CreateAsset(texture, path);
                    Fill(tile, texture, tiles.Count == 1 ? 100 : 5, tiles.Count == 1 ? 30 : 2);
                    return tile;
                }
                var a = Create(0, 0, 1); var b = Create(1, 0, 2);
                var c = Create(0, 1, 1); var d = Create(1, 1, .5f);
                var far = Create(4, 4, 1);
                var originals = tiles.ToDictionary(t => t, t => Pixels(Map(t)));
                string farPath = AssetDatabase.GetAssetPath(Map(far));
                byte[] farBytes = File.ReadAllBytes(farPath);
                var guids = tiles.ToDictionary(t => t, t => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Map(t))));
                void Weld(MGTerrain tile, bool commit)
                {
                    using var transaction = new MGTerrainAssetTransaction();
                    MGTerrainDistantMorphSeams.Weld(tile, Map(tile), tiles, transaction);
                    if (commit) transaction.Commit();
                }
                Weld(a, false);
                foreach (var tile in tiles) Check(Pixels(Map(tile)).SequenceEqual(originals[tile]), "Rollback restores height pixels");
                Check(MGTerrainHeightEncoding.ReadDecode(Map(b)).y == 5, "Rollback restores decode metadata");
                Weld(a, true);
                CheckEdges(a, b, c, d, false);
                Check(MGTerrainHeightEncoding.ReadDecode(Map(b)).y > 5, "Taller canopy grows neighbour range");
                Check(Mathf.Abs(Lift(Map(b), 4, 4) * 2 - originals[b][4 * 8 + 4] / 65535f * 5 * 2) < .005f,
                    "Interior lift survives range expansion");
                Check(File.ReadAllBytes(farPath).SequenceEqual(farBytes), "Non-neighbour remains byte-identical");
                foreach (var tile in tiles) Check(guids[tile] == AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Map(tile))), "Height GUID survives welding");
                Weld(b, true); Weld(c, true); Weld(d, true); CheckEdges(a, b, c, d);
                // A later bake from another tile owns its edge, including a lower canopy.
                Fill(b, Map(b), 15, 3); Weld(b, true); CheckEdges(a, b, c, d);
                var once = tiles.ToDictionary(t => t, t => Pixels(Map(t)));
                Weld(b, true);
                foreach (var tile in tiles) Check(Pixels(Map(tile)).SequenceEqual(once[tile]), "Repeated weld is idempotent");
                // Mixed resolutions must fail atomically instead of silently producing a false weld.
                var mismatch = Create(-1, 0, 1, 16);
                bool rejected = false;
                try { Weld(a, true); } catch (InvalidOperationException e) { rejected = e.Message.Contains("resolution"); }
                Check(rejected, "Mismatched edge resolution is rejected");
                foreach (var tile in once.Keys) Check(Pixels(Map(tile)).SequenceEqual(once[tile]), "Invalid neighbour leaves existing pixels unchanged");
                report = "PASS: four-tile edges/corners, decoded ranges and Y scales, taller/lower rebakes, preserved interiors and GUIDs, isolated non-neighbour, idempotence, rollback including metadata, mixed-resolution rejection.";
                Debug.Log(report);
            }
            catch (Exception e) { report = e.ToString(); throw; }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousScene);
                AssetDatabase.DeleteAsset(folder);
                EditorUtility.ClearProgressBar();
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "mg-distant-morph-seams-result.txt"), report);
            }
        }

        static Texture2D Map(MGTerrain tile) => (Texture2D)tile.MeshRenderer.sharedMaterial.GetTexture("_DistantSurfaceHeightMap");
        static ushort[] Pixels(Texture2D map) => map.GetPixelData<ushort>(0).ToArray();
        static void Fill(MGTerrain tile, Texture2D map, float range, float lift)
        {
            var pixels = new ushort[map.width * map.height];
            for (int z = 0; z < map.height; z++) for (int x = 0; x < map.width; x++)
                pixels[z * map.width + x] = (ushort)Mathf.RoundToInt((lift + .1f * x + .2f * z) / range * 65535);
            map.SetPixelData(pixels, 0); map.Apply(false, false);
            EditorUtility.SetDirty(map); AssetDatabase.SaveAssetIfDirty(map);
            MGTerrainHeightEncoding.SaveMetadata(AssetDatabase.GetAssetPath(map), new Vector4(0, range, 2, 0));
            var material = tile.MeshRenderer.sharedMaterial;
            material.SetTexture("_DistantSurfaceHeightMap", map);
            material.SetVector("_DistantSurfaceHeightDecode", new Vector4(0, range, 2, 0));
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        }
        static float Lift(Texture2D map, int x, int z) => map.GetPixelData<ushort>(0)[z * map.width + x] / 65535f * MGTerrainHeightEncoding.ReadDecode(map).y;
        static void CheckEdges(MGTerrain a, MGTerrain b, MGTerrain c, MGTerrain d, bool all = true)
        {
            void Pair(MGTerrain left, MGTerrain right, bool alongZ)
            {
                var first = Map(left); var second = Map(right);
                for (int i = 0; i < 8; i++)
                {
                    float x = Lift(first, alongZ ? 7 : i, alongZ ? i : 7) * left.transform.lossyScale.y;
                    float y = Lift(second, alongZ ? 0 : i, alongZ ? i : 0) * right.transform.lossyScale.y;
                    // R16 maps retain independent ranges. Error is bounded by their quantization.
                    Check(Mathf.Abs(x - y) < .005f, "Shared world-space lift agrees along edges and at the four-way corner");
                }
            }
            // Before all four initial bakes, only the source's edges are synchronized.
            Pair(a, b, true); Pair(a, c, false);
            if (all) { Pair(b, d, false); Pair(c, d, true); }
            Check(Mathf.Abs(Lift(Map(a),7,7) - Lift(Map(d),0,0) * .5f) < .005f, "Diagonal corner agrees");
            Check(Mathf.Abs(Lift(Map(a),7,7) - Lift(Map(c),7,0)) < .005f, "All corner owners agree");
        }
        static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    public static class MGTerrainSeamValidation
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }

        [MenuItem("Tools/MashBox/MG Terrain/Validation/Mixed Resolution Seams")]
        public static void Run()
        {
            if (Application.isBatchMode && string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/SeamValidationBootstrap.unity");
            foreach (var ratio in new[] { (5, 5), (3, 9), (4, 5) }) Geometry(ratio.Item1, ratio.Item2);
            Junction();
            Propagation();
            StitchedRim();
            MeshSculptWindow.ValidateBufferedSeamJoining();
            Persistence();
            Debug.Log("MG TERRAIN MIXED SEAM VALIDATION PASSED: equal, 4:1 and non-integer grids; full-segment radius expansion; normals; T-junctions; stitched rims; coarse-edge propagation; nonuniform scale; idempotence; unchanged interiors/topology/source assets; Undo/Redo; save/reload; scene-open repair without autosaving.");
        }

        static MGTerrain Tile(Scene scene, string name, Vector3 origin, float size, int width, float bias, List<Mesh> meshes)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); go.transform.position = origin;
            var tile = go.AddComponent<MGTerrain>();
            var vertices = new Vector3[width * width]; var uv = new Vector2[vertices.Length];
            var triangles = new int[(width - 1) * (width - 1) * 6];
            for (int z = 0; z < width; z++) for (int x = 0; x < width; x++)
            {
                vertices[z * width + x] = new Vector3(size * x / (width - 1), bias + Mathf.Sin(x + z * .7f), size * z / (width - 1));
                uv[z * width + x] = new Vector2(x / (float)(width - 1), z / (float)(width - 1));
            }
            for (int z = 0, t = 0; z < width - 1; z++) for (int x = 0; x < width - 1; x++)
            {
                int i = z * width + x;
                triangles[t++] = i; triangles[t++] = i + width; triangles[t++] = i + 1;
                triangles[t++] = i + 1; triangles[t++] = i + width; triangles[t++] = i + width + 1;
            }
            var mesh = new Mesh { vertices = vertices, uv = uv, triangles = triangles }; meshes.Add(mesh);
            mesh.RecalculateBounds(); mesh.RecalculateNormals(); mesh.RecalculateTangents();
            tile.MeshFilter.sharedMesh = mesh; tile.ConfigureSurfaceGrid(width, width);
            var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
            tile.Configure(tile.MeshFilter, tile.MeshRenderer, collider);
            return tile;
        }

        static (Vector3 position, Vector3 normal)[] Edge(MGTerrain tile, bool alongZ, float coordinate)
        {
            var mesh = tile.MeshFilter.sharedMesh; var normals = mesh.normals;
            return mesh.vertices.Select((p, i) => (position: tile.MeshFilter.transform.TransformPoint(p),
                normal: tile.MeshFilter.transform.worldToLocalMatrix.transpose.MultiplyVector(normals[i]).normalized))
                .Where(p => Mathf.Abs((alongZ ? p.position.x : p.position.z) - coordinate) < .001f)
                .OrderBy(p => alongZ ? p.position.z : p.position.x).ToArray();
        }
        static void Match(MGTerrain a, MGTerrain b, bool alongZ, float coordinate)
        {
            var first = Edge(a, alongZ, coordinate); var second = Edge(b, alongZ, coordinate);
            Check(first.Length >= 2 && second.Length >= 2, "Missing validation edge");
            void Compare((Vector3 position, Vector3 normal)[] points, (Vector3 position, Vector3 normal)[] other)
            {
                float Along(Vector3 p) => alongZ ? p.z : p.x;
                foreach (var point in points)
                {
                    float t = Along(point.position);
                    if (t < Along(other[0].position) - .001f || t > Along(other[other.Length - 1].position) + .001f) continue;
                    int i = 0; while (i < other.Length - 2 && Along(other[i + 1].position) < t) i++;
                    float blend = Mathf.InverseLerp(Along(other[i].position), Along(other[i + 1].position), t);
                    Check(Mathf.Abs(point.position.y - Mathf.Lerp(other[i].position.y, other[i + 1].position.y, blend)) < .0001f,
                        $"Cracked seam at {point.position} between {a.name} and {b.name}");
                    // At shared knots the normal must be identical. At inserted knots it
                    // must follow the enclosing coarse segment's interpolated normal.
                    if (blend < .00001f || blend > .99999f || points.Length >= other.Length)
                        Check((point.normal - Vector3.Lerp(other[i].normal, other[i + 1].normal, blend).normalized).sqrMagnitude < .00001f,
                            "Seam normal mismatch");
                }
            }
            Compare(first, second); Compare(second, first);
        }

        static void Stable(List<MGTerrain> tiles)
        {
            var vertices = tiles.Select(t => t.MeshFilter.sharedMesh.vertices).ToArray();
            var normals = tiles.Select(t => t.MeshFilter.sharedMesh.normals).ToArray();
            int changes = 0;
            MGTerrainSeams.Join(tiles, Vector3.zero, float.PositiveInfinity, true, _ => changes++);
            Check(changes == 0, "Repeated repair should not dirty any tile");
            for (int i = 0; i < tiles.Count; i++)
            {
                Check(vertices[i].SequenceEqual(tiles[i].MeshFilter.sharedMesh.vertices), "Repeated repair moved vertices");
                Check(normals[i].SequenceEqual(tiles[i].MeshFilter.sharedMesh.normals), "Repeated repair changed normals");
            }
        }

        static void Geometry(int aWidth, int bWidth)
        {
            var scene = EditorSceneManager.NewPreviewScene(); var meshes = new List<Mesh>();
            try
            {
                var a = Tile(scene, "A", Vector3.zero, 8, aWidth, 0, meshes);
                var b = Tile(scene, "B", new Vector3(8, 0, 0), 8, bWidth, 3, meshes);
                var tiles = new List<MGTerrain> { a, b };
                var interiors = meshes.Select(m => m.vertices[m.vertexCount / 2]).ToArray();
                var topology = meshes.Select(m => m.triangles).ToArray();
                MGTerrainSeams.Join(tiles, Vector3.zero, float.PositiveInfinity, true);
                Match(a, b, true, 8); Stable(tiles);
                for (int i = 0; i < meshes.Count; i++)
                {
                    if ((i == 0 ? aWidth : bWidth) % 2 == 1) Check(meshes[i].vertices[meshes[i].vertexCount / 2] == interiors[i], "Interior changed");
                    Check(meshes[i].triangles.SequenceEqual(topology[i]), "Topology changed");
                }
                // A tiny dab changes one knot; constraints must reach the rest of its coarse segment.
                var vertices = meshes[0].vertices; int index = aWidth - 1;
                vertices[index].y += 2; meshes[0].vertices = vertices;
                MGTerrainSeams.Join(tiles, new Vector3(8, 0, 0), .1f, true);
                Match(a, b, true, 8); Stable(tiles);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh); }
        }

        static void Junction()
        {
            var scene = EditorSceneManager.NewPreviewScene(); var meshes = new List<Mesh>();
            try
            {
                var coarse = Tile(scene, "Large", Vector3.zero, 8, 2, 0, meshes);
                var a = Tile(scene, "Small A", new Vector3(8, 0, 0), 4, 5, 3, meshes);
                var b = Tile(scene, "Small B", new Vector3(8, 0, 4), 4, 5, 5, meshes);
                coarse.transform.localScale = new Vector3(1, 2, 1);
                var tiles = new List<MGTerrain> { b, coarse, a };
                MGTerrainSeams.Join(tiles, Vector3.zero, float.PositiveInfinity, true);
                Match(coarse, a, true, 8); Match(coarse, b, true, 8); Match(a, b, false, 4); Stable(tiles);
                // Whole-mesh normal recalculation during another dab must be repaired at distant borders.
                meshes[1].RecalculateNormals();
                var neighbours = MGTerrainSeams.Neighbours(tiles, new List<MGTerrain> { a });
                MGTerrainSeams.Join(neighbours, Vector3.zero, float.PositiveInfinity, true);
                Match(coarse, a, true, 8); Match(coarse, b, true, 8); Match(a, b, false, 4); Stable(tiles);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh); }
        }

        static void Propagation()
        {
            var scene = EditorSceneManager.NewPreviewScene(); var meshes = new List<Mesh>();
            try
            {
                var large = Tile(scene, "Coarse span", Vector3.zero, 16, 2, 0, meshes);
                var tiles = new List<MGTerrain> { large };
                for (int i = 0; i < 4; i++) tiles.Add(Tile(scene, "Small " + i, new Vector3(16, 0, i * 4), 4, 5, i + 2, meshes));
                var distant = Tile(scene, "Unrelated", new Vector3(100, 0, 100), 4, 5, 7, meshes); tiles.Add(distant);
                var original = distant.MeshFilter.sharedMesh.vertices;
                var touched = new List<MGTerrain> { tiles[1] };
                MGTerrainSeams.Join(MGTerrainSeams.Neighbours(tiles, touched), Vector3.zero, float.PositiveInfinity, true,
                    null, new HashSet<MGTerrain>(touched), tiles);
                for (int i = 1; i <= 4; i++) Match(large, tiles[i], true, 16);
                Check(original.SequenceEqual(distant.MeshFilter.sharedMesh.vertices), "Unrelated tile changed");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh); }
        }

        static void StitchedRim()
        {
            var scene = EditorSceneManager.NewPreviewScene(); var meshes = new List<Mesh>();
            try
            {
                var large = Tile(scene, "Stitched", Vector3.zero, 8, 2, 0, meshes);
                var fine = Tile(scene, "Fine", new Vector3(8, 0, 0), 8, 9, 4, meshes);
                var mesh = large.MeshFilter.sharedMesh;
                mesh.vertices = mesh.vertices.Concat(new[] { new Vector3(8, 5, 4) }).ToArray();
                mesh.uv = mesh.uv.Concat(new[] { new Vector2(1, .5f) }).ToArray();
                mesh.triangles = new[] { 0,2,1,1,2,4,4,2,3 }; mesh.RecalculateBounds(); mesh.RecalculateNormals();
                var tiles = new List<MGTerrain> { large, fine };
                MGTerrainSeams.Join(tiles, Vector3.zero, float.PositiveInfinity, true);
                Match(large, fine, true, 8); Stable(tiles);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh); }
        }

        static void Persistence()
        {
            string folder = "Assets/MGSeamValidation_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var meshes = new List<Mesh>();
            try
            {
                string scenePath = folder + "/Test.unity";
                EditorSceneManager.SaveScene(scene, scenePath);
                var root = new GameObject("Validation World"); SceneManager.MoveGameObjectToScene(root, scene);
                var world = root.AddComponent<MGTerrainWorld>(); world.TileSize = 8;
                var a = Tile(scene, "A", Vector3.zero, 8, 3, 0, meshes);
                var b = Tile(scene, "B", new Vector3(8, 0, 0), 8, 9, 3, meshes);
                var sourceVertices = meshes.Select(mesh => mesh.vertices).ToArray();
                a.transform.SetParent(root.transform, true); b.transform.SetParent(root.transform, true);
                for (int i = 0; i < meshes.Count; i++) AssetDatabase.CreateAsset(meshes[i], folder + "/source" + i + ".asset");
                root.SetActive(false);
                // Repair uses hierarchy ownership even for disabled worlds.
                EditorSceneManager.SaveScene(scene, scenePath);
                Match(a, b, true, 8);
                Check(a.EditableSculptMesh == a.MeshFilter.sharedMesh && b.EditableSculptMesh == b.MeshFilter.sharedMesh,
                    "Seam repair did not prepare owned editable meshes (inactive tiles must participate in Undo)");
                for (int i = 0; i < meshes.Count; i++) Check(sourceVertices[i].SequenceEqual(meshes[i].vertices), "Source mesh asset was overwritten");
                var before = a.MeshFilter.sharedMesh;
                var positions = before.vertices; positions[2].y += 4; before.vertices = positions;
                var damaged = before.vertices;
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                MGTerrainSeamRepair.Repair(scene); Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
                Match(a, b, true, 8);
                Undo.PerformUndo(); Check(a.MeshFilter.sharedMesh.vertices.SequenceEqual(damaged),
                    $"Undo did not restore the damaged edge: expected {damaged[2].y}, got {a.MeshFilter.sharedMesh.vertices[2].y}");
                Undo.PerformRedo(); Match(a, b, true, 8);
                EditorSceneManager.SaveScene(scene, scenePath);
                string mapA = AssetDatabase.GetAssetPath(a.MeshFilter.sharedMesh), mapB = AssetDatabase.GetAssetPath(b.MeshFilter.sharedMesh);
                var expectedA = a.MeshFilter.sharedMesh.vertices; var expectedB = b.MeshFilter.sharedMesh.vertices;
                EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.ImportAsset(mapA, ImportAssetOptions.ForceUpdate); AssetDatabase.ImportAsset(mapB, ImportAssetOptions.ForceUpdate);
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                var reloaded = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<MGTerrain>(true)).OrderBy(t => t.name).ToArray();
                Check(reloaded[0].MeshFilter.sharedMesh.vertices.SequenceEqual(expectedA) && reloaded[1].MeshFilter.sharedMesh.vertices.SequenceEqual(expectedB), "Saved seam geometry did not survive reload");
                Match(reloaded[0], reloaded[1], true, 8);
                // Save damaged mesh bytes directly, then exercise the real scene-open
                // queue synchronously. Opening must fix memory without an implicit save.
                var reopenedMesh = reloaded[0].MeshFilter.sharedMesh;
                var broken = reopenedMesh.vertices; broken[2].y += 3; reopenedMesh.vertices = broken;
                EditorUtility.SetDirty(reopenedMesh); AssetDatabase.SaveAssetIfDirty(reopenedMesh);
                byte[] damagedBytes = File.ReadAllBytes(mapA);
                EditorSceneManager.CloseScene(scene, true);
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                var pending = (System.Collections.ICollection)typeof(MGTerrainGridRepair)
                    .GetField("Pending", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                var process = typeof(MGTerrainGridRepair).GetMethod("ProcessNext", BindingFlags.Static | BindingFlags.NonPublic);
                for (int i = 0; pending.Count > 0 && i < 1024; i++) process.Invoke(null, null);
                Check(pending.Count == 0, "Scene-open repair queue did not drain");
                reloaded = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<MGTerrain>(true)).OrderBy(t => t.name).ToArray();
                Match(reloaded[0], reloaded[1], true, 8);
                Check(scene.isDirty && damagedBytes.SequenceEqual(File.ReadAllBytes(mapA)), "Opening a scene must leave repaired meshes dirty without autosaving");
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                SceneManager.SetActiveScene(previous); AssetDatabase.DeleteAsset(folder);
                foreach (var mesh in meshes) if (mesh != null && !EditorUtility.IsPersistent(mesh)) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }
}
#endif

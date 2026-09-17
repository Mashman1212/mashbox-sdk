#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.Sculpting;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    public static class MGTerrainTileValidation
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        [MenuItem("MashBox/Validation/Validate Terrain Tile Authoring")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            TerrainData data = null;
            try
            {
                var go = new GameObject("Original Tile"); SceneManager.MoveGameObjectToScene(go, scene);
                var tile = go.AddComponent<MGTerrain>();
                var vertices = new Vector3[9];
                for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++) vertices[z * 3 + x] = new Vector3(x * 5, x + z, z * 5);
                var mesh = new Mesh { vertices = vertices, triangles = new[] { 0,3,1,1,3,4,1,4,2,2,4,5,3,6,4,4,6,7,4,7,5,5,7,8 } };
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); tile.MeshFilter.sharedMesh = mesh;
                tile.ConfigureSurfaceGrid(3, 3);
                var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
                tile.Configure(tile.MeshFilter, tile.MeshRenderer, collider);
                var world = MGTerrainWorldEditor.Adopt(new[] { tile });
                Check(tile.MeshFilter.sharedMesh == mesh && tile.MeshCollider == collider && tile.transform.parent == world.transform, "Adoption must preserve the original surface and collider.");
                var east = MGTerrainTileAuthoring.AddTile(tile, Vector2Int.right);
                var north = MGTerrainTileAuthoring.AddTile(tile, Vector2Int.up);
                var corner = MGTerrainTileAuthoring.AddTile(east, Vector2Int.up);
                Check(world.Chunks.Count == 4, "Expected four registered tiles.");
                Check(east.MeshFilter.sharedMesh != mesh, "New tiles must own their geometry.");
                bool occupied = false;
                try { MGTerrainTileAuthoring.AddTile(tile, Vector2Int.right); } catch (InvalidOperationException) { occupied = true; }
                Check(occupied && world.Chunks.Count == 4, "Occupied additions must not create objects.");
                CheckEdges(world);
                var tiles = world.Chunks.ToList();
                foreach (var chunk in tiles)
                {
                    chunk.HeightOnlySculpt = true;
                    var modifier = MGTerrainTileAuthoring.Modifier(chunk);
                    modifier.AddStroke(modifier.CreateStroke(MeshSculptModifier.SculptMode.Smooth, MeshSculptModifier.StrokeSpace.World,
                        new Vector3(10, 3, 10), Vector3.up, 8, .7f, 2));
                    modifier.ApplyLatestStrokePreview();
                }
                MGTerrainTileAuthoring.JoinBrushEdges(tiles, new Vector3(10, 3, 10), 8);
                CheckEdges(world);
                foreach (var chunk in tiles) MGTerrainTileAuthoring.Modifier(chunk).Rebuild();
                CheckEdges(world);
                data = new TerrainData { heightmapResolution = 33, size = new Vector3(20, 20, 20) };
                var heights = new float[33,33];
                for (int z = 0; z < 33; z++) for (int x = 0; x < 33; x++) heights[z,x] = .5f;
                data.SetHeights(0, 0, heights);
                var terrainGo = Terrain.CreateTerrainGameObject(data); SceneManager.MoveGameObjectToScene(terrainGo, scene);
                terrainGo.transform.position = new Vector3(0, 7, 0);
                MGTerrainTileAuthoring.Conform(tile, terrainGo.GetComponent<Terrain>(), null, 1, 0);
                foreach (var chunk in tiles)
                    foreach (var v in chunk.MeshFilter.sharedMesh.vertices)
                        Check(Mathf.Abs(chunk.MeshFilter.transform.TransformPoint(v).y - 17) < .001f, "Conform must include source world height.");
                CheckEdges(world);
                var stampGo = new GameObject("Stamp"); SceneManager.MoveGameObjectToScene(stampGo, scene);
                var stampMesh = new Mesh { vertices = new[] {new Vector3(0,9,0),new Vector3(20,9,0),new Vector3(0,9,20),new Vector3(20,9,20)}, triangles = new[] {0,2,1,1,2,3} };
                stampMesh.RecalculateBounds(); var stamp = stampGo.AddComponent<MeshCollider>(); stamp.sharedMesh = stampMesh;
                MGTerrainTileAuthoring.Conform(tile, null, stamp, 1, 1);
                foreach (var chunk in tiles)
                    Check(Mathf.Abs(chunk.MeshFilter.sharedMesh.vertices[4].y - 10) < .001f, "Mesh stamp must project to the source plus offset.");
                CheckEdges(world);
                Debug.Log("MG TERRAIN TILE VALIDATION PASSED: adoption, neighbors/corners, occupied guard, seam smoothing/replay, Unity Terrain world-height import, mesh stamping.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (data != null) UnityEngine.Object.DestroyImmediate(data);
            }
        }
        static void CheckEdges(MGTerrainWorld world)
        {
            var samples = new Dictionary<Vector2, float>();
            foreach (var tile in world.Chunks)
                foreach (var v in tile.MeshFilter.sharedMesh.vertices)
                {
                    var p = tile.MeshFilter.transform.TransformPoint(v); var key = new Vector2(p.x, p.z);
                    if (samples.TryGetValue(key, out float height)) Check(Mathf.Abs(height - p.y) < .001f, "Tile seam diverged.");
                    samples[key] = p.y;
                }
        }
    }
}
#endif

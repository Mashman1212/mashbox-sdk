using System;
using System.IO;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainTileIdentityValidation
    {
        static void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Tile Asset Names")]
        internal static void Run()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("MG Terrain World"); root.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var mesh = new Mesh();
            try
            {
                root.AddComponent<MGTerrainWorld>();
                root.transform.position = new Vector3(-115,235,632);
                mesh.vertices = new[] {new Vector3(-256,0,-256), new Vector3(256,0,256)};
                mesh.RecalculateBounds();
                var go = new GameObject("Terrain Tile (0, 1)"); go.transform.SetParent(root.transform,false);
                var tile = go.AddComponent<MGTerrain>(); tile.MeshFilter.sharedMesh = mesh;
                go.transform.localPosition = new Vector3(-256,0,256);
                Check(MGTerrainAssetStore.Prefix(tile)=="MG Terrain World_Tile_-1_0", "Mesh footprint handles centered pivots and negative coordinates");
                var duplicate = new GameObject(go.name); duplicate.transform.SetParent(root.transform,false);
                var other = duplicate.AddComponent<MGTerrain>(); other.MeshFilter.sharedMesh = mesh;
                duplicate.transform.localPosition = new Vector3(256,0,768);
                Check(MGTerrainAssetStore.Prefix(other)=="MG Terrain World_Tile_0_1", "Duplicate stale labels resolve from distinct grid positions");
                root.transform.position += new Vector3(100,9,-25); duplicate.transform.SetAsFirstSibling();
                Check(MGTerrainAssetStore.Prefix(tile)=="MG Terrain World_Tile_-1_0", "World movement and sibling order preserve identity");
                go.name = "bad stale label";
                Check(MGTerrainAssetStore.Prefix(tile)=="MG Terrain World_Tile_-1_0", "Authored label is not identity");
                string report="PASS: geometry-based coordinates, centered pivots, negative cells, stale duplicate labels, world movement and hierarchy reorder; exact plain asset prefix.";
                File.WriteAllText(Path.Combine(Path.GetTempPath(),"mg-tile-identity-result.txt"),report); Debug.Log(report);
            }
            catch(Exception e) { File.WriteAllText(Path.Combine(Path.GetTempPath(),"mg-tile-identity-result.txt"),e.ToString()); throw; }
            finally {UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(mesh); EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}

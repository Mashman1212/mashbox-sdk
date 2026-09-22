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
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Tile Asset Names")]
        internal static void Run()
        {
            var scene=EditorSceneManager.NewPreviewScene();
            var root=new GameObject("Identity Test"); root.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
            try
            {
                root.AddComponent<MGTerrainWorld>();
                var tileObject=new GameObject("Terrain Tile (0, 1)");tileObject.transform.SetParent(root.transform,false);
                var tile=tileObject.AddComponent<MGTerrain>();
                string prefix=MGTerrainAssetStore.Prefix(tile);
                Check(prefix.EndsWith("_Tile_0_1"),"Two displayed coordinates");
                tileObject.transform.localPosition=new Vector3(-512,35,256);
                var sibling=new GameObject("Other child");sibling.transform.SetParent(root.transform,false);sibling.transform.SetAsFirstSibling();
                Check(MGTerrainAssetStore.Prefix(tile)==prefix,"Position and sibling order cannot change identity");
                tile.name="Terrain Tile (-1, 2)";
                Check(MGTerrainAssetStore.Prefix(tile).EndsWith("_Tile_-1_2"),"Negative coordinates preserved when actually named that way");
                string beforeDuplicate=MGTerrainAssetStore.Prefix(tile);
                sibling.name=tile.name; var duplicate=sibling.AddComponent<MGTerrain>();
                string duplicatePrefix=MGTerrainAssetStore.Prefix(duplicate);
                Check(MGTerrainAssetStore.Prefix(tile)==beforeDuplicate,"Adding a duplicate label cannot rename existing assets");
                Check(duplicatePrefix!=beforeDuplicate && duplicatePrefix.EndsWith("_Tile_-1_2"),"Duplicate labels have distinct asset keys and retain two coordinates");
                sibling.transform.SetAsLastSibling();
                Check(MGTerrainAssetStore.Prefix(duplicate)==duplicatePrefix,"Duplicate asset key survives hierarchy reorder");
                UnityEngine.Object.DestroyImmediate(sibling);
                Check(MGTerrainAssetStore.Prefix(tile)==beforeDuplicate,"Removing duplicate cannot rename existing assets");
                string report="PASS: two-coordinate labels; transform/hierarchy independence; duplicate labels get distinct stable asset keys without blocking or renaming their peers.";
                File.WriteAllText(Path.Combine(Path.GetTempPath(),"mg-tile-identity-result.txt"),report); Debug.Log(report);
            }
            catch(Exception e) {File.WriteAllText(Path.Combine(Path.GetTempPath(),"mg-tile-identity-result.txt"),e.ToString());throw;}
            finally { UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene); }
        }
        static void Check(bool value,string label) {if(!value)throw new InvalidOperationException(label);}
    }
}

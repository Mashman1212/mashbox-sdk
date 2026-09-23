using System;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainCleanupScopeValidation
    {
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Selected World Cleanup")]
        internal static void Run()
        {
            string root="Assets/MGCleanupScope_"+Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets",Path.GetFileName(root));
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                EditorSceneManager.SaveScene(scene,root+"/Test.unity");
                string folder=MGTerrainSceneAssets.Folder(scene);
                MGTerrainWorld World(string name) {var go=new GameObject(name);go.SetActive(false);var w=go.AddComponent<MGTerrainWorld>();w.TileSize=10;return w;}
                var selected=World("Current World");var old=World("Old World");
                Mesh Mesh(string name) {var m=new Mesh {vertices=new[]{Vector3.zero,new Vector3(10,0,10)}};m.RecalculateBounds();AssetDatabase.CreateAsset(m,folder+"/"+name+".asset");return m;}
                MGTerrain Tile(MGTerrainWorld w,Mesh m) {var go=new GameObject("Terrain Tile (0, 0)");go.transform.SetParent(w.transform,false);go.AddComponent<MeshFilter>().sharedMesh=m;go.AddComponent<MeshRenderer>();return go.AddComponent<MGTerrain>();}
                var currentMesh=Mesh("Current");var oldMesh=Mesh("Old");var orphan=Mesh("Orphan");
                string oldGuid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(oldMesh));
                string orphanGuid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(orphan));
                var currentTile=Tile(selected,currentMesh);var oldTile=Tile(old,oldMesh);
                var plan=MGTerrainDataCleanup.Build(selected);
                if(plan.Tiles.Length!=1||plan.Tiles[0]!=currentTile||plan.Items.Any(i=>i.Asset==oldMesh))throw new Exception("Other world's terrain entered selected cleanup plan");
                MGTerrainDataCleanup.Execute(plan);
                string moved=AssetDatabase.GUIDToAssetPath(oldGuid);
                if(!moved.Contains("/Other Terrain Data/")||oldTile.MeshFilter.sharedMesh!=oldMesh)throw new Exception("Other-world reference was not safely relocated");
                if(File.Exists(AssetDatabase.GUIDToAssetPath(orphanGuid)))throw new Exception("Unused asset survived cleanup");
                var keep=MGTerrainDataCleanup.WorldOutputs(plan);
                var files=AssetDatabase.FindAssets("",new[]{folder}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>!AssetDatabase.IsValidFolder(p));
                if(files.Any(p=>!keep.Contains(p)))throw new Exception("Unrelated asset remains in selected folder");
                File.WriteAllText(Path.Combine(Path.GetTempPath(),"mg-cleanup-scope-result.txt"),"PASS: selected-world-only plan; inactive other-world assets moved outside folder with GUID/reference intact; unused asset trashed; folder contains only selected outputs.");
            }
            catch(Exception e) {File.WriteAllText(Path.Combine(Path.GetTempPath(),"mg-cleanup-scope-result.txt"),e.ToString());throw;}
            finally {EditorSceneManager.CloseScene(scene,true);UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);AssetDatabase.DeleteAsset(root);}
        }
    }
}

using System;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainControlMapValidation
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Control Map Ownership")]
        internal static void Run()
        {
            string folder="Assets/MGControlValidation_"+Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                EditorSceneManager.SaveScene(scene,folder+"/Test.unity");
                var root=new GameObject("Test World");root.SetActive(false);
                var world=root.AddComponent<MGTerrainWorld>(); world.TileSize=10;
                var mesh=new Mesh {vertices=new[]{Vector3.zero,new Vector3(10,0,10)}};mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh,folder+"/mesh.asset");
                var map=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                map.SetPixels(new[]{Color.red,Color.green,Color.blue,Color.black});map.Apply();
                AssetDatabase.CreateAsset(map,folder+"/original.asset");
                var material=new Material(Shader.Find("Shader Graphs/MG_Lit_Trail"));
                material.SetTexture("_ControlMap1",map);material.SetTexture("_ControlMap2",map);
                AssetDatabase.CreateAsset(material,folder+"/original.mat");
                var tiles=new MGTerrain[2];
                for(int i=0;i<2;i++)
                {
                    var go=new GameObject("stale");go.transform.SetParent(root.transform,false);go.transform.localPosition=new Vector3(10*i,0,0);
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
                    tiles[i]=go.AddComponent<MGTerrain>();
                }
                // Both empty component fields and shared shader slots must be repaired.
                MGTerrainControlMapOwnership.Repair(world);
                Check(tiles[0].MeshRenderer.sharedMaterial!=tiles[1].MeshRenderer.sharedMaterial,"Materials must be independent");
                var maps=tiles.SelectMany(t=>new[]{t.ControlMap1,t.ControlMap2}).ToArray();
                Check(maps.Distinct().Count()==4,"Every tile and channel needs its own control texture");
                foreach(var tile in tiles)
                {
                    Check(tile.MeshRenderer.sharedMaterial.GetTexture("_ControlMap1")==tile.ControlMap1,"Channel 1 binding");
                    Check(tile.MeshRenderer.sharedMaterial.GetTexture("_ControlMap2")==tile.ControlMap2,"Channel 2 binding");
                }
                Check(maps.All(m=>m.GetPixels32().SequenceEqual(map.GetPixels32())),"Paint pixels must survive exactly");
                var paths=maps.Select(AssetDatabase.GetAssetPath).ToArray();var guids=paths.Select(AssetDatabase.AssetPathToGUID).ToArray();
                MGTerrainControlMapOwnership.Repair(world);
                Check(guids.SequenceEqual(paths.Select(AssetDatabase.AssetPathToGUID)),"Repeat repair preserves GUIDs");
                tiles[0].ControlMap1.SetPixel(0,0,Color.white);tiles[0].ControlMap1.Apply();
                Check(tiles[1].ControlMap1.GetPixel(0,0)==Color.red,"Painting a tile cannot affect another tile");
                var plan=MGTerrainDataCleanup.Build(world);
                Check(plan.Items.Count(i=>i.Destination.Contains("_ControlMap"))==4,"Cleanup must retain four separate control outputs");
                File.WriteAllText(Path.Combine(Path.GetTempPath(),"mg-control-validation.txt"),"PASS: shared materials/maps isolated, shader/component bindings agree, pixels preserved, repaint isolation, repeated GUID stability, cleanup controls stay separate.");
            }
            catch(Exception e) {File.WriteAllText(Path.Combine(Path.GetTempPath(),"mg-control-validation.txt"),e.ToString());throw;}
            finally {EditorSceneManager.CloseScene(scene,true);UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);AssetDatabase.DeleteAsset(folder);}
        }
    }
}

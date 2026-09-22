using System;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace MashBoxSDK.MapTools
{
    public static class MGTerrainBakeIntegrationValidation
    {
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Bake Asset Lifecycle")]
        public static void Run() => RunChecks(true);
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Morph Maps Only")]
        public static void RunMorphOnly() => RunChecks(false);
        static void RunChecks(bool cleanup)
        {
            string folder="Assets/MGTerrainIntegration_"+Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            var height=new Texture2D(8,8,TextureFormat.RFloat,false,true);
            var rgb=new Texture2D(8,8,TextureFormat.RGBA32,false);
            MGTerrainEditor editor=null;
            string report="";
            try
            {
                EditorSceneManager.SaveScene(scene,folder+"/Test.unity");
                var root=new GameObject("Validation World");root.SetActive(false);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
                var world=root.AddComponent<MGTerrainWorld>();
                var go=new GameObject("Duplicate Tile Name");go.transform.SetParent(root.transform,false);
                var mesh=new Mesh {vertices=new[]{new Vector3(0,0,0),new Vector3(10,0,0),new Vector3(0,0,10),new Vector3(10,0,10)},triangles=new[]{0,2,1,1,2,3}};
                mesh.RecalculateNormals();mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh,folder+"/OriginalMesh.asset");
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();
                var original=new Material(Shader.Find("Shader Graphs/MG_Lit_Trail"));
                AssetDatabase.CreateAsset(original,folder+"/OriginalMaterial.mat");renderer.sharedMaterial=original;
                var tile=go.AddComponent<MGTerrain>();
                var pixels=height.GetPixelData<float>(0);for(int i=0;i<pixels.Length;i++)pixels[i]=2; height.Apply();
                var colors=new Color[64];for(int i=0;i<colors.Length;i++)colors[i]=Color.green;rgb.SetPixels(colors);rgb.Apply();
                EditorSceneManager.SaveScene(scene);
                editor=(MGTerrainEditor)Editor.CreateEditor(tile,typeof(MGTerrainEditor));
                editor.ValidateDistantPublication(tile,height,rgb);
                string data=MGTerrainSceneAssets.Folder(tile);
                var first=AssetDatabase.FindAssets("",new[]{data}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x=>x).ToArray();
                var ids=first.Select(AssetDatabase.AssetPathToGUID).ToArray();
                var material=renderer.sharedMaterial;
                editor.ValidateDistantPublication(tile,height,rgb);
                var second=AssetDatabase.FindAssets("",new[]{data}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x=>x).ToArray();
                Check(first.SequenceEqual(second),"Second distant bake does not add assets");
                Check(ids.SequenceEqual(second.Select(AssetDatabase.AssetPathToGUID)),"All repeated-bake GUIDs survive");
                Check(renderer.sharedMaterial==material,"Morph reuses the tile material");
                Check(tile.GetComponentsInChildren<MGTerrainDistantSurface>(true).Length==1,"Only one distant surface hierarchy");
                editor.ValidateDistantPublication(tile,height,rgb,false);
                Check(tile.GetComponentsInChildren<MGTerrainDistantSurface>(true).Length==1,"Cancelled distant bake restores old scene surface");
                Check(ids.SequenceEqual(first.Select(AssetDatabase.AssetPathToGUID)),"Cancelled distant bake preserves assets");
                editor.ValidateDistantPublication(tile,height,rgb,false,true);
                Check(tile.GetComponentsInChildren<MGTerrainDistantSurface>(true).Length==1,"Cancelled morph bake retains old proxy");
                editor.ValidateDistantPublication(tile,height,rgb,true,true);
                Check(tile.GetComponentsInChildren<MGTerrainDistantSurface>(true).Length==0,"Morph bake retires old proxy");
                Check(renderer.sharedMaterial==material && material.GetTexture("_DistantSurfaceHeightMap")!=null,"Morph uses existing terrain material and height map");
                var morphPaths=AssetDatabase.FindAssets("",new[]{data}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x=>x).ToArray();
                Check(first.SequenceEqual(morphPaths),"Morph bake adds no meshes or materials");
                Check(ids.SequenceEqual(morphPaths.Select(AssetDatabase.AssetPathToGUID)),"Morph bake preserves asset GUIDs");
                var layerSource = new Texture2D(2,2);
                string layerSourcePath=data+"/LegacyMovedLayerNormal.asset";
                AssetDatabase.CreateAsset(layerSource,layerSourcePath);
                var terrainLayer = new TerrainLayer { diffuseTexture=layerSource,normalMapTexture=layerSource,maskMapTexture=layerSource };
                AssetDatabase.CreateAsset(terrainLayer,folder+"/SourceLayer.terrainlayer");
                material.SetTexture("_ControlMap1",layerSource); // Deliberate alias must not change ownership.
                var protectionPlan=MGTerrainDataCleanup.Build(world);
                Check(!protectionPlan.Items.Any(item=>item.Asset==layerSource),"TerrainLayer source is never moved even through a control-map alias");
                Check(protectionPlan.ProtectedSources.Contains(layerSourcePath),"Already moved source remains protected from deletion");
                Check(protectionPlan.Items.Any(item=>item.Asset==material.GetTexture("_DistantSurfaceHeightMap")),"Generated height map still organized");
                foreach(string property in new[]{"_BaseMap00","_NormalMap00","_MaskMap00","_BaseColorMap","_NormalMap","_MaskMap"})
                    Check(!MGTerrainDataCleanup.IsGeneratedMaterialTexture(property),"Source property excluded: "+property);
                report="PASS: repeated bakes preserve asset GUIDs; morph bake creates no extra mesh/material, removes old scene proxy, retains height assignment; cancellation preserves old proxy; cleanup protects TerrainLayer textures and source material slots.";
                if(!cleanup) { Debug.Log(report); return; }
                var orphan=new Texture2D(2,2);AssetDatabase.CreateAsset(orphan,data+"/Unused.asset");
                var referenced=new Texture2D(2,2);AssetDatabase.CreateAsset(referenced,data+"/ReferencedElsewhere.asset");
                var external=new Material(Shader.Find("HDRP/Lit"));external.SetTexture("_BaseColorMap",referenced);AssetDatabase.CreateAsset(external,folder+"/External.mat");AssetDatabase.SaveAssets();
                // Reproduce the old bake: a hidden separate surface still retains its assets.
                editor.ValidateDistantPublication(tile,height,rgb,true,false);
                var legacySurface=tile.GetComponentsInChildren<MGTerrainDistantSurface>(true).Single();
                string legacyMaterial=AssetDatabase.GetAssetPath(legacySurface.GetComponentInChildren<MeshRenderer>(true).sharedMaterial);
                string legacyMesh=AssetDatabase.GetAssetPath(legacySurface.GetComponentInChildren<MeshFilter>(true).sharedMesh);
                var plan=MGTerrainDataCleanup.Build(world);
                Check(plan.ObsoleteSurfaces.Count==1,"Legacy inactive morph proxy is scheduled for retirement");
                Check(!plan.Items.Any(item=>item.Source==legacyMaterial || item.Source==legacyMesh),"Legacy proxy assets are not reorganized as live data");
                var unused=MGTerrainDataCleanup.Unused(plan);
                Check(unused.Contains(data+"/Unused.asset"),"Unused data identified");
                Check(!unused.Contains(data+"/ReferencedElsewhere.asset"),"External material reference protected");
                MGTerrainDataCleanup.Execute(plan);
                Check(!File.Exists(data+"/Unused.asset") && File.Exists(data+"/ReferencedElsewhere.asset"),"Cleanup removes only unused test data");
                Check(renderer.sharedMaterial!=null && tile.MeshFilter.sharedMesh!=null,"Cleanup keeps tile references");
                Check(tile.GetComponentsInChildren<MGTerrainDistantSurface>(true).Length==0,"Cleanup retires legacy proxy without rebaking");
                Check(!File.Exists(legacyMaterial) && !File.Exists(legacyMesh),"Cleanup removes unreferenced legacy surface material and mesh");
                Check(AssetDatabase.GetAssetPath(tile.MeshFilter.sharedMesh).StartsWith(data+"/"),"Tile mesh moved into scene data folder");
                report="PASS: distant bake twice has identical file count and GUIDs; single terrain material and distant hierarchy; cancellation restores scene and files; inactive-tile cleanup retains external references and removes orphan data and obsolete morph proxy meshes/materials.";
                Debug.Log(report);
            }
            catch(Exception e){report=e.ToString();throw;}
            finally
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(),"mg-integration-result.txt"),report);
                if(editor!=null)UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(height);UnityEngine.Object.DestroyImmediate(rgb);
                EditorSceneManager.CloseScene(scene,true);UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
                AssetDatabase.DeleteAsset(folder);
            }
        }
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
    public sealed partial class MGTerrainEditor
    {
        internal void ValidateDistantPublication(MGTerrain tile,Texture2D height,Texture2D rgb,bool commit=true,bool morph=false)
        {
            m_ApplyDistantMorph=morph;
            using var tx=new MGTerrainAssetTransaction();
            var old=tile.MeshRenderer.sharedMaterials;tx.OnRollback(()=>tile.MeshRenderer.sharedMaterials=old);
            MGTerrainAssetStore.Material(tile,tx);
            string path=MGTerrainAssetStore.MapPath(tile,"Distant");
            var appearance=MGTerrainAssetStore.SaveMap(rgb,path,false,tx);
            m_DistantSpacing=2;m_DistantSmoothing=0;
            SaveDistantSurface(tile,height,appearance,path,tx);
            if(commit){ApplyDistantMorph(tile,m_LastMorphHeight);tx.Commit();}
        }
    }
}

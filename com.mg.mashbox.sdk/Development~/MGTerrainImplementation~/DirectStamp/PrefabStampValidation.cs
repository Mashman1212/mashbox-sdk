using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    internal static class PrefabStampValidation
    {
        [MenuItem("MashBox/Validation/Validate Prefab Stamp")]
        static void Run()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var resources = new List<Object>(); var paths = new List<string>();
            try
            {
                var root = new GameObject("Direct stamp validation");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var mesh = new Mesh { vertices = new[] { new Vector3(-1,0,-1), new Vector3(1,0,-1), new Vector3(1,0,1), new Vector3(-1,0,1), new Vector3(0,1,0) },
                    triangles = new[] { 0,4,1,1,4,2,2,4,3,3,4,0 },
                    uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up, new Vector2(.5f,.5f) } };
                resources.Add(mesh); mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); mesh.UploadMeshData(true);
                var texture = new Texture2D(4,4,TextureFormat.RGBA32,false); resources.Add(texture);
                var texels = new Color[16]; for (int i=0; i<16; i++) texels[i]=new Color(.8f,.2f,.1f,1);
                texture.SetPixels(texels); texture.Apply(false,true);
                var material = new Material(Shader.Find("HDRP/Lit")); resources.Add(material);
                material.SetTexture("_BaseColorMap",texture); material.SetColor("_BaseColor",Color.white);
                root.AddComponent<MeshFilter>().sharedMesh=mesh; root.AddComponent<MeshRenderer>().sharedMaterial=material;
                using var source=new PrefabStampSource(root);
                using var sampler=new PrefabStampSampler(source,true,true);
                var brush=new MeshStampBrush(source.Mesh);
                var dab=new PrefabStampAppearance.Dab { radius=8,height=6,rotation=90,falloff=0 };
                sampler.SetShape(dab);
                Check(brush.TrySurface(0,0,out _,out int triangle,out var bary),"Projected triangle lookup");
                Check(sampler.Sample(triangle,bary,false,out var rgb) && Mathf.Abs(rgb.r-.8f)<.025f && Mathf.Abs(rgb.g-.2f)<.025f,"Unlit texture RGB, without exposure");
                Check(!texture.isReadable && !mesh.isReadable,"Import readability stays unchanged");
                Check(sampler.Sample(triangle,bary,true,out var n) && n.g>.9f,"World-space normal transfer");
                var target=new GameObject("Direct stamp validation target"); target.SetActive(false);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(target,scene);
                var plane=new Mesh {vertices=new[] {new Vector3(-16,0,-16),new Vector3(16,0,-16),new Vector3(16,0,16),new Vector3(-16,0,16)},triangles=new[]{0,2,1,0,3,2}};
                resources.Add(plane); plane.RecalculateBounds(); target.AddComponent<MeshFilter>().sharedMesh=plane;
                var renderer=target.AddComponent<MeshRenderer>(); dab.tile=target.AddComponent<MGTerrain>();
                var map=new Texture2D(32,32,TextureFormat.RGBA32,false); resources.Add(map);
                var pixels=new Color[1024]; for(int i=0;i<pixels.Length;i++) pixels[i]=Color.blue;
                map.SetPixels(pixels); map.Apply();
                PrefabStampAppearance.Composite(map,sampler,dab,brush,false);
                Check(map.GetPixel(0,0)==Color.blue,"Outside footprint preserved");
                Check(map.GetPixel(16,16).r>.7f && map.GetPixel(16,16).b<.2f,"UV colour reaches destination texel");
                // Exercise actual production publication and forced disk reload, not only in-memory compositing.
                var shader=Shader.Find("Shader Graphs/MG_Lit_Trail");
                Check(shader!=null,"Terrain shader for persistence test");
                var terrainMaterial=new Material(shader); resources.Add(terrainMaterial);
                map.SetPixels(pixels); map.Apply(); terrainMaterial.SetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty,map);
                renderer.sharedMaterial=terrainMaterial;
                PrefabStampAppearance.Apply(source,brush,new List<PrefabStampAppearance.Dab>{dab},true,false);
                var saved=renderer.sharedMaterial; string matPath=AssetDatabase.GetAssetPath(saved); paths.Add(matPath);
                string mapPath=AssetDatabase.GetAssetPath(saved.GetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty)); paths.Add(mapPath);
                Check(!string.IsNullOrEmpty(mapPath),"Map is a saved asset before material assignment");
                AssetDatabase.ImportAsset(matPath,ImportAssetOptions.ForceUpdate);
                var loaded=AssetDatabase.LoadAssetAtPath<Material>(matPath);
                var loadedMap=loaded.GetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty) as Texture2D;
                Check(loadedMap!=null && loadedMap.width==32 && loadedMap.GetPixel(0,0)==Color.blue && loadedMap.GetPixel(16,16).r>.7f,"Saved material retains transferred map after reimport");
                Check(map.GetPixel(16,16)==Color.blue,"Original map remains unchanged");
                renderer.sharedMaterial=terrainMaterial;
                ValidateUserPrefab();
                Debug.Log("Direct prefab stamp PASS: unlit UV RGB, normals, non-readable sources, footprint preservation, original-map preservation, saved-map references after reimport, and project prefab sampling.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach(var path in paths) if(!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
                foreach(var resource in resources) if(resource!=null) Object.DestroyImmediate(resource);
            }
        }
        static void ValidateUserPrefab()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Maps/Cotswold Ridge/SJ_bg_hills/SJ_bg_hills_rockyHills/7_B_asset/SJ_BG_hills_7_B_PR.prefab");
            if(prefab==null) return;
            using var source=new PrefabStampSource(prefab);
            using var sampler=new PrefabStampSampler(source,true,true);
            var brush=new MeshStampBrush(source.Mesh);
            sampler.SetShape(new PrefabStampAppearance.Dab{radius=512,height=50,rotation=25,falloff=1});
            int count=0; float variation=0;
            for(int z=-8;z<=8;z++) for(int x=-8;x<=8;x++)
                if(brush.TrySurface(x/10f,z/10f,out _,out int tri,out var bary))
                {
                    Check(sampler.Sample(tri,bary,false,out var c),"Project prefab RGB sample");
                    Check(sampler.Sample(tri,bary,true,out var n),"Project prefab normal sample");
                    variation+=Mathf.Abs(c.r-c.b); count++;
                    var direction=new Vector3(n.r*2-1,n.g*2-1,n.b*2-1);
                    Check(Mathf.Abs(direction.magnitude-1)<.025f,"Project prefab normal is normalized");
                }
            Check(count>10 && variation>.05f,"Project prefab transfers textured colour, not grey");
        }
        static void Check(bool pass,string message) {if(!pass) throw new InvalidOperationException("Direct prefab stamp validation failed: "+message);}
    }
}

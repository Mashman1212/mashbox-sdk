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
                dab.falloff = 1; sampler.SetShape(dab);
                var normalMap = new Texture2D(32,32,TextureFormat.RGBA32,false,true); resources.Add(normalMap);
                var oldNormal = new Vector3(.8f,.6f,0);
                var normalPixels = new Color[1024];
                for(int j=0;j<normalPixels.Length;j++) normalPixels[j]=new Color(.9f,.8f,.5f,1);
                normalMap.SetPixels(normalPixels); normalMap.Apply();
                var outsideNormal = normalMap.GetPixel(0,0);
                PrefabStampAppearance.Composite(normalMap,sampler,dab,brush,true);
                Check(normalMap.GetPixel(0,0)==outsideNormal,"Normal outside stamp is untouched");
                Check(brush.TrySurface(-.5f/8,.5f/8,out _,out int normalTriangle,out var normalBary),"Normal texel surface lookup");
                sampler.Sample(normalTriangle,normalBary,true,out var expectedNormal);
                var actualNormal = normalMap.GetPixel(16,16);
                Check(Mathf.Abs(actualNormal.r-expectedNormal.r)<.008f && Mathf.Abs(actualNormal.g-expectedNormal.g)<.008f && Mathf.Abs(actualNormal.b-expectedNormal.b)<.008f,
                    "Projected world normal REPLACES the previous tilted normal at full weight");
                // Compare GPU rasterization against the CPU reference across the whole map,
                // including asymmetrical normals, feathering, footprint and map orientation.
                normalMap.SetPixels(normalPixels); normalMap.Apply();
                using (var projection = new PrefabStampProjection(source, true))
                using (var gpu = new PrefabStampProjection.Map(normalMap, true))
                {
                    projection.SetShape(dab); projection.Draw(gpu,dab);
                    var actual = gpu.Read(); resources.Add(actual);
                    PrefabStampAppearance.Composite(normalMap,sampler,dab,brush,true);
                    var reference = normalMap.GetPixels(); var rendered = actual.GetPixels();
                    float worst = 0;
                    for(int p=0;p<reference.Length;p++)
                        worst = Mathf.Max(worst, Mathf.Abs(reference[p].r-rendered[p].r), Mathf.Abs(reference[p].g-rendered[p].g), Mathf.Abs(reference[p].b-rendered[p].b));
                    Check(worst < .025f, "GPU world normals agree with CPU projection, max error " + worst);
                }
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
                normalMap.SetPixels(normalPixels); normalMap.Apply();
                terrainMaterial.SetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty,normalMap);
                PrefabStampAppearance.Apply(source,brush,new List<PrefabStampAppearance.Dab>{dab},false,true);
                var normalMaterial=renderer.sharedMaterial;
                paths.Add(AssetDatabase.GetAssetPath(normalMaterial.GetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty)));
                var normalMatPath=AssetDatabase.GetAssetPath(normalMaterial); paths.Add(normalMatPath);
                var normalPath=AssetDatabase.GetAssetPath(normalMaterial.GetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty)); paths.Add(normalPath);
                AssetDatabase.ImportAsset(normalMatPath,ImportAssetOptions.ForceUpdate);
                var loadedNormalMaterial=AssetDatabase.LoadAssetAtPath<Material>(normalMatPath);
                var savedNormal=loadedNormalMaterial.GetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty) as Texture2D;
                Check(savedNormal!=null && savedNormal.GetPixel(0,0)==outsideNormal && Mathf.Abs(savedNormal.GetPixel(16,16).r-expectedNormal.r)<.008f,
                    "Normals-only replacement is saved and survives reimport");
                Check(normalMap.GetPixel(16,16)==outsideNormal,"Original normal map remains unchanged");
                renderer.sharedMaterial=terrainMaterial;
                ValidateUserPrefab();
                Benchmark(dab.tile, terrainMaterial, resources, paths);
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
        static void Benchmark(MGTerrain tile, Material terrainMaterial, List<Object> resources, List<string> paths)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Maps/Cotswold Ridge/SJ_bg_hills/SJ_bg_hills_rockyHills/7_B_asset/SJ_BG_hills_7_B_PR.prefab");
            if(prefab==null) return;
            using var source = new PrefabStampSource(prefab);
            var rgb = new Texture2D(2048,2048,TextureFormat.RGBA32,true,false);
            var normal = new Texture2D(2048,2048,TextureFormat.RGBA32,true,true);
            resources.Add(rgb); resources.Add(normal);
            var pixels = new Color32[2048*2048];
            for(int i=0;i<pixels.Length;i++) pixels[i]=new Color32(128,255,128,255);
            rgb.SetPixels32(pixels); rgb.Apply(); normal.SetPixels32(pixels); normal.Apply();
            terrainMaterial.SetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty,rgb);
            terrainMaterial.SetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty,normal);
            tile.MeshRenderer.sharedMaterial=terrainMaterial;
            var timer=System.Diagnostics.Stopwatch.StartNew();
            PrefabStampAppearance.Apply(source,null,new List<PrefabStampAppearance.Dab>{
                new PrefabStampAppearance.Dab{tile=tile,radius=20,height=5,rotation=25,falloff=1}
            },true,true);
            var result=tile.MeshRenderer.sharedMaterial;
            paths.Add(AssetDatabase.GetAssetPath(result));
            paths.Add(AssetDatabase.GetAssetPath(result.GetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty)));
            paths.Add(AssetDatabase.GetAssetPath(result.GetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty)));
            var projectedRGB = (Texture2D)result.GetTexture(MGTerrainAppearanceCaptureAssets.ColourProperty);
            var projectedNormal = (Texture2D)result.GetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty);
            int coloured = 0, detailed = 0;
            for(int y=512;y<1536;y+=64) for(int x=512;x<1536;x+=64)
            {
                var c = projectedRGB.GetPixel(x,y); var n = projectedNormal.GetPixel(x,y);
                if(Mathf.Abs(c.r-c.b)>.03f && c.g<.95f) coloured++;
                if(Mathf.Abs(n.r-.5f)+Mathf.Abs(n.b-.5f)>.02f) detailed++;
                Check(Mathf.Abs(new Vector3(n.r*2-1,n.g*2-1,n.b*2-1).magnitude-1)<.025f,"GPU project prefab normal stays normalized");
            }
            Check(coloured>20 && detailed>20, "GPU transfers textured project prefab RGB and world normal details");
            Debug.Log($"GPU stamp benchmark: actual hills prefab, 2048x2048 RGB + WS normal, including save/import: {timer.Elapsed.TotalSeconds:F2}s.");
        }
        static void Check(bool pass,string message) {if(!pass) throw new InvalidOperationException("Direct prefab stamp validation failed: "+message);}
    }
}

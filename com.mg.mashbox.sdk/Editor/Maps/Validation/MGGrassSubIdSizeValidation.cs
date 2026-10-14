#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
static class MGGrassSubIdSizeValidation
{
    static string Folder => Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(MashBoxSDK.Maps.TerrainSystem.MGTerrain).Assembly).resolvedPath, "Development~/MGTerrainImplementation~/GrassSubIdSize/");
    static MGGrassSubIdSizeValidation() { EditorApplication.delayCall += Requested; }
    static void Requested() { if (File.Exists(Folder+"validate.request") && !EditorApplication.isPlayingOrWillChangePlaymode) { File.Delete(Folder+"validate.request"); Run(); } }
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    static void Set(Material m,string property,float value) { if(m.HasProperty(property)) m.SetFloat(property,value); }
    static void All(Material m,string property,float value)
    {
        Set(m,property,value);
        for(int i=0;i<8;i++) Set(m,$"_GrassLayer{i:00}_{property.TrimStart('_')}",value);
    }
    static RectInt Capture(Material m,Mesh mesh,string name)
    {
        var preview=new PreviewRenderUtility(); Texture2D image=null;
        try
        {
            preview.ambientColor=new Color(.25f,.28f,.3f,1);
            var camera=preview.camera; camera.enabled=false; camera.cameraType=CameraType.Preview;
            camera.orthographic=true; camera.orthographicSize=2; camera.aspect=1;
            camera.nearClipPlane=.01f; camera.farClipPlane=40; camera.allowHDR=true; camera.allowMSAA=false;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.12f,.14f,.16f,1); camera.useOcclusionCulling=false;
            camera.transform.position=new Vector3(0,1.25f,-10); camera.transform.rotation=Quaternion.identity;
            preview.lights[0].type=LightType.Directional; preview.lights[0].intensity=1.2f;
            preview.lights[0].transform.rotation=Quaternion.Euler(30,-25,0);
            preview.lights[1].type=LightType.Directional; preview.lights[1].intensity=.65f;
            preview.lights[1].transform.rotation=Quaternion.Euler(325,135,0);
            preview.BeginStaticPreview(new Rect(0,0,512,512)); preview.DrawMesh(mesh,Matrix4x4.identity,m,0); preview.Render(true);
            image=preview.EndStaticPreview(); var pixels=image.GetPixels32(); var bg=pixels[0];
            int minX=512,minY=512,maxX=-1,maxY=-1;
            for(int y=0;y<512;y++) for(int x=0;x<512;x++)
            {
                var p=pixels[y*512+x];
                if(Math.Abs(p.r-bg.r)+Math.Abs(p.g-bg.g)+Math.Abs(p.b-bg.b)<24) continue;
                minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
            }
            File.WriteAllBytes(Folder+name+".png",image.EncodeToPNG());
            Check(maxX>minX && maxY>minY,"Empty capture: "+name);
            Check(!ShaderUtil.ShaderHasError(m.shader),"Shader compile error: "+m.shader.name);
            return new RectInt(minX,minY,maxX-minX+1,maxY-minY+1);
        }
        finally { if(image) Object.DestroyImmediate(image); preview.Cleanup(); }
    }
    static void ValidateBounds(Material material,Mesh mesh)
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var host=new GameObject("Shader size bounds fixture");var plant=new GameObject("Plant fixture");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host,scene);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(plant,scene);
        host.SetActive(false);var terrain=host.AddComponent<MashBoxSDK.Maps.TerrainSystem.MGTerrain>();
        try
        {
            plant.AddComponent<MeshFilter>().sharedMesh=mesh;plant.AddComponent<MeshRenderer>().sharedMaterial=material;
            int prototype=terrain.FindOrAddPrototype(plant,MashBoxSDK.Maps.TerrainSystem.MGTerrain.InstanceKind.Detail);
            var layer=(MashBoxSDK.Maps.TerrainSystem.MGTerrain.DensityDetailLayer)Activator.CreateInstance(typeof(MashBoxSDK.Maps.TerrainSystem.MGTerrain.DensityDetailLayer),flags,null,new object[]{prototype,null,1f,1f,1f,1f,1,0L,0f},null);
            All(material,"_GrassWidthMultiplier",1);All(material,"_GrassHeightMultiplier",1);Set(material,"_GrassUseLayerParameters",1);
            Set(material,"_GrassLayer03_GrassWidthMultiplier",2);Set(material,"_GrassLayer03_GrassHeightMultiplier",2);
            var method=terrain.GetType().GetMethod("TryGetTreeLayerBounds",flags);object[] args={layer,null};
            Check((bool)method.Invoke(terrain,args),"Grass size bounds unavailable");var bounds=(Bounds)args[1];
            Check(Math.Abs(bounds.size.x-2)<.001 && Math.Abs(bounds.min.y-.25f)<.001 && Math.Abs(bounds.max.y-2.25f)<.001,"Culling bounds do not match per-index root-anchored scaling");
            Set(material,"_GrassLayer03_GrassWidthMultiplier",3);method.Invoke(terrain,args);bounds=(Bounds)args[1];
            Check(Math.Abs(bounds.size.x-3)<.001,"Material size edit left stale culling bounds");
            Set(material,"_GrassUseLayerParameters",0);Set(material,"_GrassWidthMultiplier",1);
            method.Invoke(terrain,args);bounds=(Bounds)args[1];Check(Math.Abs(bounds.size.x-1)<.001,"Bounds ignored shared/per-index switch");
        }
        finally
        {
            terrain.GetType().GetMethod("ReleaseDetailRenderCache",flags).Invoke(terrain,null);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    [MenuItem("Tools/MashBox/MG Terrain/Validate Grass Sub-ID Size")]
    static void Run()
    {
        var log=new StringBuilder(); var mesh=new Mesh(); Material m=null;
        bool async=ShaderUtil.allowAsyncCompilation; var active=RenderTexture.active; bool srgb=GL.sRGBWrite;
        try
        {
            ShaderUtil.allowAsyncCompilation=false;
            mesh.vertices=new[]{new Vector3(-.5f,.25f,0),new Vector3(.5f,.25f,0),new Vector3(-.5f,1.25f,0),new Vector3(.5f,1.25f,0)};
            mesh.triangles=new[]{0,2,1,1,2,3}; mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};
            mesh.normals=new[]{Vector3.back,Vector3.back,Vector3.back,Vector3.back}; mesh.tangents=new[]{new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1)};mesh.RecalculateBounds();
            foreach(string shaderName in new[]{"Shader Graphs/MG_GodGrass","Shader Graphs/MG_GodGrass_V2"})
            {
                var shader=Shader.Find(shaderName); Check(shader,"Missing shader "+shaderName);
                using(var defaults=new MaterialScope(shader))
                    for(int i=0;i<8;i++) foreach(string axis in new[]{"Width","Height"})
                        Check(defaults.Value.HasProperty($"_GrassLayer{i:00}_Grass{axis}Multiplier") && defaults.Value.GetFloat($"_GrassLayer{i:00}_Grass{axis}Multiplier")==1,"Wrong new property/default");
                m=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Shared Models/Medium Details/Cotsworld_DetailMedium_Shared.mat"));m.shader=shader;
                Set(m,"_GrassUseShapes",0);Set(m,"_GrassUseArrays",0);Set(m,"_GrassUseLayerParameters",1);Set(m,"_GrassArrayLayer",3);
                All(m,"_GrassWidthMultiplier",1);All(m,"_GrassHeightMultiplier",1);All(m,"_WindRootHeight",.25f);
                All(m,"_GrassWindResponse",0);All(m,"_WindStrength",0);All(m,"_GrassDrySize",1);All(m,"_GrassHealthySize",1);
                All(m,"_GrassFarWidth",1);All(m,"_GrassFarHeight",1);All(m,"_GrassFarYOffset",0);All(m,"_GrassCameraLean",0);
                All(m,"_AlphaCutoff",0);Set(m,"_AlphaCutoffEnable",0);All(m,"_ScalingEffects",0);
                m.SetTexture("_MainTex",Texture2D.whiteTexture);
                string name=shaderName.EndsWith("_V2")?"V2":"Legacy";
                RectInt baseline=Capture(m,mesh,name+"_01_Baseline");
                Set(m,"_GrassLayer03_GrassWidthMultiplier",2);RectInt wide=Capture(m,mesh,name+"_02_Width2");
                Check(Math.Abs(wide.width-baseline.width*2)<=3 && Math.Abs(wide.height-baseline.height)<=2,"Width did not independently double");
                Set(m,"_GrassLayer03_GrassWidthMultiplier",1);Set(m,"_GrassLayer03_GrassHeightMultiplier",2);RectInt tall=Capture(m,mesh,name+"_03_Height2");
                Check(Math.Abs(tall.height-baseline.height*2)<=3 && Math.Abs(tall.width-baseline.width)<=2 && Math.Abs(tall.y-baseline.y)<=2,"Height or root anchoring wrong");
                Set(m,"_GrassLayer03_GrassHeightMultiplier",1);Set(m,"_GrassLayer04_GrassWidthMultiplier",2);Set(m,"_GrassLayer04_GrassHeightMultiplier",2);
                RectInt other=Capture(m,mesh,name+"_04_OtherIndex");Check(other==baseline,"Another index changed this index's size");
                Set(m,"_GrassUseLayerParameters",0);Set(m,"_GrassWidthMultiplier",2);RectInt shared=Capture(m,mesh,name+"_05_Shared");
                Check(Math.Abs(shared.width-wide.width)<=2 && Math.Abs(shared.height-wide.height)<=2,"Shared fallback size failed");
                log.AppendLine($"PASS {name}: all 16 per-index defaults 1; width {baseline.width}->{wide.width}, height {baseline.height}->{tall.height}, root anchored, other index isolated, shared fallback correct; actual HDRP shader captures.");
                ValidateBounds(m,mesh);log.AppendLine("PASS: Terrain bounds follow the largest sub-ID, its root pivot, live material edits and the shared/per-index switch.");
                Object.DestroyImmediate(m);m=null;
            }
            log.AppendLine("COMPLETE: both GodGrass variants passed. Only temporary materials/meshes and isolated preview scenes used; no user material values or scene content changed.");
        }
        catch(Exception e) { log.AppendLine("FAIL: "+e);Debug.LogException(e); }
        finally { if(m)Object.DestroyImmediate(m);Object.DestroyImmediate(mesh);ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=active;GL.sRGBWrite=srgb;File.WriteAllText(Folder+"validation.txt",log.ToString()); }
    }
    sealed class MaterialScope:IDisposable { public readonly Material Value; public MaterialScope(Shader shader){Value=new Material(shader);} public void Dispose(){Object.DestroyImmediate(Value);} }
}
#endif

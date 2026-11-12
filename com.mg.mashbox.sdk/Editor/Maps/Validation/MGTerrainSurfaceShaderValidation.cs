#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MashBoxSDK.Maps.TerrainSystem;
namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainSurfaceShaderValidation
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
        static void Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,Flags).Invoke(o,a);
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Surface Shader Distance %#F8")]
        public static void Run()
        {
            var scene=EditorSceneManager.NewPreviewScene(); Mesh mesh=null; Material material=null; bool detailsVisible=MGTerrain.EditorDetailsVisible; MGTerrain.EditorDetailsVisible=false;
            try
            {
                var owner=new GameObject("Shader LOD validation"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(owner,scene);
                owner.SetActive(false); var filter=owner.AddComponent<MeshFilter>(); var master=owner.AddComponent<MeshRenderer>();
                mesh=new Mesh(); int n=65; var vertices=new Vector3[n*n]; var uv=new Vector2[n*n]; var indices=new int[(n-1)*(n-1)*6];int p=0;
                for(int z=0;z<n;z++)for(int x=0;x<n;x++){int a=z*n+x;vertices[a]=new Vector3(x*4,0,z*4);uv[a]=new Vector2(x/64f,z/64f);if(x<n-1&&z<n-1){foreach(int i in new[]{a,a+n,a+1,a+1,a+n,a+n+1})indices[p++]=i;}}
                mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=indices;mesh.RecalculateNormals();mesh.RecalculateBounds();filter.sharedMesh=mesh;
                material=new Material(AssetDatabase.LoadAssetAtPath<Shader>(MGTerrainShaderVariant.Source));master.sharedMaterial=material;
                var terrain=owner.AddComponent<MGTerrain>(); Set(terrain,"m_DrawInstances",false);Set(terrain,"m_SurfaceLodEnabled",false);Set(terrain,"m_SurfaceChunkDistance",300f);Set(terrain,"m_SurfaceShaderDistance",64f);owner.SetActive(true);terrain.RefreshSurfaceTiles();
                var cameraGo=new GameObject("Shader LOD camera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo,scene);var camera=cameraGo.AddComponent<Camera>();camera.enabled=false;camera.cameraType=CameraType.SceneView;
                Action<Vector3> select=position=>{camera.transform.position=position;Call(terrain,"PrepareWorldCamera",camera,500f);};
                Func<MeshRenderer[]> visible=()=>owner.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&!r.forceRenderingOff).ToArray();
                select(new Vector3(0,2,0)); Check(visible().Any(r=>r.sharedMaterial.shader==material.shader),"Near chunks lost tessellation");Check(visible().Any(r=>r.sharedMaterial.shader.name=="Shader Graphs/MG_Lit_Trail_Terrain"),"Far chunks did not switch");
                var originalMeshes=owner.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).ToArray();
                var far=visible().First(r=>r.sharedMaterial.shader.name=="Shader Graphs/MG_Lit_Trail_Terrain");var old=far.sharedMaterial;
                material.SetFloat("_POMAmplitudeMaster",.37f);select(new Vector3(0,2,0));Check(Mathf.Approximately(far.sharedMaterial.GetFloat("_POMAmplitudeMaster"),.37f),"Material edits did not propagate");Check(far.sharedMaterial==old,"Material edit recreated cached instance");
                camera.cameraType=CameraType.Game;select(far.bounds.center); Check(far.sharedMaterial.shader==material.shader,"Second camera did not restore near shader");
                camera.cameraType=CameraType.SceneView;select(new Vector3(0,2,0));Check(originalMeshes.SequenceEqual(owner.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh)),"Camera move rebuilt geometry");
                var cachedMaterials = owner.GetComponentsInChildren<MeshRenderer>().SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
                var timer = System.Diagnostics.Stopwatch.StartNew();
                for (int iteration=0; iteration<600; iteration++)
                {
                    camera.cameraType = iteration%2==0 ? CameraType.SceneView : CameraType.Game;
                    select(new Vector3(iteration%300,2,iteration%170));
                }
                timer.Stop();
                Check(originalMeshes.SequenceEqual(owner.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh)),"Repeated camera movement rebuilt geometry");
                Check(owner.GetComponentsInChildren<MeshRenderer>().SelectMany(r=>r.sharedMaterials).All(m=>cachedMaterials.Contains(m)),"Repeated camera movement allocated materials");
                Debug.Log($"MG Terrain: 600 Scene/Game camera preparations took {timer.ElapsedMilliseconds} ms; cached geometry and materials retained.");
                select(new Vector3(1000,5,1000));Check(visible().Length==1&&visible()[0].sharedMaterial.shader.name=="Shader Graphs/MG_Lit_Trail_Terrain","Distant combined path not optimized");Check(visible()[0].GetComponent<MeshFilter>().sharedMesh==mesh,"Combined proxy duplicated source mesh");
                Set(terrain,"m_AppearanceCaptureCamera",camera);select(new Vector3(1000,5,1000));Check(visible().All(r=>r.sharedMaterial==material),"Capture did not retain original material");Set(terrain,"m_AppearanceCaptureCamera",null);
                Set(terrain,"m_SurfaceShaderDistance",0f);select(Vector3.zero);Check(visible().All(r=>r.sharedMaterial.shader.name=="Shader Graphs/MG_Lit_Trail_Terrain"),"Zero distance did not disable tessellation");
                Set(terrain,"m_SurfaceShaderDistance",64f);select(Vector3.zero);terrain.enabled=false;Check(mesh!=null&&filter.sharedMesh==mesh&&!master.forceRenderingOff&&master.sharedMaterial==material,"Disable did not restore master or destroyed source");
                Check(!ShaderUtil.ShaderHasError(material.shader)&&!ShaderUtil.ShaderHasError(Resources.Load<Shader>("MG_Lit_Trail_Terrain")),"Shader compilation error");
                string root=Path.Combine(Path.GetTempPath(),"mg-terrain-distance");Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"validation.txt"),"PASS: World camera preparation with editor details hidden, SceneView/Game camera switching, mixed near/far chunks, material updates without allocation, camera switching, unchanged geometry, distant whole-tile proxy, capture bypass, distance zero, disable cleanup and original mesh/material ownership.");
                Debug.Log("MG Terrain surface shader distance validation passed.");
            }
            finally{MGTerrain.EditorDetailsVisible=detailsVisible;EditorSceneManager.ClosePreviewScene(scene);if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);if(material!=null)UnityEngine.Object.DestroyImmediate(material);}
        }
    }
}
#endif

using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using MG.GodSurface;
using MG.GodSurface.Editor;
namespace MG.GodSurface.Editor
{
public static class MGSurfaceGeometryValidation
{
    static string Folder;
    static void Check(bool ok,string message,StringBuilder log){if(!ok)throw new Exception(message);log.AppendLine("PASS "+message);}
    static Color[] Capture(Camera camera,RenderTexture target){RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});var old=RenderTexture.active;RenderTexture.active=target;var t=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);try{t.ReadPixels(new Rect(0,0,target.width,target.height),0,0);t.Apply();return t.GetPixels();}finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(t);}}
    static double Difference(Color[] a,Color[] b){return a.Zip(b,(x,y)=>(double)(Math.Abs(x.r-y.r)+Math.Abs(x.g-y.g)+Math.Abs(x.b-y.b))).Average()/3;}
    static void Save(Color[] pixels,string name){var t=new Texture2D(128,128,TextureFormat.RGBA32,false,true);t.SetPixels(pixels);t.Apply();File.WriteAllBytes(Path.Combine(Path.GetTempPath(),name),t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);}
    [MenuItem("MashBox/Content Tools/Validate God Surface Geometry")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        var original=SceneManager.GetActiveScene();var map=MGSurfaceWorld.ForScene(original);if(!map||!map.surfaceLibrary)return;
        var log=new StringBuilder();Scene scene=default;MGSurfaceWorld world=null;RenderTexture target=null;bool async=ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation=false;
            var shader=Shader.Find(MGSurfaceLibrary.ShaderName);foreach(var msg in ShaderUtil.GetShaderMessages(shader))log.AppendLine(msg.severity+" "+msg.message);
            Check(shader&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"Geometry shader imports without errors",log);
            Folder=AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/GodSurfaceGeometryValidation");
            AssetDatabase.CreateFolder("Assets/Editor",Path.GetFileName(Folder));
            var lib=UnityEngine.Object.Instantiate(map.surfaceLibrary);lib.geometry=null;AssetDatabase.CreateAsset(lib,Folder+"/Library.asset");
            scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            var root=new GameObject("Geometry validation");SceneManager.MoveGameObjectToScene(root,scene);world=root.AddComponent<MGSurfaceWorld>();world.roots=new[]{root.transform};world.surfaceLibrary=lib;world.previewInEditor=false;
            var one=GameObject.CreatePrimitive(PrimitiveType.Cube);var two=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var objects=new[]{one,two};int index=0;
            foreach(var obj in objects)
            {
                SceneManager.MoveGameObjectToScene(obj,scene);obj.transform.SetParent(root.transform,false);obj.layer=30;obj.transform.localPosition=new Vector3(index==0?-.8f:.8f,0,0);obj.transform.localRotation=Quaternion.Euler(15,20,10);obj.transform.localScale=new Vector3(1,1.2f,.9f);
                var filter=obj.GetComponent<MeshFilter>();var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);mesh.name="ValidationShape"+index;
                mesh.colors=Enumerable.Range(0,mesh.vertexCount).Select(v=>new Color(.2f+.8f*(v%7)/6f,1,1,1)).ToArray();
                mesh.uv2=mesh.uv.Select(v=>v*.63f+new Vector2(.13f,.21f)).ToArray();mesh.uv3=mesh.uv.Select(v=>v*1.4f).ToArray();mesh.uv4=mesh.uv.Select(v=>v*.37f).ToArray();
                var t=mesh.tangents;for(int v=0;v<t.Length;v++)t[v].w=-1;mesh.tangents=t;
                AssetDatabase.CreateAsset(mesh,Folder+"/Shape"+index+".asset");filter.sharedMesh=mesh;
                var material=new Material(lib.banks[0].previewMaterials[index]);material.SetFloat("_MGSurfaceVertexAOStrength",1);AssetDatabase.CreateAsset(material,Folder+"/Material"+index+".mat");obj.GetComponent<MeshRenderer>().sharedMaterial=material;
                if(index==0){var triangles=mesh.triangles;int middle=triangles.Length/6*3;mesh.subMeshCount=2;mesh.SetIndices(triangles.Take(middle).ToArray(),MeshTopology.Triangles,0);mesh.SetIndices(triangles.Skip(middle).ToArray(),MeshTopology.Triangles,1);obj.GetComponent<MeshRenderer>().sharedMaterials=new[]{material,lib.banks[0].previewMaterials[2]};EditorUtility.SetDirty(mesh);}
                index++;
            }

            var cameraObject=new GameObject("Geometry test camera");SceneManager.MoveGameObjectToScene(cameraObject,scene);var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<30;camera.transform.position=new Vector3(0,0,-5);camera.nearClipPlane=.1f;camera.farClipPlane=20;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            target=new RenderTexture(128,128,24,RenderTextureFormat.ARGBHalf);target.Create();
            var meshBefore=objects.Select(o=>o.GetComponent<MeshFilter>().sharedMesh).ToArray();var colliderBefore=objects.Select(o=>EditorJsonUtility.ToJson(o.GetComponent<Collider>())).ToArray();
            var geometry=MGSurfaceGeometryBaker.Bake(world);
            Check(geometry.shapes.Length==2&&geometry.chunkCount>3,"Distinct topologies and large mesh pieces baked",log);
            Capture(camera,target);var reference=Capture(camera,target);Save(reference,"GodSurfaceGeometry-reference.png");
            Check(reference.Any(c=>c.r+c.g+c.b>.02f),"Reference capture contains visible geometry",log);
            int visible=-1,draws=-1;Action<int,LODParameters,int,int> observe=(id,p,n,d)=>{if(id==camera.GetInstanceID()){visible=n;draws=d;}};
            typeof(MGSurfaceWorld).GetField("cameraDiagnostics",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(world,observe);
            world.previewInEditor=true;world.Rebuild();Check(world.LastError==null&&world.InstanceCount==2&&world.DrawGroupCount==1,"Two shapes and three material slots share one draw group: "+world.LastError,log);
            Capture(camera,target);var reconstructed=Capture(camera,target);Save(reconstructed,"GodSurfaceGeometry-reconstructed.png");
            double error=Difference(reference,reconstructed);Check(error<.001,"Position / normal / UV / AO reconstruction matches source (mean RGB "+error.ToString("F6")+")",log);
            Check(visible==2&&draws==1,"Camera renders both props with one draw command: "+visible+" props, "+draws+" draw",log);
            Check(objects.Select((o,i)=>o.GetComponent<MeshFilter>().sharedMesh==meshBefore[i]&&EditorJsonUtility.ToJson(o.GetComponent<Collider>())==colliderBefore[i]).All(x=>x),"Source mesh references and colliders preserved",log);
            world.cellDrawDistance=1;Capture(camera,target);Check(visible==0&&draws==0,"Hard cell distance cutoff rejects all reconstructed pieces",log);
            world.Release();Check(objects.All(o=>!o.GetComponent<MeshRenderer>().forceRenderingOff),"Original rendering restored when geometry renderer releases",log);
            foreach(var msg in ShaderUtil.GetShaderMessages(shader))log.AppendLine(msg.severity+" "+msg.message);Check(!ShaderUtil.ShaderHasError(shader),"Rendered shader variants compile without errors",log);
            log.AppendLine("SUCCESS");
        }
        catch(Exception ex){log.AppendLine("FAIL "+ex);Debug.LogException(ex);}
        finally{if(world)world.Release();if(target){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(scene.IsValid())EditorSceneManager.CloseScene(scene,true);if(original.IsValid())SceneManager.SetActiveScene(original);ShaderUtil.allowAsyncCompilation=async;if(!string.IsNullOrEmpty(Folder)&&AssetDatabase.IsValidFolder(Folder))AssetDatabase.DeleteAsset(Folder);File.WriteAllText(Path.Combine(Path.GetTempPath(),"GodSurfaceGeometryValidation.txt"),log.ToString());}
    }
}
}

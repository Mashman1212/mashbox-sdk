using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;

internal static class StudioFractureSurfaceValidation
{
    static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../fracture-surface-"+name));
    static Task<StudioMeshFracture.Result> task;static StudioMeshFracture.Input input;static StudioModelDocument.Node source;static int phase;static double began;static string evidence;
    static FileSystemWatcher refresh;
    static StudioMeshFracture.Input cubeInput;static StudioMeshFracture.Result jetResult;
    [InitializeOnLoadMethod]static void Requested()
    {
        if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets"))return;
                var context=SynchronizationContext.Current;
        refresh=new FileSystemWatcher(Path.GetDirectoryName(Out("refresh.txt")),"fracture-surface-refresh.txt"){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite};
        refresh.Created+=(s,e)=>context.Post(_=>{if(File.Exists(Out("refresh.txt"))){File.Delete(Out("refresh.txt"));AssetDatabase.Refresh();}},null);
        refresh.EnableRaisingEvents=true;AssemblyReloadEvents.beforeAssemblyReload+=()=>refresh.Dispose();
        if(File.Exists(Out("request.txt"))){File.Delete(Out("request.txt"));EditorApplication.update+=StartPending;}
    }
    static void StartPending(){if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;EditorApplication.update-=StartPending;Run();}
    static void Run()
    {
        if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets"))return;

        phase=0;evidence="";File.WriteAllText(Out("result.txt"),"RUNNING");
        try{var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);source=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,geometryVertices=StudioSubdivisionData.Primitive(mesh)};Begin();EditorApplication.update+=Tick;}catch(Exception ex){Finish(ex);}
    }
    static void Begin()
    {
        input=StudioMeshFracture.Capture(source);if(phase==0)cubeInput=input;var settings=new StudioMeshFracture.Settings();
        if(phase==2)settings.Gap=.001f;
        began=EditorApplication.timeSinceStartup;task=Task.Run(()=>StudioMeshFracture.Compute(input,settings,CancellationToken.None,p=>File.WriteAllText(Out("progress.txt"),phase+" "+p.Stage+" "+p.Fraction)));
    }
    static void Tick()
    {
        if(!task.IsCompleted)return;
        try
        {
            if(task.IsFaulted)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(task.Exception.GetBaseException()).Throw();var result=task.Result;
            int total=result.Pieces.Sum(p=>p.Faces.Count),interior=result.Pieces.Sum(p=>p.Faces.Count(f=>f.Exterior<0)),quads=result.Pieces.Sum(p=>p.Faces.Count(f=>f.Exterior<0&&f.Vertices.Length==4));
            evidence+=(phase==0?"Cube":phase==1?"Jet":"Jet gap")+": "+result.Pieces.Count+" pieces; "+total+" polygons; "+quads+" / "+interior+" interior quads; "+(EditorApplication.timeSinceStartup-began).ToString("F3")+" s.\n";
            if(phase==0&&total>=1500)throw new Exception("Cube topology remains excessive: "+total);
            var preview=StudioMeshFracture.PublishInspection(result,.18f);
            try
            {
                // Normalize only the disposable render copy to the existing probe camera.
                var bounds=preview.mesh.bounds;float size=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));preview.mesh.vertices=preview.mesh.vertices.Select(p=>(p-bounds.center)*(2/size)).ToArray();preview.mesh.RecalculateBounds();
                if(phase<2)Capture(preview,phase==0?"fracture-surface-cube.png":"fracture-surface-jet.png");
            }
            finally{UnityEngine.Object.DestroyImmediate(preview.mesh);}
            if(phase==0)
            {
                UnityEngine.Object.DestroyImmediate(source.mesh);var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/JetSlice/fighterJetCutTest.fbx");
                if(!asset)throw new Exception("Exact jet import missing");
                var nodes=(List<StudioModelDocument.Node>)typeof(StudioModelDocument).GetMethod("CaptureNodes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{asset});source=nodes.Where(n=>n.mesh).OrderByDescending(n=>n.mesh.vertexCount).First();
                var matrix=Matrix4x4.identity;for(int at=nodes.IndexOf(source);at>=0;at=nodes[at].parent){var n=nodes[at];matrix=Matrix4x4.TRS(n.position+n.positionOffset,n.rotation,n.scale)*matrix;}
                source.mesh.vertices=source.mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();var normal=matrix.inverse.transpose;source.mesh.normals=source.mesh.normals.Select(n=>normal.MultiplyVector(n).normalized).ToArray();source.mesh.RecalculateBounds();
                evidence+="Jet input: "+source.mesh.vertexCount+" raw vertices; "+new StudioMeshCuts(source).Faces.Count+" authored faces; bounds "+source.mesh.bounds.size+".\n";
                phase=1;Begin();return;
            }
            if(phase==1){jetResult=result;phase=2;Begin();return;}
            float retreat=float.PositiveInfinity;
            for(int p=0;p<result.Pieces.Count;p++)for(int f=0;f<result.Pieces[p].Faces.Count;f++)
            {
                var face=result.Pieces[p].Faces[f];var original=jetResult.Pieces[p].Faces[f];if(face.Exterior>=0)continue;
                for(int v=0;v<face.Vertices.Length;v++)retreat=Mathf.Min(retreat,Vector3.Dot(jetResult.Positions[original.Vertices[v]]-result.Positions[face.Vertices[v]],face.CutNormal));
            }
            if(retreat<.000499f)throw new Exception("Gap retreat below half clearance: "+retreat);
            evidence+="Minimum interface retreat per side: "+retreat.ToString("R")+" m.\n";
            cubeInput.Geometry=Enumerable.Range(0,cubeInput.Positions.Length).ToArray();var ids=cubeInput.Geometry.ToArray();var points=cubeInput.Positions.ToArray();
            var welded=StudioMeshFracture.Compute(cubeInput,new StudioMeshFracture.Settings(),CancellationToken.None,null);
            if(welded.Pieces.Count!=12||!cubeInput.Geometry.SequenceEqual(ids)||!cubeInput.Positions.SequenceEqual(points))throw new Exception("Seam weld or source immutability failed");
            var weldedMesh=StudioMeshFracture.Publish(welded,welded.Pieces[0]);
            try{if(weldedMesh.mesh.normals.Length!=weldedMesh.mesh.vertexCount||weldedMesh.mesh.uv.Length!=weldedMesh.mesh.vertexCount)throw new Exception("Welded attribute streams missing");}finally{UnityEngine.Object.DestroyImmediate(weldedMesh.mesh);}
            evidence+="Coincident split source seams weld before fracture; source arrays and published UV/normal streams retained.\n";
            Finish(null);
        }
        catch(Exception ex){Finish(ex);}
    }
 static void Capture(StudioModelDocument.Node node,string name){
  var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var root=new GameObject("Fracture probe");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);var cameraObject=new GameObject("Probe camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);
  var shader=Shader.Find("Hidden/BevelHoleProbe");if(!shader)throw new Exception("Missing capture shader");var surface=new Material(shader);surface.SetColor("_Color",new Color(.65f,.65f,.65f,1));var wireMaterial=new Material(shader);wireMaterial.SetColor("_Color",new Color(.2f,.95f,.9f,1));wireMaterial.SetFloat("_Wire",1);wireMaterial.SetFloat("_Offset",-1);wireMaterial.renderQueue=2001;
  root.layer=31;root.AddComponent<MeshFilter>().sharedMesh=node.mesh;root.AddComponent<MeshRenderer>().sharedMaterial=surface;
  var wire=new GameObject("Authored edges");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wire,scene);wire.layer=31;var mesh=new Mesh();mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;mesh.vertices=node.mesh.vertices;var ids=new List<int>();foreach(var face in new StudioMeshCuts(node).Faces)for(int c=0;c<face.Vertices.Count;c++){ids.Add(face.Vertices[c]);ids.Add(face.Vertices[(c+1)%face.Vertices.Count]);}mesh.SetIndices(ids.ToArray(),MeshTopology.Lines,0);wire.AddComponent<MeshFilter>().sharedMesh=mesh;wire.AddComponent<MeshRenderer>().sharedMaterial=wireMaterial;
  var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;camera.enabled=false;camera.cullingMask=1<<31;camera.transform.position=new Vector3(2.4f,1.7f,4.5f);camera.transform.LookAt(Vector3.zero);camera.fieldOfView=36;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.13f,.13f,1);camera.nearClipPlane=.01f;camera.farClipPlane=100;
  var rt=new RenderTexture(640,640,24);rt.Create();camera.targetTexture=rt;var previous=RenderTexture.active;var pipeline=UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
  try{UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;camera.Render();RenderTexture.active=rt;var image=new Texture2D(640,640,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,640,640),0,0);image.Apply();File.WriteAllBytes(Out(name.Replace("fracture-surface-","")),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);}
  finally{RenderTexture.active=previous;camera.targetTexture=null;UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(surface);UnityEngine.Object.DestroyImmediate(wireMaterial);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
 }


    static void Finish(Exception ex){EditorApplication.update-=Tick;File.WriteAllText(Out("result.txt"),(ex==null?"PASS\n":"FAIL\n")+evidence+(ex==null?"":ex.ToString()));if(source?.mesh)UnityEngine.Object.DestroyImmediate(source.mesh);}
}




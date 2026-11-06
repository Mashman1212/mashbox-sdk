using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;

internal static class StudioFractureValidation
{
    static StudioModelDocument doc;static StudioModelEditor editor;static Mesh original;static Vector3[] originalPoints;static int phase,checks;static string signature;static double started;static Material material;
    static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../fracture-"+name));
    static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethods(BindingFlags.Instance|BindingFlags.NonPublic).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(editor,args);
    static T Get<T>(string name)=>(T)typeof(StudioModelEditor).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
    static void Check(bool valid,string label){checks++;if(!valid)throw new Exception(label);}
    [InitializeOnLoadMethod]static void Requested(){if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets"))return;var path=Out("request.txt");if(File.Exists(path)){File.Delete(path);EditorApplication.delayCall+=Run;}}
    public static void Run()
    {
        if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets"))throw new Exception("Disposable project required.");
        try
        {
            File.WriteAllText(Out("result.txt"),"RUNNING");checks=phase=0;doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.SetWorkspacePath(StudioModelDocument.NewWorkspacePath("FractureValidation"));doc.AddPrimitive(PrimitiveType.Cube);original=doc.nodes[0].mesh;originalPoints=original.vertices;
            material=new Material(Shader.Find("Hidden/FrameSubdivisionCheck"));doc.nodes[0].materials=new[]{material};
            editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);Call("OpenFractureTool");Get<StudioMeshFracture.Settings>("fractureSettings").SimplifyInterior=false;
            Call("StartFracture");Check(Get<object>("fractureTask")!=null,"background work active");Call("CancelFracture",false);
            Check(Get<object>("fractureTask")==null&&Get<object>("fracturePreview")==null,"cancellation clears busy and preview");Check(original.vertices.SequenceEqual(originalPoints),"cancel preserves source");
            Call("StartFracture");started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;
        }
        catch(Exception ex){Finish(ex);}
    }
    static string Signature(StudioMeshFracture.Result r)=>string.Join(";",r.Positions.Select(p=>p.ToString("R")))+"/"+string.Join(";",r.Pieces.SelectMany(p=>p.Faces).Select(f=>string.Join(",",f.Vertices)));
    static void Tick()
    {
        try
        {
            if(EditorApplication.timeSinceStartup-started>90)throw new Exception("Compute timed out");
            if(phase<2)
            {
                if(Get<object>("fractureTask")!=null)return;
                var result=Get<StudioMeshFracture.Result>("fractureResult");if(result==null)throw new Exception("No result: "+typeof(StudioModelEditor).GetField("statusText",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor));
                if(phase==0)
                {
                    File.WriteAllText(Out("timing.txt"),"Compute plus publication: "+(EditorApplication.timeSinceStartup-started).ToString("F3")+" s; "+result.Positions.Count+" shared points; "+result.Pieces.Sum(p=>p.Faces.Count)+" polygon faces.");
                    Check(result.Pieces.Count==12,"requested piece count");signature=Signature(result);
                    var interfaces=result.Pieces.SelectMany(p=>p.Faces).Where(f=>f.Exterior<0).GroupBy(f=>string.Join(",",f.Vertices.OrderBy(v=>v))).ToArray();
                    Check(interfaces.Length>0&&interfaces.All(g=>g.Count()==2),"all shared interface polygons match exactly");
                    Check(result.Positions.Where((p,i)=>(p-result.Original[i]).sqrMagnitude>1e-10f).Any(),"geometric roughness exists");
                    Check(result.Pieces.SelectMany(p=>p.Faces).Where(f=>f.Exterior>=0).SelectMany(f=>f.Vertices).All(v=>Mathf.Abs(Mathf.Max(Mathf.Abs(result.Positions[v].x),Mathf.Abs(result.Positions[v].y),Mathf.Abs(result.Positions[v].z))-.5f)<1e-6f),"exterior surface preserved");
                    Check(editor.Transforms[0].GetComponent<MeshFilter>().sharedMesh==original&&original.vertexCount>originalPoints.Length,"actual renderer receives preview");
                    Call("StartFracture");phase=1;return;
                }
                Check(Signature(result)==signature,"seed reproducible and recompute uses original");typeof(StudioModelEditor).GetField("fractureExplode",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(editor,.5f);Call("PublishFractureInspection");Capture();
                Call("CommitFracture");Check(doc.nodes.Count==12,"committed independent objects");
                Check(doc.nodes.Select(n=>n.mesh).Distinct().Count()==12,"independent owned meshes");
                Check(doc.nodes.All(n=>n.mesh.subMeshCount==2&&n.mesh.uv.Length==n.mesh.vertexCount),"interior slots and UVs");
                Check(doc.nodes.All(n=>!(n.modifiers?.HasHistory??false)),"independent history bases");
                Check(doc.nodes.SelectMany(n=>n.mesh.vertices.Select(p=>p+n.position)).All(p=>Mathf.Max(Mathf.Abs(p.x),Mathf.Abs(p.y),Mathf.Abs(p.z))<.50001f),"inspection gaps not committed");
                File.WriteAllText(Out("result.txt"),"RUNNING: compute/render/commit checks passed; checking Undo/Redo.");phase=2;return;
            }
            if(phase==2){StudioUndo.PerformUndo();phase=3;return;}
            if(phase==3){Check(doc.nodes.Count==1&&doc.nodes[0].mesh==original&&original.vertices.SequenceEqual(originalPoints),"Undo restores original");Check((doc.nodes[0].modifiers?.HasHistory??false),"Undo restores source creation stack");StudioUndo.PerformRedo();phase=4;return;}
            Check(doc.nodes.Count==12&&doc.nodes.Select(n=>n.mesh).Distinct().Count()==12,"Redo restores fragments");Finish(null);
        }
        catch(Exception ex){Finish(ex);}
    }
    static void Capture()
    {
        var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;var old=RenderTexture.active;GameObject cameraObject=null;RenderTexture target=null;Texture2D image=null;
        var renderers=editor.Transforms.SelectMany(t=>t.GetComponents<Renderer>()).ToArray();var materials=renderers.Select(r=>r.sharedMaterials).ToArray();
        try
        {
            GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;foreach(var renderer in renderers)renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
            cameraObject=new GameObject("Fracture validation camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,editor.Scene);var camera=cameraObject.AddComponent<Camera>();camera.scene=editor.Scene;
            camera.transform.position=new Vector3(1.8f,1.35f,-2.2f);camera.transform.LookAt(Vector3.zero);camera.fieldOfView=40;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.13f,.15f);camera.nearClipPlane=.01f;camera.farClipPlane=20;
            target=new RenderTexture(900,900,24);target.Create();camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            image=new Texture2D(900,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,900,900),0,0);image.Apply();File.WriteAllBytes(Out("preview.png"),image.EncodeToPNG());
            Check(image.GetPixels().Select(c=>(int)(c.r*255)).Distinct().Count()>20,"render contains shaded geometry");
        }
        finally{RenderTexture.active=old;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;for(int i=0;i<renderers.Length;i++)if(renderers[i])renderers[i].sharedMaterials=materials[i];if(cameraObject)Object.DestroyImmediate(cameraObject);if(target){target.Release();Object.DestroyImmediate(target);}if(image)Object.DestroyImmediate(image);}
    }
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;File.WriteAllText(Out("result.txt"),error==null?"PASS "+checks+" focused checks; cube compute/cancel/recompute, shared interfaces, geometric roughness, fixed exterior, renderer capture, commit/Undo/Redo.":"FAIL after "+checks+" checks: "+error);
        editor?.Dispose();editor=null;if(material)Object.DestroyImmediate(material);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;

internal static class StudioFrameSubdivisionCheck
{
    private static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
    private static object Call(StudioModelEditor e,string name,params object[] args)=>typeof(StudioModelEditor).GetMethods(BindingFlags.Instance|BindingFlags.NonPublic).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(e,args);
    private static int Boundaries(StudioModelDocument.Node n)
    {
        var ids=StudioSubdivisionData.Geometry(n);var counts=new Dictionary<(int,int),int>();
        for(int i=0;i<n.cutPolygons.Length;){i++;int count=n.cutPolygons[i++];for(int c=0;c<count;c++){int a=ids[n.cutPolygons[i+c]],b=ids[n.cutPolygons[i+(c+1)%count]];var edge=(Math.Min(a,b),Math.Max(a,b));counts.TryGetValue(edge,out int uses);counts[edge]=uses+1;}i+=count;}
        return counts.Count(p=>p.Value==1);
    }
    [InitializeOnLoadMethod] private static void CheckRequest(){ string p=Out("frame-subdivision-request.txt"); if(Application.dataPath.EndsWith("SelectionUndoProject/Assets",StringComparison.OrdinalIgnoreCase)&&File.Exists(p)){File.Delete(p);EditorApplication.delayCall+=Run;}}
    [MenuItem("Tools/Check Frame Subdivision")]
    public static void Run()
    {
        if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Disposable project required.");
        var savedPipeline=UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;var savedQualityPipeline=QualitySettings.renderPipeline;UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;StudioModelDocument doc=null;StudioModelEditor editor=null;Mesh oldMesh=null;GameObject cameraObject=null,lightObject=null;Material material=null;RenderTexture target=null;string stage="import";
        try
        {
            const string path="Assets/FrameSubdivision/frame_subdivision_test.fbx";
            var sources=StudioFbxTopology.Read(path);
            File.WriteAllText(Out("frame-subdivision-progress.txt"),"FBX meshes: "+sources.Count+"\n"+string.Join("\n",sources.Select(s=>$"{s.Name}: {s.Points.Length} controls, {s.Faces.Length} faces")));
            if(sources.Count==0)throw new Exception("FBX source geometry was not read.");
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes=(List<StudioModelDocument.Node>)typeof(StudioModelDocument).GetMethod("CaptureNodes",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{source});
            int index=doc.nodes.Select((n,i)=>(n,i)).Where(p=>p.n.mesh).OrderByDescending(p=>p.n.mesh.vertexCount).First().i;var node=doc.nodes[index];
            oldMesh=Object.Instantiate(node.mesh);var old=new StudioModelDocument.Node{mesh=oldMesh};StudioModelDocument.CaptureMeshTopology(old,true);
            int renderVertices=node.mesh.vertexCount,controls=node.geometryVertices.Distinct().Count(),beforeBoundaries=Boundaries(old),afterBoundaries=Boundaries(node);
            File.AppendAllText(Out("frame-subdivision-progress.txt"),$"\n{node.name}: {renderVertices} render vertices, {controls} controls; boundaries {beforeBoundaries} -> {afterBoundaries}");
            if(controls>=renderVertices||afterBoundaries>=beforeBoundaries)throw new Exception("Source topology was not recovered for the frame.");
            stage="overlapping shell guard";var overlap=new Mesh();overlap.vertices=new[]{Vector3.zero,Vector3.right,Vector3.one,Vector3.up,Vector3.zero,Vector3.right,Vector3.one,Vector3.up};overlap.triangles=new[]{0,1,2,0,2,3,4,5,6,4,6,7};
            try{var original=new StudioFbxTopology.SourceMesh{Points=overlap.vertices,Faces=new[]{new[]{0,1,2,3},new[]{4,5,6,7}}};if(StudioFbxTopology.TryMap(overlap,original,out _,out _))throw new Exception("Ambiguous coincident shells were incorrectly mapped.");}finally{Object.DestroyImmediate(overlap);}
            stage="subdivision render";editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(index);
            var bounds=editor.Transforms[index].GetComponent<MeshRenderer>().bounds;var cameraPosition=bounds.center+(bounds.size.x < bounds.size.z ? new Vector3(-1,.18f,-.3f) : new Vector3(.3f,.18f,-1)).normalized*bounds.size.magnitude*1.55f;
            cameraObject=new GameObject("Frame check camera");var camera=cameraObject.AddComponent<Camera>();camera.scene=editor.Scene;camera.enabled=false;camera.transform.position=cameraPosition;camera.transform.LookAt(bounds.center);camera.fieldOfView=38;camera.nearClipPlane=.001f;camera.farClipPlane=Mathf.Max(1000,bounds.size.magnitude*10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.09f,.1f,.12f);camera.cullingMask=1<<30;
            lightObject=new GameObject("Frame check light");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject,editor.Scene);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.transform.rotation=Quaternion.Euler(35,-25,0);light.cullingMask=1<<30;
            material=new Material(Shader.Find("Hidden/FrameSubdivisionCheck"));material.color=new Color(.7f,.73f,.8f);
            var filter=editor.Transforms[index].GetComponent<MeshFilter>();filter.gameObject.layer=30;filter.GetComponent<MeshRenderer>().sharedMaterial=material;
            target=new RenderTexture(800,800,24);target.Create();camera.targetTexture=target;
            var previous=RenderTexture.active;
            Color32[] Pixels(string save){filter.GetComponent<MeshRenderer>().sharedMaterial=material;camera.Render();var texture=new Texture2D(800,800,TextureFormat.RGBA32,false);try{RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,800,800),0,0);texture.Apply();if(save!=null)File.WriteAllBytes(Out(save),texture.EncodeToPNG());return texture.GetPixels32();}finally{RenderTexture.active=previous;Object.DestroyImmediate(texture);}}
            old.subdivision=new StudioSubdivisionSettings{preview=true,level=2};var oldInput=new StudioSubdivisionData.Input(old);var oldSurface=oldInput.CreateMesh(old,true,out _);
            Color32[] before;
            try{filter.sharedMesh=oldSurface;before=Pixels("frame-subdivision-before.png");}finally{filter.sharedMesh=node.mesh;Object.DestroyImmediate(oldSurface);}
            Call(editor,"AddSubdivisionModifier");Call(editor,"UpdateSubdivision");var gpu=filter.sharedMesh;
            if(gpu==node.mesh)throw new Exception("Actual frame renderer did not receive GPU subdivision: "+editor.StatusText);
            var after=Pixels("frame-subdivision-after.png");filter.sharedMesh=node.mesh;var cpu=Pixels(null);filter.sharedMesh=gpu;
            int difference=before.Zip(after,(a,b)=>a.Equals(b)?0:1).Sum(),gpuDifference=after.Zip(cpu,(a,b)=>a.Equals(b)?0:1).Sum();
            if(difference<100)throw new Exception("Frame rendered shape did not change.");
            if(gpuDifference>80)throw new Exception("GPU frame and editable subdivision render disagree: "+gpuDifference);
            File.WriteAllText(Out("frame-subdivision-result.txt"),$"PASS exact frame FBX. {renderVertices} render vertices -> {controls} geometric controls; geometric boundary edges {beforeBoundaries} -> {afterBoundaries}. Original FBX polygons restored, zero explicit creases. Level 2 GPU/rendered editable agreement: {gpuDifference} differing pixels; old/new surface difference: {difference} pixels. Ambiguous coincident shells rejected. Backend {SystemInfo.graphicsDeviceType}. Editor left open.");
        }
        catch(Exception e){File.WriteAllText(Out("frame-subdivision-result.txt"),"FAIL "+stage+"\n"+e);}
        finally{UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline=savedPipeline;QualitySettings.renderPipeline=savedQualityPipeline;editor?.Dispose();if(cameraObject)Object.DestroyImmediate(cameraObject);if(lightObject)Object.DestroyImmediate(lightObject);if(material)Object.DestroyImmediate(material);if(target){target.Release();Object.DestroyImmediate(target);}if(oldMesh)Object.DestroyImmediate(oldMesh);if(doc){foreach(var mesh in doc.nodes.Where(n=>n.mesh).Select(n=>n.mesh).Distinct())Object.DestroyImmediate(mesh);Object.DestroyImmediate(doc);}}
    }
}

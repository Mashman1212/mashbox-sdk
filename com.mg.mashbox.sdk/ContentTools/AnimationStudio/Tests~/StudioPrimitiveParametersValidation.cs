using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;

internal static class StudioPrimitiveParametersValidation
{
    static int checks;
    static StudioModelDocument document;
    static StudioModelEditor editor;
    static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
    [InitializeOnLoadMethod] static void Request()
    {
        if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets",StringComparison.OrdinalIgnoreCase))return;
        string request=Out("primitive-parameters-request.txt");if(!File.Exists(request))return;
        EditorApplication.delayCall+=Run;
    }
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethods(BindingFlags.Instance|BindingFlags.NonPublic).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(editor,args);
    static int Faces(int[] polygons){int count=0;for(int i=0;i<polygons.Length;){i+=2+polygons[i+1];count++;}return count;}
    static void MeshCheck(PrimitiveType type,Vector4 p,int faces,bool closed,Vector3Int divisions=default)
    {
        var mesh=StudioModelDocument.BuildPrimitive(type,p,out var polygons,divisions);
        try
        {
            Check(Faces(polygons)==faces,type+" face count");
            Check(mesh.vertices.All(v=>!float.IsNaN(v.sqrMagnitude)&&!float.IsInfinity(v.sqrMagnitude)),type+" finite vertices");
            Check(mesh.uv.Length==mesh.vertexCount&&mesh.normals.Length==mesh.vertexCount,type+" attributes");
            var positions=mesh.vertices;var normals=mesh.normals;var triangles=mesh.triangles;
            Check(Enumerable.Range(0,triangles.Length/3).All(i=>{
                int a=triangles[i*3],b=triangles[i*3+1],c=triangles[i*3+2];
                return Vector3.Dot(Vector3.Cross(positions[b]-positions[a],positions[c]-positions[a]),normals[a]+normals[b]+normals[c])>0;
            }),type+" winding and nondegenerate triangles");
            if(closed)
            {
                var ids=StudioSubdivisionData.Primitive(mesh);var incidence=new Dictionary<(int,int),int>();
                for(int i=0;i<polygons.Length;){i++;int n=polygons[i++];for(int j=0;j<n;j++){
                    int a=ids[polygons[i+j]],b=ids[polygons[i+(j+1)%n]];var edge=(Math.Min(a,b),Math.Max(a,b));
                    incidence.TryGetValue(edge,out int count);incidence[edge]=count+1;
                }i+=n;}
                Check(incidence.Values.All(n=>n==2),type+" closed shared boundaries");
            }
        }
        finally{Object.DestroyImmediate(mesh);}
    }
    [MenuItem("Tools/Run Primitive Parameters Check")]
    public static void Run()
    {
        if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Disposable project required.");
        string request=Out("primitive-parameters-request.txt");if(File.Exists(request))File.Delete(request);
        checks=0;
        try
        {
            MeshCheck(PrimitiveType.Cube,new Vector4(2,3,4,3),54,true);
            MeshCheck(PrimitiveType.Cube,new Vector4(2,3,4,1),52,true,new Vector3Int(2,3,4));
            MeshCheck(PrimitiveType.Sphere,new Vector4(2,12,8,0),96,true);
            MeshCheck(PrimitiveType.Plane,new Vector4(2,3,4,5),20,false);
            MeshCheck(PrimitiveType.Quad,new Vector4(2,3,4,5),20,false);
            MeshCheck(PrimitiveType.Cylinder,new Vector4(2,3,12,3),84,true);
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../MashBoxStudio/Workspaces"));Directory.CreateDirectory(folder);
            string runId=Guid.NewGuid().ToString("N");
            document=ScriptableObject.CreateInstance<StudioModelDocument>();document.SetWorkspacePath(Path.Combine(folder,"PrimitiveParameters-"+runId+".workspace"));
            foreach(var type in new[]{PrimitiveType.Cube,PrimitiveType.Sphere,PrimitiveType.Plane,PrimitiveType.Quad,PrimitiveType.Cylinder})
            {
                int index=document.AddPrimitive(type);var node=document.nodes[index];
                Check(node.modifiers.operations.Count==1&&node.modifiers.operations[0].primitiveType==type,type+" creation modifier");
                var history=node.modifiers.Copy();history.operations[0].parameters.x*=2;
                var work=StudioModifierEvaluator.Evaluate(history);
                try{Check(history.operations.All(o=>string.IsNullOrEmpty(o.failure)),type+" replay failure");Check(Mathf.Abs(work.mesh.bounds.size.x-node.mesh.bounds.size.x*2)<.0001f,type+" dimension replay");}
                finally{Object.DestroyImmediate(work.mesh);}
            }
            string saved=Path.Combine(folder,"PrimitiveParametersRoundtrip-"+runId+".workspace");StudioWorkspaceText.Write(document,saved);
            var loaded=StudioWorkspaceText.Read(saved);
            try{Check(loaded.nodes.Select(n=>n.modifiers.operations[0].primitiveType).SequenceEqual(document.nodes.Select(n=>n.modifiers.operations[0].primitiveType)),"workspace types");}
            finally{foreach(var mesh in loaded.nodes.Select(n=>n.mesh))Object.DestroyImmediate(mesh);Object.DestroyImmediate(loaded);}
            editor=new StudioModelEditor(()=>{});editor.Load(document);editor.SelectObject(0);
            var cube=document.nodes[0];string id=cube.modifiers.operations[0].id;
            var before=Pixels();
            Call("ChangeModifier",cube,id,(Action<StudioModifierOperation>)(o=>o.parameters.x=2),false);
            Check(Mathf.Abs(cube.mesh.bounds.size.x-2)<.0001f,"published width");
            var after=Pixels();Check(before.Zip(after,(a,b)=>a.Equals(b)?0:1).Sum()>20,"bound renderer changed");
            var downstream=new StudioModifierOperation{kind="Transform",name="Move components",offsets=new Vector3[cube.mesh.vertexCount]};downstream.Bind(cube);
            var changed=cube.modifiers.Copy();changed.operations.Add(downstream);changed.operations[0].cubeDivisions=new Vector3Int(2,1,1);
            var failed=StudioModifierEvaluator.Evaluate(changed);
            try{Check(!string.IsNullOrEmpty(changed.operations[1].failure),"changed topology must reject downstream IDs");}finally{Object.DestroyImmediate(failed.mesh);}
            // Defer history calls outside drawing, allowing the edit to settle.
            EditorApplication.delayCall+=UndoCheck;
        }
        catch(Exception e){Finish("FAIL "+e);}
    }
    static Color32[] Pixels()
    {
        var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
        var obj=new GameObject("Primitive render check");var cam=obj.AddComponent<Camera>();cam.enabled=false;cam.scene=editor.Scene;
        cam.transform.position=new Vector3(0,0,-5);cam.transform.LookAt(Vector3.zero);cam.orthographic=true;cam.orthographicSize=2;
        cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.black;cam.cullingMask=1<<30;
        var renderer=editor.Transforms[0].GetComponent<MeshRenderer>();renderer.gameObject.layer=30;
        foreach(var t in editor.Transforms.Skip(1))t.gameObject.SetActive(false);
        var original=renderer.sharedMaterials;var material=new Material(Shader.Find("Unlit/Color"));material.color=Color.white;renderer.sharedMaterial=material;
        var target=new RenderTexture(128,128,24);target.Create();cam.targetTexture=target;var previous=RenderTexture.active;Texture2D texture=null;
        try{GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;cam.Render();RenderTexture.active=target;texture=new Texture2D(128,128,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,128,128),0,0);texture.Apply();File.WriteAllBytes(Out("primitive-parameters-preview.png"),texture.EncodeToPNG());return texture.GetPixels32();}
        finally{GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;renderer.sharedMaterials=original;RenderTexture.active=previous;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(texture);Object.DestroyImmediate(material);Object.DestroyImmediate(obj);}
    }
    static void UndoCheck(){try{UnityEditor.Undo.PerformUndo();EditorApplication.delayCall+=RedoCheck;}catch(Exception e){Finish("FAIL "+e);}}
    static void RedoCheck(){try{Check(Mathf.Abs(document.nodes[0].mesh.bounds.size.x-1)<.0001f,"Undo width");UnityEditor.Undo.PerformRedo();EditorApplication.delayCall+=Complete;}catch(Exception e){Finish("FAIL "+e);}}
    static void Complete(){try{Check(Mathf.Abs(document.nodes[0].mesh.bounds.size.x-2)<.0001f,"Redo width");Finish("PASS "+checks+" focused checks");}catch(Exception e){Finish("FAIL "+e);}}
    static void Finish(string result){File.WriteAllText(Out("primitive-parameters-result.txt"),result);Debug.Log(result);editor?.Dispose();if(document){foreach(var mesh in document.nodes.Select(n=>n.mesh).Distinct())if(mesh)Object.DestroyImmediate(mesh);Object.DestroyImmediate(document);}}
}

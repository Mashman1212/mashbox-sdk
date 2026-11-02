using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;

internal static class StudioBisectValidation
{
    static int checks;
    static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
    static void Check(bool test,string message){checks++;if(!test)throw new Exception(message);}
    static object Call(StudioModelEditor e,string name,params object[] args)=>typeof(StudioModelEditor).GetMethods(BindingFlags.Instance|BindingFlags.NonPublic).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(e,args);
    static void Set(StudioModelEditor e,string name,object value)=>typeof(StudioModelEditor).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(e,value);
    static StudioModelDocument.Node Cube()
    {
        var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
        return new StudioModelDocument.Node{name="Bisect cube",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,geometryVertices=StudioSubdivisionData.Primitive(mesh)};
    }
    static int Boundaries(StudioModelDocument.Node n)
    {
        var ids=StudioSubdivisionData.Geometry(n);var edges=new Dictionary<(int,int),List<(int,int)>>();
        foreach(var face in new StudioMeshCuts(n).Faces)for(int i=0;i<face.Vertices.Count;i++)
        {
            int a=ids[face.Vertices[i]],b=ids[face.Vertices[(i+1)%face.Vertices.Count]];var key=(Math.Min(a,b),Math.Max(a,b));
            if(!edges.TryGetValue(key,out var uses))edges[key]=uses=new List<(int,int)>();uses.Add((a,b));
        }
        foreach(var uses in edges.Values){Check(uses.Count<=2,"nonmanifold edge");if(uses.Count==2)Check(uses[0].Item1==uses[1].Item2&&uses[0].Item2==uses[1].Item1,"edge winding");}
        return edges.Count(p=>p.Value.Count==1);
    }
    static StudioModelDocument.Node Prism(int count, bool collinear=false)
    {
        var vertices=new List<Vector3>();for(int z=0;z<2;z++)for(int i=0;i<count;i++){float a=2*Mathf.PI*i/count;vertices.Add(new Vector3(Mathf.Cos(a),Mathf.Sin(a),z-.5f));}
        if(collinear)for(int z=0;z<2;z++)for(int i=1;i<count;i+=2)vertices[z*count+i]=(vertices[z*count+i-1]+vertices[z*count+(i+1)%count])*.5f;
        var faces=new List<int[]> {Enumerable.Range(0,count).Reverse().ToArray(),Enumerable.Range(count,count).ToArray()};
        for(int i=0;i<count;i++)faces.Add(new[]{i,(i+1)%count,(i+1)%count+count,i+count});
        var triangles=faces.SelectMany(f=>Enumerable.Range(1,f.Length-2).SelectMany(i=>new[]{f[0],f[i],f[i+1]})).ToArray();
        var mesh=new Mesh();mesh.vertices=vertices.ToArray();mesh.triangles=triangles;mesh.RecalculateNormals();
        return new StudioModelDocument.Node{name="Prism",mesh=mesh,cutTriangles=triangles,cutPolygons=faces.SelectMany(f=>new[]{0,f.Length}.Concat(f)).ToArray(),geometryVertices=Enumerable.Range(0,vertices.Count).ToArray()};
    }
    [InitializeOnLoadMethod]static void Requested()
    {
        if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets",StringComparison.OrdinalIgnoreCase))return;
        string path=Out("bisect-request.txt");if(File.Exists(path)){File.Delete(path);EditorApplication.delayCall+=Run;}
    }
    public static void Run()
    {
        if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets",StringComparison.OrdinalIgnoreCase))throw new Exception("Disposable project required.");
        checks=0;string stage="geometry";StudioModelDocument doc=null;StudioModelEditor editor=null;
        try
        {
            foreach(float distance in new[]{0,.01f,.2f})foreach(bool fill in new[]{false,true})
            {
                var n=Cube();try
                {
                    StudioMeshCuts.Bisect(n,new Plane(Vector3.right,Vector3.zero),Matrix4x4.identity,distance,fill);
                    Check(new StudioMeshCuts(n).Faces.Count==(fill?12:10),"cube face count");
                    Check(Boundaries(n)==(fill?0:8),"cube cap boundaries");
                    Check(n.mesh.vertices.All(v=>Mathf.Abs(v.x)>=distance*.5f-1e-6f),"gap distance");
                    if(fill)
                    {
                        var caps=new StudioMeshCuts(n).Faces.Where(f=>f.Vertices.All(v=>Mathf.Abs(Mathf.Abs(n.mesh.vertices[v].x)-distance*.5f)<1e-6f)).ToArray();
                        Check(caps.Length==2&&caps.All(f=>f.Vertices.Count==4),"minimal quad caps");
                        foreach(var f in caps)foreach(int v in f.Vertices)Check(Mathf.Abs(n.mesh.normals[v].x)>.999f,"flat cap normal");
                        Check(n.mesh.uv.Length==n.mesh.vertexCount,"planar UV stream");
                    }
                }finally{Object.DestroyImmediate(n.mesh);}
            }
            foreach(int count in new[]{3,5,6,8})
            {
                var n=Prism(count);try
                {
                    StudioMeshCuts.Bisect(n,new Plane(Vector3.forward,Vector3.zero),Matrix4x4.identity,.01f,true);
                    Check(Boundaries(n)==0,"prism watertight");
                    var caps=new StudioMeshCuts(n).Faces.Where(f=>f.Vertices.All(v=>Mathf.Abs(Mathf.Abs(n.mesh.vertices[v].z)-.005f)<1e-6f));
                    Check(caps.Any()&&caps.All(f=>f.Vertices.Count==4),"complex section all quads");
                }finally{Object.DestroyImmediate(n.mesh);}
            }
            var dense=Prism(8,true);try
            {
                StudioMeshCuts.Bisect(dense,new Plane(Vector3.forward,Vector3.zero),Matrix4x4.identity,.01f,true);
                Check(Boundaries(dense)==0,"collinear source boundary remains conforming");
                Check(new StudioMeshCuts(dense).Faces.Where(f=>f.Vertices.All(v=>Mathf.Abs(Mathf.Abs(dense.mesh.vertices[v].z)-.005f)<1e-6f)).All(f=>f.Vertices.Count==4),"collinear section uses quads");
            }finally{Object.DestroyImmediate(dense.mesh);}
            var scaled=Cube();try
            {
                var world=Matrix4x4.TRS(new Vector3(2,3,4),Quaternion.Euler(10,25,30),new Vector3(2,3,.7f));var normal=world.MultiplyVector(Vector3.right).normalized;
                var plane=new Plane(normal,world.MultiplyPoint3x4(Vector3.zero));StudioMeshCuts.Bisect(scaled,plane,world,.01f,true);
                Check(scaled.mesh.vertices.All(v=>Mathf.Abs(plane.GetDistanceToPoint(world.MultiplyPoint3x4(v)))>=.005f-1e-5f),"world distance under nonuniform scale");Check(Boundaries(scaled)==0,"scaled watertight");
            }finally{Object.DestroyImmediate(scaled.mesh);}
            stage="live preview";doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(Cube());editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);
            var node=doc.nodes[0];string original=StudioModifierSource.Capture(node).mesh;
            Set(editor,"bisectPlane",new Plane(Vector3.right,Vector3.zero));Set(editor,"bisectPlaced",true);Call(editor,"EvaluateBisect");
            Check(new StudioMeshCuts(node).Faces.Count==12,"live preview faces");Check(editor.Transforms[0].GetComponent<MeshFilter>().sharedMesh==node.mesh,"bound renderer");
            stage="render";Capture(editor,"bisect-preview.png");
            Set(editor,"bisectRepeatedParameters",(Vector4?)new Vector4(.2f,1,0,0));Call(editor,"EvaluateBisect");
            Check(node.mesh.vertices.All(v=>Mathf.Abs(v.x)>=.1f-1e-6f),"distance edit evaluates captured source");
            Set(editor,"bisectRepeatedParameters",(Vector4?)new Vector4(4,1,0,0));bool rejected=false;
            try{Call(editor,"EvaluateBisect");}catch(TargetInvocationException){rejected=true;}
            Check(rejected&&StudioModifierSource.Capture(node).mesh==original,"invalid gap restores source atomically");
            Set(editor,"bisectRepeatedParameters",null);
            Call(editor,"CancelBisectPreview",false);Check(StudioModifierSource.Capture(node).mesh==original,"cancel exact source");
            stage="apply";Set(editor,"bisectPlane",new Plane(Vector3.right,Vector3.zero));Set(editor,"bisectPlaced",true);Call(editor,"ApplyBisect");
            Check(node.modifiers!=null&&node.modifiers.operations.Last().name=="Bisect","ordered history");Check(Boundaries(node)==0,"committed watertight");
            var saved=StudioModifierSource.Capture(node).mesh;
            StudioUndo.PerformUndo();Check(StudioModifierSource.Capture(node).mesh==original,"Undo source");
            StudioUndo.PerformRedo();Check(StudioModifierSource.Capture(node).mesh==saved,"Redo result");
            File.WriteAllText(Out("bisect-result.txt"),"PASS "+checks+" checks. Cube zero/1cm/20cm with Fill on/off; triangular/odd/even section quad caps; manifold winding; nonuniform world distance; live renderer; Cancel; ordered history and Undo/Redo. Editor left open.");
        }
        catch(Exception ex){File.WriteAllText(Out("bisect-result.txt"),"FAIL "+stage+" after "+checks+" checks\n"+ex);}
        finally{editor?.Dispose();if(doc){foreach(var mesh in doc.nodes.Where(n=>n.mesh).Select(n=>n.mesh).Distinct())Object.DestroyImmediate(mesh);Object.DestroyImmediate(doc);}}
    }
    static void Capture(StudioModelEditor editor,string name)
    {
        var pipeline=UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
        var obj=new GameObject("Bisect validation camera");var camera=obj.AddComponent<Camera>();var material=new Material(Shader.Find("Hidden/FrameSubdivisionCheck"));var rt=new RenderTexture(600,600,24);var texture=new Texture2D(600,600,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
        var renderer=editor.Transforms[0].GetComponent<MeshRenderer>();var old=renderer.sharedMaterials;
        try
        {
            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
            camera.scene=editor.Scene;camera.enabled=false;camera.transform.position=new Vector3(1.4f,1,-2);camera.transform.LookAt(Vector3.zero);camera.fieldOfView=35;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.1f,.11f,.13f);camera.cullingMask=1<<30;renderer.gameObject.layer=30;renderer.sharedMaterial=material;
            rt.Create();camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,600,600),0,0);texture.Apply();File.WriteAllBytes(Out(name),texture.EncodeToPNG());Check(texture.GetPixels32().Select(c=>(int)c.r+c.g+c.b).Max()>120,"rendered mesh pixels");
        }
        finally{renderer.sharedMaterials=old;RenderTexture.active=previous;UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);Object.DestroyImmediate(material);Object.DestroyImmediate(obj);}
    }
}

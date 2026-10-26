using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;
public static class StudioBevelCornerViewportValidation
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static StudioModelEditor editor;static StudioModelDocument doc;static MashBoxStudioViewport view;static Material material;
    static AnimationStudioWindow controller;
    static int stage,checks,lastCapture;static bool pending;static double next;static readonly List<ulong> cacheHashes=new List<ulong>();
    static object Get(object o,string field)=>o.GetType().GetField(field,F).GetValue(o);
    static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(editor,args);
    static object Kind=>Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",BindingFlags.NonPublic),"Bevel");
    static void Check(bool pass,string error){if(!pass)throw new Exception(error);checks++;}
    static void Defaults(float width){ToolChannelPane.SetFloatDefault("Bevel Edges","Width",width);ToolChannelPane.SetFloatDefault("Bevel Edges","Subdivisions",0);ToolChannelPane.SetFloatDefault("Bevel Edges","Tension",1);}
    static void Select(bool first)
    {
        var points=(IList<Vector3>)Call("ComponentPositions");var edges=(List<(int a,int b)>)Call("Edges");editor.Components.Clear();var chosenEdges=new HashSet<(Vector3,Vector3)>();
        for(int i=0;i<edges.Count;i++){var a=points[edges[i].a];var b=points[edges[i].b];bool chosen=first?a.z==-.5f&&b.z==-.5f&&((a.x==.5f&&b.x==.5f)||(a.y==.5f&&b.y==.5f)):Mathf.Abs(a.x-a.y)<1e-5&&Mathf.Abs(b.x-b.y)<1e-5&&a.x>0&&b.x>0;if(chosen&&chosenEdges.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(a,b)))editor.Components.Add(i);}
        Check(editor.Components.Count==2,"Both passes must select the two connected edges");
    }
    public static void Run(){EditorApplication.delayCall+=Start;}
    static void Start()
    {
        try
        {
            doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);doc.nodes.Add(new StudioModelDocument.Node{name="Corner",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles});
            controller=ScriptableObject.CreateInstance<AnimationStudioWindow>();typeof(AnimationStudioWindow).GetField("modelingMode",F).SetValue(controller,true);typeof(AnimationStudioWindow).GetField("modelDocument",F).SetValue(controller,doc);typeof(AnimationStudioWindow).GetMethod("OpenViewport",F).Invoke(controller,null);
            view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Single(v=>v.Studio==controller);editor=(StudioModelEditor)Get(controller,"modelEditor");doc=editor.Document;editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);Defaults(.449f);Select(true);Call("ApplyLiveTool",Kind);
            StudioSettings.Current.disableViewportImageCache=false;view.position=new Rect(100,100,1000,750);view.LookAtDirect(Vector3.zero,Quaternion.Euler(20,-35,0),1.3f);
            material=new Material(Shader.Find("MashBox/Studio/Preview"));material.SetColor("_Color",new Color(.65f,.65f,.65f,1));material.SetFloat("_Cull",0);material.SetFloat("_CameraLighting",1);
            SceneView.duringSceneGui+=Draw;EditorApplication.update+=Tick;pending=true;next=EditorApplication.timeSinceStartup+.5;
        }catch(Exception e){Finish(e.ToString());}
    }
    static void Draw(SceneView scene)
    {
        if(!pending||scene!=view||Event.current.type!=EventType.Repaint)return;pending=false;
        var rt=new RenderTexture(1000,750,24);rt.Create();var old=RenderTexture.active;var oldTarget=view.camera.targetTexture;var oldRect=view.camera.pixelRect;GL.PushMatrix();string error=null;
        try
        {
            view.camera.targetTexture=rt;view.camera.pixelRect=new Rect(0,0,1000,750);Handles.SetCamera(new Rect(0,0,1000,750),view.camera);RenderTexture.active=rt;GL.Clear(true,true,Color.black);GL.LoadProjectionMatrix(view.camera.projectionMatrix);GL.modelview=view.camera.worldToCameraMatrix;
            var filter=editor.Transforms[0].GetComponent<MeshFilter>();material.SetPass(0);Graphics.DrawMeshNow(filter.sharedMesh,editor.Transforms[0].localToWorldMatrix);
            Call("DrawVisibleObjectWireframes",view.camera);Call("DrawComponentBatches",view);
            var texture=new Texture2D(1000,750,TextureFormat.RGBA32,false);try{texture.ReadPixels(new Rect(0,0,1000,750),0,0);texture.Apply();var pixels=texture.GetPixels32();Check(pixels.Count(p=>p.g>p.r*1.4&&p.g>p.b*.9&&p.g>70)>50,"No cyan editable wire rendered");Check(pixels.Count(p=>p.r>35&&Math.Abs(p.r-p.g)<5&&Math.Abs(p.r-p.b)<5)>1000,"No shaded geometry");File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../bevel-corner-viewport-"+stage+".png")),texture.EncodeToPNG());}finally{Object.DestroyImmediate(texture);}
            var wire=((IDictionary)Get(editor,"selectionWireframes"))[doc.nodes[0]];var positions=(IList<Vector3>)Get(wire,"vertices");var indices=(int[])Get(wire,"indices");var authored=new HashSet<(Vector3,Vector3)>();var node=doc.nodes[0];var top=new StudioMeshCuts(node);var current=(IList<Vector3>)Call("ComponentPositions");
            foreach(var f in top.Faces)for(int i=0;i<f.Vertices.Count;i++)authored.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(current[f.Vertices[i]],current[f.Vertices[(i+1)%f.Vertices.Count]]));
            var drawn=new HashSet<(Vector3,Vector3)>();for(int i=0;i<indices.Length;i+=2)drawn.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(positions[indices[i]],positions[indices[i+1]]));Check(drawn.SetEquals(authored),"Wire cache differs from current editable topology");
            if(stage>0){typeof(StudioBevelJunctionTopologyValidation).GetMethod("MatchReference",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{node});Check(true,"Reference authored junction incidence");}
        }catch(Exception e){error=e.ToString();}
        finally{GL.PopMatrix();view.camera.targetTexture=oldTarget;view.camera.pixelRect=oldRect;RenderTexture.active=old;Object.DestroyImmediate(rt);}
        if(error!=null)Finish(error);else{stage++;next=EditorApplication.timeSinceStartup+.1;}
    }
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.1;
        try{
            if(!pending&&stage>cacheHashes.Count){
                if(!view.ViewportCacheReady){view.Repaint();return;}
                Check(view.ViewportCacheCaptureCount>lastCapture,"Live edit did not refresh viewport capture");lastCapture=view.ViewportCacheCaptureCount;
                var rt=(RenderTexture)Get(view,"cachedScene");var old=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();ulong hash=1469598103934665603;foreach(byte b in t.GetRawTextureData<byte>())hash=unchecked((hash^b)*1099511628211);Object.DestroyImmediate(t);RenderTexture.active=old;cacheHashes.Add(hash);
            }
            if(stage>=4){Check(cacheHashes.Distinct().Count()==4,"Viewport cached pixels did not change across widths");Finish("PASS "+checks+" native shared-corner shaded/wire and full-controller cache checks");return;}if(!pending){if(stage==1)Select(false);Defaults(new[]{.449f,.1f,.5f,.994f}[stage]);Call("EvaluateLiveTool",Kind);pending=true;}view.Repaint();}catch(Exception e){Finish(e.ToString());}
    }
    static void Finish(string result)
    {
        SceneView.duringSceneGui-=Draw;EditorApplication.update-=Tick;File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../bevel-corner-viewport-result.txt")),result);EditorApplication.delayCall+=()=>{if(view)view.Close();if(controller)Object.DestroyImmediate(controller);if(material)Object.DestroyImmediate(material);if(doc){foreach(var n in doc.nodes)if(n.mesh)Object.DestroyImmediate(n.mesh);Object.DestroyImmediate(doc);}if(result.StartsWith("PASS")){string matrix=StudioBevelCornerRegressionValidation.Validate();string junction=StudioBevelJunctionTopologyValidation.Validate();EditorApplication.Exit(matrix.StartsWith("PASS")&&junction.StartsWith("PASS")?0:1);}else EditorApplication.Exit(1);};
    }
}

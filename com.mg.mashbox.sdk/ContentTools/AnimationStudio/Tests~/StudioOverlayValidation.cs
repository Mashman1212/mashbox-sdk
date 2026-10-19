#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
public class StudioOverlayValidationWindow:EditorWindow
{
    const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    public StudioModelDocument source;
    bool ran;
    public static void Run(StudioModelDocument document)
    {
        var window=CreateInstance<StudioOverlayValidationWindow>();window.source=document;
        window.titleContent=new GUIContent("Modeler validation");window.position=new Rect(80,80,420,80);window.ShowUtility();
    }
    void OnGUI()
    {
        GUILayout.Label("Checking batched modeler overlaysâ€¦");if(ran||Event.current.type!=EventType.Repaint)return;ran=true;
        var result=new List<string>();IDisposable editor=null;SceneView view=null;StudioModelDocument doc=null;RenderTexture rt=null;Texture2D pixels=null;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);result.Add("PASS "+message);}
        try
        {
            var original=source;
            Check(original&&original.nodes.Count>=2&&original.nodes.Take(2).All(n=>n.mesh&&n.mesh.vertexCount>100),"Validation source has two meshes with at least 101 vertices each");
            doc=Object.Instantiate(original);foreach(var node in doc.nodes)node.mesh=Object.Instantiate(node.mesh);
            var type=typeof(StudioModelDocument).Assembly.GetType("MashBoxSDK.AnimationStudio.StudioModelEditor");
            editor=(IDisposable)Activator.CreateInstance(type,Flags,null,new object[]{(Action)(()=>{})},null);
            void Call(string name,params object[] args)=>type.GetMethod(name,Flags).Invoke(editor,args);
            Call("Load",doc);Call("SelectObject",0,false,false);
            var mode=type.GetField("mode",Flags);var selected=(HashSet<int>)type.GetField("Components",Flags).GetValue(editor);
            int Builds()=>(int)type.GetProperty("ComponentOverlayBuilds",Flags).GetValue(editor);
            int Calls()=>(int)type.GetProperty("ComponentOverlayDrawCalls",Flags).GetValue(editor);
            Mesh Cached(string name)=>(Mesh)type.GetField(name,Flags).GetValue(editor);
            void Mode(string name)=>mode.SetValue(editor,Enum.Parse(mode.FieldType,name));
            view=ScriptableObject.CreateInstance<SceneView>();view.position=new Rect(0,0,512,512);
            rt=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32);rt.Create();view.camera.targetTexture=rt;
            var bounds=doc.nodes[0].mesh.bounds;
            view.camera.transform.position=bounds.center-new Vector3(0,0,50);view.camera.transform.rotation=Quaternion.identity;
            view.camera.orthographic=true;view.camera.orthographicSize=bounds.extents.y*1.15f;
            var previous=RenderTexture.active;RenderTexture.active=rt;
            GL.PushMatrix();GL.LoadProjectionMatrix(view.camera.projectionMatrix);GL.modelview=view.camera.worldToCameraMatrix;
            try
            {
                Mode("Vertex");GL.Clear(true,true,Color.black);Call("DrawComponentBatches",view);
                Check(Calls()==2,"All tree edges and vertex dots require two draw calls");
                Check(Cached("componentDots").vertexCount==doc.nodes[0].mesh.vertexCount*4,"Every authored vertex has a GPU marker");
                int build=Builds();var dots=Cached("componentDots");
                for(int i=0;i<30;i++)Call("DrawComponentBatches",view);
                Check(Builds()==build&&Cached("componentDots")==dots,"Repeated camera repaint reuses geometry buffers");
                pixels=new Texture2D(512,512,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,512,512),0,0);pixels.Apply();
                Check(pixels.GetPixels32().Count(c=>c.g>40)>100,"GPU overlays produce visible pixels");
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(),"studio-overlays-vertices.png"),pixels.EncodeToPNG());
                selected.Add(0);selected.Add(100);Call("DrawComponentBatches",view);
                Check(Calls()==3&&Cached("componentDots")==dots,"Selected vertex highlights add one batch without rebuilding all dots");
                foreach(string name in new[]{"Edge","Face"})
                {
                    Mode(name);selected.Clear();selected.Add(0);GL.Clear(true,true,Color.black);Call("DrawComponentBatches",view);
                    Check(Calls()==2,"Selected "+name+" overlay uses two batches");
                    Check(Cached("componentHighlight").GetIndexCount(0)>0,name+" selection generates highlight geometry");
                }
                Mode("Vertex");selected.Clear();Call("DrawComponentBatches",view);
                var transforms=(Transform[])type.GetProperty("Transforms",Flags).GetValue(editor);
                var verts=doc.nodes[0].mesh.vertices;
                var edges=(List<(int a,int b)>)type.GetMethod("Edges",Flags).Invoke(editor,null);
                void Legacy()
                {
                    Handles.color=new Color(.3f,.65f,.72f,.45f);
                    foreach(var edge in edges)Handles.DrawLine(transforms[0].TransformPoint(verts[edge.a]),transforms[0].TransformPoint(verts[edge.b]));
                    Handles.color=new Color(.35f,.9f,.9f);
                    for(int i=0;i<verts.Length;i++)Handles.DotHandleCap(0,transforms[0].TransformPoint(verts[i]),Quaternion.identity,.06f,EventType.Repaint);
                }
                var draw=(Action<SceneView>)Delegate.CreateDelegate(typeof(Action<SceneView>),editor,type.GetMethod("DrawComponentBatches",Flags));
                Legacy();draw(view);
                (double ms,long bytes) Measure(Action action)
                {
                    var timer=new Stopwatch();long before=GC.GetAllocatedBytesForCurrentThread();timer.Start();for(int i=0;i<20;i++)action();timer.Stop();return(timer.Elapsed.TotalMilliseconds/20,(GC.GetAllocatedBytesForCurrentThread()-before)/20);
                }
                var old=Measure(Legacy);var now=Measure(()=>draw(view));
                result.Add($"BENCHMARK {verts.Length} vertices, {edges.Count} edges: legacy {old.ms:F3} ms / {old.bytes} B per draw; batched {now.ms:F3} ms / {now.bytes} B per draw (CPU submission; not whole-editor FPS)");
                Check(now.ms<old.ms,"Batched CPU submission is faster in the same render context");
                Call("Changed",true);Call("DrawComponentBatches",view);Check(Cached("componentDots")!=dots,"Geometry changes invalidate cached overlays");
                Call("SelectObject",1,false,false);Call("DrawComponentBatches",view);Check(Cached("componentDots").vertexCount==doc.nodes[1].mesh.vertexCount*4,"Switching objects rebuilds the right geometry");
                type.GetField("xray",Flags).SetValue(editor,false);Call("DrawComponentBatches",view);
                var mat=(Material)type.GetField("componentMaterial",Flags).GetValue(editor);
                Check(mat.GetFloat("_ZTest")== (float)CompareFunction.LessEqual,"Occluded mode enables depth testing");
                Check(!ShaderUtil.GetShaderMessages(mat.shader).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),"Overlay shader compiles without errors");
            }
            finally{GL.PopMatrix();RenderTexture.active=previous;}
            Check(typeof(MashBoxStudioViewport).Name=="MashBoxStudioViewport","Profiler viewport type uses MashBox Studio naming");
            result.Add("SUCCESS");
        }
        catch(Exception e){result.Add("FAIL "+e);UnityEngine.Debug.LogException(e);}
        finally
        {
            editor?.Dispose();if(view){view.camera.targetTexture=null;Object.DestroyImmediate(view);}if(rt){rt.Release();Object.DestroyImmediate(rt);}if(pixels)Object.DestroyImmediate(pixels);
            if(doc){foreach(var node in doc.nodes)if(node.mesh)Object.DestroyImmediate(node.mesh);Object.DestroyImmediate(doc);}
            File.WriteAllLines(Path.Combine(Path.GetTempPath(),"studio-overlay-test.txt"),result);EditorApplication.delayCall+=Close;
        }
    }
}
#endif

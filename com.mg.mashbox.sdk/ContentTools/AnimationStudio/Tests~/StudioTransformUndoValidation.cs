using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
public static class StudioTransformUndoValidation
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static AnimationStudioWindow controller;
    static MashBoxStudioViewport view;
    static StudioModelDocument document;
    static StudioModelEditor editor;
    static double next,start;
    static int renders,phase;
    static string Version=>Environment.GetCommandLineArgs().Contains("-wireAfter")?"after":"before";
    public static void Run(){EditorApplication.delayCall+=Begin;}
    static void Begin()
    {
        try
        {
            StudioSettings.Current.viewportVSync=false;StudioSettings.Current.disableViewportImageCache=true;StudioSettings.Current.showWorkspaceFps=false;StudioSettings.Current.hideObjectWireframe=false;
            string sourcePath=AssetDatabase.FindAssets("MTB_Frame_Bestial_Lynx t:Model").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(path=>path.EndsWith("/MTB_Frame_Bestial_Lynx.fbx",StringComparison.OrdinalIgnoreCase));
            var source=string.IsNullOrEmpty(sourcePath)?null:AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if(!source)throw new Exception("MTB_Frame_Bestial_Lynx not imported");
            document=ScriptableObject.CreateInstance<StudioModelDocument>();
            document.nodes=(System.Collections.Generic.List<StudioModelDocument.Node>)typeof(StudioModelDocument).GetMethod("CaptureNodes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{source});
            controller=ScriptableObject.CreateInstance<AnimationStudioWindow>();
            typeof(AnimationStudioWindow).GetField("modelingMode",Flags).SetValue(controller,true);
            typeof(AnimationStudioWindow).GetField("modelDocument",Flags).SetValue(controller,document);
            typeof(AnimationStudioWindow).GetMethod("OpenViewport",Flags).Invoke(controller,null);
            view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Single(v=>v.Studio==controller);
            view.position=new Rect(0,0,1683,950);
            editor=(StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor",Flags).GetValue(controller);
            int main=document.nodes.FindIndex(n=>n.mesh);editor.SelectObject(main);
            var bounds=editor.Transforms[main].GetComponent<Renderer>().bounds;
            view.LookAtDirect(bounds.center,Quaternion.Euler(18,140,0),bounds.extents.magnitude*1.5f);
            UnityEngine.Debug.Log("WIRE_MODEL vertices="+document.nodes.Where(n=>n.mesh).Sum(n=>n.mesh.vertexCount)+" nodes="+document.nodes.Count);
            next=EditorApplication.timeSinceStartup+5;SceneView.duringSceneGui+=Observe;EditorApplication.update+=Tick;
        }
        catch(Exception e){Finish(e);}
    }
    static void Observe(SceneView scene){if(scene==view&&Event.current.type==EventType.Repaint&&start>0)renders++;}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next)return;EditorApplication.update-=Tick;
        try
        {
            Paint();int index=editor.Active;var original=editor.Transforms[index];var node=document.nodes[index];
            var beforePosition=node.position;var beforeRotation=node.rotation;var beforeScale=node.scale;
            var initialUploads=editor.WireBatchUploads;
            foreach(string action in new[]{"Transform model objects","Scale model objects","Edit object channels"})
            {
                Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(document,action);
                node.position+=new Vector3(.25f,.5f,.75f);node.rotation=Quaternion.Euler(10,25,5)*node.rotation;node.scale*=1.2f;
                document.ApplyTransform(index,editor.Transforms[index]);editor.Changed(false);Paint();Undo.IncrementCurrentGroup();
                var timer=Stopwatch.StartNew();Undo.PerformUndo();timer.Stop();double undo=timer.Elapsed.TotalMilliseconds;timer.Restart();Paint();timer.Stop();
                UnityEngine.Debug.Log($"UNDO_BENCHMARK action={action} undoMs={undo:F2} repaintMs={timer.Elapsed.TotalMilliseconds:F2} sameTransform={original==editor.Transforms[index]} sameNode={ReferenceEquals(node,document.nodes[index])} uploads={editor.WireBatchUploads-initialUploads}");
                Require(original==editor.Transforms[index] && editor.WireBatchUploads==initialUploads,"Transform Undo retains renderers and GPU geometry");
                Require(Vector3.Distance(editor.Transforms[index].localPosition,document.nodes[index].positionOffset+beforePosition)<1e-5f,"Undo updates the existing preview transform");
                Require(Vector3.Distance(document.nodes[index].position,beforePosition)<1e-5f,"Undo restores position");
                Require(Quaternion.Angle(document.nodes[index].rotation,beforeRotation)<.01f,"Undo restores rotation");Require(Vector3.Distance(document.nodes[index].scale,beforeScale)<1e-5f,"Undo restores scale");
                Undo.PerformRedo();Paint();Require(Vector3.Distance(document.nodes[index].position,beforePosition)>.1f,"Redo restores transform edit");
                Undo.PerformUndo();Paint();node=document.nodes[index];original=editor.Transforms[index];initialUploads=editor.WireBatchUploads;
            }
            var mesh=document.nodes[index].mesh;var positions=mesh.vertices;var prior=positions[0];
            Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(mesh,"Transform mesh components");positions[0]+=Vector3.one*.02f;mesh.vertices=positions;mesh.RecalculateBounds();typeof(StudioModelEditor).GetMethod("PositionsChanged",Flags).Invoke(editor,null);Undo.IncrementCurrentGroup();Undo.PerformUndo();Paint();
            Require(Vector3.Distance(mesh.vertices[0],prior)<1e-5f,"Geometry Undo still restores mesh positions");
            var beforePivot=mesh.vertices[0];var oldPreview=editor.Transforms[index];
            Undo.IncrementCurrentGroup();document.Repivot(index,editor.Transforms,oldPreview.position+Vector3.one*.1f);Undo.SetCurrentGroupName("Edit object channels");editor.Changed(true);Paint();Undo.IncrementCurrentGroup();
            Undo.PerformUndo();Paint();Require(Vector3.Distance(mesh.vertices[0],beforePivot)<1e-5f,"Pivot channel Undo restores compensated geometry");Require(oldPreview!=editor.Transforms[index],"Geometry-changing channel Undo uses the full restore path");
            Finish(null);
        }catch(Exception e){Finish(e);}
    }
    static void Require(bool pass,string text){if(!pass)throw new Exception(text);UnityEngine.Debug.Log("WIRE_CHECK "+text);}
    static void Paint(){view.MarkDirty(MashBoxStudioViewport.DirtyState.All);view.SendEvent(new Event{type=EventType.Layout});view.SendEvent(new Event{type=EventType.Repaint});}
    static Color[] Snapshot(string name)
    {
        var parent=typeof(EditorWindow).GetField("m_Parent",Flags).GetValue(view);
        var target=new RenderTexture(1000,700,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);var pixels=new Texture2D(1000,700,TextureFormat.RGBA32,false,true);var active=RenderTexture.active;
        try{target.Create();parent.GetType().GetMethod("GrabPixels",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(parent,new object[]{target,new Rect(330,110,1000,700)});RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1000,700),0,0);pixels.Apply();File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../wire-"+name+".png")),pixels.EncodeToPNG());return pixels.GetPixels();}
        finally{RenderTexture.active=active;Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);}
    }
    static int Cyan(Color[] pixels)=>pixels.Count(p=>p.g>p.r*1.4f&&p.b>p.r*1.3f&&p.g>.3f);
    static void Validate()
    {
        var shader=Shader.Find("Hidden/MashBox/ModelerWireBatch");Require(shader&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"GPU line shader compiles");
        var bounds=editor.Transforms[editor.Active].GetComponent<Renderer>().bounds;
        view.LookAtDirect(bounds.center,Quaternion.Euler(20,135,0),bounds.extents.magnitude*1.5f);
        editor.UseBatchedWireframes=false;Paint();var native=Snapshot("native");
        editor.UseBatchedWireframes=true;Paint();var gpu=Snapshot("gpu");
        int a=Cyan(native),b=Cyan(gpu);UnityEngine.Debug.Log("WIRE_PIXELS native="+a+" gpu="+b);
        Require(a>500&&b>a*.45f&&b<a*2.5f,"Selected polygon edges render in the GPU batch");
        Require(gpu.Any(p=>Mathf.Abs(p.r-.2f)<.02f&&Mathf.Abs(p.g-.9f)<.025f&&Mathf.Abs(p.b-.75f)<.02f),"Batched outlines retain the native selection color");
        Paint();Require(gpu.SequenceEqual(Snapshot("gpu-repeat")),"Stationary outlines are deterministic without temporal flicker");
        int uploads=editor.WireBatchUploads;Paint();view.rotation*=Quaternion.AngleAxis(5,Vector3.up);Paint();
        Require(editor.WireBatchUploads==uploads,"Camera movement reuses GPU geometry");
        var node=document.nodes[editor.Active];var vertices=node.mesh.vertices;
        vertices[0]+=Vector3.one*.005f;node.mesh.vertices=vertices;node.mesh.RecalculateBounds();
        typeof(StudioModelEditor).GetMethod("PositionsChanged",Flags).Invoke(editor,null);Paint();
        Require(editor.WireBatchUploads>uploads,"Mesh edits update GPU positions");
        var cache=(System.Collections.IDictionary)typeof(StudioModelEditor).GetField("selectionWireframes",Flags).GetValue(editor);
        var wire=cache[node];var buffer=(GraphicsBuffer)wire.GetType().GetField("positionBuffer",Flags).GetValue(wire);
        var readback=new Vector3[vertices.Length];buffer.GetData(readback);
        Require(Vector3.Distance(readback[0],vertices[0])<1e-6f,"GPU vertex buffer contains the edited geometry");
        for(int angle=0;angle<3;angle++)
        {
            view.LookAtDirect(bounds.center,Quaternion.Euler(angle==2?85:20,angle*45+90,0),bounds.extents.magnitude*1.5f);Paint();
            Require(Cyan(Snapshot("angle-"+angle))>200,"Edges remain visible at viewing angle "+angle);
        }
        typeof(StudioModelEditor).GetField("displayXray",Flags).SetValue(editor,true);Paint();var xray=Snapshot("xray");
        Require(Cyan(xray)>200,"X-ray draws edges without surface occlusion");
        typeof(StudioModelEditor).GetField("displayXray",Flags).SetValue(editor,false);
        var cover=GameObject.CreatePrimitive(PrimitiveType.Quad);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cover,editor.Scene);
        var coverMaterial=new Material(Shader.Find("Hidden/MashBox/AnimationStudioPreview"));
        try
        {
            cover.transform.position=bounds.center+(view.camera.transform.position-bounds.center)*.25f;
            cover.transform.rotation=view.camera.transform.rotation;cover.transform.localScale=Vector3.one*bounds.size.magnitude*3;
            coverMaterial.SetFloat("_Cull",0);coverMaterial.SetFloat("_Unlit",1);coverMaterial.SetColor("_Color",Color.gray);
            cover.GetComponent<Renderer>().sharedMaterial=coverMaterial;Paint();int hidden=Cyan(Snapshot("occluded"));
            typeof(StudioModelEditor).GetField("displayXray",Flags).SetValue(editor,true);Paint();int through=Cyan(Snapshot("occluded-xray"));
            Require(through>hidden*3+500,"Solid faces occlude normal outlines; X-ray alone reveals them");
        }
        finally {Object.DestroyImmediate(cover);Object.DestroyImmediate(coverMaterial);typeof(StudioModelEditor).GetField("displayXray",Flags).SetValue(editor,false);}
        view.orthographic=true;Paint();Require(editor.ObjectWireDrawCalls<20,"Orthographic wireframe remains batched");Snapshot("ortho");
        view.orthographic=false;Paint();
        editor.Changed();Paint();Require(!buffer.IsValid(),"Topology invalidation disposes old GPU buffers");
    }
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;SceneView.duringSceneGui-=Observe;
        if(view)view.Close();if(controller)Object.DestroyImmediate(controller);
        if(document){foreach(var n in document.nodes)if(n.mesh)Object.DestroyImmediate(n.mesh);Object.DestroyImmediate(document);}
        if(error!=null)UnityEngine.Debug.LogException(error);else UnityEngine.Debug.Log("STUDIO_WIRE_BENCHMARK_SUCCESS");
        EditorApplication.Exit(error==null?0:1);
    }
}




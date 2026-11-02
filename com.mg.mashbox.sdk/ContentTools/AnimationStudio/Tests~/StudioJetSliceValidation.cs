using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;

internal static class StudioJetSliceValidation
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+n));
    static int checks,phase,warm;static double ready;static string evidence="";
    static StudioModelDocument doc;static StudioModelEditor editor;static AnimationStudioWindow host;static MashBoxStudioViewport view;
    static StudioModifierSource source;static Vector2 offset;static bool calibrated;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static object Call(object value,string name,params object[] args)=>value.GetType().GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(value,args);
    static double Area(Mesh m){var v=m.vertices;var t=m.triangles;double sum=0;for(int i=0;i<t.Length;i+=3)sum+=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).magnitude*.5;return sum;}
    [InitializeOnLoadMethod]static void Request(){if(Application.dataPath.EndsWith("SelectionUndoProject/Assets",StringComparison.OrdinalIgnoreCase)&&File.Exists(Out("jet-slice-request.txt"))){File.Delete(Out("jet-slice-request.txt"));EditorApplication.delayCall+=Run;}}
    public static void Run()
    {
        if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets",StringComparison.OrdinalIgnoreCase))throw new Exception("Disposable project required");
        try
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/JetSlice/fighterJetCutTest.fbx");Check(asset,"Jet asset not imported");
            var nodes=(List<StudioModelDocument.Node>)typeof(StudioModelDocument).GetMethod("CaptureNodes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{asset});
            var node=nodes.Where(n=>n.mesh).OrderByDescending(n=>n.mesh.vertexCount).First();var matrix=Matrix4x4.identity;for(int at=nodes.IndexOf(node);at>=0;at=nodes[at].parent){var part=nodes[at];matrix=Matrix4x4.TRS(part.position+part.positionOffset,part.rotation,part.scale)*matrix;}node.mesh.vertices=node.mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();var normalMatrix=matrix.inverse.transpose;node.mesh.normals=node.mesh.normals.Select(n=>normalMatrix.MultiplyVector(n).normalized).ToArray();node.mesh.RecalculateBounds();source=StudioModifierSource.Capture(node);
            evidence=$"Exact fighterJetCutTest.fbx: {node.mesh.vertexCount} vertices, {new StudioMeshCuts(node).Faces.Count} authored faces. ";
            foreach(var direction in new[]{Vector3.right,Vector3.up,Vector3.forward,new Vector3(1,.37f,.21f).normalized})
            {
                var n=source.Restore();var plane=new Plane(direction,n.mesh.bounds.center+direction*n.mesh.bounds.size.magnitude*.01237f);double before=Area(n.mesh);
                try
                {
                    var old=source.Restore();try{var cutter=new StudioMeshCuts(old);cutter.ApplyMultiCut(old,cutter.SliceCut(plane));evidence+="Legacy accepted; ";}catch(Exception e){evidence+="Legacy rejected: "+e.Message+"; ";}finally{Object.DestroyImmediate(old.mesh);}
                    Check(new StudioMeshCuts(n).ApplyPlaneSlice(n,plane),"Plane did not slice jet");
                    Check(new StudioMeshCuts(n).Faces.Count>new StudioMeshCuts(node).Faces.Count,"No authored face split");
                    Check(Math.Abs(Area(n.mesh)-before)<=Math.Max(1e-6,before*1e-5),"Through slice changed rendered surface area");
                    Check(new StudioMeshCuts(n).BevelSourceTriangles(n.mesh).Sum(t=>t.Count)==n.mesh.triangles.Length,"Render ownership after slice");
                }finally{Object.DestroyImmediate(n.mesh);}
                foreach(bool fill in new[]{false,true})
                {
                    var b=source.Restore();try{StudioMeshCuts.Bisect(b,plane,Matrix4x4.identity,.01f,fill);Check(new StudioMeshCuts(b).Faces.Count>0,"Bisect empty");Check(b.mesh.vertices.All(v=>Mathf.Abs(plane.GetDistanceToPoint(v))>=.005f-1e-5f),"Bisect gap");}
                    finally{Object.DestroyImmediate(b.mesh);}
                }
            }
            foreach(var n in nodes.Where(n=>n.mesh))Object.DestroyImmediate(n.mesh);
            AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();Call(host,"SwitchStudioMode",true);
            Load();phase=0;warm=0;ready=EditorApplication.timeSinceStartup+.8;EditorApplication.update+=Tick;
            File.WriteAllText(Out("jet-slice-progress.txt"),evidence+"\nGeometry passed; checking native drags.");
        }
        catch(Exception e){Finish("FAIL setup "+e);}
    }
    static void Load()
    {
        doc=ScriptableObject.CreateInstance<StudioModelDocument>();var n=source.Restore();n.name="fighterJetCutTest";n.materials=new[]{StudioMeshMaterialPreview.DefaultMaterial};doc.nodes.Add(n);doc.lighting.ground=false;
        Call(host,"LoadStudioModel",doc);editor=host.ToolbarModelEditor;editor.SelectObject(0);view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);
        view.position=new Rect(40,40,1100,800);view.modelGridVisible=false;view.LookAtDirect(n.mesh.bounds.center,Quaternion.Euler(24,-32,0),n.mesh.bounds.size.magnitude*1.05f);view.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);view.Repaint();
        calibrated=false;
    }
    static void Send(EventType type,Vector2 p)=>view.SendEvent(new Event{type=type,button=0,mousePosition=p+offset});
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<ready)return;
        try
        {
            if(warm++<3){view.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);view.Repaint();ready=EditorApplication.timeSinceStartup+.5;return;}
            if(!calibrated)
            {
                var p=view.ModelCanvasRect.center;offset=Vector2.zero;EditorGUIUtility.editingTextField=false;Send(EventType.MouseMove,p);offset=p-view.CanvasInputOrigin-(view.CanvasMousePosition??Vector2.zero);calibrated=true;
            }
            bool bisect=phase>=3;float slope=new[]{0f,.45f,-.65f}[phase%3];
            if(bisect)Call(editor,"OpenBisectTool");else editor.ActivateCutTool(StudioModelEditor.CutTool.MultiCut);
            var bounds=(Rect)Call(editor,"MultiProjectedBounds",view);var start=new Vector2(bounds.xMin-12,bounds.center.y-(bounds.width*.5f+12)*slope);var end=new Vector2(bounds.xMax+12,bounds.center.y+(bounds.width*.5f+12)*slope);
            int before=new StudioMeshCuts(doc.nodes[0]).Faces.Count;
            Send(EventType.MouseMove,start+view.CanvasInputOrigin);Send(EventType.MouseDown,start+view.CanvasInputOrigin);
            Check((bool)typeof(StudioModelEditor).GetField(bisect?"bisectDragging":"multiSlicing",F).GetValue(editor),"Native outside drag did not start: "+editor.StatusText);
            Send(EventType.MouseDrag,end+view.CanvasInputOrigin);Send(EventType.MouseUp,end+view.CanvasInputOrigin);
            Check(new StudioMeshCuts(doc.nodes[0]).Faces.Count>before,(bisect?"Bisect":"Multi-Cut")+" native angle "+slope+" failed: "+editor.StatusText);
            if(bisect)Call(editor,"ApplyBisect");
            evidence+=$"Native {(bisect?"Bisect":"Multi-Cut")} slope {slope} passed. ";
            if(phase==5){view.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);view.Repaint();EditorApplication.update-=Tick;EditorApplication.delayCall+=Capture;return;}
            phase++;Load();warm=0;ready=EditorApplication.timeSinceStartup+.5;
        }
        catch(Exception e){Finish("FAIL native phase "+phase+" "+e);}
    }
    static void Capture()
    {
        try
        {
            var rt=typeof(MashBoxStudioViewport).GetField("cachedScene",F).GetValue(view) as RenderTexture;Check(rt&&rt.IsCreated(),"No native rendered scene");
            var previous=RenderTexture.active;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
            try{RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Out("jet-slice-native.png"),image.EncodeToPNG());}
            finally{RenderTexture.active=previous;Object.DestroyImmediate(image);}
            CaptureSurface();
            Finish("PASS "+checks+" exact-jet geometry and six native outside drags. "+evidence);
        }catch(Exception e){Finish("FAIL capture "+e);}
    }
    static void CaptureSurface()
    {
        var renderer=editor.Transforms[0].GetComponent<MeshRenderer>();var filter=renderer.GetComponent<MeshFilter>();var live=filter.sharedMesh;
        var original=source.Restore();var oldMaterials=renderer.sharedMaterials;int oldLayer=renderer.gameObject.layer;
        var pipeline=UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;var active=RenderTexture.active;
        var obj=new GameObject("Jet slice render check");var camera=obj.AddComponent<Camera>();var material=new Material(Shader.Find("Hidden/FrameSubdivisionCheck"));var target=new RenderTexture(1000,800,24);
        try
        {
            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
            var bounds=renderer.bounds;camera.scene=editor.Scene;camera.enabled=false;camera.transform.position=bounds.center+new Vector3(.7f,.5f,-1).normalized*bounds.extents.magnitude*3.6f;camera.transform.LookAt(bounds.center);camera.fieldOfView=35;camera.nearClipPlane=.001f;camera.farClipPlane=10000;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.09f,.11f);camera.cullingMask=1<<30;
            renderer.gameObject.layer=30;renderer.sharedMaterials=Enumerable.Repeat(material,live.subMeshCount).ToArray();target.Create();camera.targetTexture=target;
            Color32[] Pixels(string path)
            {
                camera.Render();var image=new Texture2D(1000,800,TextureFormat.RGBA32,false);
                try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1000,800),0,0);image.Apply();File.WriteAllBytes(Out(path),image.EncodeToPNG());return image.GetPixels32();}
                finally{Object.DestroyImmediate(image);}
            }
            filter.sharedMesh=original.mesh;var before=Pixels("jet-slice-before.png");filter.sharedMesh=live;var after=Pixels("jet-slice-after.png");
            int difference=before.Zip(after,(a,b)=>a.Equals(b)?0:1).Sum();Check(difference>20,"Bisect did not change actual renderer pixels");evidence+=$"Actual bound renderer before/after: {difference} changed pixels. ";
        }
        finally{filter.sharedMesh=live;renderer.sharedMaterials=oldMaterials;renderer.gameObject.layer=oldLayer;UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;RenderTexture.active=active;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(material);Object.DestroyImmediate(obj);Object.DestroyImmediate(original.mesh);}
    }
    static void Finish(string result){EditorApplication.update-=Tick;File.WriteAllText(Out("jet-slice-result.txt"),result+"\n"+evidence);}
}

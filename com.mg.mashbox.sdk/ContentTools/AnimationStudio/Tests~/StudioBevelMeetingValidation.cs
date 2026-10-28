#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;
using Undo=MashBoxSDK.AnimationStudio.StudioUndo;

public static class StudioBevelMeetingValidation
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks;static string stage;static double next;static Queue<(string,Action)> steps=new Queue<(string,Action)>();
    static AnimationStudioWindow host;static MashBoxStudioViewport view;static StudioModelEditor editor;static StudioModelDocument doc;
    static Mesh source;static Vector3[] sourceVertices;static int[] sourcePolygons;static object kind;
    static Vector3[] terminalPoints;static int[] terminalTriangles;static int history;static Material renderMaterial;
    static bool capture;static string captureName;static double captureDeadline;
    static string Out(string file)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+file));
    static void Check(bool value,string message){if(!value)throw new Exception(stage+": "+message);checks++;}
    static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length&&m.GetParameters().Select((p,i)=>args[i]==null||p.ParameterType.IsInstanceOfType(args[i])).All(v=>v)).Invoke(editor,args);
    static List<(int a,int b)> Edges(StudioMeshCuts t)
    {
        var seen=new HashSet<(Vector3,Vector3)>();var result=new List<(int,int)>();
        foreach(var f in t.Faces)for(int i=0;i<f.Vertices.Count;i++){int a=f.Vertices[i],b=f.Vertices[(i+1)%f.Vertices.Count];if(seen.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(t.Positions[a],t.Positions[b])))result.Add((a,b));}
        return result;
    }
    static StudioModelDocument.Node Cube(float size)
    {
        var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polys);mesh.vertices=mesh.vertices.Select(p=>p*size).ToArray();mesh.RecalculateBounds();
        mesh.colors=Enumerable.Repeat(new Color(.3f,.5f,.7f,1),mesh.vertexCount).ToArray();
        for(int c=1;c<8;c++)
        {
            if(c%3==0)mesh.SetUVs(c,Enumerable.Repeat(new Vector4(c,2,3,4),mesh.vertexCount).ToList());
            else if(c%3==1)mesh.SetUVs(c,Enumerable.Repeat(new Vector3(c,2,3),mesh.vertexCount).ToList());
            else mesh.SetUVs(c,Enumerable.Repeat(new Vector2(c,2),mesh.vertexCount).ToList());
        }
        return new StudioModelDocument.Node{name="Meeting corner",mesh=mesh,cutPolygons=polys,cutTriangles=mesh.triangles,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial}};
    }
    static StudioModelDocument.Node First(float size,float width,StudioMeshCuts.BevelJunction style)
    {
        var n=Cube(size);var t=new StudioMeshCuts(n);float half=size*.5f;
        var selected=Edges(t).Where(e=>t.Positions[e.a].y==half&&t.Positions[e.b].y==half&&((t.Positions[e.a].x==half&&t.Positions[e.b].x==half)||(t.Positions[e.a].z==-half&&t.Positions[e.b].z==-half))).ToArray();
        Check(selected.Length==2,"First operation must select exactly the two top edges");
        t.BevelEdges(n,selected,new StudioMeshCuts.BevelOptions{Width=width,Subdivisions=0,Tension=1,Junction=style});return n;
    }
    static (int a,int b) Vertical(StudioMeshCuts t,float size)
    {
        float half=size*.5f;var edges=Edges(t).Where(e=>t.Positions[e.a].x==half&&t.Positions[e.b].x==half&&t.Positions[e.a].z==-half&&t.Positions[e.b].z==-half).ToArray();
        Check(edges.Length==1,"Second operation must select only the remaining vertical edge");return edges[0];
    }
    static void Valid(StudioModelDocument.Node n)
    {
        var t=new StudioMeshCuts(n);var uses=new Dictionary<(Vector3,Vector3),(int count,Vector3 start)>();double volume=0;
        foreach(var face in t.Faces)
        {
            Check(face.Vertices.Count>=3,"Collapsed authored face");var origin=t.Positions[face.Vertices[0]];Vector3 area=Vector3.zero;
            for(int i=1;i+1<face.Vertices.Count;i++){var a=t.Positions[face.Vertices[i]];var b=t.Positions[face.Vertices[i+1]];area+=Vector3.Cross(a-origin,b-origin);volume+=Vector3.Dot(origin,Vector3.Cross(a,b))/6d;}
            Check(area.sqrMagnitude>0,"Zero-area face");
            for(int i=0;i<face.Vertices.Count;i++)
            {
                var a=t.Positions[face.Vertices[i]];var b=t.Positions[face.Vertices[(i+1)%face.Vertices.Count]];Check(a!=b,"Zero-length boundary edge");
                var key=StudioMeshCuts.NormalEdgePositionKeyForBevel(a,b);uses.TryGetValue(key,out var use);Check(use.count==0||use.start.Equals(b),"Inconsistent winding");uses[key]=(use.count+1,a);
            }
        }
        Check(uses.Values.All(u=>u.count==2),"Open or non-manifold contact");Check(volume>0,"Inverted/empty result");Check(t.Positions.Distinct().Count()-uses.Count+t.Faces.Count==2,"Unexpected topology at contact");
        Check(n.mesh.triangles.All(i=>i>=0&&i<n.mesh.vertexCount),"Invalid render index");
    }
    static void Matrix()
    {
        foreach(float size in new[]{.01f,.125f,1f,10f,100f})foreach(float fraction in new[]{.1f,.2f})foreach(var style in new[]{StudioMeshCuts.BevelJunction.Uniform,StudioMeshCuts.BevelJunction.Patch})
        {
            stage="size="+size+" fraction="+fraction+" style="+style;float first= size*fraction;var n=First(size,first,style);
            try
            {
                var expected=new Vector3(size*.5f-first,size*.5f,-size*.5f+first);
                var originalContact=n.mesh.vertices.Distinct().OrderBy(p=>(p-expected).sqrMagnitude).First();
                var protectedMesh=Object.Instantiate(n.mesh);var protectedPoints=protectedMesh.vertices;
                foreach(float multiplier in new[]{.9f,1f,2f,100f})
                {
                    var mesh=Object.Instantiate(protectedMesh);var part=new StudioModelDocument.Node{mesh=mesh,cutPolygons=n.cutPolygons,cutTriangles=n.cutTriangles,materials=n.materials};
                    try
                    {
                        var t=new StudioMeshCuts(part);var result=t.BevelEdges(part,new[]{Vertical(t,size)},new StudioMeshCuts.BevelOptions{Width=first*2*multiplier,Subdivisions=0,Junction=style});
                        Valid(part);var unique=part.mesh.vertices.Distinct().ToArray();
                        Check(unique.Length==(multiplier<1?14:13),"Meeting points did not become one geometric vertex at the event");
                        Check(unique.Contains(originalContact),"Contact moved the stationary boundary vertex");
                        if(multiplier>=1)Check(Mathf.Abs(result.AppliedWidth-first*2)<size*2e-6f,"Wrong clamped event distance");
                        for(int c=0;c<8;c++)Check(part.mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord0+c)==protectedMesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord0+c),"UV dimension lost");
                        Check(part.mesh.colors.All(color=>Mathf.Abs(color.r-.3f)<1e-6f&&Mathf.Abs(color.g-.5f)<1e-6f&&Mathf.Abs(color.b-.7f)<1e-6f&&Mathf.Abs(color.a-1)<1e-6f),"Colors changed");
                    }
                    finally{Object.DestroyImmediate(mesh);}
                }
                Check(protectedMesh.vertices.SequenceEqual(protectedPoints),"Captured source changed");Object.DestroyImmediate(protectedMesh);
            }
            finally{Object.DestroyImmediate(n.mesh);}
        }
    }
    static void SelectVertical()
    {
        editor.Components.Clear();var points=editor.ActiveMesh.vertices;var edges=(List<(int a,int b)>)Call("Edges");
        var seen=new HashSet<(Vector3,Vector3)>();for(int i=0;i<edges.Count;i++){var a=points[edges[i].a];var b=points[edges[i].b];if(a.x==.5f&&b.x==.5f&&a.z==-.5f&&b.z==-.5f&&seen.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(a,b)))editor.Components.Add(i);}
        Check(editor.Components.Count==1,"Live selection mismatch");
    }
    static void Preview(float width){ToolChannelPane.SetFloatDefault("Bevel Edges","Width",width);Call("EvaluateLiveTool",kind);Valid(doc.nodes[0]);}
    static void Capture(string name){capture=true;captureName=name;captureDeadline=EditorApplication.timeSinceStartup+15;view.Repaint();}
    public static void Run()=>EditorApplication.delayCall+=Start;
    static void Start()
    {
        try
        {
            if(!Application.dataPath.Contains("StudioControlsValidation"))throw new Exception("Disposable project required");
            Matrix();stage="shared clipping regression";var regression=StudioBevelSubdivisionValidation.Validate();Check(regression.StartsWith("PASS"),regression);
            doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(First(1,.2f,StudioMeshCuts.BevelJunction.Uniform));source=doc.nodes[0].mesh;sourceVertices=source.vertices;sourcePolygons=doc.nodes[0].cutPolygons.ToArray();
            AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(host,new object[]{true});typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});editor=host.ToolbarModelEditor;view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);
            editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);SelectVertical();Call("EstablishSelectionBaseline");kind=Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",BindingFlags.NonPublic),"Bevel");
            ToolChannelPane.SetFloatDefault("Bevel Edges","Subdivisions",0);ToolChannelPane.SetFloatDefault("Bevel Edges","EdgeFlow",0);
            view.position=new Rect(100,100,1000,750);view.LookAtDirect(Vector3.zero,Quaternion.Euler(22,-40,0),1.25f);renderMaterial=new Material(Shader.Find("MashBox/Studio/Preview"));renderMaterial.SetColor("_Color",new Color(.65f,.65f,.65f,1));renderMaterial.SetFloat("_Cull",0);renderMaterial.SetFloat("_CameraLighting",1);SceneView.duringSceneGui+=Draw;
            void Step(string name,Action action)=>steps.Enqueue((name,action));
            Step("below contact preview",()=>{Preview(.2f);Check(editor.ActiveMesh.vertices.Distinct().Count()==14,"Premature weld");Capture("bevel-meeting-live-below.png");});
            Step("contact preview",()=>{Preview(1);Check(editor.ActiveMesh.vertices.Distinct().Count()==13,"Live contact not welded");terminalPoints=editor.ActiveMesh.vertices;terminalTriangles=editor.ActiveMesh.triangles;Capture("bevel-meeting-live-contact.png");});
            Step("reverse width",()=>{Preview(.2f);Check(editor.ActiveMesh.vertices.Distinct().Count()==14,"Pulling back did not restore the captured topology");});
            Step("cancel",()=>{Call("CancelLiveTool",false);Check(editor.ActiveMesh.vertices.SequenceEqual(sourceVertices)&&doc.nodes[0].cutPolygons.SequenceEqual(sourcePolygons),"Cancel did not restore source");});
            Step("apply contact",()=>{SelectVertical();Preview(1);history=Undo.RetainedActions;Call("CommitLiveTool",false);Check(Undo.RetainedActions==history+1,"Contact apply not one history action");Check(editor.ActiveMesh.vertices.Distinct().Count()==13,"Apply did not retain contact");});
            Step("Undo contact",()=>Undo.PerformUndo());
            Step("verify Undo",()=>{Check(editor.ActiveMesh.vertices.SequenceEqual(sourceVertices)&&doc.nodes[0].cutPolygons.SequenceEqual(sourcePolygons),"Undo did not restore source");});
            Step("Redo contact",()=>Undo.PerformRedo());
            Step("verify Redo",()=>{Check(editor.ActiveMesh.vertices.SequenceEqual(terminalPoints)&&editor.ActiveMesh.triangles.SequenceEqual(terminalTriangles),"Redo did not restore terminal geometry");Valid(doc.nodes[0]);Check(editor.Transforms[0].GetComponent<MeshFilter>().sharedMesh==editor.ActiveMesh,"Renderer bound to stale mesh");Capture("bevel-meeting-live-redo.png");});
            next=EditorApplication.timeSinceStartup+.5;EditorApplication.update+=Pump;
        }
        catch(Exception e){Finish(stage+"\n"+e);}
    }
    static void Pump()
    {
        if(capture){if(EditorApplication.timeSinceStartup>captureDeadline)Finish("Render timeout");else view.Repaint();return;}
        if(EditorApplication.timeSinceStartup<next)return;
        try{if(steps.Count==0){Finish("PASS "+checks+" exact contact, seam/attribute/topology, reversible live preview, native Undo/Redo/render and shared clipping regressions");return;}UnityEditor.Undo.FlushUndoRecordObjects();var step=steps.Dequeue();stage=step.Item1;step.Item2();UnityEditor.Undo.FlushUndoRecordObjects();next=EditorApplication.timeSinceStartup+.25;}
        catch(Exception e){Finish(stage+"\n"+e);}
    }
    static void Draw(SceneView scene)
    {
        if(!capture||scene!=view||Event.current.type!=EventType.Repaint)return;capture=false;
        var rt=new RenderTexture(1000,750,24);rt.Create();var active=RenderTexture.active;var target=view.camera.targetTexture;var rect=view.camera.pixelRect;GL.PushMatrix();
        try
        {
            view.camera.targetTexture=rt;view.camera.pixelRect=new Rect(0,0,1000,750);Handles.SetCamera(new Rect(0,0,1000,750),view.camera);RenderTexture.active=rt;GL.Clear(true,true,new Color(.22f,.22f,.22f));GL.LoadProjectionMatrix(view.camera.projectionMatrix);GL.modelview=view.camera.worldToCameraMatrix;
            renderMaterial.SetPass(0);Graphics.DrawMeshNow(editor.Transforms[0].GetComponent<MeshFilter>().sharedMesh,editor.Transforms[0].localToWorldMatrix);Call("DrawVisibleObjectWireframes",view.camera);Call("DrawComponentBatches",view);
            var texture=new Texture2D(1000,750,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,1000,750),0,0);texture.Apply();var pixels=texture.GetPixels32();Check(pixels.Count(p=>p.g>p.r*1.4&&p.g>p.b*.9&&p.g>70)>50,"No native authored wire pixels");Check(pixels.Count(p=>p.r>35&&Math.Abs(p.r-p.g)<5&&Math.Abs(p.r-p.b)<5)>1000,"No shaded geometry");File.WriteAllBytes(Out(captureName),texture.EncodeToPNG());Object.DestroyImmediate(texture);
        }
        catch(Exception e){Finish(stage+"\n"+e);}
        finally{GL.PopMatrix();view.camera.targetTexture=target;view.camera.pixelRect=rect;RenderTexture.active=active;Object.DestroyImmediate(rt);}
    }
    static void Finish(string result)
    {
        SceneView.duringSceneGui-=Draw;EditorApplication.update-=Pump;File.WriteAllText(Out("bevel-meeting-result.txt"),result);EditorApplication.delayCall+=()=>{if(view)view.Close();if(host)Object.DestroyImmediate(host);if(renderMaterial)Object.DestroyImmediate(renderMaterial);EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};
    }
}
#endif

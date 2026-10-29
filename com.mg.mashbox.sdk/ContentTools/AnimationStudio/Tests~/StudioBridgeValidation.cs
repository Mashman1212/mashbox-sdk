#if MASHBOX_STUDIO_TESTS
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
using Undo=MashBoxSDK.AnimationStudio.StudioUndo;

public static class StudioBridgeValidation
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks;static string stage;static Queue<(string,Action)> steps=new Queue<(string,Action)>();static double next;
    static AnimationStudioWindow host;static MashBoxStudioViewport view;static StudioModelEditor editor;static StudioModelDocument doc;
    static Vector3[] originalPoints;static int[] originalTriangles,selected;static int history;static Material material;
    static bool capture;static string captureName;static Color32[] before;static double deadline;
    static void Check(bool value,string message){if(!value)throw new Exception(stage+": "+message);checks++;}
    static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length&&m.GetParameters().Select((p,i)=>args[i]==null||p.ParameterType.IsInstanceOfType(args[i])).All(v=>v)).Invoke(editor,args);
    static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
    static StudioModelDocument.Node Build(List<Vector3> points,List<int[]> faces)
    {
        var mesh=new Mesh{indexFormat=points.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.SetVertices(points);mesh.subMeshCount=2;
        var polys=new List<int>();var indices=new[]{new List<int>(),new List<int>()};
        for(int f=0;f<faces.Count;f++)
        {
            int sub=f%2;polys.Add(sub);polys.Add(faces[f].Length);polys.AddRange(faces[f]);
            for(int i=1;i+1<faces[f].Length;i++)indices[sub].AddRange(new[]{faces[f][0],faces[f][i],faces[f][i+1]});
        }
        for(int sub=0;sub<2;sub++)mesh.SetTriangles(indices[sub],sub);mesh.RecalculateNormals();mesh.RecalculateBounds();
        mesh.tangents=Enumerable.Repeat(new Vector4(1,0,0,1),points.Count).ToArray();mesh.colors=points.Select((p,i)=>new Color(i/(float)Math.Max(1,points.Count),.4f,.8f,1)).ToArray();
        for(int c=0;c<8;c++)
        {
            if(c%3==0)mesh.SetUVs(c,points.Select((p,i)=>new Vector4(i,c,3,4)).ToList());
            else if(c%3==1)mesh.SetUVs(c,points.Select((p,i)=>new Vector3(i,c,3)).ToList());
            else mesh.SetUVs(c,points.Select((p,i)=>new Vector2(i,c)).ToList());
        }
        return new StudioModelDocument.Node{name="Bridge source",mesh=mesh,cutPolygons=polys.ToArray(),cutTriangles=mesh.triangles,normalGroups=Enumerable.Range(0,points.Count).ToArray(),materials=new[]{StudioMeshMaterialPreview.DefaultMaterial,StudioMeshMaterialPreview.DefaultMaterial}};
    }
    static StudioModelDocument.Node Panels(int count,int pairs=1,float scale=1)
    {
        var points=new List<Vector3>();var faces=new List<int[]>();
        for(int pair=0;pair<pairs;pair++)for(int side=0;side<2;side++)
        {
            int start=points.Count;float x=pair*5+(side==0?-1:2);
            for(int i=0;i<=count;i++){points.Add(new Vector3(x,i,0)*scale);points.Add(new Vector3(x+1,i,0)*scale);}
            for(int i=0;i<count;i++)faces.Add(new[]{start+i*2,start+i*2+1,start+i*2+3,start+i*2+2});
        }
        return Build(points,faces);
    }
    static StudioModelDocument.Node Tubes(int count)
    {
        var points=new List<Vector3>();var faces=new List<int[]>();
        for(int part=0;part<2;part++)
        {
            int start=points.Count;
            for(int row=0;row<2;row++)for(int i=0;i<count;i++)
            {int angle=(i+part)%count;float a=angle*Mathf.PI*2/count;points.Add(new Vector3(Mathf.Cos(a),Mathf.Sin(a),part*3+row));}
            for(int i=0;i<count;i++)faces.Add(new[]{start+i,start+(i+1)%count,start+count+(i+1)%count,start+count+i});
        }
        return Build(points,faces);
    }
    static List<(int a,int b)> Edges(StudioMeshCuts t)
    {
        var seen=new HashSet<(Vector3,Vector3)>();var edges=new List<(int,int)>();
        foreach(var face in t.Faces)for(int i=0;i<face.Vertices.Count;i++){int a=face.Vertices[i],b=face.Vertices[(i+1)%face.Vertices.Count];if(seen.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(t.Positions[a],t.Positions[b])))edges.Add((a,b));}return edges;
    }
    static (int a,int b)[] PanelEdges(StudioModelDocument.Node n,int pair=0,float scale=1)
    {
        var top=new StudioMeshCuts(n);float a=pair*5*scale,b=(pair*5+2)*scale;
        return Edges(top).Where(e=>Mathf.Abs(top.Positions[e.a].y-top.Positions[e.b].y)>0&&((top.Positions[e.a].x==a&&top.Positions[e.b].x==a)||(top.Positions[e.a].x==b&&top.Positions[e.b].x==b))).Reverse().ToArray();
    }
    static void Verify(StudioModelDocument.Node n,int oldFaces)
    {
        var top=new StudioMeshCuts(n);var uses=new Dictionary<(Vector3,Vector3),List<(Vector3 a,Vector3 b)>>();
        foreach(var face in top.Faces)
        {
            Check(face.Vertices.Count==4,"Bridge changed editable quad boundaries");
            for(int i=0;i<4;i++)
            {
                var a=top.Positions[face.Vertices[i]];var b=top.Positions[face.Vertices[(i+1)%4]];Check(!a.Equals(b),"Collapsed edge");var key=StudioMeshCuts.NormalEdgePositionKeyForBevel(a,b);
                if(!uses.TryGetValue(key,out var list))uses.Add(key,list=new List<(Vector3,Vector3)>());list.Add((a,b));
            }
        }
        foreach(var list in uses.Values){Check(list.Count<=2,"Bridge introduced non-manifold edge");if(list.Count==2)Check(list[0].a.Equals(list[1].b)&&list[0].b.Equals(list[1].a),"Bridge winding disagrees with source");}
    }
    static void Attributes(StudioModelDocument.Node n,Action bridge,int added)
    {
        var mesh=n.mesh;var points=mesh.vertices;var normals=mesh.normals;var tangents=mesh.tangents;var colors=mesh.colors;var polygons=n.cutPolygons.ToArray();var groups=n.normalGroups.ToArray();
        var uv=new List<Vector4>[8];var dims=new int[8];var indices=new int[mesh.subMeshCount][];
        for(int c=0;c<8;c++){uv[c]=new List<Vector4>();mesh.GetUVs(c,uv[c]);dims[c]=mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0+c);}
        for(int sub=0;sub<indices.Length;sub++)indices[sub]=mesh.GetTriangles(sub);
        bridge();Check(mesh.vertexCount==points.Length+added*4,"Wrong bridge vertex count");Check(n.cutPolygons.Take(polygons.Length).SequenceEqual(polygons),"Source face metadata changed");
        Check(mesh.vertices.Take(points.Length).SequenceEqual(points)&&mesh.normals.Take(points.Length).SequenceEqual(normals)&&mesh.tangents.Take(points.Length).SequenceEqual(tangents)&&mesh.colors.Take(points.Length).SequenceEqual(colors),"Source vertex attributes changed");
        Check(n.normalGroups.Take(groups.Length).SequenceEqual(groups),"Source normal groups changed");
        for(int sub=0;sub<indices.Length;sub++)Check(mesh.GetTriangles(sub).Take(indices[sub].Length).SequenceEqual(indices[sub]),"Original render triangulation changed");
        for(int c=0;c<8;c++){var values=new List<Vector4>();mesh.GetUVs(c,values);Check(mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0+c)==dims[c],"UV dimension changed");Check(values.Take(points.Length).SequenceEqual(uv[c]),"Original UV values changed");}
        Check(n.normalGroups.Length==mesh.vertexCount,"Normal groups not extended");
    }
    static void Reject(StudioModelDocument.Node n,(int,int)[] edges,string part)
    {
        var points=n.mesh.vertices;var tris=n.mesh.triangles;var polys=n.cutPolygons.ToArray();bool failed=false;
        try{new StudioMeshCuts(n).BridgeEdges(n,edges);}catch(InvalidOperationException e){failed=e.Message.IndexOf(part,StringComparison.OrdinalIgnoreCase)>=0;}
        Check(failed,"Invalid selection not rejected: "+part);Check(n.mesh.vertices.SequenceEqual(points)&&n.mesh.triangles.SequenceEqual(tris)&&n.cutPolygons.SequenceEqual(polys),"Rejected bridge partially mutated mesh");Object.DestroyImmediate(n.mesh);
    }
    static void Geometry()
    {
        foreach(int count in new[]{1,2,4,12})foreach(float scale in new[]{.001f,1f,100f})
        {
            stage="open chain count="+count+" scale="+scale;var n=Panels(count,1,scale);
            try{var edges=PanelEdges(n,0,scale);Attributes(n,()=>Check(new StudioMeshCuts(n).BridgeEdges(n,edges)==count,"Wrong quad count"),count);Check(new StudioMeshCuts(n).Faces.Count==count*3,"Gap not filled with one quad per edge");Verify(n,count*2);}
            finally{Object.DestroyImmediate(n.mesh);}
        }
        foreach(int count in new[]{3,4,8,16})
        {
            stage="closed loops count="+count;var n=Tubes(count);var t=new StudioMeshCuts(n);var edges=Edges(t).Where(e=>t.Positions[e.a].z==t.Positions[e.b].z&&(t.Positions[e.a].z==1||t.Positions[e.a].z==3)).ToArray();
            try{Attributes(n,()=>Check(t.BridgeEdges(n,edges)==count,"Loop count wrong"),count);Verify(n,count*2);Check(new StudioMeshCuts(n).Faces.Count==3*count,"Loop bridge missing faces");}
            finally{Object.DestroyImmediate(n.mesh);}
        }
        stage="invalid selections";
        var bad=Panels(2);var selected=PanelEdges(bad);Reject(bad,selected.Take(3).ToArray(),"same edge count");
        bad=Panels(1);var top=new StudioMeshCuts(bad);var first=top.Faces[0].Vertices;Reject(bad,new[]{(first[0],first[1]),(first[1],first[2])},"separate");
        bad=Panels(1);top=new StudioMeshCuts(bad);first=top.Faces[0].Vertices;Reject(bad,new[]{(first[0],first[1]),(first[2],first[3])},"already filled");
        bad=Panels(2);top=new StudioMeshCuts(bad);var p=top.Positions;var interior=Edges(top).First(e=>p[e.a].y==1&&p[e.b].y==1&&p[e.a].x<=0&&p[e.b].x<=0);Reject(bad,new[]{interior,PanelEdges(bad)[0]},"boundary");
        bad=Panels(2);selected=PanelEdges(bad);top=new StudioMeshCuts(bad);p=top.Positions;var extra=Edges(top).First(e=>p[e.a].x==-1&&p[e.b].x==-1);Reject(bad,selected.Concat(new[]{extra}).ToArray(),"exactly two");
        bad=Build(new List<Vector3>{new Vector3(-1,0,0),new Vector3(1,0,0),new Vector3(0,0,1),new Vector3(0,-1,0),new Vector3(0,1,0),new Vector3(0,0,-1)},new List<int[]>{new[]{0,1,2},new[]{3,4,5}});Reject(bad,new[]{(0,1),(3,4)},"touch");
        bad=Build(new List<Vector3>{Vector3.zero,Vector3.right,Vector3.up,Vector3.left,new Vector3(1,1,1),new Vector3(-1,1,1),new Vector3(-1,-1,1)},new List<int[]>{new[]{0,1,4},new[]{0,2,5},new[]{0,3,6}});Reject(bad,new[]{(0,1),(0,2),(0,3)},"branched");
        bad=Panels(1);bad.skinned=true;Reject(bad,PanelEdges(bad),"static");
    }
    static void Select(int pair)
    {
        editor.Components.Clear();var points=editor.ActiveMesh.vertices;var edges=(List<(int a,int b)>)Call("Edges");float a=pair*5,b=pair*5+2;
        for(int i=0;i<edges.Count;i++)if(points[edges[i].a].y!=points[edges[i].b].y&&((points[edges[i].a].x==a&&points[edges[i].b].x==a)||(points[edges[i].a].x==b&&points[edges[i].b].x==b)))editor.Components.Add(i);
        Check(editor.Components.Count==4,"Native bridge selection wrong");Call("EstablishSelectionBaseline");
    }
    static void Capture(string name){capture=true;captureName=name;deadline=EditorApplication.timeSinceStartup+15;view.Repaint();}
    public static void Run()=>EditorApplication.delayCall+=Start;
    static void Start()
    {
        try
        {
            if(!Application.dataPath.Contains("StudioControlsValidation"))throw new Exception("Disposable project required");StudioShortcuts.Reset();Geometry();stage="native";
            doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(Panels(2,2));doc.SetWorkspacePath(Path.Combine(StudioModelDocument.WorkspaceFolder,"bridge.workspace"));
            AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(host,new object[]{true});typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});editor=host.ToolbarModelEditor;view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);
            editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);Select(0);originalPoints=editor.ActiveMesh.vertices;originalTriangles=editor.ActiveMesh.triangles;selected=editor.Components.OrderBy(i=>i).ToArray();
            view.position=new Rect(100,100,1000,750);view.LookAtDirect(new Vector3(1,1,0),Quaternion.Euler(0,180,0),3);material=new Material(Shader.Find("MashBox/Studio/Preview"));material.SetColor("_Color",new Color(.65f,.65f,.65f,1));material.SetFloat("_Cull",0);material.SetFloat("_CameraLighting",1);SceneView.duringSceneGui+=Draw;
            void Step(string name,Action action)=>steps.Enqueue((name,action));
            Step("before",()=>Capture("bridge-before.png"));
            Step("bridge",()=>{history=Undo.RetainedActions;Call("Run",(Action)(()=>Call("BridgeSelectedEdges")),true);Check(editor.StatusText=="Ready","Native bridge failed: "+editor.StatusText);Check(new StudioMeshCuts(doc.nodes[0]).Faces.Count==10,"Native bridge not applied");Check(editor.Components.Count==0,"Consumed edge selection not cleared");Check(Undo.RetainedActions==history+1,"Bridge not one Undo action");Check(editor.Transforms[0].GetComponent<MeshFilter>().sharedMesh==editor.ActiveMesh,"Renderer binding stale");Capture("bridge-after.png");});
            Step("Undo",()=>Undo.PerformUndo());Step("verify Undo",()=>{Check(editor.ActiveMesh.vertices.SequenceEqual(originalPoints)&&editor.ActiveMesh.triangles.SequenceEqual(originalTriangles),"Undo geometry mismatch");Check(editor.Components.SetEquals(selected),"Undo selection mismatch");});
            Step("Redo",()=>Undo.PerformRedo());Step("verify Redo",()=>{Check(new StudioMeshCuts(doc.nodes[0]).Faces.Count==10,"Redo failed");Verify(doc.nodes[0],8);});
            Step("repeat",()=>{Select(1);history=Undo.RetainedActions;var evt=new Event{type=EventType.KeyDown,keyCode=KeyCode.G};Check(editor.TryExecuteModelCommand(evt,view),"G did not dispatch");Check(new StudioMeshCuts(doc.nodes[0]).Faces.Count==12,"G did not repeat Bridge on new selection");Check(Undo.RetainedActions==history+1,"Repeated Bridge not grouped");});
            Step("roundtrip",()=>{string path=Path.Combine(StudioModelDocument.WorkspaceFolder,"bridge-saved.workspace");doc.SaveAs(path);var read=StudioWorkspaceText.Read(path);Check(new StudioMeshCuts(read.nodes[0]).Faces.Count==12&&read.nodes[0].normalGroups.Length==read.nodes[0].mesh.vertexCount,"Workspace lost bridge topology/groups");editor.RefreshModelCommandCatalog();Check(StudioModelCommands.Category(StudioModelCommands.StableId("Mesh Tools/Bridge Edges"))=="Mesh Tools","Missing shortcut command");Check(!Resources.FindObjectsOfTypeAll<ToolChannelPane>().Any(),"Bridge opened a pane");});
            Step("no cross-object combine",()=>
            {
                doc.nodes.Add(Panels(1));editor.Load(doc);editor.SelectObject(0);editor.SelectObject(1,true);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);
                Call("ActivateComponentTarget",0,StudioModelEditor.ComponentMode.Edge);editor.Components.Add(0);Call("ActivateComponentTarget",1,StudioModelEditor.ComponentMode.Edge);editor.Components.Add(0);Call("EstablishSelectionBaseline");
                var triangles=doc.nodes.Select(n=>n.mesh.triangles.ToArray()).ToArray();history=Undo.RetainedActions;Call("Run",(Action)(()=>Call("BridgeSelectedEdges")),true);
                Check(editor.StatusText.Contains("one mesh"),"Cross-object operation did not warn");Check(doc.nodes.Count==2&&doc.nodes.Select((n,i)=>n.mesh.triangles.SequenceEqual(triangles[i])).All(v=>v),"Cross-object bridge combined or changed meshes");Check(Undo.RetainedActions==history,"Rejected cross-object bridge created history");
            });
            next=EditorApplication.timeSinceStartup+.5;EditorApplication.update+=Pump;
        }
        catch(Exception e){Finish(stage+"\n"+e);}
    }
    static void Pump()
    {
        if(capture){if(EditorApplication.timeSinceStartup>deadline)Finish("Render timeout");else view.Repaint();return;}if(EditorApplication.timeSinceStartup<next)return;
        try{if(steps.Count==0){Finish("PASS "+checks+" bridge chain/loop pairing, invalid-selection rejection, topology/attributes, native history/repeat/workspace and rendered gap-fill checks");return;}UnityEditor.Undo.FlushUndoRecordObjects();var step=steps.Dequeue();stage=step.Item1;step.Item2();UnityEditor.Undo.FlushUndoRecordObjects();next=EditorApplication.timeSinceStartup+.25;}
        catch(Exception e){Finish(stage+"\n"+e);}
    }
    static void Draw(SceneView scene)
    {
        if(!capture||scene!=view||Event.current.type!=EventType.Repaint)return;capture=false;var rt=new RenderTexture(1000,750,24);rt.Create();var active=RenderTexture.active;var target=view.camera.targetTexture;var rect=view.camera.pixelRect;GL.PushMatrix();
        try
        {
            view.camera.targetTexture=rt;view.camera.pixelRect=new Rect(0,0,1000,750);Handles.SetCamera(new Rect(0,0,1000,750),view.camera);RenderTexture.active=rt;GL.Clear(true,true,new Color(.22f,.22f,.22f));GL.LoadProjectionMatrix(view.camera.projectionMatrix);GL.modelview=view.camera.worldToCameraMatrix;material.SetPass(0);Graphics.DrawMeshNow(editor.Transforms[0].GetComponent<MeshFilter>().sharedMesh,editor.Transforms[0].localToWorldMatrix);Call("DrawVisibleObjectWireframes",view.camera);Call("DrawComponentBatches",view);
            var texture=new Texture2D(1000,750,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,1000,750),0,0);texture.Apply();var pixels=texture.GetPixels32();Check(pixels.Select(p=>p.r).Distinct().Count()>8,"Empty native surface capture");
            if(captureName=="bridge-before.png")before=pixels;else{Check(pixels.Zip(before,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Count(d=>d>30)>2000,"Native gap did not fill");var point=view.camera.WorldToScreenPoint(editor.Transforms[0].TransformPoint(new Vector3(1,.5f,0)));int index=Mathf.Clamp((int)point.y,0,749)*1000+Mathf.Clamp((int)point.x,0,999);Check(Math.Abs(pixels[index].r-before[index].r)+Math.Abs(pixels[index].g-before[index].g)+Math.Abs(pixels[index].b-before[index].b)>30,"New bridge interior not rendered");}
            File.WriteAllBytes(Out(captureName),texture.EncodeToPNG());Object.DestroyImmediate(texture);
        }
        catch(Exception e){Finish(stage+"\n"+e);}
        finally{GL.PopMatrix();view.camera.targetTexture=target;view.camera.pixelRect=rect;RenderTexture.active=active;Object.DestroyImmediate(rt);}
    }
    static void Finish(string result)
    {
        SceneView.duringSceneGui-=Draw;EditorApplication.update-=Pump;File.WriteAllText(Out("bridge-result.txt"),result);EditorApplication.delayCall+=()=>{if(view)view.Close();if(host)Object.DestroyImmediate(host);if(material)Object.DestroyImmediate(material);EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};
    }
}
#endif

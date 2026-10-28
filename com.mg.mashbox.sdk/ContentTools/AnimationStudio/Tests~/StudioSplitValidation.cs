#if MASHBOX_STUDIO_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;

public static class StudioSplitValidation
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    static AnimationStudioWindow host;static MashBoxStudioViewport view;static StudioModelEditor editor;static StudioModelDocument doc;
    static Queue<(string,Action)> steps=new Queue<(string,Action)>();static double next;static int checks;static string stage;
    static Matrix4x4 originalWorld; static Vector3 childWorld; static StudioModelDocument.Node sourceNode; static Vector3 sourcePosition,sourceOffset,sourceScale; static Quaternion sourceRotation,sourcePivot;
    static Mesh original;static int[] polygons;static HashSet<int> selectedFaces;static Vector3[] sourcePoints;static int retained;static Color32[] before;
    static StudioModelEditor.ComponentMode ReadMode => (StudioModelEditor.ComponentMode)typeof(StudioModelEditor).GetField("mode",F).GetValue(editor);
    static object Call(string name,params object[] args) => typeof(StudioModelEditor).GetMethod(name,F).Invoke(editor,args);
    static void Check(bool value,string message){if(!value)throw new Exception(stage+": "+message);checks++;}
    static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
    static void Action(Action action){Call("Run",action,true);Check(editor.StatusText=="Ready","Command failed: "+editor.StatusText);}
    static StudioModelDocument.Node Cube(string name)
    {
        var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polys);int count=mesh.vertexCount;
        mesh.colors=Enumerable.Range(0,count).Select(v=>new Color(v/(float)count,.3f,.8f,1)).ToArray();
        mesh.SetUVs(2,Enumerable.Range(0,count).Select(v=>new Vector3(v,.2f,.4f)).ToList());mesh.SetUVs(7,Enumerable.Range(0,count).Select(v=>new Vector4(v,.2f,.4f,.8f)).ToList());
        return new StudioModelDocument.Node{name=name,mesh=mesh,cutPolygons=polys,cutTriangles=mesh.triangles,normalGroups=Enumerable.Range(0,count).ToArray(),materials=new[]{StudioMeshMaterialPreview.DefaultMaterial},position=new Vector3(.2f,.3f,.1f),rotation=Quaternion.Euler(12,23,5),scale=new Vector3(-1,1.2f,.8f)};
    }
    static void Setup(StudioModelEditor.ComponentMode mode,bool all=false)
    {
        doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(Cube("Source"));
        if(all){var shifted=doc.nodes[0].mesh.vertices;for(int v=0;v<shifted.Length;v++)shifted[v]+=new Vector3(2,.7f,-.2f);doc.nodes[0].mesh.vertices=shifted;doc.nodes[0].mesh.RecalculateBounds();doc.nodes[0].positionOffset=new Vector3(.25f,-.15f,.3f);}
        doc.nodes.Add(new StudioModelDocument.Node{name="Preserved child",parent=0,position=Vector3.up});
        doc.SetWorkspacePath(Path.Combine(StudioModelDocument.WorkspaceFolder,"split-"+Guid.NewGuid().ToString("N")+".workspace"));
        typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});editor=host.ToolbarModelEditor;
        editor.SelectObject(0);editor.SetToolbarComponentMode(mode);
        if(all)for(int i=0;i<6;i++)editor.Components.Add(i);else editor.Components.Add(0);
        Call("EstablishSelectionBaseline");original=doc.nodes[0].mesh;sourcePoints=original.vertices;polygons=doc.nodes[0].cutPolygons.ToArray();selectedFaces=(HashSet<int>)Call("SplitSelectionFaces");
        originalWorld=editor.Transforms[0].localToWorldMatrix;childWorld=editor.Transforms[1].position;sourceNode=doc.nodes[0];sourcePosition=sourceNode.position;sourceOffset=sourceNode.positionOffset;sourceScale=sourceNode.scale;sourceRotation=sourceNode.rotation;sourcePivot=sourceNode.pivotRotation;
        Check(selectedFaces.Count>0&&(all||selectedFaces.Count<6),"Selection did not resolve a direct face patch");
        editor.SetModelDrawMode(view,DrawCameraMode.Textured);view.modelGridVisible=false;view.LookAtDirect(Vector3.zero,Quaternion.Euler(15,20,0),3);view.Repaint();
    }
    static void Split()=>Action(()=>Call("SplitSelectedComponents"));
    static void VerifyPartial()
    {
        Check(doc.nodes.Count==3,"Partial split should retain source/child and add one mesh");var remain=doc.nodes[0];var extracted=doc.nodes[2];
        Check(new StudioMeshCuts(extracted).Faces.Count==selectedFaces.Count,"Wrong faces moved");Check(new StudioMeshCuts(remain).Faces.Count==6-selectedFaces.Count,"Source still has cut faces or lost extra faces");
        Check(remain.mesh!=original&&extracted.mesh!=original&&remain.mesh!=extracted.mesh,"Split shares mutable geometry across sides");
        Check(original.vertices.SequenceEqual(sourcePoints)&&original.triangles.Length==36,"Source/shared mesh mutated");
        CheckSourcePose();Check(doc.nodes[1].parent==0,"Child parent changed");
        Check(remain.rotation==extracted.rotation&&remain.scale==extracted.scale&&remain.parent==extracted.parent,"Split changed orientation/scale/parent");
        Check(extracted.mesh.bounds.center.sqrMagnitude<1e-10f,"Extracted pivot is not centered");
        var patchPoints=new StudioMeshCuts(new StudioModelDocument.Node{mesh=original,cutPolygons=polygons,cutTriangles=original.triangles}).Faces.Where((f,id)=>selectedFaces.Contains(id)).SelectMany(f=>f.Vertices).Distinct().Select(v=>sourcePoints[v]).ToArray();
        var patchBounds=new Bounds(patchPoints[0],Vector3.zero);foreach(var point in patchPoints)patchBounds.Encapsulate(point);
        Check(Vector3.Distance(editor.Transforms[2].position,originalWorld.MultiplyPoint3x4(patchBounds.center))<1e-5f,"Pivot did not move to extracted mesh center");
        Check(new StudioMeshCuts(remain).Faces.Concat(new StudioMeshCuts(extracted).Faces).All(f=>f.Vertices.Count==4),"Authored quads lost");
        foreach(var part in new[]{remain,extracted})
        {
            Check(part.mesh.triangles.Length==new StudioMeshCuts(part).Faces.Count*6,"Render triangles inconsistent");
            Check(part.mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord2)==3&&part.mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord7)==4,"UV dimensions changed");
            var uv2=new List<Vector3>();part.mesh.GetUVs(2,uv2);var uv7=new List<Vector4>();part.mesh.GetUVs(7,uv7);
            var points=part.mesh.vertices;var colors=part.mesh.colors;var normals=part.mesh.normals;var tangents=part.mesh.tangents;
            for(int v=0;v<points.Length;v++)
            {
                int old=(int)uv2[v].x;int partIndex=doc.nodes.IndexOf(part);Check(Vector3.Distance(editor.Transforms[partIndex].TransformPoint(points[v]),originalWorld.MultiplyPoint3x4(sourcePoints[old]))<1e-5f,"Compacted vertex moved in world space");Check(uv7[v]==new Vector4(old,.2f,.4f,.8f),"UV data changed");Check(colors[v]==original.colors[old],"Vertex color changed");Check(normals[v]==original.normals[old],"Normal changed");Check(tangents[v]==original.tangents[old],"Tangent changed");Check(part.normalGroups[v]==old,"Normal group changed");
            }
            Check(part.materials.SequenceEqual(doc.nodes[0].materials),"Material assignments changed");
            int index=doc.nodes.IndexOf(part);Check(editor.Transforms[index].GetComponent<MeshFilter>().sharedMesh==part.mesh,"Renderer bound to wrong mesh");
        }
        var sourceTriangles=WorldTriangles(new[]{0},true);var splitTriangles=WorldTriangles(new[]{0,2},false);Check(sourceTriangles.SequenceEqual(splitTriangles),"Render triangle partition changed surface geometry");
        Check(editor.Active==2&&ReadMode==StudioModelEditor.ComponentMode.Object,"New object not selected in Object mode");
    }
    static string[] WorldTriangles(int[] nodes,bool old)
    {
        var result=new List<string>();foreach(int i in nodes)
        {var mesh=old?original:doc.nodes[i].mesh;var points=mesh.vertices;var tris=mesh.triangles;var matrix=editor.Transforms[i].localToWorldMatrix;
        for(int t=0;t<tris.Length;t+=3)result.Add(string.Join(";",new[]{tris[t],tris[t+1],tris[t+2]}.Select(v=>matrix.MultiplyPoint3x4(points[v]).ToString("F4"))));}
        return result.OrderBy(s=>s).ToArray();
    }
    public static void Run()=>EditorApplication.delayCall+=Start;
    static void Start()
    {
        try
        {
            if(!Application.dataPath.Contains("StudioControlsValidation"))throw new Exception("Disposable project required");
            AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(host,new object[]{true});
            view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);
            void Step(string name,Action action)=>steps.Enqueue((name,action));
            Step("Face setup",()=>Setup(StudioModelEditor.ComponentMode.Face));
            Step("Before render",()=>before=Render("split-before.png"));
            Step("Face split",()=>{retained=Undo.RetainedActions;Split();VerifyPartial();Check(Undo.RetainedActions==retained+1,"Split not one Undo action");});
            Step("After render",()=>{var after=Render("split-after.png");Check(after.Zip(before,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Count(d=>d>12)<after.Length*.01,"Partition changed rendered surfaces");});
            Step("Undo",()=>Undo.PerformUndo());
            Step("Check Undo",()=>{Check(doc.nodes.Count==2&&doc.nodes[0].mesh==original,"Undo did not restore original node/mesh");Check(doc.nodes[0].cutPolygons.SequenceEqual(polygons),"Undo lost polygons");Check(ReadMode==StudioModelEditor.ComponentMode.Face&&editor.Components.SetEquals(new[]{0}),"Undo lost component selection");});
            Step("Redo",()=>Undo.PerformRedo());Step("Check Redo",VerifyPartial);
            Step("Vertex setup",()=>Setup(StudioModelEditor.ComponentMode.Vertex));
            Step("Vertex incident patch",()=>{editor.IsolateViewportSelection();var dictionary=(System.Collections.IDictionary)typeof(StudioModelEditor).GetField("isolatedComponents",F).GetValue(editor);var mask=dictionary[doc.nodes[0]];var isolated=(HashSet<int>)mask.GetType().GetField("faces",F|BindingFlags.Public).GetValue(mask);Check(selectedFaces.SetEquals(isolated),"Vertex split differs from Isolate incident faces");Check(selectedFaces.Count==3,"Vertex selection flood-filled beyond directly attached cube faces");Split();VerifyPartial();});
            Step("Edge setup",()=>Setup(StudioModelEditor.ComponentMode.Edge));
            Step("Edge incident patch",()=>{Check(selectedFaces.Count==2,"Edge split should take its two incident cube faces");Split();VerifyPartial();});
            Step("All faces setup",()=>Setup(StudioModelEditor.ComponentMode.Face,true));
            Step("Reject all faces",()=>
            {
                int count=Undo.RetainedActions;Call("Run",(Action)(()=>Call("SplitSelectedComponents")),true);
                Check(editor.StatusText.Contains("empty mesh"),"All-face split did not warn");
                Check(doc.nodes.Count==2&&doc.nodes[0].mesh==original,"Rejected split changed source or created output");
                Check(Undo.RetainedActions==count,"Rejected split added history");CheckSourcePose();
            });
            Step("Multi and shared",Multi);
            Step("Atomic rejection and custom topology",Geometry);
            Step("Registry",()=>{editor.RefreshModelCommandCatalog();Check(StudioModelCommands.Category(StudioModelCommands.StableId("Mesh Tools/Split Selection"))=="Mesh Tools","Split missing shortcut/custom toolbar catalog");Check(!Resources.FindObjectsOfTypeAll<ToolChannelPane>().Any(),"Split opened a tool pane");});
            next=EditorApplication.timeSinceStartup+.5;EditorApplication.update+=Pump;
        }
        catch(Exception e){Finish(e.ToString());}
    }
    static void CheckSourcePose()
    {
        Check(doc.nodes[0].position==sourcePosition&&doc.nodes[0].positionOffset==sourceOffset,"Source position/offset changed");
        Check(doc.nodes[0].rotation==sourceRotation&&doc.nodes[0].scale==sourceScale&&doc.nodes[0].pivotRotation==sourcePivot,"Source rotation/scale/pivot changed");
        Check(doc.nodes[0].name=="Source"&&doc.nodes[1].parent==0,"Source identity/hierarchy changed");
        Check(Vector3.Distance(editor.Transforms[1].position,childWorld)<1e-5f,"Source child moved");
        for(int c=0;c<4;c++)Check(Vector4.Distance(editor.Transforms[0].localToWorldMatrix.GetColumn(c),originalWorld.GetColumn(c))<1e-6f,"Source world transform changed");
    }
    static void Multi()
    {
        Setup(StudioModelEditor.ComponentMode.Face);var source=doc.nodes[0];var sharedMesh=source.mesh;
        doc.nodes.Add(new StudioModelDocument.Node{name="Unselected shared",mesh=source.mesh,cutPolygons=source.cutPolygons,cutTriangles=source.cutTriangles,materials=source.materials});
        doc.nodes.Add(Cube("Other"));editor.Load(doc);editor.SelectObject(0);editor.SelectObject(3,true);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);
        Call("ActivateComponentTarget",0,StudioModelEditor.ComponentMode.Face);editor.Components.Add(0);Call("ActivateComponentTarget",3,StudioModelEditor.ComponentMode.Face);editor.Components.Add(1);Call("EstablishSelectionBaseline");
        int count=Undo.RetainedActions;Split();Check(doc.nodes.Count==6,"Multiple sources did not create one output each");Check(doc.nodes[2].mesh==sharedMesh&&sharedMesh.triangles.Length==36,"Unselected shared instance changed");Check(new StudioMeshCuts(doc.nodes[0]).Faces.Count==5&&new StudioMeshCuts(doc.nodes[3]).Faces.Count==5,"Multiple source cuts incomplete");Check(Undo.RetainedActions==count+1,"Multiple source split not grouped");
    }
    static void Geometry()
    {
        var d=ScriptableObject.CreateInstance<StudioModelDocument>();d.nodes.Add(Cube("Good"));d.nodes.Add(Cube("Skin"));d.nodes[1].skinned=true;var mesh=d.nodes[0].mesh;bool failed=false;
        try{d.SplitMeshes(new Dictionary<int,HashSet<int>>{{0,new HashSet<int>{0}},{1,new HashSet<int>{0}}});}catch(InvalidOperationException){failed=true;}
        Check(failed&&d.nodes.Count==2&&d.nodes[0].mesh==mesh,"Multi-source rejection partially changed document");
        d=ScriptableObject.CreateInstance<StudioModelDocument>();d.nodes.Add(Cube("Partial"));d.nodes.Add(Cube("All"));mesh=d.nodes[0].mesh;failed=false;
        try{d.SplitMeshes(new Dictionary<int,HashSet<int>>{{0,new HashSet<int>{0}},{1,new HashSet<int>(Enumerable.Range(0,6))}});}catch(InvalidOperationException e){failed=e.Message.Contains("empty mesh");}
        Check(failed&&d.nodes.Count==2&&d.nodes[0].mesh==mesh,"Full-face target did not atomically reject multiple-source split");
        // One true triangle plus an authored five-sided polygon, with explicit diagonals.
        mesh=new Mesh();mesh.vertices=new[]{new Vector3(0,0,0),new Vector3(2,0,0),new Vector3(3,1,0),new Vector3(1,3,0),new Vector3(0,1,0),new Vector3(4,0,0),new Vector3(5,0,0),new Vector3(4,1,0)};
        mesh.subMeshCount=2;mesh.SetTriangles(new[]{0,1,4,1,2,4,2,3,4},0);mesh.SetTriangles(new[]{5,6,7},1);
        var n=new StudioModelDocument.Node{name="N-gon",mesh=mesh,cutPolygons=new[]{0,5,0,1,2,3,4,1,3,5,6,7},cutTriangles=mesh.triangles,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial,StudioMeshMaterialPreview.CheckerMaterial}};
        d=ScriptableObject.CreateInstance<StudioModelDocument>();d.nodes.Add(n);var outputs=d.SplitMeshes(new Dictionary<int,HashSet<int>>{{0,new HashSet<int>{0}}});
        Check(new StudioMeshCuts(d.nodes[0]).Faces.Single().Vertices.Count==3&&new StudioMeshCuts(d.nodes[outputs[0]]).Faces.Single().Vertices.Count==5,"N-gon/true triangle boundaries changed");
        Check(d.nodes[outputs[0]].mesh.GetTriangles(0).SequenceEqual(new[]{0,1,4,1,2,4,2,3,4}),"Authored n-gon triangulation changed");Check(d.nodes[0].materials.SequenceEqual(n.materials)&&d.nodes[1].materials.SequenceEqual(n.materials),"Submesh material identity changed");
        string path=Path.Combine(StudioModelDocument.WorkspaceFolder,"split-roundtrip.workspace");var saved=d.SaveAs(path);var reopened=StudioWorkspaceText.Read(path);
        Check(reopened.nodes.Count==2&&new StudioMeshCuts(reopened.nodes[1]).Faces.Single().Vertices.Count==5,"Workspace round-trip lost split topology");
    }
    static Color32[] Render(string name)
    {
        var camera=view.camera;var target=camera.targetTexture;var active=RenderTexture.active;var rt=new RenderTexture(640,480,24);rt.Create();var texture=new Texture2D(640,480,TextureFormat.RGBA32,false);
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,640,480),0,0);texture.Apply();File.WriteAllBytes(Out(name),texture.EncodeToPNG());var pixels=texture.GetPixels32();Check(pixels.Select(c=>(c.r<<16)|(c.g<<8)|c.b).Distinct().Count()>8,"Rendered capture empty");return pixels;}
        finally{camera.targetTexture=target;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(rt);}
    }
    static void Pump()
    {
        if(EditorApplication.timeSinceStartup<next)return;
        try{if(steps.Count==0){Finish("PASS "+checks+" split face/edge/vertex incidence, topology/attributes, grouped native history, shared/multiple sources, empty-source cleanup, workspace and rendered surface checks");return;}UnityEditor.Undo.FlushUndoRecordObjects();var step=steps.Dequeue();stage=step.Item1;step.Item2();UnityEditor.Undo.FlushUndoRecordObjects();next=EditorApplication.timeSinceStartup+.25;}
        catch(Exception e){Finish(e.ToString());}
    }
    static void Finish(string result)
    {
        EditorApplication.update-=Pump;File.WriteAllText(Out("split-result.txt"),result);EditorApplication.delayCall+=()=>{if(view)view.Close();if(host)UnityEngine.Object.DestroyImmediate(host);EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};
    }
}
#endif

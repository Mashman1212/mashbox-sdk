#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;

public static class StudioCombineValidation
{
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    static StudioModelDocument doc; static StudioModelEditor editor;
    static AnimationStudioWindow host; static MashBoxStudioViewport view;
    static int checks; static Queue<Action> steps = new Queue<Action>(); static double next;
    static Vector3[] expected; static Vector3 first, center; static Matrix4x4 childWorld; static int retained; static ToolChannelPane pane;
    static int[] sourcePolygons; static Color32[] beforePixels;
    static string Output(string name) => Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
    static void Check(bool value,string message) { if(!value) throw new Exception(message); checks++; }
    static object Call(string name,params object[] args) => typeof(StudioModelEditor).GetMethod(name,F).Invoke(editor,args);
    public static void Run() { EditorApplication.delayCall += Start; }
    static void Start()
    {
        try
        {
            if (!Application.dataPath.Contains("StudioControlsValidation")) throw new Exception("Disposable project required");
            doc = ScriptableObject.CreateInstance<StudioModelDocument>();
            doc.SetWorkspacePath(Path.Combine(StudioModelDocument.WorkspaceFolder,"combine-validation.workspace"));
            doc.nodes.Add(new StudioModelDocument.Node { name="Consumed group",position=new Vector3(.4f,.1f,0) });
            for(int i=0;i<3;i++)
            {
                var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
                mesh.colors=Enumerable.Repeat(i==0?Color.red:Color.green,mesh.vertexCount).ToArray();
                mesh.SetUVs(3,Enumerable.Repeat(new Vector4(i+1,2,3,4),mesh.vertexCount).ToList());
                doc.nodes.Add(new StudioModelDocument.Node {name="Mesh "+i,parent=i<2?0:-1,position=new Vector3((i-1)*2,0,0),rotation=Quaternion.Euler(12,i*21,7),scale=new Vector3(i==1?-1.1f:1,1.3f,.8f),mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial}});
            }
            // A consumed mesh has an unrelated child which must survive in world space.
            doc.nodes.Add(new StudioModelDocument.Node {name="Keep child",parent=3,position=new Vector3(0,2,0)});
            AnimationStudioWindow.Open(); host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(host,new object[]{true});
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});
            editor=host.ToolbarModelEditor; view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);
            editor.SetModelDrawMode(view,DrawCameraMode.Textured);view.modelGridVisible=false;view.LookAtDirect(Vector3.zero,Quaternion.Euler(15,20,0),6);view.Repaint();
            ToolChannelPane.SetFloatDefault("Combine Meshes","KeepFirstName",1);ToolChannelPane.SetFloatDefault("Combine Meshes","CombineUVs",1);ToolChannelPane.SetFloatDefault("Combine Meshes","Pivot",0);
            editor.SelectObject(2);editor.SelectObject(1,true);editor.SelectObject(3,true);
            expected=new[]{2,1,3}.SelectMany(i=>doc.nodes[i].mesh.vertices.Select(v=>editor.Transforms[i].TransformPoint(v))).ToArray();
            childWorld=editor.Transforms[4].localToWorldMatrix;
            first=editor.Transforms[2].position; center=new[]{2,1,3}.Select(i=>editor.Transforms[i].TransformPoint(doc.nodes[i].mesh.bounds.center)).Aggregate(Vector3.zero,(a,b)=>a+b)/3;
            sourcePolygons=doc.nodes[1].cutPolygons.ToArray();
            Call("OpenCombineTool");pane=(ToolChannelPane)typeof(StudioModelEditor).GetField("combineToolPane",F).GetValue(editor);
            Check(pane,"Shared Combine pane opened");
            steps.Enqueue(()=> { CapturePane(); beforePixels=Render("combine-before.png");retained=Undo.RetainedActions; });
            steps.Enqueue(()=> { Check(pane.ExecuteCommand(false),"Apply failed"); Check(pane,"Apply closed pane"); Check(doc.nodes.Count==2,"Consumed meshes/groups left transforms behind or lost child"); Check(doc.nodes[0].name=="Keep child"&&doc.nodes[0].parent==-1,"Unrelated child not reparented"); VerifyCombined(first);Check(doc.nodes.Last().name=="Mesh 1","First selected name not used");Check(Undo.RetainedActions==retained+1,"Combine was not one Undo action"); });
            steps.Enqueue(()=> { var after=Render("combine-after.png");int difference=after.Zip(beforePixels,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Count(d=>d>12);Check(difference<after.Length*.015,"Rendered combine changed surface placement: "+difference); });
            steps.Enqueue(()=>Undo.PerformUndo());
            steps.Enqueue(()=> { Check(doc.nodes.Count==5,"Undo did not restore hierarchy");Check(doc.nodes[1].cutPolygons.SequenceEqual(sourcePolygons),"Undo lost original polygon metadata");Check(editor.Active==3,"Undo did not restore active selection"); Check(((int[])Call("OrderedObjectSelection")).SequenceEqual(new[]{2,1,3}),"Undo lost first selection order"); });
            steps.Enqueue(()=>Undo.PerformRedo());
            steps.Enqueue(()=> {Check(doc.nodes.Count==2,"Redo did not combine");VerifyCombined(first);Check(doc.nodes.Last().name=="Mesh 1","Redo lost first selected name");});
            steps.Enqueue(()=>Undo.PerformUndo());
            steps.Enqueue(()=> {ToolChannelPane.SetFloatDefault("Combine Meshes","KeepFirstName",0);ToolChannelPane.SetFloatDefault("Combine Meshes","Pivot",1);Check(pane.ExecuteCommand(true),"Execute failed");Check(!pane,"Execute kept pane open"); VerifyCombined(center);Check(doc.nodes.Last().name=="Mesh 1 Combined","Toggle off did not retain Combined naming");});
            steps.Enqueue(()=> {TestUVsAndFailure();TestLargeAndHierarchy();TestAttributesAndNativeFaces();Check(CommandExists(),"Combine not registered for shortcut/custom toolbar discovery");});
            next=EditorApplication.timeSinceStartup+1;EditorApplication.update+=Pump;
        }
        catch(Exception e){Finish(e.ToString());}
    }
    static bool CommandExists()
    {
        // Menu discovery is the standard shortcut registration path.
        editor.RefreshModelCommandCatalog();
        return StudioModelCommands.Category(StudioModelCommands.StableId("Mesh Tools/Combine Meshes…")) == "Mesh Tools";
    }
    static void VerifyCombined(Vector3 pivot)
    {
        var n=doc.nodes.Last();int i=doc.nodes.Count-1;
        Check(Vector3.Distance(editor.Transforms[i].position,pivot)<1e-4,"Wrong pivot");
        var points=n.mesh.vertices.Select(v=>editor.Transforms[i].TransformPoint(v)).ToArray();
        Check(points.Length==expected.Length&&points.Zip(expected,Vector3.Distance).All(d=>d<1e-4),"Combine moved vertices");
        Check(n.mesh.subMeshCount==1&&n.materials[0]==StudioMeshMaterialPreview.DefaultMaterial,"Material slots not combined");
        Check(new StudioMeshCuts(n).Faces.Count==18&&new StudioMeshCuts(n).Faces.All(f=>f.Vertices.Count==4),"Authored cube quads lost");
        Check(n.mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord3)==4,"UV dimension lost");
        Check(n.mesh.colors.Length==n.mesh.vertexCount,"Vertex colors lost");
        Check(editor.Transforms[i].GetComponent<MeshFilter>().sharedMesh==n.mesh,"Renderer bound to stale mesh");
        Check(editor.Active==i,"Result not selected");
        Check(n.mesh.name==n.name,"Mesh and object names differ");
        var child=editor.Transforms[0].localToWorldMatrix;for(int row=0;row<4;row++)for(int col=0;col<4;col++)Check(Mathf.Abs(child[row,col]-childWorld[row,col])<1e-4,"Unrelated child moved");
    }
    static Color32[] Render(string name)
    {
        var camera=view.camera;var previous=camera.targetTexture;var active=RenderTexture.active;var rt=new RenderTexture(640,480,24);rt.Create();
        var texture=new Texture2D(640,480,TextureFormat.RGBA32,false);
        try {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,640,480),0,0);texture.Apply();File.WriteAllBytes(Output(name),texture.EncodeToPNG());var pixels=texture.GetPixels32();Check(pixels.Select(c=>(c.r<<16)|(c.g<<8)|c.b).Distinct().Count()>8,"Native surface capture was empty");return pixels;}
        finally {camera.targetTexture=previous;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(rt);}
    }
    static StudioModelDocument Pair(int uvCount=1,int vertexCount=3)
    {
        var d=ScriptableObject.CreateInstance<StudioModelDocument>();
        for(int i=0;i<2;i++) {var m=new Mesh{indexFormat=vertexCount>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16};m.vertices=Enumerable.Range(0,vertexCount).Select(v=>new Vector3(v%3,v/3,0)).ToArray();m.triangles=new[]{0,1,2};for(int c=0;c<uvCount;c++)m.SetUVs(c,Enumerable.Repeat(new Vector2(i+c+1,2),vertexCount).ToList());d.nodes.Add(new StudioModelDocument.Node{name="Part",mesh=m,position=Vector3.right*i});}
        return d;
    }
    static void TestUVsAndFailure()
    {
        var d=Pair(2);var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var t=d.Build(scene);
        int result=d.CombineMeshes(new[]{0,1},t,false,false);var m=d.nodes[result].mesh;
        Check(d.nodes.Count==1,"Pair left source nodes");
        for(int c=0;c<4;c++){var uv=new List<Vector2>();m.GetUVs(c,uv);Check(uv.Count==6,"Separate channel missing");Check(uv[c<2?0:3].x==(c<2?c+1:c-2+2),"Separate channel value changed");Check(uv[c<2?3:0]==Vector2.zero,"Separate channel padding not zero");}
        Check(new StudioMeshCuts(d.nodes[0]).Faces.All(f=>f.Vertices.Count==3),"True triangles converted to quads");
        UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        d=Pair(5);scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();t=d.Build(scene);bool rejected=false;
        try{d.CombineMeshes(new[]{0,1},t,false,false);}catch(InvalidOperationException){rejected=true;}
        Check(rejected&&d.nodes.Count==2,"UV overflow was not atomic");
        d.nodes[0].skinned=true;Check(!d.CanCombineMeshes(new[]{0,1},out _),"Skin accepted");d.nodes[0].skinned=false;
        d.nodes[0].scale=Vector3.zero;d.ApplyTransform(0,t[0]);rejected=false;
        try{d.CombineMeshes(new[]{0,1},t,true,false);}catch(InvalidOperationException){rejected=true;}
        Check(rejected&&d.nodes.Count==2,"Zero scale was not rejected atomically");
        UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
    }
    static void TestLargeAndHierarchy()
    {
        var d=Pair(1,33000);var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var t=d.Build(scene);
        d.CombineMeshes(new[]{0,1},t,true,true);Check(d.nodes[0].mesh.indexFormat==UnityEngine.Rendering.IndexFormat.UInt32,"Large combine did not promote indices");
        UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
    }
    static void CapturePane()
    {
        var parent=typeof(EditorWindow).GetField("m_Parent",F).GetValue(pane);var rect=new Rect(Vector2.zero,pane.position.size);
        var rt=new RenderTexture((int)rect.width,(int)rect.height,0);rt.Create();var old=RenderTexture.active;
        try {parent.GetType().GetMethod("GrabPixels",F).Invoke(parent,new object[]{rt,rect});RenderTexture.active=rt;var texture=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);texture.Apply();Check(texture.GetPixels32().Select(c=>c.r).Distinct().Count()>20,"Pane did not render");File.WriteAllBytes(Output("combine-options.png"),texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);}
        finally {RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(rt);}
    }
    static void TestAttributesAndNativeFaces()
    {
        var d=Pair();var firstMesh=d.nodes[0].mesh;
        firstMesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};firstMesh.RecalculateNormals();firstMesh.tangents=Enumerable.Repeat(new Vector4(1,0,0,1),3).ToArray();
        d.nodes[0].normalGroups=new[]{0,1,2};d.nodes[1].normalGroups=new[]{0,1,2};
        var second=d.nodes[1].mesh;second.vertices=new[]{Vector3.zero,Vector3.right,Vector3.one,Vector3.up};second.SetIndices(new[]{0,1,2,3},MeshTopology.Quads,0);second.SetUVs(0,Enumerable.Repeat(new Vector3(4,5,6),4).ToList());
        var material=new Material(Shader.Find("Hidden/Internal-Colored"));d.nodes[1].materials=new[]{material};
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var t=d.Build(scene);
        d.CombineMeshes(new[]{0,1},t,true,false);var n=d.nodes[0];
        Check(n.mesh.vertexCount==7,"Native quad vertex count changed");
        Check(new StudioMeshCuts(n).Faces.Select(f=>f.Vertices.Count).SequenceEqual(new[]{3,4}),"Native quad/true triangle boundaries lost");
        Check(n.mesh.subMeshCount==2&&n.materials.Contains(material),"Distinct materials lost");
        Check(n.normalGroups.Take(3).Intersect(n.normalGroups.Skip(3)).Count()==0,"Smoothing namespaces merged");
        Check(n.mesh.normals.Length==7&&n.mesh.normals.Skip(3).All(v=>v.sqrMagnitude>.5f),"Missing source normals not generated");
        Check(n.mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord0)==3,"Mixed UV dimensions truncated");
        Check(firstMesh.vertexCount==3&&firstMesh.triangles.SequenceEqual(new[]{0,1,2}),"Shared source mesh mutated");
        UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        // Group selection includes descendants without duplicate mesh inputs.
        d=Pair();d.nodes.Add(new StudioModelDocument.Node{name="Group"});d.nodes[0].parent=2;d.nodes[1].parent=2;
        Check(d.CombineInputs(new[]{2,0}).SequenceEqual(new[]{0,1}),"Group inputs duplicated or omitted descendants");
    }
    static void Pump()
    {
        if(EditorApplication.timeSinceStartup<next)return;
        try{if(steps.Count==0){Finish("PASS "+checks+" combine topology, UVs, hierarchy cleanup, first/average pivot, native Undo/Redo, pane lifecycle and rendered surface checks");return;}UnityEditor.Undo.FlushUndoRecordObjects();steps.Dequeue()();UnityEditor.Undo.FlushUndoRecordObjects();next=EditorApplication.timeSinceStartup+.3;}
        catch(Exception e){Finish(e.ToString());}
    }
    static void Finish(string result)
    {
        EditorApplication.update-=Pump;File.WriteAllText(Output("combine-result.txt"),result);
        EditorApplication.delayCall+=()=>{if(view)view.Close();if(host)UnityEngine.Object.DestroyImmediate(host);EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};
    }
}
#endif

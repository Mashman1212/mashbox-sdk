using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Undo=MashBoxSDK.AnimationStudio.StudioUndo;
using Object=UnityEngine.Object;

internal static class StudioModifierValidation
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
    private static int checks;
    private static object Call(StudioModelEditor editor,string name,params object[] args)=>typeof(StudioModelEditor).GetMethods(Flags).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(editor,args);
    private static void Check(bool valid,string message){if(!valid)throw new Exception(message);checks++;}
    [InitializeOnLoadMethod] private static void Queue()
    {
        if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets",StringComparison.OrdinalIgnoreCase)||!File.Exists(Out("modifier-request")))return;
        EditorApplication.update+=RunWhenReady;
    }
    private static void RunWhenReady(){if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;EditorApplication.update-=RunWhenReady;Run();}
    private static void Tool(StudioModelEditor editor,string kind)
    {
        Call(editor,"ActivateComponentTarget",0,kind=="Bevel"?StudioModelEditor.ComponentMode.Edge:StudioModelEditor.ComponentMode.Face);
        editor.Components.Clear();editor.Components.Add(0);
        var live=Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",BindingFlags.NonPublic),kind);
        Call(editor,"EvaluateLiveTool",live);Call(editor,"CommitLiveTool",true);
    }
    [MenuItem("Tools/Run Modifier Check")] public static void Run()
    {
        if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Disposable project required.");
        if(File.Exists(Out("modifier-request")))File.Delete(Out("modifier-request")); StudioModelDocument doc=null;StudioModelEditor editor=null;string stage="setup";
        try
        {
            doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
            doc.nodes.Add(new StudioModelDocument.Node{name="Modifier stack check",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,geometryVertices=StudioSubdivisionData.Primitive(mesh)});
            editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);var original=mesh.vertices;
            stage="live bevel";Tool(editor,"Bevel");Check(doc.nodes[0].modifiers.operations.Single().kind=="Bevel","Committed bevel was not recorded.");
            string bevelId=doc.nodes[0].modifiers.operations[0].id;
            stage="live extrusion";Tool(editor,"Extrude");Check(doc.nodes[0].modifiers.operations.Count==2,"Extrude history missing.");
            var beforeWidth=mesh.vertices;
            stage="edit earlier bevel";Call(editor,"ChangeModifier",doc.nodes[0],bevelId,(Action<StudioModifierOperation>)(o=>o.parameters.x*=.75f),false);
            Check(doc.nodes[0].modifiers.operations.All(o=>string.IsNullOrEmpty(o.failure)),"Earlier bevel width edit broke compatible downstream extrusion.");
            Check(!beforeWidth.SequenceEqual(mesh.vertices),"Earlier bevel edit did not alter final geometry.");
            var replay=StudioModifierEvaluator.Evaluate(doc.nodes[0].modifiers.Copy());try{Check(replay.mesh.vertices.SequenceEqual(mesh.vertices),"Replay disagrees with published result.");}finally{Object.DestroyImmediate(replay.mesh);}
            stage="subdivision";Call(editor,"AddSubdivisionModifier");var n=doc.nodes[0];
            Check(n.modifiers.operations.Select(o=>o.kind).SequenceEqual(new[]{"Bevel","Extrude","Subdivision"}),"Creation order changed.");
            Check(n.modifiers.HasDisplay,"Terminal subdivision has no GPU input.");var subdivided=mesh.vertexCount;
            Check(subdivided>beforeWidth.Length,"Subdivision did not expose denser editable geometry.");
            stage="GPU renderer";Call(editor,"UpdateSubdivision");var filter=editor.Transforms[0].GetComponent<MeshFilter>();
            Check(filter.sharedMesh!=mesh,"Terminal subdivision is not GPU rendered: "+editor.StatusText);
            var cameraObject=new GameObject("Modifier GPU check");var camera=cameraObject.AddComponent<Camera>();camera.scene=editor.Scene;camera.enabled=false;
            camera.transform.position=Vector3.one*2;camera.transform.LookAt(Vector3.zero);camera.fieldOfView=35;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.cullingMask=1<<30;
            filter.gameObject.layer=30;var material=new Material(Shader.Find("Unlit/Color"));material.color=Color.white;filter.GetComponent<MeshRenderer>().sharedMaterial=material;
            var target=new RenderTexture(192,192,24);target.Create();camera.targetTexture=target;var previous=RenderTexture.active;
            Color32[] Pixels(){var texture=new Texture2D(192,192,TextureFormat.RGBA32,false);try{camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,192,192),0,0);texture.Apply();return texture.GetPixels32();}finally{Object.DestroyImmediate(texture);}}
            try{var gpu=filter.sharedMesh;var a=Pixels();filter.sharedMesh=mesh;var b=Pixels();filter.sharedMesh=gpu;Check(a.Any(p=>p.r>200)&&a.Zip(b,(x,y)=>x.Equals(y)?0:1).Sum()<12,"Strict-order GPU surface differs from editable result pixels.");}
            finally{RenderTexture.active=previous;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(material);}
            stage="tool after subdivision";Tool(editor,"Extrude");n=doc.nodes[0];
            Check(n.modifiers.operations.Last().inputCount==subdivided,"Operation after subdivision used the old low-poly cage.");
            Check(!n.modifiers.HasDisplay&&mesh.vertexCount>subdivided,"Later operation did not own the final result.");
            var expected=StudioModifierEvaluator.Evaluate(n.modifiers.Copy());try{Check(expected.mesh.vertices.SequenceEqual(mesh.vertices),"Recorded operations fail to reproduce committed live result.");}finally{Object.DestroyImmediate(expected.mesh);}
            stage="workspace";string saved=Path.GetFullPath(Path.Combine(Application.dataPath,"../MashBoxStudio/Workspaces/modifier-validation-"+Guid.NewGuid().ToString("N")+".workspace"));Directory.CreateDirectory(Path.GetDirectoryName(saved));StudioWorkspaceText.Write(doc,saved);var restored=StudioWorkspaceText.Read(saved);
            try{Check(restored.nodes[0].modifiers.operations.Count==4,"Workspace lost operation records.");var result=StudioModifierEvaluator.Evaluate(restored.nodes[0].modifiers);try{Check(result.mesh.vertices.SequenceEqual(mesh.vertices),"Workspace history replay changed geometry.");}finally{Object.DestroyImmediate(result.mesh);}}
            finally{foreach(var owned in restored.nodes.Select(x=>x.mesh).Distinct())Object.DestroyImmediate(owned);Object.DestroyImmediate(restored);}
            stage="direct component transform";Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(mesh,"Transform mesh components");var moved=mesh.vertices;moved[0]+=Vector3.up*.01f;mesh.vertices=moved;mesh.RecalculateBounds();Call(editor,"FinishPendingModifiers");
            Check(doc.nodes[0].modifiers.operations.Last().kind=="Transform","Component edit was not recorded as offsets.");
            stage="delete and repair";Call(editor,"ChangeModifier",doc.nodes[0],bevelId,null,true);
            Check(doc.nodes[0].modifiers.operations.Any(o=>!string.IsNullOrEmpty(o.failure)),"Deleting upstream topology did not report broken downstream selection.");
            UnityEditor.Undo.FlushUndoRecordObjects();Undo.PerformUndo();Check(doc.nodes[0].modifiers.operations[0].id==bevelId,"Delete modifier Undo lost history.");
            stage="clear history";var visible=mesh.vertices;Call(editor,"ClearModifierHistory");Check(doc.nodes[0].modifiers?.HasHistory!=true&&mesh.vertices.SequenceEqual(visible),"Clear History changed appearance or retained records.");
            UnityEditor.Undo.FlushUndoRecordObjects();Undo.PerformUndo();Check(doc.nodes[0].modifiers?.HasHistory==true,"Clear History Undo did not restore records.");Undo.PerformRedo();Check(doc.nodes[0].modifiers?.HasHistory!=true,"Clear History Redo failed.");
            Undo.PerformUndo();editor.SelectObject(0);
            var panel=ScriptableObject.CreateInstance<StudioModifierValidationPanel>();panel.Editor=editor;panel.Document=doc;panel.titleContent=new GUIContent("Modifier stack check");panel.position=new Rect(200,180,380,740);panel.Show();panel.Repaint();editor=null;doc=null;
            File.WriteAllText(Out("modifier-result.txt"),$"PASS {checks} focused checks. {SystemInfo.graphicsDeviceType}. Live Bevel/Extrude recording, earlier-width replay, strict Subdivision ordering, actual GPU renderer pixels, high-poly editing after Subdivision, workspace history replay, component offsets, broken dependency reporting, delete/clear Undo/Redo. Disposable editor and inspection panel left open.");
        }
        catch(Exception e){File.WriteAllText(Out("modifier-result.txt"),"FAIL "+stage+"\n"+e);}
        finally{editor?.Dispose();if(doc){foreach(var mesh in doc.nodes.Select(n=>n.mesh).Distinct())if(mesh)Object.DestroyImmediate(mesh);Object.DestroyImmediate(doc);}}
    }
}
internal sealed class StudioModifierValidationPanel:EditorWindow
{
    internal StudioModelEditor Editor;internal StudioModelDocument Document;
    private void OnGUI(){if(Editor!=null)Editor.DrawInspector();}
    private void OnDestroy(){Editor?.Dispose();Editor=null;if(Document){foreach(var mesh in Document.nodes.Select(n=>n.mesh).Distinct())if(mesh)Object.DestroyImmediate(mesh);Object.DestroyImmediate(Document);}}
}




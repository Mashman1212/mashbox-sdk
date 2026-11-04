using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;
using Undo=MashBoxSDK.AnimationStudio.StudioUndo;
internal static class StudioFrozenRotationValidation
{
    static int checks;
    static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+n));
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static bool Same(Vector3[] a,Vector3[] b)=>a.Length==b.Length&&a.Zip(b,(x,y)=>(x-y).sqrMagnitude<1e-8f).All(x=>x);
    static Vector3[] World(StudioModelEditor editor)=>editor.Transforms[1].GetComponent<MeshFilter>().sharedMesh.vertices.Select(editor.Transforms[1].TransformPoint).ToArray();
    [InitializeOnLoadMethod] static void Request(){if(Application.dataPath.EndsWith("SelectionUndoProject/Assets")&&File.Exists(Out("frozen-rotation-request.txt")))EditorApplication.delayCall+=Run;}
    static void Case(Quaternion parent)
    {
        var doc=ScriptableObject.CreateInstance<StudioModelDocument>();StudioModelEditor editor=null;
        try
        {
            var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var faces);var local=mesh.vertices;
            doc.nodes.Add(new StudioModelDocument.Node{name="Parent",rotation=parent});
            doc.nodes.Add(new StudioModelDocument.Node{name="Cube",parent=0,mesh=mesh,rotation=Quaternion.Euler(45,0,0),position=new Vector3(3,2,1),cutPolygons=faces,cutTriangles=mesh.triangles,geometryVertices=StudioSubdivisionData.Primitive(mesh)});
            doc.nodes.Add(new StudioModelDocument.Node{name="Child",parent=1,position=Vector3.one});
            editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(1);
            var original=World(editor);var orientation=doc.nodes[1].RetainedWorldRotation(editor.Transforms[1]);var child=editor.Transforms[2].localToWorldMatrix;
            Undo.IncrementCurrentGroup();doc.Freeze(1,editor.Transforms);Undo.IncrementCurrentGroup();
            Check(doc.nodes[1].RotationChannels.sqrMagnitude<1e-8f,"Freeze did not zero channels");
            Check(Quaternion.Angle(orientation,doc.nodes[1].RetainedWorldRotation(editor.Transforms[1]))<.001f,"Freeze lost world rotation");
            Check(Same(original,World(editor)),"Freeze changed world geometry");
            for(int i=0;i<16;i++)Check(Mathf.Abs(child[i]-editor.Transforms[2].localToWorldMatrix[i])<.0001f,"Freeze child world pose");
            UnityEditor.Undo.PerformUndo();Check(Quaternion.Angle(editor.Transforms[1].localRotation,Quaternion.Euler(45,0,0))<.001f,"Freeze Undo");
            UnityEditor.Undo.PerformRedo();Check(doc.nodes[1].RotationChannels.sqrMagnitude<1e-8f&&Quaternion.Angle(orientation,doc.nodes[1].RetainedWorldRotation(editor.Transforms[1]))<.001f,"Freeze Redo retained frame: channels="+doc.nodes[1].RotationChannels+" retained="+doc.nodes[1].frozenRotation+" transform="+editor.Transforms[1].rotation+" expected="+orientation);
            editor.SetObjectChannels(doc.nodes[1].position,new Vector3(0,10,0),doc.nodes[1].scale,true);
            var turned=doc.nodes[1].RetainedWorldRotation(editor.Transforms[1]);original=World(editor);
            doc.Freeze(1,editor.Transforms);
            Check(Quaternion.Angle(turned,doc.nodes[1].RetainedWorldRotation(editor.Transforms[1]))<.001f,"Repeated Freeze lost accumulated frame");
            Check(Same(original,World(editor)),"Repeated Freeze moved mesh");
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../MashBoxStudio/Workspaces"));Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"FrozenRotation-"+Guid.NewGuid().ToString("N")+".workspace");StudioWorkspaceText.Write(doc,path);var loaded=StudioWorkspaceText.Read(path);
            try{Check(Quaternion.Angle(loaded.nodes[1].frozenRotation,doc.nodes[1].frozenRotation)<.001f&&Quaternion.Angle(loaded.nodes[1].frozenPivotBaseline,doc.nodes[1].frozenPivotBaseline)<.001f,"Workspace retained offset/baseline");}
            finally{foreach(var m in loaded.nodes.Select(n=>n.mesh).Where(m=>m))Object.DestroyImmediate(m);Object.DestroyImmediate(loaded);}
            typeof(StudioModelEditor).GetMethod("BakeEditingPivot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,null);
            Check(Quaternion.Angle(editor.Transforms[1].rotation,turned)<.001f,"Bake did not restore retained orientation");
            Check(doc.nodes[1].positionOffset.Equals(Vector3.zero)&&doc.nodes[1].frozenRotation.Equals(Quaternion.identity),"Bake left frozen offsets");
            Check(Same(original,World(editor)),"Bake moved mesh");
            UnityEditor.Undo.PerformUndo();Check(Quaternion.Angle(turned,doc.nodes[1].RetainedWorldRotation(editor.Transforms[1]))<.001f&&Same(original,World(editor)),"Bake Undo frame/mesh");
            UnityEditor.Undo.PerformRedo();Check(Quaternion.Angle(editor.Transforms[1].rotation,turned)<.001f&&Same(original,World(editor)),"Bake Redo frame/mesh");
            editor.SetObjectChannels(Vector3.zero,Vector3.zero,Vector3.one,true);
            Check(Same(local.Select(editor.Transforms[0].TransformPoint).ToArray(),World(editor)),"Zero did not return mesh to parent frame/origin");
        }
        finally{editor?.Dispose();foreach(var mesh in doc.nodes.Select(n=>n.mesh).Where(m=>m).Distinct())Object.DestroyImmediate(mesh);Object.DestroyImmediate(doc);}
    }
    [MenuItem("Tools/Run Frozen Rotation Check")] public static void Run()
    {
        if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets"))throw new InvalidOperationException("Disposable project required.");
        if(File.Exists(Out("frozen-rotation-request.txt")))File.Delete(Out("frozen-rotation-request.txt"));checks=0;
        try{Case(Quaternion.identity);Case(Quaternion.Euler(0,25,0));File.WriteAllText(Out("frozen-rotation-result.txt"),"PASS "+checks+" focused checks");}
        catch(Exception e){File.WriteAllText(Out("frozen-rotation-result.txt"),"FAIL after "+checks+": "+e);}
    }
}

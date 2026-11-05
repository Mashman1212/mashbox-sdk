using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;
internal static class StudioBakePivotValidation
{
    static int checks;
    static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+n));
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    [InitializeOnLoadMethod] static void Request(){if(Application.dataPath.EndsWith("SelectionUndoProject/Assets")&&File.Exists(Out("bake-pivot-request.txt")))EditorApplication.delayCall+=Run;}
    static Vector3[] World(StudioModelEditor e,int i)=>e.Transforms[i].GetComponent<MeshFilter>().sharedMesh.vertices.Select(e.Transforms[i].TransformPoint).ToArray();
    static bool Same(Vector3[] a,Vector3[] b)=>a.Length==b.Length&&a.Zip(b,(x,y)=>(x-y).sqrMagnitude<1e-9f).All(x=>x);
    [MenuItem("Tools/Run Bake Pivot Check")] public static void Run()
    {
        if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets"))throw new InvalidOperationException("Disposable project required.");
        if(File.Exists(Out("bake-pivot-request.txt")))File.Delete(Out("bake-pivot-request.txt"));
        StudioModelDocument doc=null;StudioModelEditor editor=null;checks=0;
        try
        {
            doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
            var pivot=Quaternion.Euler(45,0,0);
            doc.nodes.Add(new StudioModelDocument.Node{name="Cube",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,geometryVertices=StudioSubdivisionData.Primitive(mesh),pivotRotation=pivot});
            doc.nodes.Add(new StudioModelDocument.Node{name="Shared cube",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,geometryVertices=StudioSubdivisionData.Primitive(mesh),position=Vector3.right*3});
            doc.nodes.Add(new StudioModelDocument.Node{name="Child",parent=0,position=new Vector3(1,2,3),rotation=Quaternion.Euler(12,24,36)});
            editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);
            var original=World(editor,0);var other=World(editor,1);var child=editor.Transforms[2].localToWorldMatrix;
            typeof(StudioModelEditor).GetMethod("BakeEditingPivot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,null);
            Check(Quaternion.Angle(doc.nodes[0].rotation,pivot)<.001f,"rotation channels did not adopt pivot");
            Check(Quaternion.Angle(doc.nodes[0].pivotRotation,Quaternion.identity)<.001f,"pivot offset remains");
            Check(Mathf.Abs(doc.nodes[0].RotationChannels.x-45)<.001f,"45 degree channel");
            Check(Same(original,World(editor,0)),"bake changed bound mesh world geometry");Check(Same(other,World(editor,1)),"shared instance changed");
            Check(doc.nodes[0].mesh!=doc.nodes[1].mesh,"compensation did not separate shared mesh");
            for(int i=0;i<16;i++)Check(Mathf.Abs(editor.Transforms[2].localToWorldMatrix[i]-child[i])<.0001f,"child world pose");
            UnityEditor.Undo.PerformUndo();
            Check(Quaternion.Angle(doc.nodes[0].rotation,Quaternion.identity)<.001f&&Quaternion.Angle(doc.nodes[0].pivotRotation,pivot)<.001f,"Undo frame");
            Check(Same(original,World(editor,0)),"Undo bound geometry");
            UnityEditor.Undo.PerformRedo();Check(Quaternion.Angle(doc.nodes[0].rotation,pivot)<.001f&&Same(original,World(editor,0)),"Redo frame/geometry");
            editor.SetObjectChannels(doc.nodes[0].position,Vector3.zero,doc.nodes[0].scale,true);
            Check(Quaternion.Angle(editor.Transforms[0].rotation,Quaternion.identity)<.001f,"zero rotation did not square frame");
            Check(Same(original.Select(p=>Quaternion.Inverse(pivot)*p).ToArray(),World(editor,0)),"zero did not rotate geometry by minus45");
            MultiSelection();
            File.WriteAllText(Out("bake-pivot-result.txt"),"PASS "+checks+" focused checks");
        }
        catch(Exception ex){File.WriteAllText(Out("bake-pivot-result.txt"),"FAIL after "+checks+": "+ex);}
        finally{editor?.Dispose();if(doc){foreach(var m in doc.nodes.Select(n=>n.mesh).Where(m=>m).Distinct())Object.DestroyImmediate(m);Object.DestroyImmediate(doc);}}
    }
    static void MultiSelection()
    {
        var doc=ScriptableObject.CreateInstance<StudioModelDocument>();
        var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
        var first=Quaternion.Euler(20,0,0);var second=Quaternion.Euler(0,35,0);
        doc.nodes.Add(new StudioModelDocument.Node{name="First",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,pivotRotation=first});
        doc.nodes.Add(new StudioModelDocument.Node{name="Second",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,position=Vector3.right*3,pivotRotation=second});
        var editor=new StudioModelEditor(()=>{});
        try
        {
            editor.Load(doc);editor.SelectObject(0);editor.SelectObject(1,true);
            var world0=World(editor,0);var world1=World(editor,1);
            doc.nodes[1].skinned=true;
            bool rejected=false;
            try{typeof(StudioModelEditor).GetMethod("BakeEditingPivot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,null);}
            catch(TargetInvocationException exception) when(exception.InnerException is InvalidOperationException){rejected=true;}
            Check(rejected&&Quaternion.Angle(doc.nodes[0].pivotRotation,first)<.001f&&Same(world0,World(editor,0)),"Invalid selected object rejects the whole bake before mutation");
            doc.nodes[1].skinned=false;
            typeof(StudioModelEditor).GetMethod("BakeEditingPivot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,null);
            Check(Quaternion.Angle(doc.nodes[0].rotation,first)<.001f&&Quaternion.Angle(doc.nodes[1].rotation,second)<.001f,"Multi-selection bakes both pivot frames");
            Check(Same(world0,World(editor,0))&&Same(world1,World(editor,1))&&doc.nodes[0].mesh!=doc.nodes[1].mesh,"Multi-selection preserves both shared-mesh surfaces independently");
            UnityEditor.Undo.PerformUndo();
            Check(Quaternion.Angle(doc.nodes[0].pivotRotation,first)<.001f&&Quaternion.Angle(doc.nodes[1].pivotRotation,second)<.001f,"One Undo restores both pivot frames");
            UnityEditor.Undo.PerformRedo();
            Check(Quaternion.Angle(doc.nodes[0].rotation,first)<.001f&&Quaternion.Angle(doc.nodes[1].rotation,second)<.001f&&Same(world0,World(editor,0))&&Same(world1,World(editor,1)),"One Redo reapplies both pivots");
        }
        finally
        {
            editor.Dispose();
            foreach(var item in doc.nodes.Select(n=>n.mesh).Where(m=>m).Distinct())Object.DestroyImmediate(item);
            Object.DestroyImmediate(doc);
        }
    }
}

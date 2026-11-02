using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;
internal static class StudioSceneUnitsValidation
{
    static int checks;
    static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
    [InitializeOnLoadMethod] static void Request()
    {
        if(Application.dataPath.EndsWith("SelectionUndoProject/Assets")&&File.Exists(Out("scene-units-request.txt")))EditorApplication.delayCall+=Run;
    }
    static StudioModelDocument.Node Cube(float size)
    {
        var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,new Vector4(size,size,size,1),out var faces);
        return new StudioModelDocument.Node{name="Unit check",mesh=mesh,cutPolygons=faces,cutTriangles=mesh.triangles,geometryVertices=StudioSubdivisionData.Primitive(mesh)};
    }
    static string Tool(StudioLengthUnit unit,string kind,float size)
    {
        var node=Cube(size);
        try
        {
            float internalDistance=StudioSceneUnits.Internal((float)(size*.1/StudioSceneUnits.MetersPerUnit(unit)));
            if(kind=="Bisect")StudioMeshCuts.Bisect(node,new Plane(Vector3.right,Vector3.zero),Matrix4x4.identity,internalDistance,true);
            else StudioModifierEvaluator.Apply(node,new StudioModifierOperation{kind=kind,parameters=new Vector4(internalDistance,1,0,0),faces=new[]{0},edges=new[]{0,1}});
            Check(node.mesh.vertexCount>24,kind+" produced no geometry at "+size);
            Check(node.mesh.vertices.All(p=>!float.IsNaN(p.sqrMagnitude)&&!float.IsInfinity(p.sqrMagnitude)),kind+" finite positions");
            // Float conversion roundoff is allowed; quantize below the tested feature scale.
            return string.Join(";",node.mesh.vertices.Select(p=>Mathf.RoundToInt(p.x/size*100000)+","+Mathf.RoundToInt(p.y/size*100000)+","+Mathf.RoundToInt(p.z/size*100000)))+"|"+string.Join(",",node.cutPolygons);
        }
        finally{Object.DestroyImmediate(node.mesh);}
    }
    [MenuItem("Tools/Run Scene Units Check")] public static void Run()
    {
        if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets"))throw new InvalidOperationException("Disposable project required.");
        string request=Out("scene-units-request.txt");if(File.Exists(request))File.Delete(request);
        StudioModelDocument doc=null;StudioModelEditor editor=null;SceneView view=null;checks=0;
        try
        {
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../MashBoxStudio/Workspaces"));Directory.CreateDirectory(folder);
            doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.SetWorkspacePath(Path.Combine(folder,"Units-"+Guid.NewGuid().ToString("N")+".workspace"));
            doc.sceneUnits=StudioLengthUnit.Meters;doc.gridSpacing=.25f;doc.nodes.Add(Cube(1));
            var creation=new StudioModifierOperation{kind="Primitive",name="Cube",primitiveType=PrimitiveType.Cube,parameters=new Vector4(1,1,1,1),cubeDivisions=Vector3Int.one};creation.Bind(doc.nodes[0]);
            doc.nodes[0].modifiers=new StudioModifierHistory{source=StudioModifierSource.Capture(doc.nodes[0]),operations=new System.Collections.Generic.List<StudioModifierOperation>{creation}};
            editor=new StudioModelEditor(()=>{});editor.Load(doc);StudioModelCommands.SetActive(editor);
            var original=doc.nodes[0].mesh.vertices;var parameters=doc.nodes[0].modifiers.operations[0].parameters;string source=doc.nodes[0].modifiers.source.mesh;
            view=ScriptableObject.CreateInstance<SceneView>();view.Show();
            Check(StudioModelEditor.SetViewportCameraLens(view,60,.00005f,.0005f,false),"small clip range rejected");
            float near=view.cameraSettings.nearClip,far=view.cameraSettings.farClip;
            var units=(StudioLengthUnit[])Enum.GetValues(typeof(StudioLengthUnit));
            foreach(var unit in units)
            {
                StudioSceneUnits.SetUnit(unit);
                Check(doc.sceneUnits==unit,"workspace unit");
                foreach(float distance in new[]{-.25f,0,.001f,.0254f,1,100})
                    Check(Mathf.Abs(StudioSceneUnits.Internal(StudioSceneUnits.Display(distance))-distance)<=Mathf.Max(1e-8f,Mathf.Abs(distance)*1e-6f),"roundtrip "+unit);
                Check(doc.nodes[0].mesh.vertices.SequenceEqual(original),"unit switch resized geometry");
                Check(doc.nodes[0].modifiers.operations[0].parameters==parameters&&doc.nodes[0].modifiers.source.mesh==source,"unit switch altered modifier values");
                Check(view.cameraSettings.nearClip==near&&view.cameraSettings.farClip==far,"unit switch altered clipping");
                Check(StudioSceneUnits.GridSpacing==StudioSceneUnits.DefaultGrid(unit),"grid default");
                var snapped=StudioModelEditor.SnapConstrainedGrid(Vector3.zero,new Vector3(1.4f,2.6f,3.2f)*StudioSceneUnits.GridSpacing,Quaternion.identity,StudioModelEditor.MoveAxes.Free,StudioSceneUnits.GridSpacing);
                Check((snapped-new Vector3(1,3,3)*StudioSceneUnits.GridSpacing).sqrMagnitude<1e-10f,"grid snap spacing");
                StudioSceneUnits.SetGrid(StudioSceneUnits.DefaultGrid(unit)*3);StudioSceneUnits.ResetGrid();
                Check(StudioSceneUnits.GridSpacing==StudioSceneUnits.DefaultGrid(unit),"grid reset");
            }
            foreach(float size in new[]{1f,.01f})foreach(string tool in new[]{"Bevel","Extrude","Bisect"})
            {
                string baseline=null;
                foreach(var unit in units){doc.sceneUnits=unit;var result=Tool(unit,tool,size);if(baseline==null)baseline=result;else Check(result==baseline,tool+" unit-dependent geometry");}
            }
            doc.sceneUnits=StudioLengthUnit.Inches;doc.gridSpacing=.0762f;
            string saved=Path.Combine(folder,"UnitsRoundtrip-"+Guid.NewGuid().ToString("N")+".workspace");StudioWorkspaceText.Write(doc,saved);
            var loaded=StudioWorkspaceText.Read(saved);
            try{Check(loaded.sceneUnits==doc.sceneUnits&&loaded.gridSpacing==doc.gridSpacing,"workspace unit/grid persistence");Check(loaded.nodes[0].mesh.vertices.SequenceEqual(original),"workspace geometry persistence");}
            finally{foreach(var mesh in loaded.nodes.Select(n=>n.mesh))Object.DestroyImmediate(mesh);Object.DestroyImmediate(loaded);}
            StudioSceneUnits.SetUnit(StudioLengthUnit.Millimeters);
            UnityEditor.Undo.PerformUndo();Check(doc.sceneUnits==StudioLengthUnit.Inches,"unit Undo");
            UnityEditor.Undo.PerformRedo();Check(doc.sceneUnits==StudioLengthUnit.Millimeters,"unit Redo");
            File.WriteAllText(Out("scene-units-result.txt"),"PASS "+checks+" focused native checks");
        }
        catch(Exception e){File.WriteAllText(Out("scene-units-result.txt"),"FAIL after "+checks+" checks: "+e);}
        finally{if(view)view.Close();editor?.Dispose();if(doc){foreach(var mesh in doc.nodes.Select(n=>n.mesh).Distinct())if(mesh)Object.DestroyImmediate(mesh);Object.DestroyImmediate(doc);}}
    }
}

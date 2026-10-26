using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;

// Exiting fixture: disposable project only. Exercises the shared snap resolver and
// captured component publishers; does not synthesize physical pointer movement.
public static class StudioAxisSnapValidation
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static StudioModelDocument doc; static StudioModelEditor editor; static MashBoxStudioViewport view;
    static int checks; static string stage;
    static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length&&m.GetParameters().Select((p,i)=>args[i]==null||p.ParameterType.IsInstanceOfType(args[i])).All(v=>v)).Invoke(editor,args);
    static void Set(string name,object value)=>typeof(StudioModelEditor).GetField(name,F).SetValue(editor,value);
    static void Check(bool value,string message){if(!value)throw new Exception(stage+": "+message);checks++;}
    static void Near(Vector3 a,Vector3 b,string message)=>Check((a-b).sqrMagnitude<1e-9f,message+" "+a+" != "+b);
    static void Target(int node,StudioModelEditor.ComponentMode mode){Call("ActivateComponentTarget",node,mode);editor.Components.Clear();int count=(int)Call("ComponentCount");for(int i=0;i<count;i++)editor.Components.Add(i);}
    public static void Run(){EditorApplication.delayCall+=()=>{try{RunChecks();Finish(null);}catch(Exception ex){Finish(ex);}};}
    static void RunChecks()
    {
        doc=ScriptableObject.CreateInstance<StudioModelDocument>();
        for(int i=0;i<2;i++){var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);doc.nodes.Add(new StudioModelDocument.Node{name="Moving "+i,mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,position=Vector3.right*(i==0?-1.5f:1.5f),rotation=Quaternion.Euler(13,21,7),scale=new Vector3(.8f,1.1f,1.3f)});}
        var destination=new Mesh{name="Stationary snap targets"};destination.vertices=new[]{new Vector3(2.2f,3.3f,4.4f),new Vector3(-5,-6,-7),new Vector3(8,9,10)};destination.triangles=new[]{0,1,2};
        doc.nodes.Add(new StudioModelDocument.Node{name="Destination",mesh=destination});
        editor=new StudioModelEditor(()=>{});editor.Load(doc);view=ScriptableObject.CreateInstance<MashBoxStudioViewport>();view.Bind(editor.Scene);view.Show();
        var axes=new[]{StudioModelEditor.MoveAxes.X,StudioModelEditor.MoveAxes.Y,StudioModelEditor.MoveAxes.Z,StudioModelEditor.MoveAxes.XY,StudioModelEditor.MoveAxes.XZ,StudioModelEditor.MoveAxes.YZ};
        foreach(bool multi in new[]{false,true})foreach(var mode in new[]{StudioModelEditor.ComponentMode.Vertex,StudioModelEditor.ComponentMode.Edge,StudioModelEditor.ComponentMode.Face})foreach(bool local in new[]{false,true})foreach(var axis in axes)
        {
            stage=(multi?"multi ":"single ")+mode+" "+axis+" "+local;
            editor.SelectObject(0);Target(0,mode);if(multi)Target(1,mode);editor.Transforms[1].gameObject.SetActive(multi);
            var orientation=local?Quaternion.Euler(17,31,9):Quaternion.identity;
            editor.Transforms[2].rotation=orientation;
            var origin=multi?(Vector3)Call("MultiComponentCenter",new List<int>{0,1}):editor.EditingPivotPosition;
            Set("moveDragOrigin",origin);Set("moveDragOrientation",orientation);Set("moveDragAxes",axis);
            var destinationPoint=orientation*new Vector3(2.2f,3.3f,4.4f);
            var expected=StudioModelEditor.ConstrainMove(origin,destinationPoint,orientation,axis);
            var epsilon=orientation*new Vector3(.001f,.001f,.001f);
            var raw=StudioModelEditor.ConstrainMove(origin,destinationPoint+epsilon,orientation,axis);
            Set("pivotSnapHeld",true);Set("gridSnapHeld",false);
            foreach(bool ortho in new[]{false,true})
            {
                view.orthographic=ortho;view.LookAtDirect(Vector3.zero,ortho?Quaternion.Euler(70,-120,25):Quaternion.Euler(-20,40,0),5);
                var actual=(Vector3)Call("ResolveMoveSnap",view,ortho?new Vector2(-10000,9000):new Vector2(1,2),raw,origin);
                Near(actual,expected,"Vertex snapping changed with camera or pointer");
                var delta=Quaternion.Inverse(orientation)*(actual-origin);
                for(int a=0;a<3;a++)if(((int)axis&(1<<a))==0)Check(Mathf.Abs(delta[a])<.00002f,"Locked vertex coordinate moved");
            }
            Set("gridSnapHeld",true);
            Near((Vector3)Call("ResolveMoveSnap",null,Vector2.zero,raw,origin),expected,"Grid overrode held vertex snap");
            Set("pivotSnapHeld",false);StudioSettings.Current.gridSpacing=.5f;
            var grid=(Vector3)Call("ResolveMoveSnap",null,Vector2.zero,raw,origin);
            var gridPoint=Quaternion.Inverse(orientation)*grid;var gridDelta=Quaternion.Inverse(orientation)*(grid-origin);
            for(int a=0;a<3;a++)if(((int)axis&(1<<a))==0)Check(Mathf.Abs(gridDelta[a])<.00002f,"Locked grid coordinate moved");else Check(Mathf.Abs(gridPoint[a]*2-Mathf.Round(gridPoint[a]*2))<.00002f,"Active coordinate missed grid");
            Set("gridSnapHeld",false);
            var before=doc.nodes.Take(2).Select(n=>n.mesh.vertices).ToArray();
            UnityEditor.Undo.ClearAll();StudioUndo.ClearForTests();StudioUndo.IncrementCurrentGroup();
            if(multi)
            {
                var gesture=Call("CaptureMultiComponentGesture",new List<int>{0,1},origin,orientation,0);
                Call("ApplyMultiComponentGesture",gesture,expected,orientation,Vector3.one);CheckVertices(before,expected-origin,2);
                Call("ApplyMultiComponentGesture",gesture,origin,orientation,Vector3.one);
            }
            else
            {
                var drag=Call("CaptureMoveDrag",0,origin,orientation,editor.SelectedVertices(),doc.nodes[0].mesh.vertices);Set("moveDrag",drag);
                Call("ApplyMoveDrag",expected);CheckVertices(before,expected-origin,1);Call("ApplyMoveDrag",origin);Set("moveDrag",null);
            }
            for(int i=0;i<(multi?2:1);i++){var restored=doc.nodes[i].mesh.vertices;if(!multi)Check(restored.SequenceEqual(before[i]),"Captured single movement did not restore exact source");else for(int v=0;v<restored.Length;v++)Near(restored[v],before[i][v],"Multi movement did not restore source within transform precision");}
            Check(StudioUndo.RetainedActions==1,"Snap samples made more than one history action");
        }
        stage="no stationary target";Target(0,StudioModelEditor.ComponentMode.Vertex);Target(1,StudioModelEditor.ComponentMode.Vertex);editor.Transforms[1].gameObject.SetActive(true);editor.Transforms[2].gameObject.SetActive(false);
        Set("pivotSnapHeld",true);Set("gridSnapHeld",true);Set("moveDragAxes",StudioModelEditor.MoveAxes.Y);
        var previous=new Vector3(.1f,.2f,.3f);Near((Vector3)Call("ResolveMoveSnap",null,Vector2.zero,new Vector3(2,3,4),previous),previous,"Missing vertex fell back to grid or moving geometry");
        Check(checks>500,"Incomplete snap matrix");
    }
    static void CheckVertices(Vector3[][] before,Vector3 delta,int count)
    {
        for(int n=0;n<count;n++){var matrix=editor.Transforms[n].localToWorldMatrix;var after=doc.nodes[n].mesh.vertices;for(int i=0;i<after.Length;i++)Near(matrix.MultiplyVector(after[i]-before[n][i]),delta,"Published geometry left constrained trajectory");}
    }
    static void Finish(Exception exception)
    {
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../axis-snap-result.txt")),exception==null?"PASS "+checks+" constrained vertex/grid, V/E/F, world/local, camera independence and single/multi geometry checks":stage+"\n"+exception);
        EditorApplication.delayCall+=()=>{if(view)view.Close();editor?.Dispose();if(doc){foreach(var node in doc.nodes)if(node.mesh)Object.DestroyImmediate(node.mesh);Object.DestroyImmediate(doc);}double end=EditorApplication.timeSinceStartup+1;void Exit(){if(EditorApplication.timeSinceStartup<end)return;EditorApplication.update-=Exit;EditorApplication.Exit(exception==null?0:1);}EditorApplication.update+=Exit;};
    }
}

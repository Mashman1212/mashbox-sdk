#if MASHBOX_STUDIO_TESTS
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Reflection;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;
public static class StudioSelectionGrowthValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;static StudioModelEditor editor;static StudioModelDocument doc;static int checks;
 static object Call(string n,params object[] a)=>typeof(StudioModelEditor).GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==a.Length).Invoke(editor,a);
 static void Check(bool b,string text){if(!b)throw new Exception(text);checks++;}
 static void Reset(){UnityEditor.Undo.ClearAll();StudioUndo.ClearForTests();StudioUndo.IncrementCurrentGroup();}
 static void History(bool redo=false){UnityEditor.Undo.FlushUndoRecordObjects();StudioUndo.IncrementCurrentGroup();if(redo)StudioUndo.PerformRedo();else StudioUndo.PerformUndo();}
 static void Target(int node,StudioModelEditor.ComponentMode mode,params int[] ids){Call("ActivateComponentTarget",node,mode);editor.Components.Clear();editor.Components.UnionWith(ids);}
 static int[] Selection(int node){var record=((Array)Call("CaptureComponentTargets")).Cast<object>().Single(r=>(int)r.GetType().GetField("node").GetValue(r)==node);return (int[])record.GetType().GetField("components").GetValue(record);}
 static HashSet<int> Group(int id,StudioModelEditor.ComponentMode mode)
 {
  var p=editor.ActiveMesh.vertices;if(mode==StudioModelEditor.ComponentMode.Vertex)return Enumerable.Range(0,p.Length).Where(i=>p[i]==p[id]).ToHashSet();if(mode==StudioModelEditor.ComponentMode.Face)return new HashSet<int>{id};
  var edges=(List<(int a,int b)>)Call("Edges");var key=StudioMeshCuts.NormalEdgePositionKeyForBevel(p[edges[id].a],p[edges[id].b]);return Enumerable.Range(0,edges.Count).Where(i=>StudioMeshCuts.NormalEdgePositionKeyForBevel(p[edges[i].a],p[edges[i].b])==key).ToHashSet();
 }
 public static void Run(){EditorApplication.delayCall+=()=>{
  Exception error=null;
  try{
   if(!Application.dataPath.Replace("\\","/").Contains("/obj/StudioControlsValidation/"))throw new Exception("Disposable project only");
   doc=ScriptableObject.CreateInstance<StudioModelDocument>();for(int i=0;i<2;i++){var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polys);doc.nodes.Add(new StudioModelDocument.Node{mesh=mesh,cutPolygons=polys,cutTriangles=mesh.triangles});}
   editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);typeof(StudioModelEditor).GetField("weld",F).SetValue(editor,true);
   foreach(var mode in new[]{StudioModelEditor.ComponentMode.Vertex,StudioModelEditor.ComponentMode.Edge,StudioModelEditor.ComponentMode.Face})
   {
    editor.SetToolbarComponentMode(mode);int count=(int)Call("ComponentCount");var geometry=editor.ActiveMesh.vertices;var triangles=editor.ActiveMesh.triangles;
    for(int seed=0;seed<count;seed++)
    {
     Target(0,mode,seed);var originalGroup=Group(seed,mode);Reset();Call("GrowComponentSelection",false);
     int expected=mode==StudioModelEditor.ComponentMode.Vertex?4:5;
     Check(editor.Components.Select(i=>Group(i,mode).Min()).Distinct().Count()==expected,"Grow was not exactly one ring: "+mode+" seed "+seed);
     var grown=editor.Components.ToHashSet();Check(StudioUndo.RetainedActions==1,"Grow did not register one selection Undo");
     History();Check(editor.Components.SetEquals(new[]{seed}),"Grow Undo lost exact raw selection");History(true);Check(editor.Components.SetEquals(grown),"Grow Redo lost ring");
     Call("GrowComponentSelection",true);Check(editor.Components.SetEquals(originalGroup),"Shrink did not restore the seed logical component: "+mode);
    }
    Target(0,mode,Enumerable.Range(0,count).ToArray());Reset();Call("GrowComponentSelection",true);Check(editor.Components.Count==count&&StudioUndo.RetainedActions==0,"Closed fully selected mesh changed on shrink");
    Target(0,mode);Reset();Call("GrowComponentSelection",false);Check(editor.Components.Count==0&&StudioUndo.RetainedActions==0,"Empty grow changed selection/history");
    typeof(StudioModelEditor).GetField("weld",F).SetValue(editor,false);Target(0,mode,0);Call("GrowComponentSelection",false);Check(editor.Components.Count==(mode==StudioModelEditor.ComponentMode.Face?1:3),"Unwelded growth crossed a seam or render diagonal");typeof(StudioModelEditor).GetField("weld",F).SetValue(editor,true);
    Check(editor.ActiveMesh.vertices.SequenceEqual(geometry)&&editor.ActiveMesh.triangles.SequenceEqual(triangles),"Selection growth changed geometry");
   }
   foreach(var mode in new[]{StudioModelEditor.ComponentMode.Vertex,StudioModelEditor.ComponentMode.Edge,StudioModelEditor.ComponentMode.Face})
   {
    editor.SelectObject(0);editor.SelectObject(1,true);Target(0,mode,0);Target(1,mode,1);Reset();Call("GrowComponentSelection",false);
    Check(Selection(0).Length>1&&Selection(1).Length>1,"Multi-target growth skipped a mesh");Check(StudioUndo.RetainedActions==1,"Multi-target growth split history");History();Check(Selection(0).SequenceEqual(new[]{0})&&Selection(1).SequenceEqual(new[]{1}),"Multi-target Undo failed");History(true);Check(Selection(0).Length>1&&Selection(1).Length>1,"Multi-target Redo failed");
   }
   editor.SelectObject(0);Target(0,StudioModelEditor.ComponentMode.Face,0);editor.IsolateViewportSelection();Call("GrowComponentSelection",false);Check(editor.Components.SetEquals(new[]{0}),"Grow escaped face isolation");Call("GrowComponentSelection",true);Check(editor.Components.SetEquals(new[]{0}),"Hidden isolation neighbors removed visible selection");editor.RunViewportIsolation(true);
   // A center face on an authored quad grid must grow 1 -> 9 -> 25,
   // then shrink 25 -> 9 -> 1, without using rendered triangle diagonals.
   var gridMesh=new Mesh();var positions=new List<Vector3>();var indices=new List<int>();var polygons=new List<int>();
   const int side=7;for(int y=0;y<=side;y++)for(int x=0;x<=side;x++)positions.Add(new Vector3(x,y,0));
   for(int y=0;y<side;y++)for(int x=0;x<side;x++){int a=x+y*(side+1),b=a+1,d=a+side+1,c=d+1;polygons.AddRange(new[]{0,4,a,b,c,d});indices.AddRange(new[]{a,b,c,a,c,d});}
   gridMesh.SetVertices(positions);gridMesh.triangles=indices.ToArray();gridMesh.RecalculateNormals();
   doc.nodes.Add(new StudioModelDocument.Node{mesh=gridMesh,cutPolygons=polygons.ToArray(),cutTriangles=gridMesh.triangles});Call("Rebuild");editor.SelectObject(2);Target(2,StudioModelEditor.ComponentMode.Face,24);
   HashSet<int> Square(int radius)=>Enumerable.Range(0,side*side).Where(i=>Math.Abs(i%side-3)<=radius&&Math.Abs(i/side-3)<=radius).ToHashSet();
   var gridSource=gridMesh.vertices;var gridIndices=gridMesh.triangles;Reset();Call("GrowComponentSelection",false);Check(editor.Components.SetEquals(Square(1)),"Center face did not grow to all nine square neighbors");History();Check(editor.Components.SetEquals(new[]{24}),"Square grow Undo failed");History(true);Check(editor.Components.SetEquals(Square(1)),"Square grow Redo failed");
   Call("GrowComponentSelection",false);Check(editor.Components.SetEquals(Square(2)),"Second grow did not produce a 5x5 square");Call("GrowComponentSelection",true);Check(editor.Components.SetEquals(Square(1)),"Square shrink did not restore 3x3");Call("GrowComponentSelection",true);Check(editor.Components.SetEquals(new[]{24}),"Square shrink did not restore center");
   Check(gridMesh.vertices.SequenceEqual(gridSource)&&gridMesh.triangles.SequenceEqual(gridIndices),"Square selection changed mesh geometry");
   foreach(bool shrink in new[]{false,true})
   {
    string key=shrink?"Select/Shrink Selection":"Select/Grow Selection";var id=StudioModelCommands.Register("Select",shrink?"Shrink Selection":"Grow Selection").Value;
    Check(StudioShortcuts.Definitions.Any(d=>d.id==id&&d.group=="Select"),"Missing Select shortcut entry");var binding=StudioModelCommands.StandardBindings(key).Single();
    var code=shrink?KeyCode.Comma:KeyCode.Period;Check(binding.key==code&&binding.modifiers==StudioModifiers.None,"Incorrect plain punctuation default");
    Check(binding.ToString()==(shrink?",":"."),"Punctuation label incorrect");Check(binding.Matches(new Event{type=EventType.KeyDown,keyCode=code}),"Default key did not match");Check(!binding.Matches(new Event{type=EventType.KeyDown,keyCode=code,modifiers=EventModifiers.Shift}),"Shifted punctuation unexpectedly matched");
   }
  }catch(Exception e){error=e;}
  File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../selection-growth-result.txt")),error==null?"PASS "+checks+" one-ring adjacency, seams, multi-target selection/history, isolation and shortcut checks":error.ToString());
  EditorApplication.delayCall+=()=>{editor?.Dispose();if(doc){foreach(var node in doc.nodes)if(node.mesh)UnityEngine.Object.DestroyImmediate(node.mesh);UnityEngine.Object.DestroyImmediate(doc);}EditorApplication.Exit(error==null?0:1);};
 };}
}
#endif

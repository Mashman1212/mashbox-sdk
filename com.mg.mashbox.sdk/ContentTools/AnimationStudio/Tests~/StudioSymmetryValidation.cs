using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioSymmetryValidation
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static readonly List<string> report=new List<string>();
        static int failures;
        static string DirectoryPath=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Symmetry"));
        static void Check(bool value,string label){if(!value)throw new Exception(label);report.Add("PASS "+label);}
        static object Call(object target,string name,params object[] args)
        {
            try{
                var method=target.GetType().GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length>=args.Length&&m.GetParameters().Select((p,i)=>i>=args.Length?p.IsOptional:args[i]==null||p.ParameterType.IsInstanceOfType(args[i])).All(v=>v));
                return method.Invoke(target,args.Concat(method.GetParameters().Skip(args.Length).Select(p=>p.DefaultValue)).ToArray());
            }
            catch(TargetInvocationException e){throw e.InnerException;}
        }
        static void Set(object target,string name,object value)=>target.GetType().GetField(name,F).SetValue(target,value);
        static object Get(object target,string name)=>target.GetType().GetField(name,F).GetValue(target);
        static void Case(string label,Action action){try{action();}catch(Exception e){failures++;report.Add("FAIL "+label+": "+e);}}
        static void Reject(Action action,string label)
        {bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Check(rejected,label);}
        static void Cube(Action<StudioModelEditor,StudioModelDocument,StudioModelDocument.Node> action)
        {
            var doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.SetWorkspacePath(Path.GetFullPath(Path.Combine(Application.dataPath,"../MashBoxStudio/Workspaces/symmetry-"+Guid.NewGuid().ToString("N")+".workspace")));doc.AddPrimitive(PrimitiveType.Cube);
            try{using(var editor=new StudioModelEditor(()=>{})){editor.Load(doc);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);Set(editor,"symmetryEnabled",true);action(editor,doc,doc.nodes[0]);}}
            finally{foreach(var mesh in doc.nodes.Select(n=>n.mesh).Where(m=>m).Distinct())UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(doc);}
        }
        static int PositiveFace(StudioModelDocument.Node node)
        {var topology=new StudioMeshCuts(node);return Enumerable.Range(0,topology.Faces.Count).OrderByDescending(f=>topology.Faces[f].Vertices.Average(v=>topology.Positions[v].x)).First();}
        static void Symmetric(StudioModelDocument.Node node,Matrix4x4 reflection,string label)
        {
            var positions=node.mesh.vertices;
            Check(positions.All(v=>positions.Any(other=>(reflection.MultiplyPoint3x4(v)-other).sqrMagnitude<1e-8f)),label);
        }
        static void ReplayInfo(StudioModelDocument.Node node,StudioModelDocument.Node result)
        {
            report.Add("INFO Replay vertices="+result.mesh.vertexCount+"/"+node.mesh.vertexCount+" triangles="+result.mesh.triangles.Length+"/"+node.mesh.triangles.Length+" failures="+string.Join(";",node.modifiers.operations.Select(o=>o.kind+":"+o.failure)));
        }
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/SymmetryProject/Assets"))throw new Exception("Use the disposable SymmetryProject only.");
            Case("reflection frames",()=>{
                foreach(bool world in new[]{false,true})foreach(int axis in new[]{0,1,2})
                {
                    var bounds=new Bounds(new Vector3(3,5,7),new Vector3(4,6,8));
                    var matrix=Matrix4x4.TRS(new Vector3(11,-3,4),Quaternion.Euler(12,34,56),new Vector3(-2,3,.5f));
                    Vector3 a=new Vector3(2,3,4),b;
                    if(world){var p=matrix.MultiplyPoint3x4(a);p[axis]=-p[axis];b=matrix.inverse.MultiplyPoint3x4(p);}
                    else {b=a;b[axis]=2*bounds.center[axis]-a[axis];}
                    var symmetry=new StudioMeshSymmetry(new[]{a,b},new[]{7,19},matrix,bounds,world,axis);
                    Check((symmetry.Reflect(a)-b).sqrMagnitude<1e-8f,"Reflection world="+world+" axis="+axis+" with offset bounds/rotated nonuniform mirrored transform");
                    Check(symmetry.MirroredGeometry(0)==19&&symmetry.MirroredGeometry(1)==7,"Reciprocal geometric pairing "+world+"/"+axis);
                    var values=new[]{a+Vector3.one*.2f,b};symmetry.Capture(new[]{0}).Apply(values);
                    Check((values[1]-symmetry.Reflect(values[0])).sqrMagnitude<1e-8f,"Reflected displacement "+world+"/"+axis);
                }
                Reject(()=>new StudioMeshSymmetry(new[]{Vector3.one},null,Matrix4x4.Scale(Vector3.zero),new Bounds(),true,0),"Singular world transform rejected");
            });
            Case("seams and center",()=>{
                var points=new[]{new Vector3(1,2,0),new Vector3(1,2,0),new Vector3(-1,2,0),new Vector3(-1,2,0),new Vector3(0,3,0)};
                var symmetry=new StudioMeshSymmetry(points,new[]{5,5,8,8,22},Matrix4x4.identity,new Bounds(Vector3.zero,Vector3.one*8),false,0);
                var values=(Vector3[])points.Clone();values[0]+=new Vector3(.3f,.4f,.2f);values[4]+=new Vector3(.2f,.4f,.2f);
                var deformation=symmetry.Capture(new[]{0,4});deformation.Apply(values);
                Check(values[0]==values[1]&&values[2]==values[3]&&(values[2]-symmetry.Reflect(values[0])).sqrMagnitude<1e-10f,"All seam aliases follow the same geometric pair");
                Check(values[4].x==0&&Mathf.Abs(values[4].y-3.4f)<1e-5f,"Center vertices remain on the symmetry plane and move tangentially");
                var detached=new StudioMeshSymmetry(points,new[]{5,9,8,8,22},Matrix4x4.identity,new Bounds(Vector3.zero,Vector3.one*8),false,0);
                Reject(()=>detached.Capture(new[]{0}),"Ambiguous detached coincident groups rejected without joining identities");
                var missing=new StudioMeshSymmetry(new[]{Vector3.one},null,Matrix4x4.identity,new Bounds(),false,0);
                Reject(()=>missing.Capture(new[]{0}),"Missing mirrored vertex rejected");
            });
            Case("single side move and Undo",()=>Cube((editor,doc,node)=>{
                int face=PositiveFace(node);editor.Components.Add(face);var original=node.mesh.vertices;
                var symmetry=(StudioMeshSymmetry)Call(editor,"CaptureSymmetry",new object[]{original});
                var indices=editor.SelectedVertices();var center=(Vector3)Call(editor,"ComponentSelectionCenter");
                var drag=Call(editor,"CaptureMoveDrag",0,center,Quaternion.identity,indices,original);Set(editor,"moveDrag",drag);
                StudioUndo.IncrementCurrentGroup();Call(editor,"ApplyMoveDrag",center+new Vector3(.2f,.3f,.1f));StudioUndo.IncrementCurrentGroup();
                Symmetric(node,symmetry.Reflection,"Actual captured Move drag reflects both hard-normal seams");
                Check(node.mesh.vertices.Where((v,i)=>original[i].x>0).All(v=>v.x>.5f),"Selected positive face moves outward");
                var moved=node.mesh.vertices;
                StudioUndo.PerformUndo();Check(node.mesh.vertices.SequenceEqual(original),"One native StudioUndo restores both sides");
                StudioUndo.PerformRedo();Check(node.mesh.vertices.SequenceEqual(moved),"Redo restores both sides");
            }));
            Case("negative side and UVs",()=>Cube((editor,doc,node)=>{
                editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Vertex);Set(editor,"preserveMoveUVs",true);
                int index=Array.FindIndex(node.mesh.vertices,v=>v.x<0);editor.Components.Add(index);var original=node.mesh.vertices;
                var symmetry=(StudioMeshSymmetry)Call(editor,"CaptureSymmetry",new object[]{original});var indices=editor.SelectedVertices();
                var center=(Vector3)Call(editor,"ComponentSelectionCenter");var drag=Call(editor,"CaptureMoveDrag",0,center,Quaternion.identity,indices,original);Set(editor,"moveDrag",drag);
                Call(editor,"ApplyMoveDrag",center+new Vector3(-.25f,.1f,0));
                Check(node.mesh.vertices[index].x<original[index].x,"Negative side remains the driving side");
                Symmetric(node,symmetry.Reflection,"Preserve UVs and negative-side Move retain geometric symmetry");
                Check(node.mesh.uv.Length==node.mesh.vertexCount,"UV preservation retains UV stream");
            }));
            Case("scale",()=>Cube((editor,doc,node)=>{
                editor.Components.Add(PositiveFace(node));var source=node.mesh.vertices;
                var symmetry=(StudioMeshSymmetry)Call(editor,"CaptureSymmetry",new object[]{source});var center=(Vector3)Call(editor,"ComponentSelectionCenter");
                var drag=Call(editor,"CaptureScaleDrag",0,center,Quaternion.identity,editor.SelectedVertices(),source);Set(editor,"scaleDrag",drag);
                Call(editor,"ApplyScaleDrag",new Vector3(1,1.4f,.6f));Symmetric(node,symmetry.Reflection,"Actual captured Scale drag reflects opposite-side frame");
                Call(editor,"ApplyScaleDrag",Vector3.one);Check(node.mesh.vertices.SequenceEqual(source),"Scale samples use immutable source rather than accumulating");
            }));
            Case("rotation",()=>Cube((editor,doc,node)=>{
                editor.Components.Add(PositiveFace(node));var source=node.mesh.vertices;var indices=editor.SelectedVertices();
                var symmetry=(StudioMeshSymmetry)Call(editor,"CaptureSymmetry",new object[]{source});var center=(Vector3)Call(editor,"ComponentSelectionCenter");
                var output=(Vector3[])source.Clone();foreach(int i in indices)output[i]=center+Quaternion.Euler(0,20,30)*(source[i]-center);
                Call(editor,"ApplyRotationSymmetry",output,indices);node.mesh.vertices=output;
                Symmetric(node,symmetry.Reflection,"Rotate uses reflected motion instead of the same rotation on both sides");
            }));
            Case("Poke and history",()=>Cube((editor,doc,node)=>{
                editor.Components.Add(PositiveFace(node));int count=node.mesh.vertexCount;
                editor.ToolbarCommands(null).Single(c=>c.Key=="Edit Mesh/Poke").Execute();
                Check(node.mesh.vertexCount==count+2,"Poke creates one center on each matching authored face");
                Check(node.modifiers.operations.Last().kind=="Poke"&&node.modifiers.operations.Last().faces.Length==2,"Compact Poke record captures both face IDs");
                var evaluated=StudioModifierEvaluator.Evaluate(node.modifiers);
                try{ReplayInfo(node,evaluated);Check(evaluated.mesh.vertices.SequenceEqual(node.mesh.vertices)&&evaluated.mesh.triangles.SequenceEqual(node.mesh.triangles),"Symmetric topology replays with symmetry mode absent");}finally{UnityEngine.Object.DestroyImmediate(evaluated.mesh);}
            }));
            Case("Extrude preview/cancel",()=>Cube((editor,doc,node)=>{
                editor.Components.Add(PositiveFace(node));var original=node.mesh.vertices;var triangles=node.mesh.triangles;
                var symmetry=(StudioMeshSymmetry)Call(editor,"CaptureSymmetry",new object[]{original});
                Set(editor,"extrusionDistance",.2f);var kind=Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",BindingFlags.NonPublic),"Extrude");
                Call(editor,"EvaluateLiveTool",kind);Symmetric(node,symmetry.Reflection,"Normal Extrude previews both matching faces");
                Check(new StudioMeshCuts(node).Faces.Count==14,"Both extruded faces retain authored quads and walls");
                Call(editor,"CancelLiveTool",false);Check(node.mesh.vertices.SequenceEqual(original)&&node.mesh.triangles.SequenceEqual(triangles),"Cancel restores both symmetric sides exactly");
            }));
            Case("Shift extrusion",()=>Cube((editor,doc,node)=>{
                editor.Components.Add(PositiveFace(node));var original=node.mesh.vertices;var center=(Vector3)Call(editor,"ComponentSelectionCenter");
                var symmetry=(StudioMeshSymmetry)Call(editor,"CaptureSymmetry",new object[]{original});Set(editor,"tool",Tool.Move);
                Call(editor,"BeginHandleExtrusion",center+new Vector3(.2f,.15f,.1f),center,Quaternion.identity,Quaternion.identity,Vector3.one);
                Symmetric(node,symmetry.Reflection,"Shift-drag Extrude reflects signed translation and wall tips");
            }));
            Case("face deletion",()=>Cube((editor,doc,node)=>{
                editor.Components.Add(PositiveFace(node));editor.DeleteSelection();
                Check(new StudioMeshCuts(node).Faces.Count==4,"Delete removes both matching authored faces");
            }));
            Case("edge selection",()=>Cube((editor,doc,node)=>{
                editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);var edges=(List<(int a,int b)>)Call(editor,"Edges");var source=node.mesh.vertices;
                int edge=edges.FindIndex(e=>source[e.a].x>0&&source[e.b].x>0);editor.Components.Add(edge);Call(editor,"ExpandSymmetrySelection");
                Check(editor.Components.Count==2,"Authored edges mirror by geometric endpoints");
            }));
            Case("Fill Hole",()=>Cube((editor,doc,node)=>{
                editor.Components.Add(PositiveFace(node));editor.DeleteSelection();editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);
                var edges=(List<(int a,int b)>)Call(editor,"Edges");var positions=node.mesh.vertices;
                foreach(int edge in Enumerable.Range(0,edges.Count).Where(i=>positions[edges[i].a].x>0&&positions[edges[i].b].x>0))editor.Components.Add(edge);
                editor.ToolbarCommands(null).Single(c=>c.Key=="Edit Mesh/Fill Hole").Execute();
                Check(new StudioMeshCuts(node).Faces.Count==6&&editor.Components.Count==2,"Fill Hole closes matching boundary loops and selects both caps");
            }));
            Case("Multi-Cut",()=>Cube((editor,doc,node)=>{
                int face=PositiveFace(node);Set(editor,"cutTopology",new StudioMeshCuts(node));Set(editor,"cutMesh",node.mesh);
                var strokes=new List<StudioMeshCuts.FaceStroke>{new StudioMeshCuts.FaceStroke(face,new Vector3(.5f,0,-.5f),new Vector3(.5f,0,.5f))};
                Call(editor,"CommitMultiCut",strokes);Check(new StudioMeshCuts(node).Faces.Count==8,"Multi-Cut divides the source face and its matching opposite face");
                Symmetric(node,Matrix4x4.Scale(new Vector3(-1,1,1)),"Multi-Cut creates matching boundary split vertices");
            }));
            Case("plane slice",()=>Cube((editor,doc,node)=>{
                Call(editor,"CommitMultiSlice",new Plane(Vector3.right,-.25f));
                Check(new StudioMeshCuts(node).Faces.Count==14,"Off-center plane slice commits both reflected cutting planes");
                Symmetric(node,Matrix4x4.Scale(new Vector3(-1,1,1)),"Plane slice vertices remain symmetric");
            }));
            Case("Target Weld",()=>Cube((editor,doc,node)=>{
                editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Vertex);var points=node.mesh.vertices;
                int a=Array.FindIndex(points,p=>p.x>0&&p.y<0&&p.z<0),b=Array.FindIndex(points,p=>p.x>0&&p.y>0&&p.z<0);
                Call(editor,"CommitTargetWeld",0,a,b,Call(editor,"CaptureSelection"));
                Symmetric(node,Matrix4x4.Scale(new Vector3(-1,1,1)),"Target Weld independently contracts the matching opposite pair");
                Check(node.modifiers.operations.Last().kind=="Direct","Target Weld retains its strict captured history");
            }));
            Case("Merge to Center",()=>Cube((editor,doc,node)=>{
                editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Vertex);var points=node.mesh.vertices;
                editor.Components.Add(Array.FindIndex(points,p=>p.x>0&&p.y<0&&p.z<0));editor.Components.Add(Array.FindIndex(points,p=>p.x>0&&p.y>0&&p.z<0));
                var kind=Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",BindingFlags.NonPublic),"Merge");Call(editor,"EvaluateLiveTool",kind);
                Symmetric(node,Matrix4x4.Scale(new Vector3(-1,1,1)),"Merge to Center contracts each mirrored pair independently");
                Check(node.mesh.vertices.Any(p=>p.x>.4f&&Mathf.Abs(p.y)<1e-5f)&&node.mesh.vertices.Any(p=>p.x<-.4f&&Mathf.Abs(p.y)<1e-5f),"Separate merge centers remain on their original sides");
                Call(editor,"CommitLiveTool",false);Check(node.modifiers.operations.Last().mirroredVertices!=null,"Paired merge has compact replay groups");
                var result=StudioModifierEvaluator.Evaluate(node.modifiers);try{ReplayInfo(node,result);Check(result.mesh.vertices.SequenceEqual(node.mesh.vertices)&&result.mesh.triangles.SequenceEqual(node.mesh.triangles),"Paired Merge replays exactly");}finally{UnityEngine.Object.DestroyImmediate(result.mesh);}
            }));
            Case("Circularize twist",()=>Cube((editor,doc,node)=>{
                editor.Components.Add(PositiveFace(node));ToolChannelPane.SetFloatDefault("Circularize","Twist",27);
                var kind=Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",BindingFlags.NonPublic),"Circularize");Call(editor,"EvaluateLiveTool",kind);
                Symmetric(node,Matrix4x4.Scale(new Vector3(-1,1,1)),"Circularize reflects twisted results instead of rotating both sides in the same handed frame");
                Call(editor,"CommitLiveTool",false);Check(node.modifiers.operations.Last().symmetry!=null,"Circularize retains compact reflected deformation in history");
                var result=StudioModifierEvaluator.Evaluate(node.modifiers);try{Check(result.mesh.vertices.SequenceEqual(node.mesh.vertices),"Circularize twist replays with captured symmetry frame");}finally{UnityEngine.Object.DestroyImmediate(result.mesh);}
                ToolChannelPane.SetFloatDefault("Circularize","Twist",0);
            }));
            Case("Bevel",()=>Cube((editor,doc,node)=>{
                editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);var edges=(List<(int a,int b)>)Call(editor,"Edges");var points=node.mesh.vertices;
                editor.Components.Add(edges.FindIndex(e=>points[e.a].x>0&&points[e.b].x>0&&points[e.a].y>0&&points[e.b].y>0));
                var kind=Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",BindingFlags.NonPublic),"Bevel");Call(editor,"EvaluateLiveTool",kind);
                Symmetric(node,Matrix4x4.Scale(new Vector3(-1,1,1)),"Bevel constructs matching strips on the mirrored authored edges");
                Call(editor,"CancelLiveTool",false);
            }));
            Case("paired Bridge",()=>Cube((editor,doc,node)=>{
                var old=node.mesh;var mesh=new Mesh();var points=new List<Vector3>();var polygons=new List<int>();var triangles=new List<int>();
                foreach(int side in new[]{1,-1})foreach(float y in new[]{-2f,1f})
                {
                    int start=points.Count;
                    var quad=new[]{new Vector3(1,y,0),new Vector3(2,y,0),new Vector3(2,y+1,0),new Vector3(1,y+1,0)}.Select(p=>new Vector3(p.x*side,p.y,p.z)).ToArray();
                    if(side<0)Array.Reverse(quad);points.AddRange(quad);polygons.AddRange(new[]{0,4,start,start+1,start+2,start+3});triangles.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
                }
                mesh.SetVertices(points);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();node.mesh=mesh;node.cutPolygons=polygons.ToArray();node.cutTriangles=mesh.triangles;node.geometryVertices=Enumerable.Range(0,points.Count).ToArray();node.normalGroups=null;node.modifiers=null;
                editor.Load(doc);UnityEngine.Object.DestroyImmediate(old);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);
                var edges=(List<(int a,int b)>)Call(editor,"Edges");
                foreach(int edge in Enumerable.Range(0,edges.Count).Where(i=>points[edges[i].a].x>0&&points[edges[i].b].x>0&&Mathf.Abs(points[edges[i].a].y)==1&&points[edges[i].a].y==points[edges[i].b].y))editor.Components.Add(edge);
                Check(editor.Components.Count==2,"Bridge fixture selects two original boundary chains");Call(editor,"BridgeSelectedEdges");
                Check(new StudioMeshCuts(node).Faces.Count==6,"Bridge independently joins the two matching boundary pairs");
                Check(node.modifiers.operations.Last().mirroredEdges.Length==4,"Bridge compact history retains the second pair");
                var result=StudioModifierEvaluator.Evaluate(node.modifiers);try{ReplayInfo(node,result);Check(result.mesh.triangles.SequenceEqual(node.mesh.triangles)&&result.mesh.vertices.SequenceEqual(node.mesh.vertices),"Paired Bridge replays both sides");}finally{UnityEngine.Object.DestroyImmediate(result.mesh);}
            }));
            Case("restrictions",()=>Cube((editor,doc,node)=>{
                editor.Components.Add(PositiveFace(node));var original=node.mesh.vertices;doc.AddPrimitive(PrimitiveType.Cube);((HashSet<int>)Get(editor,"objects")).Add(1);
                Reject(()=>Call(editor,"ExpandSymmetrySelection"),"Multiple selected objects rejected before mutation");
                Check(node.mesh.vertices.SequenceEqual(original),"Multi-object rejection leaves source geometry intact");
                ((HashSet<int>)Get(editor,"objects")).Remove(1);doc.nodes[1].mesh=node.mesh;
                Reject(()=>Call(editor,"ExpandSymmetrySelection"),"Linked mesh instances rejected before mutation");
            }));
            Case("radial catalog",()=>Cube((editor,doc,node)=>{
                var preset=StudioRadialMenus.Defaults().Single(p=>p.id=="symmetry");
                Check(preset.commands.SequenceEqual(new[]{"Modify/Symmetry/Enabled","Modify/Symmetry/Space","Modify/Symmetry/X","Modify/Symmetry/Y","Modify/Symmetry/Z"}),"Symmetry radial has enable north, position toggle, X/Y/Z in order");
                var definitions=StudioRadialMenus.Read();Check(definitions.Any(p=>p.id=="symmetry"),"Symmetry available through the existing preset catalog");
                // Batch mode can expose synthetic mouse events as Ignore outside
                // a GUI callback. Supply the matching key code for this binding
                // check; this does not claim a physical pointer-capture replay.
                var evt=new Event{type=EventType.MouseDown,button=1,keyCode=KeyCode.Mouse1,modifiers=EventModifiers.Control|EventModifiers.Shift};
                Check(!StudioShortcuts.Matches(StudioRadialMenus.Shortcut("symmetry"),evt),"Symmetry submenu has no factory activation chord");
                Check(StudioShortcuts.Matches(StudioShortcut.AxisMenu,evt),"Ctrl+Shift+RMB retains the original Orientation menu");
                Check(StudioRadialMenus.Defaults().Single(p=>p.id=="axes").commands.SequenceEqual(new[]{"RadialPreset/symmetry","Legacy/ComponentAxes","Legacy/WorldAxes","Legacy/ObjectAxes","Legacy/CenterAxes"}),"Orientation gains a leading Symmetry submenu and keeps its original choices/order");
                var commands=editor.RadialCommands(null);Check(commands.Single(c=>c.Key=="Modify/Symmetry/Space").Label=="Position: Object","Radial shows current object positioning");
                commands.Single(c=>c.Key=="Modify/Symmetry/Space").Execute();Check((bool)Get(editor,"symmetryWorld"),"Shared radial command swaps positioning");
                StudioShortcuts.Set(StudioShortcut.AxisMenu,new[]{new StudioShortcuts.Binding(KeyCode.Mouse1,StudioModifiers.Action|StudioModifiers.Shift)});
                Check(!StudioShortcuts.Matches(StudioRadialMenus.Shortcut("symmetry"),evt)&&StudioShortcuts.Matches(StudioShortcut.AxisMenu,evt),"Explicit custom Orientation binding wins without rewriting profile");
                StudioShortcuts.Reset(StudioShortcut.AxisMenu);
            }));
            Directory.CreateDirectory(DirectoryPath);File.WriteAllLines(Path.Combine(DirectoryPath,"result.txt"),report.Concat(new[]{"Failures: "+failures}));
            EditorApplication.Exit(failures==0?0:1);
        }
    }
}

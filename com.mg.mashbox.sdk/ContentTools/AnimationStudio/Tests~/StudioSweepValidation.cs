using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public sealed class StudioSweepValidation:EditorWindow
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static readonly List<string> log=new List<string>();bool ran;
        static string Out(string file)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../../Sweep/"+file));
        public static void Begin(){if(!Application.dataPath.Replace('\\','/').EndsWith("/Splines/SplineProject/Assets"))throw new Exception("Disposable project only.");var w=GetWindow<StudioSweepValidation>(true,"Sweep validation",false);w.position=new Rect(20,20,620,650);w.Repaint();}
        static void Check(bool value,string name){if(!value)throw new Exception(name);log.Add("PASS "+name);}
        static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,F).Invoke(o,args);
        static void Set(object o,string name,object value)=>o.GetType().GetField(name,F).SetValue(o,value);
        static T Get<T>(object o,string name)=>(T)o.GetType().GetField(name,F).GetValue(o);
        static StudioSpline Curve()=>new StudioSpline{kind=StudioSplineKind.Bezier,knots=new List<StudioSpline.Knot>{new StudioSpline.Knot{position=new Vector3(-2,0,0),outgoing=new Vector3(1,2,0)},new StudioSpline.Knot{position=new Vector3(2,0,0),incoming=new Vector3(-1,2,0)}}};
        static bool Same(Vector3[] a,Vector3[] b)=>a.Length==b.Length&&a.Zip(b,(x,y)=>(x-y).sqrMagnitude<1e-9f).All(x=>x);
        static byte[] RenderMesh(Mesh mesh,string file)
        {
            var preview=new PreviewRenderUtility();var material=new Material(Shader.Find("Standard"));material.color=new Color(.8f,.55f,.18f);
            try
            {
                preview.camera.transform.SetPositionAndRotation(new Vector3(0,1,-8),Quaternion.identity);preview.camera.fieldOfView=45;preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=100;
                preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.12f,.13f,.15f);
                preview.lights[0].intensity=1.4f;preview.lights[0].transform.rotation=Quaternion.Euler(35,30,0);preview.lights[1].intensity=.8f;
                preview.BeginPreview(new Rect(0,0,640,480),GUIStyle.none);preview.DrawMesh(mesh,Matrix4x4.identity,material,0);preview.Render();var texture=preview.EndPreview();
                var previous=RenderTexture.active;RenderTexture.active=texture as RenderTexture;var image=new Texture2D(640,480,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,640,480),0,0);image.Apply();var bytes=image.EncodeToPNG();File.WriteAllBytes(Out(file),bytes);DestroyImmediate(image);RenderTexture.active=previous;return bytes;
            }
            finally{DestroyImmediate(material);preview.Cleanup();}
        }
        void OnGUI()
        {
            if(ran||Event.current.type!=EventType.Repaint)return;ran=true;var original=Event.current;
            StudioModelEditor editor=null;StudioModelDocument doc=null;SceneView view=null;
            try
            {
                var straight=new StudioSpline{knots=new List<StudioSpline.Knot>{new StudioSpline.Knot(),new StudioSpline.Knot{position=Vector3.forward*4}}};
                var p=new StudioSweepSettings{sides=8,subdivisions=8};var mesh=StudioSweep.Generate(straight,Matrix4x4.identity,p);
                Check(mesh.mesh.vertexCount==9*9+16,"Straight sweep render seam and cap vertices");
                var faces=new List<int[]>();for(int i=0;i<mesh.cutPolygons.Length;){int n=mesh.cutPolygons[i+1];faces.Add(mesh.cutPolygons.Skip(i+2).Take(n).ToArray());i+=n+2;}
                Check(faces.Count==66&&faces.Take(64).All(f=>f.Length==4)&&faces.Skip(64).All(f=>f.Length==8),"Genuine side quads and single polygon caps");
                Check(mesh.geometryVertices[0]==mesh.geometryVertices[8]&&mesh.mesh.uv[0].x==0&&mesh.mesh.uv[8].x==1,"Profile UV seam shares geometric identity");
                Check(mesh.geometryVertices[81]==7&&mesh.mesh.normals[81].z<-.99f,"Cap perimeter identity and outward normal");
                DestroyImmediate(mesh.mesh);
                var curve=Curve();var path=StudioSweep.SamplePath(curve,Matrix4x4.identity,64);
                Check(path.normal.Zip(path.tangent,(n,t)=>Mathf.Abs(Vector3.Dot(n,t))<1e-4f).All(x=>x),"Parallel transport frames remain orthogonal");
                Check(path.normal.Zip(path.normal.Skip(1),(a,b)=>Vector3.Dot(a,b)>0).All(x=>x),"Curved frame continuity");
                var closed=new StudioSpline{closed=true,knots=new List<StudioSpline.Knot>{new StudioSpline.Knot{position=Vector3.left},new StudioSpline.Knot{position=Vector3.up},new StudioSpline.Knot{position=Vector3.right},new StudioSpline.Knot{position=Vector3.down}}};
                p.twist=360;mesh=StudioSweep.Generate(closed,Matrix4x4.identity,p);int rings=StudioSweep.SamplePath(closed,Matrix4x4.identity,p.subdivisions).points.Count;
                Check(mesh.geometryVertices[0]==mesh.geometryVertices[rings*9]&&(mesh.mesh.vertices[0]-mesh.mesh.vertices[rings*9]).sqrMagnitude<1e-9f,"Closed longitudinal seam shares positions and identities");DestroyImmediate(mesh.mesh);
                p.twist=30;bool rejected=false;try{mesh=StudioSweep.Generate(closed,Matrix4x4.identity,p);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Incompatible closed twist rejected");p.twist=0;
                p.taper=AnimationCurve.Linear(0,1,1,.5f);rejected=false;try{mesh=StudioSweep.Generate(closed,Matrix4x4.identity,p);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Incompatible closed taper rejected");
                p.taper=AnimationCurve.Linear(0,1,1,1);
                var transformed=Matrix4x4.TRS(new Vector3(2,3,4),Quaternion.Euler(15,30,45),new Vector3(2,3,1));
                mesh=StudioSweep.Generate(straight,transformed,p);var center=mesh.mesh.vertices.Take(8).Aggregate(Vector3.zero,(a,b)=>a+b)/8;
                Check((center-transformed.MultiplyPoint3x4(Vector3.zero)).sqrMagnitude<1e-9f,"Transformed source sampled in world coordinates");DestroyImmediate(mesh.mesh);
                doc=CreateInstance<StudioModelDocument>();doc.nodes.Add(new StudioModelDocument.Node{name="Curve",spline=curve});
                editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);
                p.sourceId=doc.nodes[0].constraintId;p.subdivisions=32;Set(editor,"sweepParameters",p);Set(editor,"sweepCreating",true);Call(editor,"PreviewSweep");
                Check(doc.nodes.Count==2&&doc.nodes[1].mesh.vertexCount>0,"Creation preview publishes separate mesh");
                Call(editor,"CancelSweep");Check(doc.nodes.Count==1&&doc.nodes[0].spline==curve,"Creation Cancel preserves source");
                Set(editor,"sweepParameters",p);Set(editor,"sweepCreating",true);Call(editor,"PreviewSweep");Call(editor,"ApplySweep");
                Check(doc.nodes[1].modifiers.HasHistory&&string.IsNullOrEmpty(doc.nodes[1].modifiers.source?.mesh),"Apply retains compact procedural generator without mesh source snapshots");
                var before=doc.nodes[1].mesh.vertices;var beforePixels=RenderMesh(doc.nodes[1].mesh,"before.png");editor.SelectObject(0);Call(editor,"EditSpline");Set(editor,"tool",Tool.None);
                view=CreateInstance<SceneView>();view.camera.transform.SetPositionAndRotation(new Vector3(0,0,-10),Quaternion.identity);view.camera.nearClipPlane=.01f;view.camera.farClipPlane=100;view.camera.pixelRect=new Rect(0,0,600,500);Handles.SetCamera(new Rect(0,0,600,500),view.camera);
                var world=editor.Transforms[0].TransformPoint(curve.knots[0].position);var mouse=HandleUtility.WorldToGUIPoint(world);
                Event.current=new Event{type=EventType.MouseDown,button=0,mousePosition=mouse};Call(editor,"SplineSceneGUI",view);
                var dragWatch=System.Diagnostics.Stopwatch.StartNew();
                for(int step=1;step<=12;step++){Event.current=new Event{type=EventType.MouseDrag,button=0,mousePosition=HandleUtility.WorldToGUIPoint(world+Vector3.up*(.5f*step/12))};Call(editor,"SplineSceneGUI",view);}
                dragWatch.Stop();log.Add("TIMING 12 native captured knot-drag updates, 32 subdivisions / 8 sides: "+dragWatch.Elapsed.TotalMilliseconds.ToString("F2")+" ms total");
                Check(GUIUtility.hotControl!=0&&!Same(before,doc.nodes[1].mesh.vertices),"Knot drag updates sweep visibly before mouse release");
                Check(!beforePixels.SequenceEqual(RenderMesh(doc.nodes[1].mesh,"during-knot.png")),"Native rendered pixels change during captured knot drag");
                Event.current=new Event{type=EventType.MouseUp,button=0,mousePosition=HandleUtility.WorldToGUIPoint(world+Vector3.up*.5f)};Call(editor,"SplineSceneGUI",view);
                UnityEditor.Undo.FlushUndoRecordObjects();StudioUndo.IncrementCurrentGroup();StudioUndo.PerformUndo();Check(Same(before,doc.nodes[1].mesh.vertices),"One source drag Undo reevaluates sweep");StudioUndo.PerformRedo();Check(!Same(before,doc.nodes[1].mesh.vertices),"Redo reevaluates sweep");
                editor.SelectObject(0);Call(editor,"EditSpline");Set(editor,"tool",Tool.None);Get<HashSet<int>>(editor,"splineKnots").Add(0);
                var knot=doc.nodes[0].spline.knots[0];var tip=editor.Transforms[0].TransformPoint(knot.position+knot.outgoing);before=doc.nodes[1].mesh.vertices;
                Event.current=new Event{type=EventType.MouseDown,button=0,mousePosition=HandleUtility.WorldToGUIPoint(tip)};Call(editor,"SplineSceneGUI",view);
                Event.current=new Event{type=EventType.MouseDrag,button=0,mousePosition=HandleUtility.WorldToGUIPoint(tip+Vector3.right*.3f)};Call(editor,"SplineSceneGUI",view);
                Check(GUIUtility.hotControl!=0&&!Same(before,doc.nodes[1].mesh.vertices),"Tangent drag updates sweep before release");
                RenderMesh(doc.nodes[1].mesh,"during-tangent.png");
                Event.current=new Event{type=EventType.MouseUp,button=0,mousePosition=HandleUtility.WorldToGUIPoint(tip+Vector3.right*.3f)};Call(editor,"SplineSceneGUI",view);
                var node=doc.nodes[1];before=node.mesh.vertices;var saved=node.modifiers.Copy();Set(editor,"sweepTarget",node);Set(editor,"sweepBefore",saved);Set(editor,"sweepCreating",false);var edited=p.Copy();edited.radius=.3f;Set(editor,"sweepParameters",edited);Call(editor,"PreviewSweep");Check(!Same(before,node.mesh.vertices),"Parameter edit updates geometry");Call(editor,"CancelSweep");Check(Same(before,node.mesh.vertices),"Parameter Cancel restores evaluated geometry");
                int duplicate=doc.DuplicateObjects(new[]{1})[0];Call(editor,"Rebuild");Check(doc.nodes[duplicate].modifiers.operations[0].sweep.sourceId==doc.nodes[0].constraintId&&!ReferenceEquals(doc.nodes[duplicate].modifiers,node.modifiers),"Sweep-only duplication retains independent source link");
                int count=doc.nodes.Count;doc.DuplicateObjects(new[]{0,1});Call(editor,"Rebuild");Check(doc.nodes[count+1].modifiers.operations[0].sweep.sourceId==doc.nodes[count].constraintId,"Source and sweep duplication remaps stable link");
                var cycle=new Dictionary<int,int>{{0,1}};bool cycleRejected=false;try{StudioSweepDependencies.Validate(doc,cycle);}catch(InvalidOperationException){cycleRejected=true;}Check(cycleRejected,"Reparent dependency cycle rejected before publication");
                var src=doc.nodes[0];doc.nodes.RemoveAt(0);Call(editor,"Rebuild");Check(node.mesh.vertexCount==0&&!string.IsNullOrEmpty(node.modifiers.operations[0].failure),"Missing source clears stale mesh and reports recoverable error");doc.nodes.Insert(0,src);Call(editor,"Rebuild");Check(node.mesh.vertexCount>0&&string.IsNullOrEmpty(node.modifiers.operations[0].failure),"Restored source recovers sweep");
                var sourceBefore=src.spline.Copy();src.spline.Insert(0,.4f);editor.Changed(false);Check(node.mesh.vertexCount>0&&string.IsNullOrEmpty(node.modifiers.operations[0].failure),"Inserted source knot reevaluates sweep");src.spline.Remove(new[]{1});src.spline.Reverse();editor.Changed(false);Check(node.mesh.vertexCount>0&&string.IsNullOrEmpty(node.modifiers.operations[0].failure),"Removed knot and reversed source reevaluate sweep");src.spline.SetKind(StudioSplineKind.Linear);editor.Changed(false);Check(node.mesh.vertexCount>0&&string.IsNullOrEmpty(node.modifiers.operations[0].failure),"Spline type change reevaluates sweep");src.spline=sourceBefore;editor.Changed(false);
                StudioUndo.IncrementCurrentGroup();StudioUndo.RegisterCompleteObjectUndo(doc,"Delete Sweep Source Validation");doc.nodes.RemoveAt(0);Call(editor,"Rebuild");UnityEditor.Undo.FlushUndoRecordObjects();StudioUndo.IncrementCurrentGroup();StudioUndo.PerformUndo();node=doc.nodes[1];src=doc.nodes[0];Check(node.mesh.vertexCount>0&&string.IsNullOrEmpty(node.modifiers.operations[0].failure),"Undo source deletion restores relationship and evaluation");
                var worldBefore=node.mesh.vertices.Select(v=>editor.Transforms[1].TransformPoint(v)).ToArray();
                int parent=doc.GroupObjects(new[]{0,1},editor.Transforms,Vector3.zero);Call(editor,"Rebuild");
                doc.nodes[parent].position+=new Vector3(2,3,0);doc.ApplyTransform(parent,editor.Transforms[parent]);editor.Changed(false);
                Check(Same(worldBefore.Select(v=>v+new Vector3(2,3,0)).ToArray(),node.mesh.vertices.Select(v=>editor.Transforms[1].TransformPoint(v)).ToArray()),"Shared parent transforms source and sweep exactly once");
                worldBefore=node.mesh.vertices.Select(v=>editor.Transforms[1].TransformPoint(v)).ToArray();doc.Repivot(1,editor.Transforms,new Vector3(1,1,1));editor.Changed(false);
                Check(Same(worldBefore,node.mesh.vertices.Select(v=>editor.Transforms[1].TransformPoint(v)).ToArray()),"Live sweep pivot compensation preserves world geometry");
                src.spline.knots[0].position+=Vector3.up*.1f;editor.Changed(false);Check(node.modifiers.operations.All(o=>string.IsNullOrEmpty(o.failure)),"Sweep pivot compensation replays after source edit");
                var operation=new StudioModifierOperation{kind="Transform",name="Move",offsets=new Vector3[node.mesh.vertexCount]};operation.Bind(node);node.modifiers.operations.Add(operation);src.spline.knots[0].position+=Vector3.up;editor.Changed(false);Check(string.IsNullOrEmpty(operation.failure),"Downstream modifier reevaluates matching topology");node.modifiers.operations[0].sweep.sides++;editor.Changed(false);Check(!string.IsNullOrEmpty(operation.failure),"Topology change reports strict downstream input failure");node.modifiers.operations.Remove(operation);
                string file=Path.Combine(StudioModelDocument.WorkspaceFolder,"SweepValidation.workspace");Directory.CreateDirectory(Path.GetDirectoryName(file));doc.SaveAs(file);var loaded=StudioWorkspaceText.Read(file);Check(loaded.nodes[1].modifiers.operations[0].sweep.sourceId==loaded.nodes[0].constraintId,"Workspace round trip retains source and parameters");DestroyImmediate(loaded);
                editor.SelectObject(1);before=node.mesh.vertices;Call(editor,"ClearModifierHistory");log.Add("BAKE sameNode="+ReferenceEquals(node,doc.nodes[1])+" history="+(node.modifiers?.HasHistory??false)+" vertices="+Same(before,node.mesh.vertices));Check(node.modifiers?.HasHistory!=true&&Same(before,node.mesh.vertices),"Clear History bakes final geometry");
                var watch=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<30;i++){src.spline.knots[0].position+=Vector3.up*.001f;editor.Changed(false);}watch.Stop();log.Add("TIMING 30 source edits with remaining dependent sweeps: "+watch.Elapsed.TotalMilliseconds.ToString("F2")+" ms");
                Check(editor.ToolbarCommands(null).Any(c=>c.Key=="Curves/Sweep Mesh"),"Sweep command is in shared catalog");
            }
            catch(Exception ex){log.Add("FAIL "+ex);}
            finally{Event.current=original;GUIUtility.hotControl=0;editor?.Dispose();if(view)DestroyImmediate(view);if(doc)DestroyImmediate(doc);File.WriteAllLines(Out("result.txt"),log);EditorApplication.delayCall+=()=>EditorApplication.Exit(log.Any(x=>x.StartsWith("FAIL"))?1:0);}
        }
    }
}

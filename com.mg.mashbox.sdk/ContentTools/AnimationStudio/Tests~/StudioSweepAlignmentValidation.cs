using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed class StudioSweepAlignmentHost:EditorWindow
    {
        internal StudioModelEditor editor;internal StudioModelDocument.Node owner;internal StudioSweepSettings settings;
        void OnGUI()
        {
            if(editor==null)return;var f=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(StudioModelEditor).GetMethod("DrawHierarchyDragRow",f).Invoke(editor,new object[]{new Rect(10,25,300,22),new GUIContent("Profile"),EditorStyles.miniButton,1});
            typeof(StudioModelEditor).GetMethod("TrySweepProfileDrop",f).Invoke(editor,new object[]{new Rect(10,90,300,22),settings,owner});
        }
    }
    internal static class StudioSweepAlignmentEvents
    {
        static StudioSweepAlignmentHost host;static StudioModelEditor editor;static StudioModelDocument doc;static int step;static double next;
        static readonly string result=Path.GetFullPath(Path.Combine(Application.dataPath,"../../SweepAlignment/result.txt"));
        static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(editor,args);
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);File.AppendAllText(result,"PASS "+name+"\n");}
        internal static void Start()
        {
            try{
            doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(new StudioModelDocument.Node{name="Path",spline=new StudioSpline{knots=new List<StudioSpline.Knot>{new StudioSpline.Knot(),new StudioSpline.Knot{position=Vector3.forward*4}}}});
            var mesh=new Mesh{vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)},triangles=new[]{0,1,2,0,2,3}};mesh.RecalculateNormals();
            doc.nodes.Add(new StudioModelDocument.Node{name="Profile",mesh=mesh,cutPolygons=new[]{0,4,0,1,2,3},cutTriangles=mesh.triangles,geometryVertices=new[]{0,1,2,3}});
            editor=new StudioModelEditor(()=>{if(host)host.Repaint();});editor.Load(doc);editor.SelectObject(0);Call("ActivateTool",Tool.Move);Call("OpenSweepTool",null,null);
            host=ScriptableObject.CreateInstance<StudioSweepAlignmentHost>();host.editor=editor;host.owner=doc.nodes[2];host.settings=host.owner.modifiers.operations[0].sweep.Copy();host.position=new Rect(30,30,360,250);host.ShowUtility();
            typeof(StudioModelEditor).GetField("beginHierarchyDrag",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(editor,(Action<string>)(_=>{}));
            next=EditorApplication.timeSinceStartup+1;EditorApplication.update+=Tick;
            }catch(Exception e){Finish(e);}
        }
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.2;
            try{
            switch(step++){
            case 0:host.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=new Vector2(35,35)});break;
            case 1:host.SendEvent(new Event{type=EventType.MouseDrag,button=0,mousePosition=new Vector2(70,35),delta=new Vector2(35,0)});Check(DragAndDrop.GetGenericData("MashBoxStudio.HierarchyDrag")!=null,"Native hierarchy drag creates payload");Check(editor.Active==2,"Hierarchy drag keeps Sweep destination selected");break;
            case 2:host.SendEvent(new Event{type=EventType.DragUpdated,mousePosition=new Vector2(70,100)});Check(DragAndDrop.visualMode==DragAndDropVisualMode.Link,"Profile field accepts hierarchy payload");break;
            case 3:host.SendEvent(new Event{type=EventType.DragPerform,mousePosition=new Vector2(70,100)});Check(host.settings.profileId==doc.nodes[1].constraintId&&editor.Active==2,"DragPerform assigns source without changing selected Sweep");Call("ChangeModifier",host.owner,host.owner.modifiers.operations[0].id,(Action<StudioModifierOperation>)(o=>o.sweep=host.settings),false);Check(host.owner.mesh.vertexCount>0&&editor.ToolOptionsTitle=="Move Tool","Dropped source generates geometry without tool lockout");Check(doc.nodes[1].parent==-1,"Source assignment does not reparent hierarchy");Finish(null);break;
            }
            }catch(Exception e){Finish(e);}
        }
        static void Finish(Exception e){EditorApplication.update-=Tick;if(e!=null)File.AppendAllText(result,"FAIL "+e+"\n");if(host)host.Close();editor?.Dispose();if(doc)UnityEngine.Object.DestroyImmediate(doc);EditorApplication.Exit(e==null?0:1);}
    }
    public sealed class StudioSweepAlignmentValidation:EditorWindow
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static readonly List<string> log=new List<string>();bool ran;
        static string Out(string file)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../SweepAlignment/"+file));
        public static void Begin(){if(!Application.dataPath.Replace('\\','/').EndsWith("/SymmetryProject/Assets"))throw new Exception("Disposable project only.");var w=GetWindow<StudioSweepAlignmentValidation>(true,"Sweep validation",false);w.position=new Rect(20,20,620,650);w.Repaint();}
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

        static void ProfileChecks()
        {
            var curve=new StudioSpline{knots=new List<StudioSpline.Knot>{new StudioSpline.Knot(),new StudioSpline.Knot{position=Vector3.forward*4}}};
            var settings=new StudioSweepSettings{sides=-1,subdivisions=-1,radius=-1,profileWidth=-1,profileHeight=float.NaN,twist=float.PositiveInfinity};settings.ClampFields();
            Check(settings.sides==4&&settings.subdivisions==1&&settings.radius>0&&settings.profileWidth>0&&float.IsFinite(settings.profileHeight)&&settings.twist==0,"Input bounds clamp negative counts, dimensions and nonfinite angles");
            settings.sides=999;settings.subdivisions=99999;settings.twist=999999;settings.ClampFields();Check(settings.sides==256&&settings.subdivisions==4096&&settings.twist==360000,"Upper count and twist bounds");
            settings=new StudioSweepSettings{sides=3};bool rejected=false;try{StudioSweep.Generate(curve,Matrix4x4.identity,settings);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Generator rejects fewer than four tube sides");
            settings=new StudioSweepSettings{profileShape=1,profileWidth=2,subdivisions=4};var node=StudioSweep.Generate(curve,Matrix4x4.identity,settings);
            RenderMesh(node.mesh,"ribbon.png");Check(node.mesh.vertexCount==10&&new StudioMeshCuts(node).Faces.Count==4,"Ribbon produces open quad strip without caps");
            Check(Mathf.Abs(Vector3.Distance(node.mesh.vertices[0],node.mesh.vertices[1])-2)<1e-5,"Ribbon width respected");DestroyImmediate(node.mesh);
            settings.profileShape=2;settings.profileHeight=1;node=StudioSweep.Generate(curve,Matrix4x4.identity,settings);
            RenderMesh(node.mesh,"square.png");Check(new StudioMeshCuts(node).Faces.Count==18&&new StudioMeshCuts(node).Faces.All(f=>f.Vertices.Count==4),"Square produces genuine quads and quad caps");DestroyImmediate(node.mesh);
            for(int shape=0;shape<4;shape++)for(int h=0;h<3;h++)for(int v=0;v<3;v++)
            {
                var custom=new[]{new Vector3(-2,-1,0),new Vector3(2,-1,0),new Vector3(1,2,0),new Vector3(-1,1,0)};
                var aligned=new StudioSweepSettings{profileShape=shape,profileClosed=true,subdivisions=1,align=true,horizontalAlignment=h,verticalAlignment=v,horizontalOffset=.3f,verticalOffset=.4f};
                var result=StudioSweep.Generate(curve,Matrix4x4.identity,aligned,null,shape==3?custom:null);
                int count=shape==0?aligned.sides+1:shape==1?2:5;
                var ring=result.mesh.vertices.Take(count).ToArray();float left=ring.Min(p=>p.x),right=ring.Max(p=>p.x),bottom=ring.Min(p=>p.y),top=ring.Max(p=>p.y);
                Check(Mathf.Abs((h==1?left:h==2?right:(left+right)*.5f)-.3f)<1e-5f&&Mathf.Abs((v==1?top:v==2?bottom:(bottom+top)*.5f)-.4f)<1e-5f,"Alignment anchors and offsets shape="+shape+" horizontal="+h+" vertical="+v);
                DestroyImmediate(result.mesh);
            }
            var off=new StudioSweepSettings{profileShape=2,align=false,horizontalOffset=20,verticalOffset=-10};
            var offMesh=StudioSweep.Generate(curve,Matrix4x4.identity,off);var defaultMesh=StudioSweep.Generate(curve,Matrix4x4.identity,new StudioSweepSettings{profileShape=2});
            Check(Same(offMesh.mesh.vertices,defaultMesh.mesh.vertices),"Align off preserves existing placement and ignores offsets");DestroyImmediate(offMesh.mesh);DestroyImmediate(defaultMesh.mesh);
            off.align=true;off.horizontalAlignment=2;off.verticalAlignment=1;var copy=off.Copy();Check(copy.align&&copy.horizontalAlignment==2&&copy.verticalAlignment==1&&copy.horizontalOffset==20&&copy.verticalOffset==-10,"Alignment survives settings serialization");
            off.horizontalOffset=float.NaN;off.verticalOffset=float.PositiveInfinity;off.horizontalAlignment=99;off.verticalAlignment=-1;off.ClampFields();Check(off.horizontalOffset==0&&off.verticalOffset==0&&off.horizontalAlignment==2&&off.verticalAlignment==0,"Alignment clamps invalid inputs");
            var road=new[]{new Vector3(-2,1,0),new Vector3(-2,-.2f,0),new Vector3(2,-.2f,0),new Vector3(2,1,0),new Vector3(1.7f,1,0),new Vector3(1.7f,0,0),new Vector3(-1.7f,0,0),new Vector3(-1.7f,1,0)};
            foreach(bool reversed in new[]{false,true})
            {
                var profile=reversed?road.Reverse().ToArray():road;
                var roadSettings=new StudioSweepSettings{profileShape=3,profileClosed=true,subdivisions=2};
                var generated=StudioSweep.Generate(curve,Matrix4x4.identity,roadSettings,null,profile);
                Check(profile.Select((v,i)=>Mathf.Abs(generated.mesh.vertices[i].y-v.y)<1e-5f).All(v=>v),"Upright road walls at zero roll, reversed="+reversed);
                var normals=generated.mesh.normals;int capStart=3*9;
                Check(normals.Skip(capStart).Take(8).All(n=>n.z<-.99f)&&normals.Skip(capStart+8).All(n=>n.z>.99f),"Outward start/end caps regardless of perimeter winding, reversed="+reversed);
                DestroyImmediate(generated.mesh);
            }
            var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-1,-.5f,0),new Vector3(1,-.5f,0),new Vector3(1,.5f,0),new Vector3(-1,.5f,0)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();
            var source=new StudioModelDocument.Node{name="Profile",mesh=mesh,cutPolygons=new[]{0,4,0,1,2,3},cutTriangles=mesh.triangles,geometryVertices=new[]{0,1,2,3}};
            var doc=CreateInstance<StudioModelDocument>();doc.nodes.Add(new StudioModelDocument.Node{name="Path",spline=curve});doc.nodes.Add(source);
            var editor=new StudioModelEditor(()=>{});editor.Load(doc);
            try
            {
                editor.SelectObject(1);Set(editor,"mode",StudioModelEditor.ComponentMode.Face);editor.Components.Add(0);
                settings=new StudioSweepSettings{sourceId=doc.nodes[0].constraintId,subdivisions=4};Call(editor,"CaptureSweepProfile",settings);
                Check(settings.profileShape==3&&settings.profileClosed&&settings.profileVertices.Length==4,"Face selection captures live perimeter and stable source ID");
                Check(Vector3.Dot(settings.profileWorldY,Vector3.up)>.999f,"Captured profile up is independent of first boundary edge");
                var xy=(Vector3[])Call(editor,"ResolveSweepProfile",settings);Check(xy.Length==4,"Live source resolves profile points");
                Set(editor,"sweepParameters",settings);Set(editor,"sweepCreating",true);Call(editor,"PreviewSweep");Call(editor,"ApplySweep");var output=doc.nodes.Last();var before=output.mesh.vertices;
                var vertices=mesh.vertices;vertices[1]+=Vector3.right;vertices[2]+=Vector3.right;mesh.vertices=vertices;mesh.RecalculateBounds();editor.Changed(false);
                Check(!Same(before,output.mesh.vertices)&&output.modifiers.operations.All(o=>string.IsNullOrEmpty(o.failure)),"Editing source face vertices updates linked sweep synchronously");
                before=output.mesh.vertices;source.position+=Vector3.right*5;doc.ApplyTransform(1,editor.Transforms[1]);editor.Changed(false);Check(Same(before,output.mesh.vertices),"Profile translation keeps cross section centered on path");
                source.scale=new Vector3(2,1,1);doc.ApplyTransform(1,editor.Transforms[1]);editor.Changed(false);Check(!Same(before,output.mesh.vertices),"Profile scale updates cross section size");
                before=output.mesh.vertices;source.rotation=Quaternion.Euler(25,0,30);doc.ApplyTransform(1,editor.Transforms[1]);editor.Changed(false);
                Check(!Same(before,output.mesh.vertices)&&output.mesh.vertexCount>0&&string.IsNullOrEmpty(output.modifiers.operations[0].failure),"Object rotation changes live profile including tilted offsets");
                source.rotation=Quaternion.identity;doc.ApplyTransform(1,editor.Transforms[1]);editor.Changed(false);
                editor.SelectObject(1);before=output.mesh.vertices;Call(editor,"ChangeComponentMode",StudioModelEditor.ComponentMode.Vertex,null);
                Set(editor,"moveDrag",Call(editor,"CaptureMoveDrag",0,Vector3.zero,Quaternion.identity,new HashSet<int>{1},mesh.vertices,false));
                Call(editor,"ApplyMoveDrag",new Vector3(.2f,.15f,.1f));Set(editor,"moveDrag",null);
                Check(!Same(before,output.mesh.vertices)&&output.mesh.vertexCount>0&&string.IsNullOrEmpty(output.modifiers.operations[0].failure),"Actual vertex Move drag refreshes live sweep before release including out-of-plane deformation");
                Call(editor,"ChangeComponentMode",StudioModelEditor.ComponentMode.Face,null);editor.Components.Clear();editor.Components.Add(0);Call(editor,"ActivateTool",Tool.Rotate);
                before=output.mesh.vertices;var gesture=Call(editor,"CaptureMultiComponentGesture",new List<int>{1},Vector3.zero,Quaternion.identity,0,false);
                Call(editor,"ApplyMultiComponentGesture",gesture,Vector3.zero,Quaternion.Euler(0,20,15),Vector3.one);
                Check(!Same(before,output.mesh.vertices)&&output.mesh.vertexCount>0&&string.IsNullOrEmpty(output.modifiers.operations[0].failure),"Face Rotate gesture refreshes live source sweep: vertices="+output.mesh.vertexCount+", same="+Same(before,output.mesh.vertices)+", failure="+output.modifiers.operations[0].failure);
                mesh.vertices=new[]{new Vector3(-1,-.5f,0),new Vector3(2,-.5f,0),new Vector3(2,.5f,0),new Vector3(-1,.5f,0)};mesh.RecalculateBounds();editor.Changed(false);
                var savedTopology=source.cutPolygons;source.cutPolygons=new[]{0,3,0,1,2,0,3,0,2,3};editor.Changed(false);
                Check(output.mesh.vertexCount==0&&!string.IsNullOrEmpty(output.modifiers.operations[0].failure),"Changed source topology fails explicitly instead of rebinding components");source.cutPolygons=savedTopology;editor.Changed(false);Check(output.mesh.vertexCount>0,"Restored source topology repairs live sweep");
                var live=output.modifiers.operations[0].sweep;string savedId=live.profileId;live.profileId="missing";editor.Changed(false);Check(output.mesh.vertexCount==0,"Missing profile clears stale output");live.profileId=savedId;editor.Changed(false);Check(output.mesh.vertexCount>0,"Profile restoration regenerates output");
                live.profileId=output.constraintId;rejected=false;try{StudioSweepDependencies.Validate(doc);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Self-profile dependency rejected");live.profileId=savedId;
                int count=doc.nodes.Count;doc.DuplicateObjects(new[]{1,2});Call(editor,"Rebuild");Check(doc.nodes[count+1].modifiers.operations[0].sweep.profileId==doc.nodes[count].constraintId,"Duplicating profile and output remaps live link");
                editor.SelectObject(1);Set(editor,"mode",StudioModelEditor.ComponentMode.Edge);editor.Components.Add(0);editor.Components.Add(1);settings=new StudioSweepSettings{sourceId=doc.nodes[0].constraintId};Call(editor,"CaptureSweepProfile",settings);
                Check(!settings.profileClosed&&settings.profileVertices.Length==3,"Selected open edges become uncapped three-point profile");
                node=(StudioModelDocument.Node)Call(editor,"GenerateSweep",settings);Check(new StudioMeshCuts(node).Faces.Count==64,"Open custom profile creates only side quads");DestroyImmediate(node.mesh);
                string json=JsonUtility.ToJson(settings);var restored=JsonUtility.FromJson<StudioSweepSettings>(json);Check(restored.profileId==source.constraintId&&restored.profileVertices.SequenceEqual(settings.profileVertices),"Live profile IDs and selection survive serialization");
                                editor.SelectObject(2);Call(editor,"ActivateTool",Tool.Move);
                Check(editor.ToolOptionsTitle=="Move Tool","Selecting sweep retains Move Tool Options");
                Call(editor,"OpenSweepTool",output,null);
                Check(Get<ToolChannelPane>(editor,"sweepPane")==null&&!Get<bool>(editor,"sweepPending"),"Existing Sweep command does not open a tool session");
                var sourceSettings=output.modifiers.operations[0].sweep.Copy();var activeBefore=editor.Active;
                Call(editor,"CaptureSweepSource",sourceSettings,output,1);
                Check(sourceSettings.profileId==source.constraintId&&sourceSettings.profileVertices.Length==4&&editor.Active==activeBefore,"Whole-mesh source capture preserves selected Sweep");
                bool invalid=false;try{Call(editor,"CaptureSweepSource",sourceSettings,output,2);}catch(TargetInvocationException){invalid=true;}Check(invalid,"Self source assignment rejected");
                Call(editor,"ActivateTool",Tool.Rotate);
                editor.SelectObject(0);int nodeCount=doc.nodes.Count;Call(editor,"OpenSweepTool",null,null);
                Check(doc.nodes.Count==nodeCount+1&&doc.nodes.Last().modifiers.operations[0].kind=="Sweep"&&!Get<bool>(editor,"sweepPending")&&Get<ToolChannelPane>(editor,"sweepPane")==null,"Creation immediately commits an object modifier without taking Tool Options");
                Check(editor.ToolOptionsTitle=="Rotate Tool","Sweep creation preserves ordinary transform tool");
                var worldBefore=output.mesh.vertices.Select(v=>editor.Transforms[2].TransformPoint(v)).ToArray();int parent=doc.GroupObjects(new[]{1,2},editor.Transforms,Vector3.zero);Call(editor,"Rebuild");
                doc.nodes[parent].scale=Vector3.one*2;doc.ApplyTransform(parent,editor.Transforms[parent]);editor.Changed(false);
                Check(Same(worldBefore.Select(v=>v*2).ToArray(),output.mesh.vertices.Select(v=>editor.Transforms[2].TransformPoint(v)).ToArray()),"Shared profile/output parent scale applies once");
            }
            finally{editor.Dispose();DestroyImmediate(doc);if(mesh)DestroyImmediate(mesh);}
        }
        void OnGUI()
        {
            if(ran||Event.current.type!=EventType.Repaint)return;ran=true;var original=Event.current;
            StudioModelEditor editor=null;StudioModelDocument doc=null;SceneView view=null;
            try
            {
                ProfileChecks();
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
            finally{Event.current=original;GUIUtility.hotControl=0;editor?.Dispose();if(view)DestroyImmediate(view);if(doc)DestroyImmediate(doc);File.WriteAllLines(Out("result.txt"),log);EditorApplication.delayCall+=()=>{if(log.Any(x=>x.StartsWith("FAIL")))EditorApplication.Exit(1);else StudioSweepAlignmentEvents.Start();};}
        }
    }
}





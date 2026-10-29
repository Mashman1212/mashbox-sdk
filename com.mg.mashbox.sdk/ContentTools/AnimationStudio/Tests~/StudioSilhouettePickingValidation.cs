using System;using System.IO;using System.Linq;using System.Reflection;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;using Object=UnityEngine.Object;
public static class StudioSilhouettePickingValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static StudioModelEditor ed;static StudioModelDocument doc;static SceneView view;static int checks;static int stage;
 static object Call(string n,params object[] a)=>typeof(StudioModelEditor).GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==a.Length&&m.GetParameters().Select((p,i)=>a[i]==null||p.ParameterType.IsInstanceOfType(a[i])).All(b=>b)).Invoke(ed,a);
 static void Set(string n,object v)=>typeof(StudioModelEditor).GetField(n,F).SetValue(ed,v);
 static void Check(bool v,string m){if(!v)throw new Exception(m);checks++;}
 public static void Run(){EditorApplication.delayCall+=()=>{try{doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();doc.nodes.Add(new StudioModelDocument.Node{name="Two sided boundary",mesh=mesh,cutPolygons=new[]{0,4,0,1,2,3},cutTriangles=mesh.triangles});ed=new StudioModelEditor(()=>{});ed.Load(doc);ed.SelectObject(0);Set("xray",false);view=ScriptableObject.CreateInstance<SceneView>();view.position=new Rect(100,100,650,650);view.Show();view.orthographic=true;view.LookAtDirect(Vector3.zero,Quaternion.identity,5);SceneView.duringSceneGui+=Draw;view.Repaint();}catch(Exception e){Finish(e.ToString());}};}
 static void Draw(SceneView s){if(s!=view||Event.current.type!=EventType.Repaint)return;string result;var cam=view.camera;bool ortho=stage<2,reverse=(stage%2)==1;try{
 Vector2 P(Vector3 w)=>HandleUtility.WorldToGUIPoint(w);
 var edge=P(new Vector3(0,1,0));var outside=edge+Vector2.down*12;
 ed.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);Check((int)Call("MeshAtModePointer",view,outside)==0,"Outside silhouette edge target "+ortho+" rear="+reverse);
 Check((int)Call("FindComponentAt",view,outside)>=0,"Outside edge narrow phase");Call("UpdateSelectedComponentHover",view,outside);Check((int)typeof(StudioModelEditor).GetField("hoverPreviewNode",F).GetValue(ed)==0,"Hover agrees with click");
 Set("end",outside);Set("additive",false);Set("subtract",false);Set("selectionClickCount",1);Call("PickCore",view,new Rect(outside,Vector2.zero),true);Check(ed.Components.Count==1,"Outside edge click route selection");Check(ed.ToolbarComponentMode==StudioModelEditor.ComponentMode.Edge,"Click retains edge mode");Check((int)Call("FindComponentAt",view,edge+Vector2.down*19)==-1,"Beyond radius rejected");
 ed.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Vertex);var vertex=P(new Vector3(-1,1,0))+Vector2.down*12;Check((int)Call("MeshAtModePointer",view,vertex)==0,"Outside vertex target");Set("end",vertex);Call("PickCore",view,new Rect(vertex,Vector2.zero),true);Check(ed.Components.Count==1,"Outside vertex click route selection");
 ed.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);Check((int)Call("MeshAtModePointer",view,outside)==-1,"Faces retain exact silhouette");Check((int)Call("MeshAtModePointer",view,P(Vector3.zero))==0,"Face interior both windings");
 ed.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Object);Check((int)Call("MeshAtModePointer",view,outside)==-1,"Objects retain exact silhouette");

 stage++;if(stage<4){EditorApplication.delayCall+=()=>{view.orthographic=stage<2;view.LookAtDirect(Vector3.zero,(stage%2)==1?Quaternion.Euler(0,180,0):Quaternion.identity,5);view.Repaint();};return;}
 // A genuine foreground surface must still occlude selected components.
 var blocker=GameObject.CreatePrimitive(PrimitiveType.Quad);var mesh2=Object.Instantiate(blocker.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(blocker);doc.nodes.Add(new StudioModelDocument.Node{name="Occluder",mesh=mesh2,position=new Vector3(0,1,1),scale=Vector3.one*3});ed.Load(doc);ed.SelectObject(0);ed.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);Set("xray",false);var q=HandleUtility.WorldToGUIPoint(new Vector3(0,1,0))+Vector2.down*12;Check((int)Call("FindComponentAt",view,q)==-1,"Foreground surface still blocks edge");Set("xray",true);Check((int)Call("FindComponentAt",view,q)>=0,"Through selection bypass preserved");
 result="PASS "+checks+" front/back orthographic/perspective silhouette target, hover/click routing, vertex/edge radius, face/object precision and occlusion checks";
 }catch(Exception e){result=e.ToString();}Finish(result);}
 static void Finish(string r){SceneView.duringSceneGui-=Draw;File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../silhouette-picking-result.txt")),r);EditorApplication.delayCall+=()=>{if(view)view.Close();ed?.Dispose();EditorApplication.Exit(r.StartsWith("PASS")?0:1);};}
}



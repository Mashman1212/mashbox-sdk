#if MASHBOX_STUDIO_TESTS
using System; using System.IO; using System.Linq; using System.Reflection; using System.Collections.Generic;
using UnityEditor; using UnityEngine; using MashBoxSDK.AnimationStudio;
using Undo=MashBoxSDK.AnimationStudio.StudioUndo;
public static class DirectNormalsValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic; static AnimationStudioWindow host; static MashBoxStudioViewport view; static StudioModelEditor editor; static StudioModelDocument doc;
 static Queue<Action> steps=new Queue<Action>(); static double ready; static int checks; static ToolChannelPane pane; static int originalCount; static List<string> errors=new List<string>();
 static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
 static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
 static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethod(name,F).Invoke(editor,args);
 static ToolChannelPane Pane(string field)=>(ToolChannelPane)typeof(StudioModelEditor).GetField(field,F).GetValue(editor);
 static void Select(StudioModelEditor.ComponentMode mode){editor.SelectObject(0);editor.SetToolbarComponentMode(mode);editor.Components.Clear();editor.Components.Add(0);}
 static void Open(string method,string field,string title){Call(method);pane=Pane(field);Check(pane&&pane.IsEmbedded,"Not embedded: "+title);Check(editor.ToolOptionsTitle==title,"Wrong panel title: "+editor.ToolOptionsTitle);Check(typeof(EditorWindow).GetField("m_Parent",F).GetValue(pane)==null,"Native popup created");Check(Resources.FindObjectsOfTypeAll<ToolChannelPane>().Length==1,"Multiple controllers alive");pane.Focus();Check(typeof(EditorWindow).GetField("m_Parent",F).GetValue(pane)==null,"Focusing created a popup");view.Repaint();}
 public static void Run(){EditorApplication.delayCall+=Start;}
 static void Start(){try{
  if(!Application.dataPath.Contains("StudioControlsValidation"))throw new Exception("Disposable project required");
  Application.logMessageReceived+=(m,s,t)=>{if(t==LogType.Exception||(t==LogType.Error&&m.Contains("GUILayout")))errors.Add(m);};
  doc=ScriptableObject.CreateInstance<StudioModelDocument>();
  for(int i=0;i<2;i++){var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polys);doc.nodes.Add(new StudioModelDocument.Node{name="First selected "+i,position=Vector3.right*i*2,mesh=mesh,cutPolygons=polys,cutTriangles=mesh.triangles,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial}});}
  host=ScriptableObject.CreateInstance<AnimationStudioWindow>();typeof(AnimationStudioWindow).GetField("modelingMode",F).SetValue(host,true);typeof(AnimationStudioWindow).GetMethod("OpenViewport",F).Invoke(host,null);typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});
  editor=host.ToolbarModelEditor;view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);view.position=new Rect(40,40,1100,760);view.LookAtDirect(Vector3.zero,Quaternion.Euler(20,30,0),4);originalCount=doc.nodes[0].mesh.vertexCount;
  int Groups()=>doc.nodes[0].normalGroups.Distinct().Count();
  steps.Enqueue(()=>{editor.SelectObject(0);ToolChannelPane.SetFloatDefault("Soften Edges","Angle",0);editor.ApplyEdgeNormals(false);Check(Groups()==8,"Direct Soften retained angle limit");Check(ToolChannelPane.GetFloatDefault("Soften Edges","Angle",45)==0,"Direct action overwrote options angle");Check(Resources.FindObjectsOfTypeAll<ToolChannelPane>().Length==0,"Direct action opened options");Check(editor.StatusText=="Edges softened.","Direct action status still angle-limited");});
  steps.Enqueue(()=>{editor.ApplyEdgeNormals(true);Check(Groups()==24,"Harden did not split cube normals");Undo.PerformUndo();Check(Groups()==8,"Undo did not restore softened normals");Undo.PerformRedo();Check(Groups()==24,"Redo did not restore hardened normals");});
  steps.Enqueue(()=>{ToolChannelPane.SetFloatDefault("Soften Edges","Angle",45);Open("OpenSoftenTool","softenToolPane","Soften Edges");});
  steps.Enqueue(()=>{Check(Groups()==24,"Soften options ignored 45-degree threshold");ToolChannelPane.SetFloatDefault("Soften Edges","Angle",180);((Action)typeof(ToolChannelPane).GetField("preview",F).GetValue(pane))();});
  steps.Enqueue(()=>{Check(Groups()==8,"Soften options ignored 180-degree input");Capture("direct-normals.png");pane.Close();Check(Groups()==24,"Options Cancel did not restore normals");Check(errors.Count==0,"Native errors: "+string.Join(";",errors));});
  EditorApplication.update+=Tick;ready=EditorApplication.timeSinceStartup+1;
 }catch(Exception e){Finish(e.ToString());}}
 static void Capture(string name){var parent=typeof(EditorWindow).GetField("m_Parent",F).GetValue(view);var rect=new Rect(Vector2.zero,view.position.size);var rt=new RenderTexture((int)rect.width,(int)rect.height,0);rt.Create();var old=RenderTexture.active;try{parent.GetType().GetMethod("GrabPixels",F).Invoke(parent,new object[]{rt,rect});RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);image.ReadPixels(rect,0,0);image.Apply();var pixels=image.GetPixels32();var flipped=new Color32[pixels.Length];for(int y=0;y<image.height;y++)Array.Copy(pixels,y*image.width,flipped,(image.height-1-y)*image.width,image.width);image.SetPixels32(flipped);image.Apply();File.WriteAllBytes(Out(name),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);checks++;}finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(rt);}}
 static void Tick(){if(EditorApplication.timeSinceStartup<ready)return;try{if(steps.Count==0){Finish("PASS "+checks+" direct Soften/Harden, angle independence, Undo/Redo, parameterized options and cancel checks");return;}UnityEditor.Undo.FlushUndoRecordObjects();steps.Dequeue()();UnityEditor.Undo.FlushUndoRecordObjects();ready=EditorApplication.timeSinceStartup+.8;}catch(Exception e){Finish(e.ToString());}}
 static void Finish(string result){EditorApplication.update-=Tick;File.WriteAllText(Out("direct-normals-result.txt"),result);EditorApplication.delayCall+=()=>{if(editor!=null)editor.CloseToolOptions();if(view)view.Close();if(host)UnityEngine.Object.DestroyImmediate(host);EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};}
}
#endif


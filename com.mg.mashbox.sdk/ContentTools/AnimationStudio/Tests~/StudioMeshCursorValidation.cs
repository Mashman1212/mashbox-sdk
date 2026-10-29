#if MASHBOX_STUDIO_TESTS
using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;using System.Reflection;
using UnityEditor;using UnityEngine;using UnityEngine.UIElements;using MashBoxSDK.AnimationStudio;
public static class StudioMeshCursorValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;static int checks;static AnimationStudioWindow host;static MashBoxStudioViewport view;static StudioModelEditor editor;static StudioModelDocument doc;
 static Queue<Action> steps=new Queue<Action>();static double ready;static bool probed;static Exception failure;static IMGUIContainer canvas;
 static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
 static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
 static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethod(name,F).Invoke(editor,args);
 static void Kind(StudioMeshToolCursors.Kind expected,EventModifiers modifiers=0)=>Check(editor.MeshToolCursor(modifiers)==expected,"Wrong cursor: "+expected);
 public static void Run(){EditorApplication.delayCall+=Start;}
 static void Atlas()
 {
  var kinds=(StudioMeshToolCursors.Kind[])Enum.GetValues(typeof(StudioMeshToolCursors.Kind));var atlas=new Texture2D(384,192,TextureFormat.RGBA32,false);var pixels=new Color32[384*192];
  for(int y=0;y<192;y++)for(int x=0;x<384;x++)pixels[y*384+x]=(x%96<48)?new Color32(235,235,235,255):new Color32(48,48,48,255);
  var identities=new HashSet<string>();int index=0;
  foreach(var kind in kinds.Where(k=>k!=StudioMeshToolCursors.Kind.None))
  {
   const int size=32;var texture=StudioMeshToolCursors.Get(kind);Check(texture&&texture.width==size&&texture.height==size,"Invalid cursor dimensions");Check(ReferenceEquals(texture,StudioMeshToolCursors.Get(kind)),"Cursor texture not reused");
   var source=texture.GetPixels32();Check(source.Any(p=>p.a==0)&&source.Any(p=>p.a==255),"Cursor transparency lost");Check(source.Any(p=>p.a>0&&p.a<255),"Vector contour antialiasing missing");Check(source.Any(p=>p.r==255&&p.g==255&&p.b==255&&p.a==255),"Cursor white fill missing");Check(identities.Add(Convert.ToBase64String(texture.EncodeToPNG())),"Cursor artwork not distinct: "+kind);
   for(int p=0;p<source.Length;p++)
   {
    var color=source[p];if(color.a==0)continue;int x=p%size,y=size-1-p/size;
    Check(kind==StudioMeshToolCursors.Kind.MultiCut||(x<32&&y<32),"Cursor artwork exceeds its padded canvas");
    if(kind==StudioMeshToolCursors.Kind.MultiCut||x<6||y<6)Check(color.r==color.g&&color.g==color.b,"Cursor body is colored");
   }
   if(kind==StudioMeshToolCursors.Kind.MultiCut){Check(source.All(p=>p.a==0||(p.r==p.g&&p.g==p.b&&p.r>=64)),"Knife contains invalid greyscale paint");Check(source[(31-2)*32+2].a>0,"Blade tip does not meet hotspot");}
   int ox=(index%4)*96+16,oy=(1-index/4)*96+16;
   for(int y=0;y<size;y++)for(int x=0;x<size;x++)for(int dy=0;dy<1;dy++)for(int dx=0;dx<1;dx++)
   {int p=(oy+y+dy)*384+ox+x+dx;pixels[p]=Color32.Lerp(pixels[p],source[y*size+x],source[y*size+x].a/255f);pixels[p].a=255;}
   index++;
  }
  Check(StudioMeshToolCursors.Get(StudioMeshToolCursors.Kind.None)==null,"Idle mode allocated a cursor");Check(StudioMeshToolCursors.Hotspot==new Vector2(2,2),"Cursor hotspot moved");atlas.SetPixels32(pixels);atlas.Apply();File.WriteAllBytes(Out("mesh-tool-cursors.png"),atlas.EncodeToPNG());UnityEngine.Object.DestroyImmediate(atlas);
 }
 static void Component(StudioModelEditor.ComponentMode mode){editor.SelectObject(0);editor.SetToolbarComponentMode(mode);editor.Components.Add(0);}
 static void Pane(string method,StudioMeshToolCursors.Kind kind){Call(method);Kind(kind);editor.CloseToolOptions();Kind(StudioMeshToolCursors.Kind.None);}
 static void Start()
 {
  try{
   if(!Application.dataPath.Contains("StudioControlsValidation"))throw new Exception("Disposable project required");Atlas();
   doc=ScriptableObject.CreateInstance<StudioModelDocument>();for(int i=0;i<2;i++){var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var p);doc.nodes.Add(new StudioModelDocument.Node{name="Cursor fixture "+i,mesh=mesh,cutPolygons=p,cutTriangles=mesh.triangles,position=Vector3.right*i*2,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial}});}
   host=ScriptableObject.CreateInstance<AnimationStudioWindow>();typeof(AnimationStudioWindow).GetField("modelingMode",F).SetValue(host,true);typeof(AnimationStudioWindow).GetMethod("OpenViewport",F).Invoke(host,null);typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});editor=host.ToolbarModelEditor;view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);view.position=new Rect(50,50,1100,760);editor.SelectObject(0);
   steps.Enqueue(()=>{Kind(StudioMeshToolCursors.Kind.None);Component(StudioModelEditor.ComponentMode.Edge);Pane("OpenBevelTool",StudioMeshToolCursors.Kind.Bevel);Pane("OpenEdgeFlowTool",StudioMeshToolCursors.Kind.EdgeFlow);Pane("OpenSoftenTool",StudioMeshToolCursors.Kind.Soften);});
   steps.Enqueue(()=>{Component(StudioModelEditor.ComponentMode.Face);Pane("ExtrudeSelected",StudioMeshToolCursors.Kind.Extrude);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Object);editor.SelectObject(0);editor.SelectObject(1,true);Pane("OpenCombineTool",StudioMeshToolCursors.Kind.Combine);});
   steps.Enqueue(()=>{editor.ActivateTargetWeld();Kind(StudioMeshToolCursors.Kind.TargetWeld);editor.ActivateTool(Tool.Move);Kind(StudioMeshToolCursors.Kind.None);editor.SelectObject(0);editor.ActivateCutTool(StudioModelEditor.CutTool.EdgeLoop);Kind(StudioMeshToolCursors.Kind.EdgeLoop);editor.ActivateCutTool(StudioModelEditor.CutTool.MultiCut);Kind(StudioMeshToolCursors.Kind.MultiCut);Kind(StudioMeshToolCursors.Kind.EdgeLoop,EventModifiers.Control);
    var points=(IList)typeof(StudioModelEditor).GetField("multiPoints",F).GetValue(editor);points.Add(Activator.CreateInstance(typeof(StudioModelEditor).GetNestedType("MultiPoint",BindingFlags.NonPublic)));Kind(StudioMeshToolCursors.Kind.MultiCut,EventModifiers.Control);Call("ResetMultiCutGesture");SceneView.duringSceneGui+=Probe;view.Repaint();});
   steps.Enqueue(()=>{Check(probed&&failure==null,"Native cursor registration failed: "+failure);var saved=canvas;view.Close();view=null;Check(saved.style.cursor.keyword==StyleKeyword.Null,"Viewport shutdown retained cursor style");});
   EditorApplication.update+=Tick;ready=EditorApplication.timeSinceStartup+.8;
  }catch(Exception e){Finish(e.ToString());}
 }
 static void Probe(SceneView current)
 {
  if(current!=view||Event.current.type!=EventType.Repaint||probed)return;probed=true;
  try{
   var handlers=(IDictionary)typeof(MashBoxStudioViewport).GetField("studioInputHandlers",F).GetValue(view);
   foreach(DictionaryEntry pair in handlers){var action=(Action)pair.Value.GetType().GetField("original",F).GetValue(pair.Value);if(action.Method.DeclaringType==typeof(SceneView)||action.Method.Name=="OnSceneGUI"){canvas=(IMGUIContainer)pair.Key;break;}}
   Check(canvas!=null,"No camera input container");var pointer=typeof(MashBoxStudioViewport).GetField("<CanvasMousePosition>k__BackingField",F);pointer.SetValue(view,(Vector2?)(view.ModelCanvasRect.center-view.CanvasInputOrigin));
   var update=typeof(MashBoxStudioViewport).GetMethod("UpdateMeshToolCursor",F);var flags=Event.current.modifiers;Event.current.modifiers=0;
   void Update(bool blocked=false)=>update.Invoke(view,new object[]{canvas,blocked});
   void Clear(string label)=>Check(canvas.style.cursor.keyword==StyleKeyword.Null,label);
   Update();Check(canvas.style.cursor.value.texture==StudioMeshToolCursors.Get(StudioMeshToolCursors.Kind.MultiCut),"UI Toolkit did not receive scalpel texture");Check(canvas.style.cursor.value.hotspot==StudioMeshToolCursors.Hotspot,"Native style hotspot mismatch");
   Update(true);Clear("Menu block retained cursor");Update();Event.current.alt=true;Update();Clear("Camera Alt retained cursor");Event.current.alt=false;
   var navigating=typeof(MashBoxStudioViewport).GetField("navigating",F);navigating.SetValue(view,true);Update();Clear("Navigation retained cursor");navigating.SetValue(view,false);
   var held=typeof(MashBoxStudioViewport).GetField("selectionPaintHeldKey",F);held.SetValue(view,KeyCode.Tab);Update();Clear("Selection paint retained tool cursor");held.SetValue(view,KeyCode.None);
   pointer.SetValue(view,(Vector2?)(new Vector2(view.ModelCanvasRect.xMin-20,view.ModelCanvasRect.center.y)-view.CanvasInputOrigin));Update();Clear("Panel region retained tool cursor");pointer.SetValue(view,(Vector2?)(view.ModelCanvasRect.center-view.CanvasInputOrigin));Update();
   foreach(var tool in new[]{Tool.Move,Tool.Rotate,Tool.Scale,Tool.None})
   {
    editor.ActivateCutTool(StudioModelEditor.CutTool.MultiCut);Update();Check(canvas.style.cursor.value.texture==StudioMeshToolCursors.Get(StudioMeshToolCursors.Kind.MultiCut),"Cursor not rearmed");
    int before=view.MeshNativeCursorClearCount;editor.ActivateTool(tool);Update();Clear("Transform retained custom cursor: "+tool);Kind(StudioMeshToolCursors.Kind.None);Check(view.MeshNativeCursorClearCount==before+1,"Transform did not release native custom cursor: "+tool);
   }
   Event.current.modifiers=flags;
  }catch(Exception e){failure=e;}
 }
 static void Tick(){if(EditorApplication.timeSinceStartup<ready)return;try{if(steps.Count==0){Finish("PASS "+checks+" cached distinct cursor textures, mode routing, scoped native style, navigation/UI suppression and teardown checks");return;}steps.Dequeue()();ready=EditorApplication.timeSinceStartup+.8;}catch(Exception e){Finish(e.ToString());}}
 static void Finish(string result){EditorApplication.update-=Tick;SceneView.duringSceneGui-=Probe;File.WriteAllText(Out("mesh-cursors-result.txt"),result);EditorApplication.delayCall+=()=>{if(editor!=null)editor.CloseToolOptions();if(view)view.Close();if(host)UnityEngine.Object.DestroyImmediate(host);StudioMeshToolCursors.Dispose();EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};}
}
#endif

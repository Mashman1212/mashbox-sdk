using System;using System.IO;using System.Linq;using System.Reflection;using System.Diagnostics;using System.Collections.Generic;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;using Object=UnityEngine.Object;
public static class StudioMeshUndoValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;static StudioModelEditor editor;static StudioModelDocument doc;static MashBoxStudioViewport view;static bool pending,fast;static int checks;static string stage;static Color32[] baselinePixels;static object initialBuffer;static readonly List<double> times=new List<double>(),redraws=new List<double>();static bool measureFrame;static double frameMs;static Exception frameError;
 static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length&&m.GetParameters().Select((p,i)=>args[i]==null||p.ParameterType.IsInstanceOfType(args[i])).All(v=>v)).Invoke(editor,args);
 static void Check(bool value,string message){if(!value)throw new Exception(stage+": "+message);checks++;}
 public static void Before(){Start(false);}public static void After(){Start(true);}
 static void Start(bool expected){fast=expected;EditorApplication.delayCall+=()=>{try{
  var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FrameProbe.fbx");if(!model)throw new Exception("Missing disposable frame input");doc=StudioModelDocument.Import(model,AssetDatabase.GenerateUniqueAssetPath("Assets/MeshUndoProbe.asset"));editor=new StudioModelEditor(()=>{});editor.Load(doc);int node=doc.nodes.Select((n,i)=>(n,i)).Where(p=>p.n.mesh&&!p.n.skinned).OrderByDescending(p=>p.n.mesh.vertexCount).First().i;editor.SelectObject(node);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Vertex);editor.Components.UnionWith(Enumerable.Range(0,editor.ActiveMesh.vertexCount));Call("EstablishSelectionBaseline");view=ScriptableObject.CreateInstance<MashBoxStudioViewport>();view.position=new Rect(80,80,900,700);view.Bind(editor.Scene);view.Show();editor.Frame(view);SceneView.duringSceneGui+=Draw;pending=true;view.Repaint();
 }catch(Exception ex){Finish(ex);}};}
 static Color32[] Render()
 {
  var camera=view.camera;var rt=new RenderTexture(400,400,24);rt.Create();var old=camera.targetTexture;var rect=camera.pixelRect;var previous=RenderTexture.active;try{camera.targetTexture=rt;camera.pixelRect=new Rect(0,0,400,400);camera.Render();RenderTexture.active=rt;var image=new Texture2D(400,400,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,400,400),0,0);image.Apply();var pixels=image.GetPixels32();Object.DestroyImmediate(image);return pixels;}finally{camera.targetTexture=old;camera.pixelRect=rect;RenderTexture.active=previous;Object.DestroyImmediate(rt);}
 }
 static void Draw(SceneView scene)
 {
  if(scene!=view||Event.current.type!=EventType.Repaint)return;if(measureFrame){measureFrame=false;try{var clock=Stopwatch.StartNew();Call("DrawSceneWireframes",view.camera);Call("DrawComponents",view);clock.Stop();frameMs=clock.Elapsed.TotalMilliseconds;}catch(Exception ex){frameError=ex;}return;}if(!pending)return;pending=false;try{Call("DrawSceneWireframes",view.camera);Call("DrawComponents",view);baselinePixels=Render();initialBuffer=typeof(StudioModelEditor).GetField("markerPositions",F).GetValue(editor);EditorApplication.delayCall+=Validate;}catch(Exception ex){Finish(ex);}
 }
 static void Validate()
 {
  try
  {
   stage="prime render/cache";var originalPixels=baselinePixels;var mesh=editor.ActiveMesh;var original=mesh.vertices;var transforms=editor.Transforms.ToArray();var buffer=initialBuffer;var untouched=doc.nodes.Where(n=>n.mesh&&n.mesh!=mesh).Select(n=>(n.mesh,n.mesh.vertices)).ToArray();var changed=(Vector3[])original.Clone();var shift=Vector3.right*Mathf.Max(.1f,mesh.bounds.size.magnitude*.15f);for(int i=0;i<changed.Length;i++)changed[i]+=shift;
   stage="native mesh Undo";UnityEditor.Undo.ClearAll();StudioUndo.ClearForTests();StudioUndo.IncrementCurrentGroup();StudioUndo.RegisterCompleteObjectUndo(mesh,"Transform mesh components");mesh.vertices=changed;mesh.RecalculateBounds();Call("PositionsChanged",false,changed,null);var movedPixels=Render();Check(!originalPixels.SequenceEqual(movedPixels),"Native renderer did not display the changed mesh");UnityEditor.Undo.FlushUndoRecordObjects();StudioUndo.IncrementCurrentGroup();
   for(int i=0;i<12;i++)
   {
    var timer=Stopwatch.StartNew();StudioUndo.PerformUndo();timer.Stop();if(i>=2)times.Add(timer.Elapsed.TotalMilliseconds);measureFrame=true;frameMs=-1;view.SendEvent(new Event{type=EventType.Layout});view.SendEvent(new Event{type=EventType.Repaint});Check(!measureFrame&&frameMs>=0&&frameError==null,"Redraw failed: "+frameError);if(i>=2)redraws.Add(frameMs);Check(mesh.vertices.SequenceEqual(original),"Undo CPU positions wrong");if(fast)Check(editor.Transforms.SequenceEqual(transforms),"Position Undo rebuilt scene objects");if(fast)Check(ReferenceEquals(buffer,typeof(StudioModelEditor).GetField("markerPositions",F).GetValue(editor)),"Position Undo discarded marker buffer");
    if(i==0){var restored=Render();Check(originalPixels.SequenceEqual(restored),"Existing renderer did not display restored positions");}
    StudioUndo.PerformRedo();Check(mesh.vertices.SequenceEqual(changed),"Redo CPU positions wrong");if(fast)Check(editor.Transforms.SequenceEqual(transforms),"Redo rebuilt scene objects");
   }
   foreach(var item in untouched)Check(item.mesh.vertices.SequenceEqual(item.Item2),"Undo changed an unrelated mesh");
   stage="selection-only Undo";transforms=editor.Transforms.ToArray();buffer=typeof(StudioModelEditor).GetField("markerPositions",F).GetValue(editor);editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.Clear,null);StudioUndo.PerformUndo();Check(editor.Components.Count==mesh.vertexCount,"Selection Undo lost vertices");if(fast)Check(ReferenceEquals(buffer,typeof(StudioModelEditor).GetField("markerPositions",F).GetValue(editor)),"Selection Undo discarded marker buffer");Check(editor.Transforms.SequenceEqual(transforms),"Selection Undo rebuilt scene");
   Finish(null);
  }catch(Exception ex){Finish(ex);}
 }
 static void Finish(Exception ex){SceneView.duringSceneGui-=Draw;var sorted=times.OrderBy(t=>t).ToArray();string result=ex==null?"PASS "+checks+" native mesh/selection history and renderer checks\nvertices="+editor.ActiveMesh.vertexCount+" meshes="+doc.nodes.Count(n=>n.mesh)+" Undo median ms="+sorted[sorted.Length/2].ToString("F3")+" max ms="+sorted.Last().ToString("F3")+" first wire/marker redraw median ms="+redraws.OrderBy(t=>t).ElementAt(redraws.Count/2).ToString("F3"):stage+"\n"+ex;File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../mesh-undo-"+(fast?"after":"before")+".txt")),result);EditorApplication.delayCall+=()=>{if(view)view.Close();editor?.Dispose();double exitAt=EditorApplication.timeSinceStartup+1;void Exit(){if(EditorApplication.timeSinceStartup<exitAt)return;EditorApplication.update-=Exit;EditorApplication.Exit(ex==null?0:1);}EditorApplication.update+=Exit;};}
}

#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
public static class StudioOwnedWindowLifetimeValidation {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 const BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic;
 static int checks;
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
 public static void Run(){var windows=new List<EditorWindow>();StudioModelDocument document=null;Mesh mesh=null;StudioModelEditor editor=null;try{
  Func<Type,EditorWindow> make=t=>{var w=(EditorWindow)ScriptableObject.CreateInstance(t);windows.Add(w);return w;};
  var root=make(typeof(MashBoxStudioViewport));var other=make(typeof(MashBoxStudioViewport));
  var preferences=make(typeof(StudioControlsPreferences));var recorder=make(typeof(StudioShortcutRecorder));var theme=make(typeof(StudioSettingsWindow));
  StudioWindowOwnership.Register(preferences,root);StudioWindowOwnership.Register(recorder,preferences);StudioWindowOwnership.Register(theme,other);
  var links=(Dictionary<EditorWindow,EditorWindow>)typeof(StudioWindowOwnership).GetField("links",S).GetValue(null);
  StudioWindowOwnership.Register(root,recorder);Check(!links.ContainsKey(root),"Cycle rejected");
  // SessionState stores managed identities only and recovers references on reload.
  typeof(StudioWindowOwnership).GetMethod("SaveLinks",S).Invoke(null,null);links.Clear();typeof(StudioWindowOwnership).GetMethod("RestoreLinks",S).Invoke(null,null);
  Check(links[preferences]==root&&links[recorder]==preferences,"Ownership graph restores from session");
  Object.DestroyImmediate(preferences);Check(!preferences&&!recorder,"Closing Preferences closes recorder");Check(root&&theme,"Independent owner windows survive");
  var migrated=make(typeof(StudioControlsPreferences));StudioWindowOwnership.Register(migrated,root);
  StudioWindowOwnership.ReparentOwnedWindows(root,other);Check(links[migrated]==other,"Host migration transfers ownership");
  Object.DestroyImmediate(root);Check(migrated&&theme,"Old host close preserves transferred windows");
  document=ScriptableObject.CreateInstance<StudioModelDocument>();mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
  document.nodes.Add(new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles});editor=new StudioModelEditor(()=>{});editor.Load(document);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);editor.Components.Add(0);
  var original=mesh.vertices;var kind=Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",F),"Bevel");ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.2f);
  typeof(StudioModelEditor).GetMethod("EvaluateLiveTool",F).Invoke(editor,new[]{kind});Check(!mesh.vertices.SequenceEqual(original),"Real pending preview exists");
  var pane=(ToolChannelPane)make(typeof(ToolChannelPane));pane.position=new Rect(100,100,340,235);
  typeof(ToolChannelPane).GetField("cancelPreview",F).SetValue(pane,(Action)(()=>typeof(StudioModelEditor).GetMethod("CancelLiveTool",F,null,new[]{typeof(bool)},null).Invoke(editor,new object[]{false})));
  StudioWindowOwnership.Register(pane,other);
  var uv=make(typeof(StudioUVEditor));var lighting=make(typeof(StudioLightingWindow));var ao=make(typeof(StudioVertexAOWindow));var audio=make(typeof(StudioAudioKeysWindow));
  foreach(var w in new[]{uv,lighting,ao,audio})StudioWindowOwnership.Register(w,other);
  Object.DestroyImmediate(other);Check(!pane&&!migrated&&!theme&&!uv&&!lighting&&!ao&&!audio,"Studio close cascades to all registered tools/settings");
  Check(mesh.vertices.SequenceEqual(original),"Owner close cancels unapplied preview");
  Check(typeof(StudioModelEditor).GetField("livePreview",F).GetValue(editor)==null,"Owner close disposes preview transaction");
  Check(!(bool)typeof(StudioModelEditor).GetField("previewQueued",F).GetValue(editor),"No queued preview remains");
  Check(links.Count==0,"Closed ownership links released");
  File.WriteAllText(Path.Combine(Application.dataPath,"../owned-window-lifetime-result.txt"),"PASS "+checks+" ownership/lifecycle assertions; native minimize/restore not simulated");
 }catch(Exception e){File.WriteAllText(Path.Combine(Application.dataPath,"../owned-window-lifetime-result.txt"),"FAIL after "+checks+" checks: "+e);}finally{
  foreach(var w in windows)if(w)Object.DestroyImmediate(w);editor?.Dispose();if(mesh)Object.DestroyImmediate(mesh);if(document)Object.DestroyImmediate(document);EditorApplication.Exit(0);
 }}
}
#endif


#if MASHBOX_STUDIO_TESTS
using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;
public static class StudioViewMenuValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;static AnimationStudioWindow host;static MashBoxStudioViewport view;static StudioModelEditor editor;static StudioModelDocument doc;static int checks;
 static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
 static StudioMenu Menu(string title){var menu=new StudioMenu(title);typeof(StudioModelEditor).GetMethod("PopulateModelMenu",F).Invoke(editor,new object[]{title,menu,view,null});return menu;}
 static bool State(string name)=>name=="Wireframe"?editor.ModelDrawMode==DrawCameraMode.Wireframe:name=="Shaded Wireframe"?editor.ModelDrawMode==DrawCameraMode.TexturedWire:name=="Viewport Lighting"?editor.ToolbarLighting:name=="Material Textures"?editor.ToolbarTextures:name=="Checker Map"?editor.CheckerMap:name=="Backface Culling"?editor.BackfaceCulling:editor.XRayMode;
 public static void Run(){EditorApplication.delayCall+=Start;}
 static void Start()
 {
  string result;
  try{
   doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polys);doc.nodes.Add(new StudioModelDocument.Node{mesh=mesh,cutPolygons=polys,cutTriangles=mesh.triangles,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial}});
   AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(host,new object[]{true});typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});editor=host.ToolbarModelEditor;
   view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);editor.BindModelCommands(view,_=>{});
   var expected=new[]{"Wireframe","Shaded Wireframe","Viewport Lighting","Material Textures","Checker Map","Backface Culling","X-ray Mode"};
   var menu=Menu("View");Check(menu.Items.Where(i=>!i.Separator).Select(i=>i.Text).SequenceEqual(expected),"View does not match current toolbar controls plus X-ray");
   Check(Array.IndexOf(StudioModelEditor.ModelMenuTitles,"View")==2,"View menu order incorrect");
   Check(!Menu("Mesh Display").Items.Any(i=>expected.Contains(i.Text)),"Viewport controls remained in Mesh Display");
   var ids=menu.Items.Where(i=>!i.Separator).Select(i=>i.Command.Value).ToArray();
   Check(ids.Distinct().Count()==7,"Duplicate View command IDs");
   Check(ids[0]==StudioModelCommands.StableId("Mesh Display/Wireframe"),"Legacy Wireframe ID changed");
   Check(ids[1]==StudioModelCommands.StableId("Mesh Display/Shaded Wireframe"),"Legacy Shaded Wireframe ID changed");
   Check(ids[6]==StudioShortcut.XRay,"X-ray built-in ID changed");
   Check(StudioModelCommands.StandardBindings("Mesh Display/Shaded Wireframe").Single().key==KeyCode.BackQuote,"Backtick standard binding changed");
   Check(StudioShortcuts.Definitions.Single(d=>d.id==StudioShortcut.XRay).group=="View","X-ray preferences category incorrect");
   for(int i=0;i<expected.Length;i++)
   {
    var item=menu.Items.Single(p=>p.Text==expected[i]);var id=ids[i];
    Check(item.ShortcutEligible&&item.Enabled,"View command not assignable/enabled: "+item.Text);
    Check(StudioShortcuts.Definitions.Count(d=>d.id==id&&d.group=="View")==1,"View shortcut missing/duplicated: "+item.Text);
    bool initial=State(item.Text);item.Action();Check(State(item.Text)!=initial,"Menu action failed to toggle: "+item.Text);item.Action();Check(State(item.Text)==initial,"Retained menu callback used stale state: "+item.Text);
    var saved=StudioShortcuts.Bindings(id).Select(b=>b.Copy()).ToArray();
    try{
     KeyCode key=KeyCode.F1+i;StudioShortcuts.Set(id,new[]{new StudioShortcuts.Binding(key,StudioModifiers.Action|StudioModifiers.Alt)});
     var refreshed=Menu("View").Items.Single(p=>p.Text==item.Text);Check(refreshed.Shortcut==StudioShortcuts.Label(id)&&refreshed.Shortcut.Contains("F"+(i+1)),"Menu omitted current custom shortcut");
     if(id!=StudioShortcut.XRay){var evt=new Event{type=EventType.KeyDown,keyCode=key,modifiers=EventModifiers.Control|EventModifiers.Alt};Check(editor.TryExecuteModelCommand(evt,view)&&State(item.Text)!=initial,"Custom shortcut did not dispatch View action");refreshed.Action();Check(State(item.Text)==initial,"Shortcut/menu did not share state");}
     else Check(StudioShortcuts.Pressed(id,new Event{type=EventType.KeyDown,keyCode=key,modifiers=EventModifiers.Control|EventModifiers.Alt}),"X-ray custom shortcut lost built-in matching");
    }finally{StudioShortcuts.Set(id,saved);}
   }
   result="PASS "+checks+" View menu, shortcut discovery/IDs, custom dispatch and shared toggle-state checks";
  }catch(Exception e){result=e.ToString();}
  File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../view-menu-result.txt")),result);
  EditorApplication.delayCall+=()=>{if(view)view.Close();if(host)UnityEngine.Object.DestroyImmediate(host);EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};
 }
}
#endif

using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEditor.Overlays;using UnityEditor.Toolbars;using UnityEngine;using UnityEngine.UIElements;
namespace MashBoxSDK.AnimationStudio {
 public sealed class StudioToolbarSeparatorValidation : EditorWindow {
  const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;static StudioToolbarSeparatorValidation host;static StudioModelEditor editor;static StudioModelDocument doc;static readonly List<string> log=new List<string>();static double ready;static int frames;
  static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../ToolbarSeparators/"+n));
  [InitializeOnLoadMethod]static void Boot(){if(Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.update+=Begin;}
  static void Check(bool b,string m){if(!b)throw new Exception(m);log.Add("PASS "+m);}
  static void Begin(){EditorApplication.update-=Begin;File.Delete(Out("request.txt"));log.Clear();try{
   doc=CreateInstance<StudioModelDocument>();editor=new StudioModelEditor(()=>{});editor.Load(doc);var commands=editor.ToolbarCommands(null);
   host=GetWindow<StudioToolbarSeparatorValidation>(true,"Toolbar grouping check",false);host.position=new Rect(80,80,840,210);host.rootVisualElement.style.backgroundColor=StudioTheme.Current.Panel;host.rootVisualElement.style.paddingLeft=10;host.rootVisualElement.style.paddingTop=8;
   foreach(var bar in new StudioCommandToolbar[]{new StudioMeshToolsToolbar(),new StudioMeshDisplayToolbar()}){
    var row=bar.CreatePanelContent();typeof(StudioCommandToolbar).GetMethod("UpdateMenuSeparators",F).Invoke(bar,new object[]{commands});var buttons=row.Children().OfType<EditorToolbarButton>().ToArray();Check(buttons.Length==(bar is StudioMeshToolsToolbar?21:5),"Command count preserved "+bar.GetType().Name);
    var gaps=row.Children().Where(e=>e.name=="studio-toolbar-menu-separator").ToArray();string previous=null;int expected=0;foreach(var b in buttons){string group=commands.Find(c=>c.Key==(string)b.userData)?.MenuGroup;if(previous!=null&&group!=null&&previous!=group)expected++;if(group!=null)previous=group;b.icon=StudioCommandIcons.Get((string)b.userData);b.SetEnabled(true);}
    Check(gaps.Count(e=>e.style.display.value==DisplayStyle.Flex)==expected&&expected>0,"Separators follow current menu groups "+bar.GetType().Name);Check(gaps[0].style.display.value==DisplayStyle.None&&gaps.All(e=>e.pickingMode==PickingMode.Ignore),"No leading spacer or interactive separator");Check(gaps.All(e=>e.style.width.value.value==8&&e[0].style.width.value.value==1),"Horizontal gap and divider dimensions");
    var label=new Label(bar.GetType().Name);label.style.color=StudioTheme.Current.Text;label.style.marginBottom=4;host.rootVisualElement.Add(label);host.rootVisualElement.Add(row);row.style.marginBottom=10;
   }
   var vertical=new StudioMeshToolsToolbar();((ICreateVerticalToolbar)vertical).CreateVerticalToolbarContent();typeof(StudioCommandToolbar).GetMethod("UpdateMenuSeparators",F).Invoke(vertical,new object[]{commands});var content=(VisualElement)typeof(StudioCommandToolbar).GetField("contents",F).GetValue(vertical);Check(content.Children().Where(e=>e.name=="studio-toolbar-menu-separator").All(e=>e.style.height.value.value==8&&e[0].style.height.value.value==1),"Vertical docking rotates divider and gap");
   var custom=new StudioCustomToolbar();custom.Configure(new StudioCustomToolbarDefinition{id="MashBox/Custom/separator-validation",name="Unchanged custom",commands=new List<string>{"Mesh Tools/Extrude","Mesh Tools/Circularizeâ€¦"}});Check(!custom.CreatePanelContent().Children().Any(e=>e.name=="studio-toolbar-menu-separator"),"Personal custom toolbar contents unchanged");
   var authored=new StudioCustomToolbarDefinition{id="MashBox/Custom/manual-spacer-check",name="Manual spacers",commands=new List<string>{"Mesh Tools/Extrude",StudioCustomToolbars.SpacerKey,"Mesh Tools/Circularize…",StudioCustomToolbars.SpacerKey,"Mesh Tools/Weld…"}};
   var roundtrip=JsonUtility.FromJson<StudioCustomToolbarDefinition>(JsonUtility.ToJson(authored));Check(roundtrip.commands.SequenceEqual(authored.commands),"Manual spacers survive JSON persistence in exact order");
   var manual=new StudioCustomToolbar();manual.Configure(roundtrip);var manualRow=manual.CreatePanelContent();var manualGaps=manualRow.Children().Where(e=>e.name=="studio-toolbar-spacer").ToArray();
   Check(manualRow.Children().OfType<EditorToolbarButton>().Count()==3&&manualGaps.Length==2,"Manual spacers render as elements rather than command buttons");
   Check(manualGaps.All(e=>e.pickingMode==PickingMode.Ignore&&e.style.width.value.value==8&&e.style.display.value==DisplayStyle.Flex),"Manual spacers match passive horizontal divider style");
   foreach(var button in manualRow.Children().OfType<EditorToolbarButton>()){button.icon=StudioCommandIcons.Get((string)button.userData);button.SetEnabled(true);}
   host.rootVisualElement.Add(new Label("Custom toolbar with authored spacers"));host.rootVisualElement.Add(manualRow);
   var manualVertical=new StudioCustomToolbar();manualVertical.Configure(roundtrip);((ICreateVerticalToolbar)manualVertical).CreateVerticalToolbarContent();var manualColumn=(VisualElement)typeof(StudioCommandToolbar).GetField("contents",F).GetValue(manualVertical);Check(manualColumn.Children().Where(e=>e.name=="studio-toolbar-spacer").All(e=>e.style.height.value.value==8&&e[0].style.height.value.value==1),"Manual spacers rotate with vertical docking");
   foreach(var probe in new StudioCommandToolbar[]{new StudioMeshToolsToolbar(),manual}){
    var pane=probe==manual?manualRow:probe.CreatePanelContent();
    typeof(StudioCommandToolbar).GetMethod("OnLayout",F).Invoke(probe,new object[]{Layout.VerticalToolbar});typeof(StudioCommandToolbar).GetMethod("Refresh",F).Invoke(probe,null);
    Check(pane.style.flexDirection.value==FlexDirection.Column&&pane.style.flexGrow.value==0&&pane.Children().Where(e=>e.name=="studio-toolbar-menu-separator"||e.name=="studio-toolbar-spacer").All(e=>e.style.width.value.value<=64),"Dock event preserves compact vertical contents through refresh "+probe.GetType().Name);
    typeof(StudioCommandToolbar).GetMethod("OnLayout",F).Invoke(probe,new object[]{Layout.HorizontalToolbar});typeof(StudioCommandToolbar).GetMethod("Refresh",F).Invoke(probe,null);
    Check(pane.style.flexDirection.value==FlexDirection.Row&&pane.Children().Where(e=>e.name=="studio-toolbar-menu-separator"||e.name=="studio-toolbar-spacer").All(e=>e.style.width.value.value==8),"Horizontal return restores rows and eight-point gaps "+probe.GetType().Name);
   }
   ready=EditorApplication.timeSinceStartup+.6;frames=0;host.Repaint();EditorApplication.update+=Capture;
  }catch(Exception e){Finish(e);}}
  static void Capture(){if(EditorApplication.timeSinceStartup<ready)return;if(frames++==0){host.Repaint();ready=EditorApplication.timeSinceStartup+.3;return;}try{
   var gaps=host.rootVisualElement.Query<VisualElement>().ToList().Where(e=>(e.name=="studio-toolbar-menu-separator"||e.name=="studio-toolbar-spacer")&&e.resolvedStyle.display!=DisplayStyle.None).ToArray();Check(gaps.Length>0&&gaps.All(e=>e.worldBound.width>=7.9f&&e[0].worldBound.width>.9f),"Native retained layout gives visible gaps real bounds");Check(gaps.All(e=>e[0].worldBound.height>=e.worldBound.height-.5f&&e.worldBound.height>=20),"Built-in and authored dividers span full rendered toolbar height");
   var parent=typeof(EditorWindow).GetField("m_Parent",F).GetValue(host);var rect=new Rect(Vector2.zero,host.position.size);var rt=new RenderTexture((int)rect.width,(int)rect.height,0);rt.Create();var old=RenderTexture.active;
   try{parent.GetType().GetMethod("GrabPixels",F).Invoke(parent,new object[]{rt,rect});RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);try{image.ReadPixels(rect,0,0);image.Apply();Check(image.GetPixels32().Select(c=>c.r).Distinct().Count()>20,"Native toolbar pixels rendered");File.WriteAllBytes(Out("toolbar-separators.png"),image.EncodeToPNG());}finally{DestroyImmediate(image);}}finally{RenderTexture.active=old;DestroyImmediate(rt);}Finish(null);
  }catch(Exception e){Finish(e);}}
  static void Finish(Exception e){EditorApplication.update-=Capture;if(e!=null)log.Add("FAIL "+e);File.WriteAllLines(Out("result.txt"),log);if(host)host.Close();editor?.Dispose();if(doc)DestroyImmediate(doc);}
 }
}

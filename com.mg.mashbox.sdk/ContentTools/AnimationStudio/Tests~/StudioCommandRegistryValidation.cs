#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
internal static class StudioCommandRegistryValidation {
 static int checks;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
 public static void Run(){
  string result=Path.Combine(Application.dataPath,"../command-registry-result.txt");
  var view=ScriptableObject.CreateInstance<MashBoxStudioViewport>();
  string prior=StudioPreferenceStore.GetString(StudioShortcuts.PreferenceKey,"");
  try { using(var editor=new StudioModelEditor(()=>{})) {
   editor.BindModelCommands(view,d=>editor.Load(d));
   var populate=typeof(StudioModelEditor).GetMethod("PopulateModelMenu",BindingFlags.NonPublic|BindingFlags.Instance);
   int items=0;
   foreach(var category in StudioModelEditor.ModelMenuTitles){
    var menu=new StudioMenu(category,true);
    populate.Invoke(editor,new object[]{category,menu,view,(Action<StudioModelDocument>)(d=>editor.Load(d))});
    Action<System.Collections.Generic.List<StudioMenu.Item>> inspect=null;
    inspect=list=>{foreach(var item in list){if(item.Children.Count>0)inspect(item.Children);else if(!item.Separator && !item.Text.Contains("SDK rules unavailable")){
     if(!item.ShortcutEligible){Check(!item.Command.HasValue,"Excluded UI must not register shortcuts: "+item.Text);continue;}
     Check(item.Command.HasValue,"Missing command: "+category+"/"+item.Text);
     Check(StudioShortcuts.Definitions.Any(d=>d.id==item.Command.Value),"Missing Preferences row: "+item.Text);items++;
    }}};inspect(menu.Items);
   }
   Check(string.Join(",",StudioModelEditor.ModelMenuTitles)=="File,Edit,Select,Create,Modify,Window,Display,Mesh Tools,Mesh Display,UV,Lighting","Requested top menu order");
   var legacy=StudioModelCommands.Register("Deform","Blend Shape").Value;var nested=StudioModelCommands.Register("Mesh Tools","Deform/Blend Shape").Value;
   Check(legacy==nested&&StudioModelCommands.Key(nested)=="Deform/Blend Shape","Deform shortcut identity preserved");
   Check(StudioModelCommands.Category(nested)=="Mesh Tools","Nested Deform dispatch category");
   Check(StudioShortcuts.Definitions.Single(d=>d.id==nested).group=="Mesh Tools","Deform Preferences group matches its menu");
   var meshMenu=new StudioMenu("Mesh Tools",true);populate.Invoke(editor,new object[]{"Mesh Tools",meshMenu,view,(Action<StudioModelDocument>)(d=>editor.Load(d))});
   Check(meshMenu.Items.Single(i=>i.Text=="Deform").Children.Count==2,"Both Deform operations nested once");
   Check(items>50,"Expected all menu actions, got "+items);
   foreach(string name in new[]{"Bevel…","Merge Vertices","Insert Edge Loop","Multi-Cut"})
    Check(StudioShortcuts.Definitions.Any(d=>d.group=="Mesh Tools"&&d.name==name),"Missing Mesh Tools/"+name);
   var soften=StudioShortcuts.Definitions.Where(d=>d.group=="Mesh Display"&&d.name.StartsWith("Soften")).ToArray();
   Check(soften.Length==2 && soften[0].id!=soften[1].id,"Soften and Soften options need distinct bindings");
   Check(StudioShortcuts.Bindings(StudioShortcut.Move)[0].key==KeyCode.W,"Legacy Move default");
   Check(StudioShortcuts.Definitions.Select(d=>d.id).Distinct().Count()==StudioShortcuts.Definitions.Length,"Unique command IDs");
   var bevel=StudioShortcuts.Definitions.Single(d=>d.group=="Mesh Tools"&&d.name=="Bevel…");
   Check(bevel.defaults.Any(b=>b.key==KeyCode.B&&b.modifiers==StudioModifiers.Action),"Standard Bevel default");
   Check(StudioShortcuts.Definitions.Single(d=>d.group=="Mesh Display"&&d.name=="Shaded Wireframe").defaults.Any(b=>b.key==KeyCode.BackQuote&&b.allowShift),"Standard backtick and tilde toggle default");
   StudioShortcuts.Set(bevel.id,new[]{new StudioShortcuts.Binding(KeyCode.F8)});
   var evt=new Event{type=EventType.KeyDown,keyCode=KeyCode.F8};
   Check(editor.TryExecuteModelCommand(evt,view)&&evt.type==EventType.Used,"Disabled tool consumes shortcut safely");
   string export=Path.Combine(Application.dataPath,"../registry-hotkeys.json");
   StudioShortcuts.Export(export);StudioShortcuts.Reset(bevel.id);StudioShortcuts.Import(export);
   Check(StudioShortcuts.Bindings(bevel.id)[0].key==KeyCode.F8,"Dynamic binding import/export");
   StudioShortcuts.Reload();Check(StudioShortcuts.Bindings(bevel.id)[0].key==KeyCode.F8,"Dynamic binding reload");
   Check(!StudioShortcuts.Definitions.Any(d=>d.group=="Display"),"Display excluded from shortcut list");
   Check(!StudioShortcuts.Definitions.Any(d=>d.group=="Modify"&&d.name=="Local Axes"),"No duplicate Local Axes action");
   var toggle=StudioShortcuts.Definitions.Single(d=>d.group=="Select"&&d.name=="Through Selection");
   var xray=typeof(StudioModelEditor).GetField("xray",BindingFlags.Instance|BindingFlags.NonPublic);
   bool before=(bool)xray.GetValue(editor);
   StudioShortcuts.Set(toggle.id,new[]{new StudioShortcuts.Binding(KeyCode.F9)});
   Check(editor.TryExecuteModelCommand(new Event{type=EventType.KeyDown,keyCode=KeyCode.F9},view),"Dispatch registered action");
   Check((bool)xray.GetValue(editor)!=before,"Shortcut executes existing menu callback");
   Check(!StudioShortcuts.Definitions.Any(d=>d.id==StudioShortcut.Preferences||d.name.Contains("Recent")||d.name.Contains("Themes")||d.name.Contains("Foliage Morph Set")||d.name.Contains("Lighting Settings")),"UI navigation excluded");
   var future=new StudioMenu("Mesh Tools");future.AddItem(new GUIContent("Future Tool"),false,()=>{});
   Check(StudioShortcuts.Definitions.Any(d=>d.name=="Future Tool"),"Future menu additions auto-register");
   foreach(float value in new[]{.1f,1f,2f,4f})Check(StudioDragSensitivity.Normalize(value)==value,"Sensitivity "+value);
   File.WriteAllText(result,"PASS "+checks+" assertions across "+items+" menu actions");
  }
  StudioPreferenceStore.SetString(StudioShortcuts.PreferenceKey,prior);StudioShortcuts.Reload();UnityEngine.Object.DestroyImmediate(view);EditorApplication.Exit(0);
  }catch(Exception e){File.WriteAllText(result,"FAIL "+e);EditorApplication.Exit(1);}
 }
}
}
#endif

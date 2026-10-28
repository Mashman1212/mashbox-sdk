using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;
using MashBoxSDK.AnimationStudio;
using Object = UnityEngine.Object;

public class StudioCustomToolbarValidation : EditorWindow
{
    const BindingFlags F = BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static MashBoxStudioViewport view; static StudioToolbarCustomization customization; static StudioCustomToolbarValidation host;
    static int checks, stage; static double next; static string failure; static bool painted;
    static string savedHotkeys; static List<StudioCustomToolbarDefinition> definitions;
    static bool statisticsBefore;
    static bool statisticsAfter; static bool deferredObserved;
    static string Out(string name) => Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
    static void Check(bool value,string message) { if(!value)throw new Exception(message);checks++; }
    static void Call(string name) => typeof(MashBoxStudioViewport).GetMethod(name,F).Invoke(view,null);
    static void Set(object target,string field,object value) => target.GetType().GetField(field,F).SetValue(target,value);
    static T Get<T>(object target,string field) => (T)target.GetType().GetField(field,F).GetValue(target);
    public static void Run() { EditorApplication.delayCall += Start; }
    static void Start()
    {
        try
        {
            AnimationStudioWindow.Open();
            var studio = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(studio,new object[]{true});
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(x=>x.Studio==studio);
            Call("InitializeStudioToolbars");
            savedHotkeys=File.Exists(StudioPreferenceStore.HotkeysPath)?File.ReadAllText(StudioPreferenceStore.HotkeysPath):null;
            definitions=new List<StudioCustomToolbarDefinition>{StudioCustomToolbars.Create("Mesh Essentials"),StudioCustomToolbars.Create("View Controls")};
            definitions[0].commands.AddRange(new[]{"Create/Polygon Primitives/Cube","Mesh Tools/Bevel…","Modify/Move","Modify/Rotate","Modify/Scale"});
            definitions[1].commands.AddRange(new[]{"Viewport/Camera","Viewport/Lighting","Control/MashBox/Viewport/Orientation","Control/MashBox/Viewport/Textures","Display/Mesh Statistics"});
            StudioCustomToolbars.Save(definitions);
            Check(StudioCustomToolbars.Read().Count==2,"Definitions did not persist");
            foreach(var definition in definitions)
            {
                var overlay=view.overlayCanvas.overlays.OfType<StudioCustomToolbar>().SingleOrDefault(x=>x.id==definition.id);
                Check(overlay!=null,"Missing custom ID "+definition.id+"; registered="+string.Join(",",view.overlayCanvas.overlays.OfType<StudioCustomToolbar>().Select(x=>x.id)));
                Check(overlay.displayName==definition.name,"Overlay identity/name mismatch: "+overlay.id);
                Check(overlay.displayed,"New overlay hidden");
                var content=overlay.rootVisualElement.Q("custom-toolbar-contents") ?? overlay.CreatePanelContent();
                Check(content.Children().Count()==definition.commands.Count,"Buttons missing");
                Check(content.Children().OfType<EditorToolbarButton>().Where(x=>x.userData is string).All(x=>string.IsNullOrEmpty(x.text)&&x.icon&&!string.IsNullOrWhiteSpace(x.tooltip)),"Icon-only function missing artwork or tooltip");
            }
            var editor=studio.ToolbarModelEditor; var commands=editor.ToolbarCommands(view);
            Check(commands.Count>60,"Incomplete function catalog: "+commands.Count);
            Check(commands.Select(x=>x.Key).Distinct().Count()==commands.Count,"Duplicate function IDs");
            foreach(var command in commands)
            {
                var icon=StudioCommandIcons.Get(command.Key);Check(icon&&icon.width==24&&icon.height==24,"Missing icon: "+command.Path);
                Check(ReferenceEquals(icon,StudioCommandIcons.Get(command.Key)),"Icon recreated: "+command.Path);
            }
            Check(commands.Any(x=>x.Key=="Control/MashBox/Viewport/Textures"),"Toolbar-only function missing");
            foreach(string path in new[]{"Edit/Preferences…","Window/Themes…","Viewport/Camera","Viewport/Lighting","Mesh Tools/Bevel…","Display/Mesh Statistics"})Check(commands.Any(x=>x.Key==path),"Function absent: "+path);
            var cube=commands.Single(x=>x.Key=="Create/Polygon Primitives/Cube"); Check(cube.Enabled,"Cube command disabled"); cube.Execute();
            Check(studio.ToolbarModelEditor.Document&&studio.ToolbarModelEditor.Document.nodes.Count>0,"Toolbar catalog did not execute actual primitive command");
            var bar=view.overlayCanvas.overlays.OfType<StudioCustomToolbar>().First();
            bar.Undock();bar.floatingPosition=new Vector2(350,160);bar.displayed=false;Call("SaveStoredToolbars");string layout=StudioPreferenceStore.GetString("Studio.Toolbars");
            bar.displayed=true;Call("RestoreStoredToolbars");Check(!bar.displayed,"Saved custom visibility not restored");Check(bar.floating,"Custom floating layout not restored");
            Check(StudioPreferenceStore.GetString("Studio.Toolbars")==layout,"Read-only restoration rewrote layout");bar.displayed=true;
            statisticsBefore=view.Studio.ToolbarModelEditor.CameraOptionsVisible;
            Check(bar.containerWindow==view,"Custom toolbar has wrong command owner");
            Check(view.Studio.ToolbarModelEditor.ToolbarCommands(view).Any(x=>x.Key=="Display/Mesh Statistics"&&x.Enabled),"Statistics function unavailable");
            bar.Execute("Viewport/Camera");
            EditorApplication.delayCall+=()=>{statisticsAfter=view.Studio.ToolbarModelEditor.CameraOptionsVisible;deferredObserved=true;};
            var other=view.overlayCanvas.overlays.OfType<StudioCustomToolbar>().Last();var original=new SaveData(other);
            view.overlayCanvas.RestoreOverlay(other,JsonUtility.FromJson<SaveData>("{\"containerId\":\"overlay-toolbar__left\",\"index\":1000,\"displayed\":true,\"layout\":2}"));
            Check(other.rootVisualElement.Q("custom-toolbar-contents")?.style.flexDirection.value==FlexDirection.Column,"Vertical custom toolbar content missing or wrong direction");
            view.overlayCanvas.RestoreOverlay(other,original);
            StudioToolbarCustomization.Open(view);customization=Resources.FindObjectsOfTypeAll<StudioToolbarCustomization>().Single();
            Set(customization,"selected",definitions[0].id);
            customization.Insert("Viewport/Camera",1);
            var stored=StudioCustomToolbars.Read();Check(stored[0].commands[1]=="Viewport/Camera","Add at index failed");
            customization.Insert("Viewport/Camera",4,definitions[0].id,1);
            stored=StudioCustomToolbars.Read();Check(stored[0].commands[3]=="Viewport/Camera","Reorder failed");Check(stored[0].commands.Count==6,"Reorder duplicated function");
            Check(view.overlayCanvas.overlays.OfType<StudioCustomToolbar>().Count()==2,"Edit duplicated overlays");
            Set(customization,"toolbars",StudioCustomToolbars.Read());
            definitions=StudioCustomToolbars.Read();definitions[0].name="Mesh Essentials";StudioCustomToolbars.Save(definitions);
            host=CreateInstance<StudioCustomToolbarValidation>();host.position=new Rect(50,50,960,680);host.ShowUtility();
            Application.logMessageReceived+=Log; next=EditorApplication.timeSinceStartup+.5;EditorApplication.update+=Tick;
        }catch(Exception e){Finish(e.ToString());}
    }
    static void Log(string s,string trace,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)failure=s+"\n"+trace; }
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.25;
        try
        {
            if(failure!=null)throw new Exception(failure);
            if(stage==0&&!deferredObserved)
                foreach(EditorApplication.CallbackFunction callback in EditorApplication.delayCall?.GetInvocationList()??Array.Empty<Delegate>())
                    if(callback.Method.DeclaringType?.DeclaringType==typeof(StudioCommandToolbar)||callback.Method.DeclaringType?.DeclaringType==typeof(StudioCustomToolbarValidation)||callback.Method.DeclaringType==typeof(StudioCustomToolbarValidation))
                    { EditorApplication.delayCall-=callback;callback(); }
            host.SendEvent(new Event{type=EventType.Layout});host.SendEvent(new Event{type=EventType.Repaint});if(!painted)return;painted=false;
            if(stage==0)
            {
                Check(deferredObserved&&statisticsAfter,"Deferred camera toolbar command did not execute; observed="+deferredObserved+" before="+statisticsBefore+" after="+statisticsAfter);
                foreach(var overlay in view.overlayCanvas.overlays.OfType<StudioCustomToolbar>())
                    Check(overlay.rootVisualElement.Q("custom-toolbar-contents")!=null,"Native contents absent after layout: "+overlay.id+" collapsed="+overlay.collapsed+" layout="+overlay.layout+" tree="+string.Join(",",overlay.rootVisualElement.Query<VisualElement>().ToList().Select(x=>x.name)));
                // Actual + button coordinates are obtained from native GUILayout's row rects.
                var rows=lastRects.Where(x=>x.height==24&&x.width>400).ToArray();Check(rows.Length>5,"Missing native function rows");
                int before=StudioCustomToolbars.Read()[0].commands.Count;
                var p=new Vector2(host.position.width-46, host.position.height*.32f+110);
                host.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=p});host.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=p});
                Check(StudioCustomToolbars.Read()[0].commands.Count==before+1,"Native + button failed");
                stage++;return;
            }
            if(stage==1)
            {
                int count=StudioCustomToolbars.Read()[0].commands.Count;
                var source=new Vector2(310,host.position.height*.32f+110);
                host.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=source});
                host.SendEvent(new Event{type=EventType.MouseDrag,button=0,mousePosition=source+new Vector2(8,0)});
                Check(DragAndDrop.GetGenericData("MashBox.CustomToolbar.Function")!=null,"Native function drag did not start");
                var drop=new Vector2(340,110);
                host.SendEvent(new Event{type=EventType.DragUpdated,mousePosition=drop});host.SendEvent(new Event{type=EventType.DragPerform,mousePosition=drop});
                Check(StudioCustomToolbars.Read()[0].commands.Count==count+1,"Native function drop did not add");
                string first=StudioCustomToolbars.Read()[0].commands[0];source=new Vector2(330,60);
                host.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=source});host.SendEvent(new Event{type=EventType.MouseDrag,button=0,mousePosition=source+new Vector2(8,0)});
                Check(DragAndDrop.GetGenericData("MashBox.CustomToolbar.Function")!=null,"Native reorder drag did not start");
                drop=new Vector2(340,170);host.SendEvent(new Event{type=EventType.DragUpdated,mousePosition=drop});host.SendEvent(new Event{type=EventType.DragPerform,mousePosition=drop});
                Check(StudioCustomToolbars.Read()[0].commands.Count==count+1,"Native reorder duplicated function");
                Check(StudioCustomToolbars.Read()[0].commands[0]!=first,"Native reorder left item in place");
                // Reopen reconstructs definitions from the settings file, not a retained list.
                customization.Close();StudioToolbarCustomization.Open(view);customization=Resources.FindObjectsOfTypeAll<StudioToolbarCustomization>().Single();
                Check(Get<List<StudioCustomToolbarDefinition>>(customization,"toolbars")[0].commands.Count==8,"Reopen lost contents");
                Set(customization,"selected",definitions[1].id);host.position=new Rect(50,50,760,540);stage++;return;
            }
            if(stage==2)
            {
            Check(savedHotkeys==(File.Exists(StudioPreferenceStore.HotkeysPath)?File.ReadAllText(StudioPreferenceStore.HotkeysPath):null),"Customization modified hotkeys");
            var nativeHost=typeof(EditorWindow).GetField("m_Parent",F).GetValue(view);
            var rect=new Rect(Vector2.zero,view.position.size);var capture=new RenderTexture((int)rect.width,(int)rect.height,0);capture.Create();var previous=RenderTexture.active;
            try { nativeHost.GetType().GetMethod("GrabPixels",F).Invoke(nativeHost,new object[]{capture,rect});RenderTexture.active=capture;
                var image=new Texture2D(capture.width,capture.height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,capture.width,capture.height),0,0);image.Apply();File.WriteAllBytes(Out("custom-toolbar-native-viewport.png"),image.EncodeToPNG());Object.DestroyImmediate(image);
            } finally { RenderTexture.active=previous;Object.DestroyImmediate(capture); }
            stage=3;host.position=new Rect(50,50,960,1000);return;
            }
            var all=StudioCustomToolbars.Read();all.RemoveAt(0);StudioCustomToolbars.Save(all);
            Check(view.overlayCanvas.overlays.OfType<StudioCustomToolbar>().Count()==1,"Delete left overlay behind");
            Check(StudioCustomToolbars.Read().Count==1,"Delete not saved");
            Finish("PASS "+checks+" catalog, native overlay identity, add/reorder/reopen/delete, layout, command execution and UI rendering checks");
        }catch(Exception e){Finish(e.ToString());}
    }
    static readonly List<Rect> lastRects=new List<Rect>();
    void OnGUI()
    {
        if(!customization)return;bool paint=Event.current.type==EventType.Repaint;RenderTexture rt=null;var old=RenderTexture.active;
        try
        {
            customization.position=position;
            if(paint){rt=new RenderTexture((int)position.width,(int)position.height,0);rt.Create();RenderTexture.active=rt;GL.PushMatrix();GL.Clear(true,true,Color.clear);GL.LoadPixelMatrix(0,rt.width,rt.height,0);}
            using(StudioTheme.Scope())
            {
                EditorGUI.DrawRect(new Rect(Vector2.zero,position.size),StudioTheme.Current.Panel);
                if(stage==3)
                {
                    int i=0;foreach(var command in view.Studio.ToolbarModelEditor.ToolbarCommands(view))
                    {
                        int x=(i%3)*320,y=(i/3)*28;
                        GUI.DrawTexture(new Rect(x+4,y+2,24,24),StudioCommandIcons.Get(command.Key),ScaleMode.ScaleToFit);
                        GUI.Label(new Rect(x+34,y+2,280,24),command.Path);i++;
                    }
                }
                else customization.DrawControls();
            }
            if(paint)
            {
                lastRects.Clear();
                var flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
                var cache=typeof(GUILayoutUtility).GetProperty("current",flags)?.GetValue(null)??typeof(GUILayoutUtility).GetField("current",flags)?.GetValue(null);
                var top=cache.GetType().GetField("topLevel",flags).GetValue(cache);
                void Walk(object item,Vector2 offset)
                {
                    var type=item.GetType();var rect=(Rect)type.GetField("rect",flags).GetValue(item);lastRects.Add(new Rect(rect.position+offset,rect.size));
                    var children=type.GetField("entries",flags)?.GetValue(item) as System.Collections.IEnumerable;
                    if(children!=null)foreach(var child in children)Walk(child,offset);
                }
                Walk(top,Vector2.zero);
                var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();
                Check(t.GetPixels32().Select(x=>x.r).Distinct().Count()>20,"Customization did not render");File.WriteAllBytes(Out("custom-toolbar-ui-"+stage+".png"),t.EncodeToPNG());Object.DestroyImmediate(t);painted=true;
            }
        }catch(Exception e){failure=e.ToString();}
        finally{if(paint){GL.PopMatrix();RenderTexture.active=old;Object.DestroyImmediate(rt);}}
    }
    static void Finish(string result)
    {
        EditorApplication.update-=Tick;Application.logMessageReceived-=Log;File.WriteAllText(Out("custom-toolbar-result.txt"),result);
        EditorApplication.delayCall+=()=>{try{if(host)host.Close();if(customization)customization.Close();}finally{EditorApplication.Exit(result.StartsWith("PASS")?0:1);}};
    }
}

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

public class StudioMeshToolbarsValidation : EditorWindow
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    [Serializable] class State {public string id,containerId;public int index,dockPosition,layout;public bool displayed;}
    [Serializable] class Entry {public string id;public int index;public SaveData state;}
    [Serializable] class LayoutData {public bool enabled;public List<Entry> items=new List<Entry>();}
    static int checks;static MashBoxStudioViewport view;static StudioMeshToolbarsValidation host;static double next;static int stage;
    static void Check(bool p,string message){if(!p)throw new Exception(message);checks++;}
    static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
    static State StateOf(Overlay bar)=>JsonUtility.FromJson<State>(JsonUtility.ToJson(new SaveData(bar)));
    static void Call(string name)=>typeof(MashBoxStudioViewport).GetMethod(name,F).Invoke(view,null);
    static void Open()
    {
        AnimationStudioWindow.Open();var studio=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
        typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(studio,new object[]{true});
        view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(x=>x.Studio==studio);Call("InitializeStudioToolbars");
    }
    public static void Run(){EditorApplication.delayCall+=()=>{try{
        Open();var tools=view.overlayCanvas.overlays.OfType<StudioMeshToolsToolbar>().Single();var display=view.overlayCanvas.overlays.OfType<StudioMeshDisplayToolbar>().Single();
        var added=new Overlay[]{tools,display};
        var old=view.overlayCanvas.overlays.Where(x=>x is ToolbarOverlay&&x.GetType().Assembly==typeof(MashBoxStudioViewport).Assembly&&!added.Contains(x)).ToArray();
        var saved=new LayoutData{enabled=false};foreach(var bar in old){var state=new SaveData(bar);saved.items.Add(new Entry{id=bar.id,index=StateOf(bar).index,state=state});}
        string oldJson=JsonUtility.ToJson(saved);StudioPreferenceStore.SetString("Studio.Toolbars",oldJson);
        foreach(var bar in added)view.overlayCanvas.RestoreOverlay(bar,JsonUtility.FromJson<SaveData>("{\"containerId\":\"overlay-toolbar__right\",\"displayed\":false,\"layout\":2}"));
        Call("RestoreStoredToolbars");
        foreach(var bar in added)
        {
            var state=StateOf(bar);Check(bar.displayed,"New toolbar is not enabled: "+bar.id);Check(state.containerId=="overlay-toolbar__top","New toolbar not at top");Check(state.dockPosition==0,"New toolbar right-aligned");Check(state.layout==1,"New toolbar not horizontal");
            var contents=bar.CreatePanelContent();var buttons=contents.Children().OfType<EditorToolbarButton>().ToArray();Check(buttons.Length==8,"Menu functions missing");
            var commands=view.Studio.ToolbarModelEditor.ToolbarCommands(view);
            foreach(var button in buttons){string key=(string)button.userData;var command=commands.SingleOrDefault(x=>x.Key==key);Check(command!=null,"Unknown command: "+key);Check(button.icon,"Missing icon: "+key);Check(string.IsNullOrEmpty(button.text),"Visible word label: "+key);Check(button.enabledSelf==command.Enabled,"Menu availability differs: "+key);Check(button.tooltip==command.Path,"Tooltip differs: "+key);}
        }
        foreach(var entry in saved.items)
        {var actual=StateOf(view.overlayCanvas.overlays.Single(x=>x.id==entry.id));var expected=JsonUtility.FromJson<State>(JsonUtility.ToJson(entry.state));Check(actual.containerId==expected.containerId&&actual.dockPosition==expected.dockPosition&&actual.index==expected.index,"Existing toolbar moved: "+entry.id);}
        Check(!view.overlayCanvas.overlaysEnabled,"Saved disabled canvas overridden");Check(StudioPreferenceStore.GetString("Studio.Toolbars")==oldJson,"Default introduction rewrote settings during load");
        var definition=StudioCustomToolbars.Create("Coexistence");definition.commands.Add("Modify/Move");StudioCustomToolbars.Save(new List<StudioCustomToolbarDefinition>{definition});
        Check(view.overlayCanvas.overlays.Contains(tools)&&view.overlayCanvas.overlays.Contains(display),"Custom toolbar sync removed built-ins");
        tools.displayed=false;view.overlayCanvas.RestoreOverlay(display,JsonUtility.FromJson<SaveData>("{\"containerId\":\"overlay-toolbar__left\",\"displayed\":true,\"dockPosition\":0,\"layout\":2}"));Call("FlushStoredToolbarSave");
        string savedJson=StudioPreferenceStore.GetString("Studio.Toolbars");tools.displayed=true;display.Undock();Call("RestoreStoredToolbars");
        Check(!tools.displayed,"Saved hidden state reset");Check(StateOf(display).containerId=="overlay-toolbar__left"&&StateOf(display).layout==2,"Saved placement reset");Check(StudioPreferenceStore.GetString("Studio.Toolbars")==savedJson,"Saved state rewritten on reload");
        var menu=new StudioMenu();view.AddStudioToolbarMenu(menu);var root=menu.Items.Single(x=>x.Text=="Toolbars");Check(root.Children.Count(x=>x.Text=="Mesh Tools")==1,"Missing/duplicate Mesh Tools menu");Check(root.Children.Count(x=>x.Text=="Mesh Display")==1,"Missing/duplicate Mesh Display menu");
        Finish("PASS "+checks+" default-on/top-leading, command/icon/availability, existing placement, custom coexistence and saved layout checks",false);
    }catch(Exception ex){Finish(ex.ToString(),false);}};}
    public static void Render(){EditorApplication.delayCall+=()=>{try{
        Open();host=CreateInstance<StudioMeshToolbarsValidation>();host.position=new Rect(100,100,420,150);host.titleContent=new GUIContent("Mesh toolbars validation");
        host.rootVisualElement.style.paddingLeft=12;host.rootVisualElement.style.paddingTop=8;host.rootVisualElement.style.backgroundColor=StudioTheme.Current.Panel;
        foreach(var bar in new StudioCommandToolbar[]{view.overlayCanvas.overlays.OfType<StudioMeshToolsToolbar>().Single(),view.overlayCanvas.overlays.OfType<StudioMeshDisplayToolbar>().Single()})
        {var label=new Label(bar.displayName);label.style.color=StudioTheme.Current.Text;label.style.marginBottom=5;host.rootVisualElement.Add(label);var content=bar.CreatePanelContent();content.style.flexDirection=FlexDirection.Row;content.style.marginBottom=12;host.rootVisualElement.Add(content);}
        typeof(MashBoxStudioViewport).GetMethod("StyleStudioToolbar",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{host.rootVisualElement});
        host.ShowUtility();next=EditorApplication.timeSinceStartup+1;EditorApplication.update+=Tick;
    }catch(Exception ex){Finish(ex.ToString(),true);}};}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.3;
        try{
            if(stage++==0){host.Repaint();return;}
            var buttons=host.rootVisualElement.Query<EditorToolbarButton>().ToList();Check(buttons.Count==16,"Rendered buttons missing");foreach(var b in buttons)Check(b.worldBound.width>0&&b.worldBound.height>0,"Button has no rendered bounds: "+b.worldBound);
            var parent=typeof(EditorWindow).GetField("m_Parent",F).GetValue(host);var rect=new Rect(Vector2.zero,host.position.size);var rt=new RenderTexture((int)rect.width,(int)rect.height,0);rt.Create();var previous=RenderTexture.active;
            try{parent.GetType().GetMethod("GrabPixels",F).Invoke(parent,new object[]{rt,rect});RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();Check(image.GetPixels32().Select(x=>x.r).Distinct().Count()>20,"Retained toolbar UI did not render");File.WriteAllBytes(Out("mesh-toolbars-native.png"),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);}finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(rt);}
            Finish("PASS "+checks+" retained toolbar layout/native pixel checks",true);
        }catch(Exception ex){Finish(ex.ToString(),true);}
    }
    static void Finish(string result,bool render){EditorApplication.update-=Tick;File.WriteAllText(Out(render?"mesh-toolbars-render-result.txt":"mesh-toolbars-result.txt"),result);EditorApplication.Exit(result.StartsWith("PASS")?0:1);}
}

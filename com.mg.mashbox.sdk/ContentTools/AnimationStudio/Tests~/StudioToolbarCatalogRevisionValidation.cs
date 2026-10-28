using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using MashBoxSDK.AnimationStudio;

public class StudioToolbarCatalogRevisionValidation : EditorWindow
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
    [Serializable] class ToolbarFile {public string format,layout,customToolbars;public int version;}
    [Serializable] class NativeState {public string containerId;public int dockPosition,index,layout;public bool displayed;}
    [Serializable] class SavedItem {public string id;public int index;public SaveData state;}
    [Serializable] class SavedLayout {public bool enabled;public List<SavedItem> items;}
    static int checks,stage;static MashBoxStudioViewport view;static StudioModelEditor editor;
    static StudioToolbarCatalogRevisionValidation host;static StudioMenuPopup popup;static double next;static bool painted;
    static readonly string[] pages={"File","Edit","Select","Create","Modify","Window","Mesh Tools","Mesh Display","UV","Render","Display"};
    static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static void Set(Type t,string field,object value)=>t.GetField(field,F).SetValue(null,value);
    static void Call(string method)=>typeof(MashBoxStudioViewport).GetMethod(method,F).Invoke(view,null);
    static StudioMenu Menu(string title)
    {
        var menu=new StudioMenu(title);var loaded=typeof(StudioModelEditor).GetField("commandLoaded",F).GetValue(editor);
        typeof(StudioModelEditor).GetMethod("PopulateModelMenu",F).Invoke(editor,new object[]{title,menu,view,loaded});return menu;
    }
    static void Open()
    {
        AnimationStudioWindow.Open();var studio=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
        typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(studio,new object[]{true});
        view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(x=>x.Studio==studio);editor=view.Studio.ToolbarModelEditor;Call("InitializeStudioToolbars");
    }
    static void ResetToolbarCache(){Set(typeof(StudioToolbarPreferences),"current",null);Set(typeof(StudioToolbarPreferences),"dirty",false);Set(typeof(StudioToolbarPreferences),"unreadable",false);}
    public static void Run(){EditorApplication.delayCall+=()=>{string result;try{
        if(!Application.dataPath.Replace('\\','/').EndsWith("/CustomToolbarProject/Assets"))throw new Exception("Disposable CustomToolbarProject required");
        string generalBefore=File.Exists(StudioPreferenceStore.SettingsPath)?File.ReadAllText(StudioPreferenceStore.SettingsPath):null;
        string hotkeysBefore=File.Exists(StudioPreferenceStore.HotkeysPath)?File.ReadAllText(StudioPreferenceStore.HotkeysPath):null;
        if(File.Exists(StudioToolbarPreferences.PathName))File.Delete(StudioToolbarPreferences.PathName);
        string legacyLayout="{\"enabled\":false,\"items\":[]}";
        string legacyDefinitions="{\"items\":[{\"id\":\"MashBox/Custom/migration\",\"name\":\"Migrated\",\"commands\":[\"Modify/Move\",\"Mesh Display/Recalculate Normals\"]}]}";
        var dataField=typeof(StudioPreferenceStore).GetField("data",F);
        string general="{\"format\":\"MashBoxStudio.Preferences\",\"version\":1,\"entries\":[{\"key\":\"Studio.Toolbars\",\"value\":"+Quote(legacyLayout)+"},{\"key\":\"Studio.CustomToolbars\",\"value\":"+Quote(legacyDefinitions)+"}]}";
        dataField.SetValue(null,JsonUtility.FromJson(general,dataField.FieldType));Set(typeof(StudioPreferenceStore),"dirty",false);Set(typeof(StudioPreferenceStore),"migrateLegacy",false);ResetToolbarCache();
        Check(StudioPreferenceStore.GetString("Studio.Toolbars")==legacyLayout,"Legacy layout not migrated in memory");
        Check(StudioPreferenceStore.GetString("Studio.CustomToolbars")==legacyDefinitions,"Legacy contents not migrated in memory");
        Check(!File.Exists(StudioToolbarPreferences.PathName),"Read-only migration created a file");
        Check(StudioCustomToolbars.Read().Single().commands.SequenceEqual(new[]{"Modify/Move"}),"Retired button still shown");
        StudioPreferenceStore.SetString("Studio.Toolbars",legacyLayout);StudioPreferenceStore.Flush();
        var disk=JsonUtility.FromJson<ToolbarFile>(File.ReadAllText(StudioToolbarPreferences.PathName));
        Check(disk.format=="MashBoxStudio.ToolbarOverlays"&&disk.version==1,"Wrong toolbar file schema");Check(disk.layout==legacyLayout&&disk.customToolbars==legacyDefinitions,"Combined file lost legacy state");
        Check(generalBefore==(File.Exists(StudioPreferenceStore.SettingsPath)?File.ReadAllText(StudioPreferenceStore.SettingsPath):null),"Toolbar write changed general settings file");
        Check(hotkeysBefore==(File.Exists(StudioPreferenceStore.HotkeysPath)?File.ReadAllText(StudioPreferenceStore.HotkeysPath):null),"Toolbar write changed hotkey file");
        ResetToolbarCache();Check(StudioPreferenceStore.GetString("Studio.Toolbars")==legacyLayout,"Combined file did not reload");
        StudioPreferenceStore.SetString("Studio.Toolbars","");StudioPreferenceStore.SetString("Studio.CustomToolbars","{\"items\":[]}");Open();
        string[] top={StudioGridToolbar.Id,StudioViewportToolbar.Id,StudioDisplayToolbar.Id,StudioPolygonPrimitivesToolbar.Id,StudioMeshToolsToolbar.Id,StudioMeshDisplayToolbar.Id};
        var indices=new List<int>();for(int i=0;i<top.Length;i++){var b=view.overlayCanvas.overlays.Single(x=>x.id==top[i]);var state=JsonUtility.FromJson<NativeState>(JsonUtility.ToJson(new SaveData(b)));Check(state.containerId=="overlay-toolbar__top"&&state.dockPosition==0&&state.layout==1&&b.displayed,"Wrong factory dock: "+b.id);indices.Add(state.index);}Check(indices.SequenceEqual(indices.OrderBy(x=>x))&&indices.Distinct().Count()==top.Length,"Wrong relative factory toolbar order: "+string.Join(",",indices));
        var selection=JsonUtility.FromJson<NativeState>(JsonUtility.ToJson(new SaveData(view.overlayCanvas.overlays.Single(x=>x.id==StudioSelectionToolbar.Id))));Check(selection.containerId=="overlay-toolbar__left"&&selection.layout==2,"Selection toolbar not at left");
        Call("SaveStoredToolbars");var older=JsonUtility.FromJson<SavedLayout>(StudioPreferenceStore.GetString("Studio.Toolbars"));older.items.RemoveAll(x=>x.id==StudioPolygonPrimitivesToolbar.Id);
        var newPrimitive=view.overlayCanvas.overlays.OfType<StudioPolygonPrimitivesToolbar>().Single();
        view.overlayCanvas.RestoreOverlay(newPrimitive,JsonUtility.FromJson<SaveData>("{\"containerId\":\"overlay-toolbar__right\",\"displayed\":false,\"layout\":2}"));
        string olderJson=JsonUtility.ToJson(older);StudioPreferenceStore.SetString("Studio.Toolbars",olderJson);Call("RestoreStoredToolbars");
        indices.Clear();foreach(string id in top)indices.Add(JsonUtility.FromJson<NativeState>(JsonUtility.ToJson(new SaveData(view.overlayCanvas.overlays.Single(x=>x.id==id)))).index);
        Check(indices.SequenceEqual(indices.OrderBy(x=>x)),"New primitive toolbar not inserted before Mesh Tools in an older layout: "+string.Join(",",indices));
        Check(StudioPreferenceStore.GetString("Studio.Toolbars")==olderJson,"Applying missing toolbar default rewrote saved layout");
        var primitive=view.overlayCanvas.overlays.OfType<StudioPolygonPrimitivesToolbar>().Single();
        var buttons=primitive.CreatePanelContent().Children().OfType<EditorToolbarButton>().ToArray();Check(buttons.Length==5,"Built-in primitives missing");Check(buttons.Select(x=>x.icon).Distinct().Count()==5,"Primitive glyphs repeated");
        var actions=editor.ToolbarCommands(view);Check(!actions.Any(x=>x.Key.Contains("Recalculate Normals")),"Recalculate Normals command remains");
        var display=view.overlayCanvas.overlays.OfType<StudioMeshDisplayToolbar>().Single();Check(display.CreatePanelContent().Children().OfType<EditorToolbarButton>().Count()==4,"Wrong Mesh Display contents");
        var hashes=new Dictionary<uint,string>();var duplicates=new List<string>();
        foreach(string title in pages)
        {
            void Walk(List<StudioMenu.Item> items,string path)
            {
                foreach(var item in items)
                {
                    if(item.Separator)continue;string full=path+"/"+item.Text;
                    if(title=="Display")Check(item.Image==null,"Display menu has icon: "+full);
                    else
                    {
                        Check(item.Image,"Menu icon missing: "+full);
                        if(!full.Contains("Vehicle Part")&&!full.Contains("No recent files")&&!full.Contains("Open Recents"))
                        {uint hash=Fingerprint(item.Image);if(hashes.TryGetValue(hash,out var same)&&same!=full){if(!(same=="Create/Polygon Primitives/Sphere"&&full=="Mesh Display/Shaded"))duplicates.Add(same+" == "+full);}else hashes[hash]=full;}
                    }
                    Walk(item.Children,full);
                }
            }
            Walk(Menu(title).Items,title);
        }
        File.WriteAllLines(Out("toolbar-icon-duplicates.txt"),duplicates);Check(duplicates.Count==0,"Duplicate action artwork: "+string.Join("; ",duplicates));
        var explicitImage=StudioCommandIcons.Get("Viewport/Camera");var other=new StudioMenu();other.AddItem(new GUIContent("Mode",explicitImage),false,()=>{});Check(ReferenceEquals(other.Items[0].Image,explicitImage),"Pulled explicit menu-image behavior lost");
        var toolbarMenu=Menu("Display").Items.Single(x=>x.Text=="Toolbars");var reset=toolbarMenu.Children.Single(x=>x.Text=="Restore Default Layout");Check(reset.Enabled&&reset.Action!=null,"Restore Default Layout menu missing");reset.Action();
        indices.Clear();foreach(string id in top)indices.Add(JsonUtility.FromJson<NativeState>(JsonUtility.ToJson(new SaveData(view.overlayCanvas.overlays.Single(x=>x.id==id)))).index);Check(indices.SequenceEqual(indices.OrderBy(x=>x)),"Explicit reset did not restore category order");
        Call("SaveStoredToolbars");StudioPreferenceStore.SetString("Studio.CustomToolbars","{\"items\":[]}");StudioPreferenceStore.Flush();
        result="PASS "+checks+" combined settings migration/independence, factory order, primitive toolbar, menu icon coverage/unique pixels and retired-control checks";
    }catch(Exception ex){result=ex.ToString();}File.WriteAllText(Out("toolbar-catalog-revision-result.txt"),result);EditorApplication.Exit(result.StartsWith("PASS")?0:1);};}
    static string Quote(string text)=>"\""+text.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    static uint Fingerprint(Texture image)
    {
        var previous=RenderTexture.active;var rt=RenderTexture.GetTemporary(24,24,0,RenderTextureFormat.ARGB32);var readable=new Texture2D(24,24,TextureFormat.RGBA32,false);
        try{Graphics.Blit(image,rt);RenderTexture.active=rt;readable.ReadPixels(new Rect(0,0,24,24),0,0);readable.Apply();uint hash=2166136261;unchecked{foreach(var p in readable.GetPixels32()){hash=(hash^p.r)*16777619;hash=(hash^p.g)*16777619;hash=(hash^p.b)*16777619;hash=(hash^p.a)*16777619;}}return hash;}
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(readable);}
    }
    public static void Read(){EditorApplication.delayCall+=()=>{string result;try{Open();var disk=JsonUtility.FromJson<ToolbarFile>(File.ReadAllText(StudioToolbarPreferences.PathName));Check(StudioPreferenceStore.GetString("Studio.Toolbars")==disk.layout,"Fresh process did not use combined layout");Check(StudioPreferenceStore.GetString("Studio.CustomToolbars")==disk.customToolbars,"Fresh process did not use combined contents");Check(view.overlayCanvas.overlays.OfType<StudioPolygonPrimitivesToolbar>().Count()==1,"Fresh process lost built-in primitives");result="PASS "+checks+" fresh-process combined toolbar file checks";}catch(Exception ex){result=ex.ToString();}File.WriteAllText(Out("toolbar-catalog-read-result.txt"),result);EditorApplication.Exit(result.StartsWith("PASS")?0:1);};}
    public static void Render(){EditorApplication.delayCall+=()=>{try{var saved=JsonUtility.FromJson<SavedLayout>(StudioPreferenceStore.GetString("Studio.Toolbars"));saved.enabled=false;StudioPreferenceStore.SetString("Studio.Toolbars",JsonUtility.ToJson(saved));Open();host=CreateInstance<StudioToolbarCatalogRevisionValidation>();host.position=new Rect(50,50,430,600);host.ShowUtility();next=EditorApplication.timeSinceStartup+.5;EditorApplication.update+=Tick;LoadPage();}catch(Exception ex){Finish(ex.ToString());}};}
    static void LoadPage()
    {
        if(popup)UnityEngine.Object.DestroyImmediate(popup);popup=CreateInstance<StudioMenuPopup>();
        ((List<List<StudioMenu.Item>>)typeof(StudioMenuPopup).GetField("columns",F).GetValue(popup)).Add(Menu(pages[stage]).Items);
        ((List<int>)typeof(StudioMenuPopup).GetField("selected",F).GetValue(popup)).Add(-1);
        host.titleContent=new GUIContent(pages[stage]);painted=false;
    }
    static void Tick(){if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.2;try{host.SendEvent(new Event{type=EventType.Layout});host.SendEvent(new Event{type=EventType.Repaint});if(!painted)return;if(++stage==pages.Length){Finish("PASS "+checks+" rendered menu icon/branch/layout checks");return;}LoadPage();}catch(Exception ex){Finish(ex.ToString());}}
    void OnGUI()
    {
        if(!popup)return;bool paint=Event.current.type==EventType.Repaint;RenderTexture rt=null;var previous=RenderTexture.active;
        try{if(paint){rt=new RenderTexture((int)position.width,(int)position.height,0);rt.Create();RenderTexture.active=rt;GL.PushMatrix();GL.Clear(true,true,Color.clear);GL.LoadPixelMatrix(0,rt.width,rt.height,0);}popup.DrawColumn(0);if(paint){var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();Check(t.GetPixels32().Select(x=>x.r).Distinct().Count()>20,"Menu did not render");File.WriteAllBytes(Out("model-menu-icons-"+pages[stage].Replace(" ","-")+".png"),t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);painted=true;}}
        catch(Exception ex){Finish(ex.ToString());}
        finally{if(paint){GL.PopMatrix();RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(rt);}}
    }
    static void Finish(string result){EditorApplication.update-=Tick;File.WriteAllText(Out("toolbar-catalog-render-result.txt"),result);EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);}
}

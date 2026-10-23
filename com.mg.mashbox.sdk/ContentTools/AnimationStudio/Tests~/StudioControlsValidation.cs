using EditorPrefs = MashBoxSDK.AnimationStudio.StudioPreferenceStore;
using System;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

// Run in a normal Unity editor session: the dispatch checks require an IMGUI event.
public static class StudioControlsValidation
{
    private const BindingFlags Flags = BindingFlags.Instance|BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool condition,string message)
    { if(!condition)throw new Exception(message);checks++;Debug.Log("PASS "+message); }
    private static Event Key(KeyCode key, EventModifiers modifiers = EventModifiers.None, EventType type = EventType.KeyDown) => new Event { type=type,keyCode=key,modifiers=modifiers };
    private static object Get(object o,string name) => o.GetType().GetField(name,Flags).GetValue(o);
    private static object Call(object o,string name,params object[] args) => o.GetType().GetMethod(name,Flags).Invoke(o,args);
    private static void Set(object o,string name,object value) => o.GetType().GetField(name,Flags).SetValue(o,value);
    public static void Run()
    {
        bool existed=EditorPrefs.HasKey(StudioShortcuts.PreferenceKey);
        string saved=EditorPrefs.GetString(StudioShortcuts.PreferenceKey,"");
        var oldEvent=Event.current;
        string unityProfile=ShortcutManager.instance.activeProfileId;
        MashBoxStudioViewport view=null;
        StudioModelEditor editor=null;
        StudioModelDocument doc=null;
        Mesh mesh=null;
        StudioControlsValidationHost host=null;
        try
        {
            StudioShortcuts.Reset();
            foreach(var definition in StudioShortcuts.Definitions)
            foreach(var binding in definition.defaults)
                Check(StudioShortcuts.Conflict(definition.id,binding)==null,"Default has no conflicting action: "+definition.name+" "+binding);
            Check(StudioShortcuts.Pressed(StudioShortcut.Move,Key(KeyCode.W)),"W remains Move");
            Check(StudioShortcuts.Pressed(StudioShortcut.Move,Key(KeyCode.W,EventModifiers.Shift)),"Shift modifier compatibility is retained");
            Check(!StudioShortcuts.Pressed(StudioShortcut.Move,Key(KeyCode.W,EventModifiers.Control)),"Ctrl+W is not plain Move");
            Check(StudioShortcuts.Pressed(StudioShortcut.Save,Key(KeyCode.S,EventModifiers.Control)),"Ctrl+S remains Save");
            Check(StudioShortcuts.Pressed(StudioShortcut.Save,Key(KeyCode.S,EventModifiers.Command)),"Cmd+S uses the same cross-platform action");
            Check(!StudioShortcuts.Pressed(StudioShortcut.Save,Key(KeyCode.S,EventModifiers.Control|EventModifiers.Shift)) &&
                StudioShortcuts.Pressed(StudioShortcut.SaveAs,Key(KeyCode.S,EventModifiers.Control|EventModifiers.Shift)),"Save and Save As remain distinct");
            Check(StudioShortcuts.Pressed(StudioShortcut.Cancel,Key(KeyCode.Escape,EventModifiers.Control|EventModifiers.Alt)),"Cancel works while modifiers are still held");
            foreach(var code in new[]{KeyCode.Equals,KeyCode.Plus,KeyCode.KeypadPlus})
                Check(StudioShortcuts.Pressed(StudioShortcut.LargerHandles,Key(code,EventModifiers.Shift)),"Increase-size alias remains: "+code);
            Check(StudioShortcuts.Pressed(StudioShortcut.Delete,Key(KeyCode.Backspace)),"Backspace remains a delete alias");
            Check(StudioShortcuts.Pressed(StudioShortcut.CommitCut,Key(KeyCode.KeypadEnter)),"Numpad Enter still applies cuts");
            StudioShortcuts.Set(StudioShortcut.Move,new[]{new StudioShortcuts.Binding(KeyCode.K,StudioModifiers.Alt)});
            Check(StudioShortcuts.Pressed(StudioShortcut.Move,Key(KeyCode.K,EventModifiers.Alt)) && !StudioShortcuts.Pressed(StudioShortcut.Move,Key(KeyCode.W)),"Remapping removes the original key");
            Check(StudioShortcuts.Label(StudioShortcut.Move)=="Alt+K","Menu labels follow remaps");
            StudioShortcuts.Reload();
            Check(StudioShortcuts.Pressed(StudioShortcut.Move,Key(KeyCode.K,EventModifiers.Alt)),"Custom binding survives reload");
            StudioShortcuts.Set(StudioShortcut.Move,Array.Empty<StudioShortcuts.Binding>());
            StudioShortcuts.Reload();
            Check(StudioShortcuts.Bindings(StudioShortcut.Move).Length==0,"Unassigned commands stay unassigned after reload");
            StudioShortcuts.Reset(StudioShortcut.Move);
            Check(StudioShortcuts.Pressed(StudioShortcut.Move,Key(KeyCode.W)),"Individual reset restores the original default");
            Check(StudioShortcuts.Conflict(StudioShortcut.Scale,new StudioShortcuts.Binding(KeyCode.W))=="Move","Overlapping commands report their conflict");
            Check(StudioShortcuts.Conflict(StudioShortcut.FrameAllUV,new StudioShortcuts.Binding(KeyCode.W))=="Move","UV tool conflicts are detected too");
            Check(StudioShortcuts.Conflict(StudioShortcut.FrameAllUV,new StudioShortcuts.Binding(KeyCode.S,StudioModifiers.Action))=="Save workspace","UV bindings cannot shadow Save");
            // Batch mode filters synthetic mouse-down events outside OnGUI. Mouse KeyCodes
            // exercise the same binding matcher without relying on an OS pointer event.
            var mouse=Key(KeyCode.Mouse1,EventModifiers.Alt);
            Check(StudioShortcuts.Matches(StudioShortcut.Dolly,mouse) && !StudioShortcuts.Matches(StudioShortcut.MarkingMenu,mouse),"Alt+RMB navigation is distinct from RMB marking");
            StudioShortcuts.Set(StudioShortcut.MarkingMenu,new[]{new StudioShortcuts.Binding(KeyCode.Mouse2,StudioModifiers.Action)});
            mouse.modifiers=EventModifiers.Control;mouse.keyCode=KeyCode.Mouse2;
            Check(StudioShortcuts.Matches(StudioShortcut.MarkingMenu,mouse),"Mouse buttons and modifiers can be remapped");
            Check(typeof(EditorWindow).IsAssignableFrom(typeof(StudioControlsPreferences)) && !typeof(SettingsProvider).IsAssignableFrom(typeof(StudioControlsPreferences)),"Studio preferences is a private popup, not a Unity settings provider");
            Check(ShortcutManager.instance.activeProfileId==unityProfile,"Studio remaps leave Unity's active shortcut profile unchanged");
            StudioShortcuts.Reset();
            host=ScriptableObject.CreateInstance<StudioControlsValidationHost>(); host.ShowUtility(); host.Focus(); host.SendEvent(new Event { type=EventType.Layout });
            host.Drive(() => {
            editor=new StudioModelEditor(()=>{});
            view=ScriptableObject.CreateInstance<MashBoxStudioViewport>();
            view.position=new Rect(0,0,1600,900);
            void Menu(EventModifiers modifiers, int button=1)
            {
                Event.current=new Event { modifiers=modifiers,button=button,mousePosition=view.ModelCanvasRect.center };
                Event.current.type=EventType.MouseDown;
                editor.BeforeModelSceneGUI(view);
            }
            void ChooseSpace(int sector)
            {
                var origin=(Vector2)Get(editor,"markingOrigin");
                var rect=(Rect)Call(editor,"MarkingItemRect",origin,sector);
                Event.current=new Event { button=1,mousePosition=GUIUtility.ScreenToGUIPoint(rect.center) };
                Event.current.type=EventType.MouseUp;
                editor.BeforeModelSceneGUI(view);
            }
            Menu(EventModifiers.Control|EventModifiers.Shift);
            Check((bool)Get(editor,"markingOpen") && (StudioModelEditor.MarkingLevel)Get(editor,"markingLevel")==StudioModelEditor.MarkingLevel.AxisSpace,"Ctrl+Shift+RMB opens the axis-space marking menu");
            var entries=(StudioModelEditor.MarkingAction[][])typeof(StudioModelEditor).GetField("MarkingItems",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            Check(entries[(int)StudioModelEditor.MarkingLevel.AxisSpace].Count(a=>a!=StudioModelEditor.MarkingAction.None)==2,"Axis menu contains exactly two choices");
            Check((bool)Call(editor,"MarkingIsCurrent",StudioModelEditor.MarkingAction.ObjectAxes) && !(bool)Call(editor,"MarkingIsCurrent",StudioModelEditor.MarkingAction.WorldAxes),"Only Object is checked for local axes");
            ChooseSpace(2);
            Check(!(bool)Get(editor,"markingOpen") && !(bool)Get(editor,"local"),"Releasing on World selects world axes and closes the menu");
            Check(!(bool)Call(editor,"MarkingIsCurrent",StudioModelEditor.MarkingAction.ObjectAxes) && (bool)Call(editor,"MarkingIsCurrent",StudioModelEditor.MarkingAction.WorldAxes),"Only World is checked after selection");
            Menu(EventModifiers.Control|EventModifiers.Shift);ChooseSpace(2);
            Check(!(bool)Get(editor,"local"),"Selecting World again never toggles both choices off");
            Menu(EventModifiers.Control|EventModifiers.Shift);ChooseSpace(6);
            Check((bool)Get(editor,"local"),"Selecting Object restores local axes");
            Menu(EventModifiers.None);
            Check((StudioModelEditor.MarkingLevel)Get(editor,"markingLevel")==StudioModelEditor.MarkingLevel.Components,"Unmodified RMB retains the component menu");
            editor.CancelMarkingMenu();
            StudioShortcuts.Set(StudioShortcut.AxisMenu,new[]{new StudioShortcuts.Binding(KeyCode.Mouse2,StudioModifiers.Action)});
            Menu(EventModifiers.Control,2);
            Check((bool)Get(editor,"markingOpen") && (StudioModelEditor.MarkingLevel)Get(editor,"markingLevel")==StudioModelEditor.MarkingLevel.AxisSpace,"Axis menu can be remapped in Studio preferences");
            editor.CancelMarkingMenu();StudioShortcuts.Reset(StudioShortcut.AxisMenu);
            Event.current=Key(KeyCode.R);editor.HandleModelKeyboard(view);
            Check((Tool)Get(editor,"tool")==Tool.Scale && Event.current.type==EventType.Used,"Dispatcher claims modelling tool keys");
            StudioShortcuts.Set(StudioShortcut.Move,new[]{new StudioShortcuts.Binding(KeyCode.K,StudioModifiers.Alt)});
            Event.current=Key(KeyCode.W);Check(!editor.HandleModelKeyboard(view) && (Tool)Get(editor,"tool")==Tool.Scale,"Old tool key no longer executes");
            Event.current=Key(KeyCode.K,EventModifiers.Alt);editor.HandleModelKeyboard(view);
            Check((Tool)Get(editor,"tool")==Tool.Move,"Modified replacement chord executes the same tool");
            EditorGUIUtility.editingTextField=true;Event.current=Key(KeyCode.R);
            Check(!editor.HandleModelKeyboard(view) && (Tool)Get(editor,"tool")==Tool.Move,"Typing in fields never switches tools");
            EditorGUIUtility.editingTextField=false;
            StudioShortcuts.Set(StudioShortcut.GridSnap,new[]{new StudioShortcuts.Binding(KeyCode.G,StudioModifiers.Action)});
            Event.current=Key(KeyCode.G,EventModifiers.Control);editor.HandleModelKeyboard(view);
            Check((bool)Get(editor,"gridSnapHeld"),"Remapped grid snap engages");
            Event.current=Key(KeyCode.G,EventModifiers.None,EventType.KeyUp);editor.HandleModelKeyboard(view);
            Check(!(bool)Get(editor,"gridSnapHeld"),"Held snap releases after its modifier was released first");
            Event.current=Key(KeyCode.G,EventModifiers.Control);editor.HandleModelKeyboard(view);editor.ReleaseShortcutHolds();
            Check(!(bool)Get(editor,"gridSnapHeld"),"Focus/remap cleanup clears held bindings");
            int ownKeys=0,nativeKeys=0;bool active=true;
            var container=new IMGUIContainer(()=>{if(Event.current.isKey)nativeKeys++;});
            var original=container.onGUIHandler;view.rootVisualElement.Add(container);
            view.BindModelInput(()=>active,()=>{ownKeys++;Event.current.Use();return true;},()=>{});
            var handlers=(System.Collections.IDictionary)Get(view,"studioInputHandlers");
            bool nativeCanvasWrapped=false;
            foreach(System.Collections.DictionaryEntry pair in handlers)
            {
                var handler=(Action)pair.Value.GetType().GetField("original",Flags).GetValue(pair.Value);
                if(ReferenceEquals(handler.Target,view) && handler.Method.DeclaringType==typeof(SceneView))nativeCanvasWrapped=true;
            }
            Check(nativeCanvasWrapped,"The actual SceneView camera callback is wrapped");
            Event.current=Key(KeyCode.BackQuote);container.onGUIHandler();
            Check(ownKeys==1 && nativeKeys==0 && Event.current.type==EventType.Used,"Studio claims keys before a wrapped Unity handler");
            view.BindModelInput(()=>active,()=>false,()=>{});
            Event.current=Key(KeyCode.BackQuote);Call(view,"RouteStudioCanvasInput");
            Check(Event.current.type==EventType.Used,"Native canvas routing suppresses the inherited overlay-menu key");
            Event.current=Key(KeyCode.BackQuote);container.onGUIHandler();
            Check(Event.current.type==EventType.Used,"Unassigned keys cannot fall through to Unity shortcuts");
            EditorGUIUtility.editingTextField=true;
            int beforeNative=nativeKeys;
            Event.current=Key(KeyCode.D,EventModifiers.Control);container.onGUIHandler();
            Check(nativeKeys==beforeNative+1 && Event.current.type==EventType.Used,"Text fields handle input first and unused text-field chords cannot leak to Unity");
            EditorGUIUtility.editingTextField=false;
            active=false;Event.current=Key(KeyCode.BackQuote);container.onGUIHandler();
            Check(Event.current.type==EventType.KeyDown,"Input outside modelling mode retains Unity behavior");
            view.BindModelInput(null,null,null);
            Check(container.onGUIHandler==original,"Leaving modelling restores the original handler");
            float size=view.size;view.ApplyStudioNavigation(StudioShortcut.Dolly,new Vector2(0,20));
            Check(view.size>size,"Studio owns zoom without a Unity shortcut profile");
            Quaternion rotation=view.rotation;view.ApplyStudioNavigation(StudioShortcut.Orbit,new Vector2(25,0));
            Check(Quaternion.Angle(view.rotation,rotation)>1,"Studio orbit changes its preview camera");
            mesh=new Mesh { vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)},triangles=new[]{0,2,1,0,3,2} };
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(new StudioModelDocument.Node{name="Quad",mesh=mesh});
            editor.Load(doc);Call(editor,"SelectObject",0,false,false);
            StudioShortcuts.Set(StudioShortcut.VertexSnap,new[]{new StudioShortcuts.Binding(KeyCode.J,StudioModifiers.Action)});
            Event.current=Key(KeyCode.J,EventModifiers.Control);editor.HandleModelKeyboard(view);
            Check((bool)Get(editor,"pivotSnapHeld"),"Remapped vertex snapping engages");
            Event.current=Key(KeyCode.J,EventModifiers.None,EventType.KeyUp);editor.HandleModelKeyboard(view);
            Check(!(bool)Get(editor,"pivotSnapHeld"),"Vertex snap releases without its original modifiers");
            string oldName=doc.name;
            Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(doc,"Studio control validation");doc.name="Control validation changed";
            StudioShortcuts.Set(StudioShortcut.Undo,new[]{new StudioShortcuts.Binding(KeyCode.U,StudioModifiers.Action)});
            StudioShortcuts.Set(StudioShortcut.Redo,new[]{new StudioShortcuts.Binding(KeyCode.U,StudioModifiers.Action|StudioModifiers.Shift)});
            Event.current=Key(KeyCode.U,EventModifiers.Control);editor.HandleModelKeyboard(view);
            Check(doc.name==oldName,"Remapped undo restores the workspace operation");
            Event.current=Key(KeyCode.U,EventModifiers.Control|EventModifiers.Shift);editor.HandleModelKeyboard(view);
            Check(doc.name=="Control validation changed","Remapped redo reapplies the workspace operation");
            editor.ActivateCutTool(StudioModelEditor.CutTool.MultiCut);
            Menu(EventModifiers.Control|EventModifiers.Shift);
            Check((bool)Get(editor,"markingOpen") && (StudioModelEditor.MarkingLevel)Get(editor,"markingLevel")==StudioModelEditor.MarkingLevel.AxisSpace && (StudioModelEditor.CutTool)Get(editor,"cutTool")==StudioModelEditor.CutTool.MultiCut,"Axis menu takes priority over applying cuts");
            editor.CancelMarkingMenu();
            StudioShortcuts.Set(StudioShortcut.CommitCut,new[]{new StudioShortcuts.Binding(KeyCode.F9)});
            Event.current=Key(KeyCode.Return);editor.HandleModelKeyboard(view);
            Check(Event.current.type!=EventType.Used,"Old Enter no longer applies cuts after remapping");
            Event.current=Key(KeyCode.F9);editor.HandleModelKeyboard(view);
            Check(Event.current.type==EventType.Used,"Remapped apply-cut shortcut is handled");
            StudioShortcuts.Set(StudioShortcut.RemoveCut,new[]{new StudioShortcuts.Binding(KeyCode.F7)});
            Event.current=Key(KeyCode.F7);editor.HandleModelKeyboard(view);
            Check(Event.current.type==EventType.Used,"Remapped remove-cut shortcut is handled");
            StudioShortcuts.Set(StudioShortcut.Cancel,new[]{new StudioShortcuts.Binding(KeyCode.F8)});
            Event.current=Key(KeyCode.Escape);editor.HandleModelKeyboard(view);
            Check((StudioModelEditor.CutTool)Get(editor,"cutTool")==StudioModelEditor.CutTool.MultiCut,"Old Escape binding does not cancel after remapping");
            Event.current=Key(KeyCode.F8);editor.HandleModelKeyboard(view);
            Check((StudioModelEditor.CutTool)Get(editor,"cutTool")==StudioModelEditor.CutTool.None,"Remapped cancel exits the cut tool");
            editor.ActivateCutTool(StudioModelEditor.CutTool.MultiCut);
            Event.current=Key(KeyCode.K,EventModifiers.Alt);editor.HandleModelKeyboard(view);
            Check((StudioModelEditor.CutTool)Get(editor,"cutTool")==StudioModelEditor.CutTool.None && (Tool)Get(editor,"tool")==Tool.Move,"Remapped tools still exit cutting");
            StudioShortcuts.Reset();
            Check(StudioShortcuts.Pressed(StudioShortcut.Move,Key(KeyCode.W)),"Restore all defaults works after multiple remaps");
            });
            StudioControlsPreferences.Open();
            var preferences=Resources.FindObjectsOfTypeAll<StudioControlsPreferences>().Single();
            Check(preferences.titleContent.text=="MashBox Studio Preferences","Studio Edit > Preferences opens the Studio popup");
            host.Draw(preferences.DrawControls); preferences.Close();
            Check(true,"Controls preferences lays out and draws inside the editor GUI");
            Debug.Log("STUDIO_CONTROLS_VALIDATION_SUCCESS "+checks+" checks");
        }
        finally
        {
            EditorGUIUtility.editingTextField=false;GUIUtility.hotControl=0;
            if(host)host.Close();
            if(view)Object.DestroyImmediate(view);editor?.Dispose();
            if(doc)Object.DestroyImmediate(doc);if(mesh)Object.DestroyImmediate(mesh);
            if(existed)EditorPrefs.SetString(StudioShortcuts.PreferenceKey,saved);else EditorPrefs.DeleteKey(StudioShortcuts.PreferenceKey);
            StudioShortcuts.Reload();Event.current=oldEvent;
        }
    }
}

public sealed class StudioControlsValidationHost : EditorWindow
{
    private Action action;
    private Exception failure;
    private bool ran;
    private bool continuous;
    private EventType expected;
    internal void Drive(Action callback,EventType type=EventType.KeyDown)
    {
        action=callback;ran=false;failure=null;expected=type;
        SendEvent(new Event { type=type,keyCode=KeyCode.F12 });
        action=null;
        if(failure!=null)throw failure;
        if(!ran)throw new Exception("Validation host did not receive its IMGUI event");
    }
    internal void Draw(Action callback)
    {
        action=callback;continuous=true;failure=null;
        SendEvent(new Event { type=EventType.Layout });
        SendEvent(new Event { type=EventType.Repaint });
        action=null;continuous=false;
        if(failure!=null)throw failure;
    }
    private void OnGUI()
    {
        if(action==null || (!continuous && Event.current.type!=expected))return;
        var callback=action;if(!continuous)action=null;ran=true;
        var previous=new Event(Event.current);
        try { callback(); }
        catch(Exception error) { failure=error; }
        finally { Event.current=previous;if(Event.current.isKey)Event.current.Use(); }
    }
}

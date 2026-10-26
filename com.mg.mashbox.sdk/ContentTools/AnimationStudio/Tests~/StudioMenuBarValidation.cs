using System;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// Run in a normal editor session so popup focus and mouse events use IMGUI.
public static class StudioMenuBarValidation
{
    private static int checks;
    private static void Check(bool condition,string message)
    { if(!condition)throw new Exception(message);checks++;Debug.Log("PASS "+message); }
    public static void Run()
    {
        StudioControlsValidationHost host=null,other=null;
        var previous=Event.current;int actions=0;
        try
        {
            host=ScriptableObject.CreateInstance<StudioControlsValidationHost>();host.position=new Rect(100,100,640,360);
            host.wantsMouseMove=true;host.ShowUtility();host.Focus();host.SendEvent(new Event{type=EventType.Layout});
            other=ScriptableObject.CreateInstance<StudioControlsValidationHost>();
            host.Drive(()=>{
                var style=StudioTheme.Styles.menuBarButton;
                Check(style.alignment==TextAnchor.MiddleCenter && style.padding.left==style.padding.right && style.padding.right<17,"Top-bar labels are centered with balanced compact padding");
                Rect RectFor(int index)=>new Rect(index*80,0,80,21);
                void DrawButtons()
                {
                    foreach(var name in new[]{"File","Edit","Create"})
                    {
                        int index=name=="File"?0:name=="Edit"?1:2;
                        var rect=RectFor(index);
                        if(!StudioMenu.MenuBarButton(host,rect,new GUIContent(name)))continue;
                        var menu=new StudioMenu();menu.AddItem(new GUIContent(name+" action"),false,()=>actions++);
                        menu.AddItem(new GUIContent("Submenu/Child"),false,()=>actions++);
                        menu.DropDown(rect,host,name);
                    }
                }
                void Mouse(EventType type,int index)
                {
                    Event.current=new Event{button=0,mousePosition=RectFor(index).center};Event.current.type=type;
                    DrawButtons();
                }
                Mouse(EventType.MouseMove,1);
                Check(!StudioMenuPopup.HasMenuBar(host),"Hover does not open menus before activation");
                Mouse(EventType.MouseDown,0);
                Check(StudioMenuPopup.IsMenuBarItemOpen(host,"File"),"Clicking File activates the menu bar");
                var popup=Resources.FindObjectsOfTypeAll<StudioMenuPopup>().Single();
                var parentSize=popup.position.size;
                typeof(StudioMenuPopup).GetMethod("SelectRow",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(popup,new object[]{0,1,true});
                Check(popup.ColumnCount==2,"A submenu can expand before switching headings");
                var child=Resources.FindObjectsOfTypeAll<StudioMenuColumnPopup>().Single();
                Check(Mathf.Abs(child.position.x-popup.position.xMax)<1 && Mathf.Abs(child.position.y-(popup.position.y+StudioMenuPopup.RowHeight))<1,"Submenu opens flush to the right and aligns with its parent row");
                Check(popup.position.size==parentSize && child.position.height<popup.position.height,"Parent and child retain their own measured dimensions");
                Mouse(EventType.MouseMove,1);
                Check(StudioMenuPopup.IsMenuBarItemOpen(host,"Edit"),"Hovering onto Edit switches its dropdown without a click");
                Check(Resources.FindObjectsOfTypeAll<StudioMenuPopup>().Single()==popup && popup.ColumnCount==1,"Switching reuses the popup and clears the old submenu");
                Mouse(EventType.MouseDrag,2);
                Check(StudioMenuPopup.IsMenuBarItemOpen(host,"Create"),"Dragging across headings also switches dropdowns");
                Check(actions==0,"Sliding headings never invokes a menu command");
                Event.current=new Event{mousePosition=RectFor(0).center};Event.current.type=EventType.MouseMove;
                Check(!StudioMenu.MenuBarButton(other,RectFor(0),new GUIContent("File")) && StudioMenuPopup.IsMenuBarItemOpen(host,"Create"),"Another window cannot steal an active menu session by hovering");
                Mouse(EventType.MouseDown,2);
                Check(!StudioMenuPopup.HasMenuBar(host),"Clicking the active heading closes the menu session");
                Mouse(EventType.MouseMove,1);
                Check(!StudioMenuPopup.HasMenuBar(host),"Hover stops switching after the menu session closes");
                var standalone=new StudioMenu();standalone.AddItem(new GUIContent("Context action"),false,()=>actions++);
                standalone.DropDown(RectFor(0));
                Check(!StudioMenuPopup.HasMenuBar(host),"Ordinary context dropdowns do not activate menu-bar tracking");
            });
            Debug.Log("STUDIO_MENU_BAR_VALIDATION_SUCCESS "+checks+" checks");
        }
        finally
        {
            foreach(var popup in Resources.FindObjectsOfTypeAll<StudioMenuPopup>())popup.Close();
            if(other)Object.DestroyImmediate(other);if(host)host.Close();GUIUtility.hotControl=0;Event.current=previous;
        }
    }
}

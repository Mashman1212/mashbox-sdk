using System;
using System.Linq;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

public static class StudioMenuGlobalInputValidation
{
    static void Check(bool pass,string message) {if(!pass)throw new Exception(message);Debug.Log("PASS "+message);}
    public static void Run()
    {
        var host=ScriptableObject.CreateInstance<StudioControlsValidationHost>();
        var view=ScriptableObject.CreateInstance<MashBoxStudioViewport>();
        var previous=Event.current;
        bool hovered=false,clicked=false;
        void Observe() {hovered=new Rect(0,0,1000,1000).Contains(Event.current.mousePosition);clicked=hovered&&Event.current.type==EventType.MouseDown;}
        try
        {
            host.position=new Rect(100,100,640,360);host.ShowUtility();host.Focus();
            view.position=new Rect(100,100,1100,800);
            view.BindModes(Observe,true);view.BindViewportControls(Observe,Observe,Observe);
            view.BindRigControls(Observe);view.BindInspector(Observe);view.BindTimeline(Observe);
            view.BindModelTools(Observe);view.BindModelToolOptions(Observe);view.BindMarkingMenu(Observe);
            view.BindModelInput(()=>true,()=>false,()=>{});
            host.Drive(()=>{
                var hierarchy=view.rootVisualElement.Q<IMGUIContainer>("mashbox-animation-rig-controls");
                void Send(IMGUIContainer panel,EventType type)
                {
                    hovered=clicked=false;Event.current=new Event{mousePosition=new Vector2(20,20),button=0};Event.current.type=type;panel.onGUIHandler();
                }
                Send(hierarchy,EventType.Repaint);Check(hovered,"Hierarchy receives hover before opening a menu");
                var menu=new StudioMenu();menu.AddItem(new GUIContent("File action"),false,()=>{});
                StudioMenuPopup.Open(menu,new Rect(100,100,80,21),view,"File");
                // Rebinding was the failure case: the old one-time input wrapper vanished.
                view.BindRigControls(Observe);
                var shield=view.rootVisualElement.Q<VisualElement>("mashbox-menu-input-shield");
                Check(shield!=null&&shield.pickingMode==PickingMode.Position&&shield.style.display.value==DisplayStyle.Flex,"An open menu shields the entire viewport content");
                foreach(string name in new[]{"mashbox-animation-rig-controls","mashbox-animation-inspector","mashbox-viewport-options","mashbox-studio-settings","mashbox-model-tools","mashbox-model-tool-options","mashbox-model-marking-menu"})
                {
                    var panel=view.rootVisualElement.Q<IMGUIContainer>(name);Check(panel!=null,"Bound panel exists: "+name);
                    Send(panel,EventType.Repaint);Check(!hovered,"Menu blocks hover in "+name);
                    Send(panel,EventType.MouseDown);Check(!clicked,"Menu blocks clicks in "+name);
                }
                Send(view.rootVisualElement.Q<IMGUIContainer>("mashbox-model-menu"),EventType.Repaint);
                Check(hovered,"Menubar remains interactive for sliding between headings");
                StudioMenuPopup.CloseMenuBar(view);
                Check(shield.style.display.value==DisplayStyle.None,"Closing the menu removes the content shield");
                Send(hierarchy,EventType.Repaint);Check(hovered,"Hierarchy hover resumes after closing the menu");
            });
            Debug.Log("STUDIO_MENU_GLOBAL_INPUT_VALIDATION_SUCCESS");
        }
        finally
        {
            StudioMenuPopup.CloseMenuBar(view);view.BindModelInput(null,null,null);
            Object.DestroyImmediate(view);host.Close();GUIUtility.hotControl=0;Event.current=previous;
        }
    }
}

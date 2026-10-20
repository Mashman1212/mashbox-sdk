using System;
using System.Linq;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
public static class StudioHierarchyContextValidation
{
    static void Check(bool pass,string text){if(!pass)throw new Exception(text);Debug.Log("PASS "+text);}
    public static void Run()
    {
        var host=ScriptableObject.CreateInstance<StudioControlsValidationHost>();host.ShowUtility();
        var doc=ScriptableObject.CreateInstance<StudioModelDocument>();
        doc.nodes.Add(new StudioModelDocument.Node{name="Parent"});
        doc.nodes.Add(new StudioModelDocument.Node{name="Child",parent=0});
        doc.nodes.Add(new StudioModelDocument.Node{name="Other"});
        doc.nodes.Add(new StudioModelDocument.Node{name="Unselected"});
        var editor=new StudioModelEditor(()=>{});
        try
        {
            editor.Load(doc);
            host.Drive(()=>{
                Event.current=new Event{type=EventType.MouseDown,button=1,mousePosition=new Vector2(20,20)};
                Check(editor.HandleHierarchyContext(new Rect(0,0,200,30),1),"Hierarchy opens its menu on right-button down");
                Check(editor.Active==1 && Event.current.type==EventType.Used,"Clicked row selected and right-click consumed");
                Check(Resources.FindObjectsOfTypeAll<StudioMenuPopup>().Length==1,"Visibility menu popup created");
            },EventType.MouseDown);
            foreach(var popup in Resources.FindObjectsOfTypeAll<StudioMenuPopup>())popup.Close();
            void Click(string label){editor.HierarchyVisibilityMenu(1).Items.Single(i=>i.Text==label).Action();}
            Click("Hide");Check(!doc.nodes[1].active && !editor.Transforms[1].gameObject.activeSelf,"Hide updates document and preview");
            Click("Show");Check(doc.nodes[0].active&&doc.nodes[1].active,"Show enables child and parent");
            Click("Isolate Visibility");Check(doc.nodes[0].active&&doc.nodes[1].active&&!doc.nodes[2].active,"Isolate retains branch and required ancestors");
            editor.SelectObject(1);editor.SelectObject(2,true);
            Click("Isolate Selected");Check(doc.nodes[0].active&&doc.nodes[1].active&&doc.nodes[2].active&&!doc.nodes[3].active,"Isolate Selected includes the complete selection and hides other objects");
            Click("Hide All Objects");Check(doc.nodes.All(n=>!n.active),"Hide all hides every object");
            Click("Show All Objects");Check(doc.nodes.All(n=>n.active),"Show all restores every object");
            Undo.PerformUndo();Check(doc.nodes.All(n=>!n.active),"Visibility changes support Undo");
            Debug.Log("STUDIO_HIERARCHY_CONTEXT_VALIDATION_SUCCESS");
        }
        finally{foreach(var popup in Resources.FindObjectsOfTypeAll<StudioMenuPopup>())popup.Close();editor.Dispose();Undo.ClearUndo(doc);Object.DestroyImmediate(doc);host.Close();}
    }
}

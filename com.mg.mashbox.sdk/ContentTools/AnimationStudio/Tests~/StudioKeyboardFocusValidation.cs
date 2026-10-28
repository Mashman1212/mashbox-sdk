using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioKeyboardFocusValidation
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static string Report=>Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/studio-keyboard-focus-validation.txt"));
        public static void Run() { var host=ScriptableObject.CreateInstance<FocusValidationWindow>(); host.Test=()=>RunChecks(host); host.Show(); }
        private static void RunChecks(FocusValidationWindow host)
        {
            var log=new List<string>();
            var view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio);
            var studio=view.Studio;
            var previous=view;
            var model=studio.ToolbarModelEditor;
            var modelTool=typeof(StudioModelEditor).GetField("tool",Flags);
            object oldTool=modelTool.GetValue(model);
            var nativeTool=Tools.current;
            Event oldEvent=new Event(Event.current);
            void Check(bool value,string name){if(!value)throw new Exception(name);log.Add("PASS "+name);}
            void Finish(Exception error=null)
            {
                modelTool.SetValue(model,oldTool); if(host)host.Close();
                if(previous)previous.Focus();
                if(error!=null)log.Add("FAIL "+error);
                File.WriteAllLines(Report,log);Debug.Log("Studio keyboard focus validation: "+log.Last());
            }
            try
            {
                EditorGUIUtility.editingTextField=true;
                view.FocusCanvas();
                Check(EditorWindow.focusedWindow==view,"Canvas restores the originating Studio window");
                Check(!EditorGUIUtility.editingTextField,"Canvas releases stale text editing focus");
                foreach(var pair in new[]{(KeyCode.W,Tool.Move),(KeyCode.E,Tool.Rotate),(KeyCode.R,Tool.Scale)})
                {
                    Event.current=new Event{type=EventType.KeyDown,keyCode=pair.Item1};
                    bool handled=model.HandleModelKeyboard(view);
                    Check(handled&&(Tool)modelTool.GetValue(model)==pair.Item2,"Modeling consumes "+pair.Item1+" and changes its own gizmo");
                }
                Check(Tools.current==nativeTool,"Studio shortcuts preserve Unity's Scene gizmo");
                Event.current=oldEvent;
                var menu=new StudioMenu();
                menu.AddItem(new GUIContent("Focus validation"),false,()=>
                {
                    try
                    {
                        Check(EditorWindow.focusedWindow==view,"Menu commands execute with their originating Studio focused");
                        // Exercise the same root event used by a Project-browser drop.
                        var other=ScriptableObject.CreateInstance<FocusValidationWindow>();other.Show();other.Focus();
                        using(var evt=DragPerformEvent.GetPooled()) {evt.target=view.rootVisualElement;view.rootVisualElement.SendEvent(evt);}
                        EditorApplication.delayCall+=()=>
                        {
                            try {Check(EditorWindow.focusedWindow==view,"Asset drops return keyboard focus to Studio after drag cleanup");other.Close();Finish();}
                            catch(Exception error){other.Close();Finish(error);}
                        };
                    }
                    catch(Exception error){Finish(error);}
                });
                var popup=StudioMenuPopup.Open(menu,new Rect(300,300,100,20),view,"Focus validation");
                ((List<int>)typeof(StudioMenuPopup).GetField("selected",Flags).GetValue(popup))[0]=0;
                typeof(StudioMenuPopup).GetMethod("InvokeSelected",Flags).Invoke(popup,null);
            }
            catch(Exception error){Finish(error);}
        }
        private sealed class FocusValidationWindow:EditorWindow { internal Action Test; private void OnGUI() { var test=Test;Test=null;test?.Invoke(); } }
    }
}




using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.AnimationStudio
{
    public sealed class StudioConstraintMoveValidation : EditorWindow
    {
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath,"../../Constraints"));
        static StudioConstraintMoveValidation window;
        Scene scene; Camera cameraView; Transform target, driven; AimConstraint aim;
        StudioModelEditor editor; int control, phase, checks; bool ready; double next;
        Vector2 start, projected; Vector3 initial;
        static readonly MethodInfo Draw = typeof(StudioModelEditor).GetMethod("DrawFreeMoveHandle",BindingFlags.NonPublic|BindingFlags.Instance);
        static readonly Handles.CapFunction Cap = Handles.RectangleHandleCap;
        [InitializeOnLoadMethod] static void Boot()
        {
            if(Application.dataPath.Replace('\\','/').EndsWith("/SelectionUndoProject/Assets"))EditorApplication.update+=Poll;
        }
        static void Poll()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            string request=Path.Combine(Root,"move-request.txt");
            if(!window&&File.Exists(request)){File.Delete(request);Begin();}
        }
        static void Begin()
        {
            window=CreateInstance<StudioConstraintMoveValidation>();
            window.scene=EditorSceneManager.NewPreviewScene();
            GameObject New(string name){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,window.scene);return go;}
            window.target=New("Aim source").transform;window.driven=New("Aiming object").transform;
            window.driven.position=new Vector3(-3,-2,2);
            window.aim=window.driven.gameObject.AddComponent<AimConstraint>();
            window.aim.AddSource(new ConstraintSource{sourceTransform=window.target,weight=1});
            StudioModelConstraints.Activate(window.aim,StudioConstraintKind.Aim,true);
            window.cameraView=New("Test camera").AddComponent<Camera>();window.cameraView.enabled=false;
            window.cameraView.transform.rotation=Quaternion.Euler(20,-25,0);
            window.cameraView.transform.position=-window.cameraView.transform.forward*10;
            window.cameraView.nearClipPlane=.01f;window.cameraView.farClipPlane=100;
            // Only the isolated handle helper is exercised; no controller preferences or document lifecycle.
            window.editor=(StudioModelEditor)FormatterServices.GetUninitializedObject(typeof(StudioModelEditor));
            window.position=new Rect(80,80,600,400);window.ShowUtility();window.Repaint();
            EditorApplication.update+=Step;
        }
        void OnGUI()
        {
            if(!cameraView)return;
            try
            {
                Handles.SetCamera(new Rect(0,0,600,400),cameraView);
                control=GUIUtility.GetControlID(0x778811,FocusType.Passive);
                if(Event.current.type==EventType.MouseDown)HandleUtility.nearestControl=control;
                using(new Handles.DrawingScope(Matrix4x4.identity))
                {
                    EditorGUI.BeginChangeCheck();
                    var value=(Vector3)Draw.Invoke(editor,new object[]{control,target.position,.15f,Cap});
                    if(EditorGUI.EndChangeCheck())target.position=value;
                    projected=HandleUtility.WorldToGUIPoint(target.position);
                }
                if(!ready){start=projected;initial=target.position;ready=true;}
            }
            catch(Exception e){Finish(e.ToString());}
        }
        static void Check(bool value,string message){if(!value)throw new Exception(message);window.checks++;}
        static void Step()
        {
            if(!window||!window.ready||EditorApplication.timeSinceStartup<window.next)return;
            window.next=EditorApplication.timeSinceStartup+.08;
            try
            {
                int step=window.phase++;
                if(step==0){window.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=window.start});return;}
                if(step>12)
                {
                    Check(Vector3.Dot(window.driven.TransformDirection(window.aim.aimVector).normalized,(window.target.position-window.driven.position).normalized)>.999f,"Aim evaluates while source is dragged");
                    window.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=window.start+new Vector2(96,48)});
                    Finish("PASS "+window.checks+" checks: continuous screen-plane movement with native Aim evaluation");return;
                }
                var point=window.start+new Vector2(step*8,step*4);
                window.SendEvent(new Event{type=EventType.MouseDrag,button=0,mousePosition=point,delta=new Vector2(8,4)});
                Check(Vector2.Distance(window.projected,point)<.75f,"Center tracks pointer at sample "+step+": "+window.projected+" vs "+point);
                Check(Mathf.Abs(Vector3.Dot(window.target.position-window.initial,window.cameraView.transform.forward))<.0001f,"No depth jitter at sample "+step);
                EditorApplication.QueuePlayerLoopUpdate();window.Repaint();
            }
            catch(Exception e){Finish("FAIL "+e);}
        }
        static void Finish(string result)
        {
            EditorApplication.update-=Step;File.WriteAllText(Path.Combine(Root,"move-result.txt"),result);
            var old=window;window=null;if(!old)return;
            if(old.scene.IsValid())EditorSceneManager.ClosePreviewScene(old.scene);
            old.cameraView=null;EditorApplication.delayCall+=()=>{if(old)old.Close();};
        }
    }
}

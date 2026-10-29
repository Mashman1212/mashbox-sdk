using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private bool autoTumble=EditorPrefs.GetBool("MashBox.Retopology.AutoTumble",false);
        private float autoTumbleOrbitSpeed=Mathf.Clamp(EditorPrefs.GetFloat("MashBox.Retopology.AutoTumble.OrbitSpeed",1f),.25f,4f);
        private float autoTumblePanSpeed=Mathf.Clamp(EditorPrefs.GetFloat("MashBox.Retopology.AutoTumble.PanSpeed",1.5f),.25f,4f);
        private bool autoTumbleTarget;
        private bool autoTumbleSpaceHeld;
        private bool autoTumbleSpaceDown;
        private bool autoTumbleSpaceCancelled;
        internal static bool AutoTumbleEnabled(bool toolbarEnabled,bool spaceHeld)=>toolbarEnabled!=spaceHeld;
        private bool AutoTumbleActive=>!autoTumbleSpaceCancelled&&AutoTumbleEnabled(autoTumble,autoTumbleSpaceDown);
        private bool autoTumbleCursorHidden;
        private bool autoTumbleToolInput, autoTumbleDrawing;
        private Vector2 autoTumbleDrawPointer,autoTumbleToolPointer;
        private double autoTumbleAimTime;
#if UNITY_EDITOR_WIN
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ShowCursor([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool show);
        private int autoTumbleCursorHideCalls;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);
#else
        private bool autoTumblePreviousCursorVisible;
#endif
        private int autoTumbleControl;
        private Vector2 autoTumbleAimDelta;
        private MashBoxStudioViewport autoTumbleView;
        private Vector3 autoTumblePoint;
        private Quaternion autoTumbleRotation;
        private double autoTumbleTime;
        private double autoTumbleReadyTime;
        private Vector2 autoTumblePointer;
        private AutoTumbleMotion autoTumbleMotion;
        internal struct AutoTumbleMotion
        {
            internal Vector3 panVelocity;
            internal Vector2 orbitVelocity;
        }
        private Mesh autoTumbleNormalMesh;
        private Vector3[] autoTumbleNormals;
        private int[] autoTumbleTriangles;

        private void ToggleAutoTumble()
        {
            autoTumble=!autoTumble;EditorPrefs.SetBool("MashBox.Retopology.AutoTumble",autoTumble);
            autoTumbleSpaceCancelled=false;
            StopAutoTumble();repaint();
        }

        private void StopAutoTumble()
        {
            if(autoTumbleControl!=0)
            {
                if(GUIUtility.hotControl==autoTumbleControl)GUIUtility.hotControl=0;
            }
            RestoreAutoTumbleCursor();
            autoTumbleControl=0;autoTumbleSpaceHeld=false;autoTumbleAimDelta=Vector2.zero;
            autoTumbleDrawing=autoTumbleToolInput=false;autoTumbleAimTime=0;
            autoTumbleTarget=false;autoTumbleView=null;autoTumbleTime=0;autoTumbleReadyTime=0;autoTumbleMotion=default;
            autoTumbleSurface=null;autoTumbleSurfaceNormal=Vector3.zero;
        }

        private void HideAutoTumbleCursor(MashBoxStudioViewport canvas)
        {
            // Cursor shape and visibility are separate. SetCursor(null) only
            // hides until Unity's next cursor-shape update, causing flicker.
            EditorGUIUtility.AddCursorRect(canvas.ModelCanvasRect,MouseCursor.CustomCursor);
            LockAutoTumbleMouse(canvas);
            if(autoTumbleCursorHidden)return;
#if UNITY_EDITOR_WIN
            int count;
            do { count=ShowCursor(false);autoTumbleCursorHideCalls++; } while(count>=0);
#else
            autoTumblePreviousCursorVisible=Cursor.visible;
            Cursor.visible=false;
#endif
            autoTumbleCursorHidden=true;
            AssemblyReloadEvents.beforeAssemblyReload+=RestoreAutoTumbleCursor;
            EditorApplication.quitting+=RestoreAutoTumbleCursor;
        }

        private void RestoreAutoTumbleCursor()
        {
            UnlockAutoTumbleMouse();
            if(!autoTumbleCursorHidden)return;
#if UNITY_EDITOR_WIN
            // Balance only this tool's decrements, preserving other cursor owners.
            while(autoTumbleCursorHideCalls>0){ShowCursor(true);autoTumbleCursorHideCalls--;}
#else
            Cursor.visible=autoTumblePreviousCursorVisible;
#endif
            autoTumbleCursorHidden=false;
            AssemblyReloadEvents.beforeAssemblyReload-=RestoreAutoTumbleCursor;
            EditorApplication.quitting-=RestoreAutoTumbleCursor;
        }

        private void DrawAutoTumbleReticle(Vector2 point)
        {
            if(autoTumbleControl==0||Event.current.type!=EventType.Repaint)return;
            Handles.BeginGUI();var color=Handles.color;var matrix=Handles.matrix;
            try
            {
                Handles.matrix=Matrix4x4.identity;
                Handles.color=new Color(.35f,1f,.8f,1);
                Handles.DrawWireDisc(point,Vector3.forward,8);
                foreach(var direction in new[]{Vector2.left,Vector2.right,Vector2.up,Vector2.down})
                    Handles.DrawLine(point+direction*4,point+direction*14);
            }
            finally{Handles.matrix=matrix;Handles.color=color;Handles.EndGUI();}
        }

        internal static bool CanAutoTumble(bool enabled,bool onCanvas,bool busy,bool manualNavigation,bool activeApplication)
            =>enabled&&onCanvas&&!busy&&!manualNavigation&&activeApplication;

        internal static Quaternion AutoTumbleFacing(Vector3 normal,Quaternion current)
        {
            if(normal.sqrMagnitude<1e-12f)return current;
            normal.Normalize();
            var forward=-normal;
            // Keep the horizon level. Near a vertical normal retain the current
            // heading rather than letting tiny surface variations spin the view.
            float yaw=AutoTumbleAngles(current).y;
            if(new Vector2(forward.x,forward.z).sqrMagnitude>.0003046f)
                yaw=Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg;
            float pitch=Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(forward.y,-1,1))*Mathf.Rad2Deg,-89,89);
            return Quaternion.Euler(pitch,yaw,0);
        }

        private static Vector2 AutoTumbleAngles(Quaternion rotation)
        {
            var forward=rotation*Vector3.forward;
            return new Vector2(Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(forward.y,-1,1))*Mathf.Rad2Deg,-89,89),
                Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg);
        }

        internal static Quaternion AutoTumbleTargetRotation(Vector3 normal,Quaternion current)
        {
            var angles=AutoTumbleAngles(current);var targetAngles=AutoTumbleAngles(AutoTumbleFacing(normal,current));
            var delta=new Vector2(targetAngles.x-angles.x,Mathf.DeltaAngle(angles.y,targetAngles.y));
            // A hover is a gentle framing correction, not a command to swing
            // fully face-on to the side of a limb or a silhouette triangle.
            delta=Vector2.ClampMagnitude(delta,12);
            return Quaternion.Euler(angles.x+delta.x,angles.y+delta.y,0);
        }

        internal static bool ShouldRetargetAutoTumble(Vector2 pointer,Vector2 previous,bool hasAnchor)
            =>!hasAnchor||(pointer-previous).sqrMagnitude>=100;

        internal static void StepAutoTumble(Vector3 pivot,Quaternion rotation,Vector3 target,Quaternion targetRotation,float viewSize,float deltaTime,ref AutoTumbleMotion motion,out Vector3 nextPivot,out Quaternion nextRotation,float orbitSpeed=1f,float panSpeed=1f)
        {
            float dt=Mathf.Clamp(deltaTime,0,.05f);
            if(dt==0){nextPivot=pivot;nextRotation=rotation;return;}
            var angles=AutoTumbleAngles(rotation);var targetAngles=AutoTumbleAngles(targetRotation);
            targetAngles.y=angles.y+Mathf.DeltaAngle(angles.y,targetAngles.y);
            var velocity=motion.orbitVelocity;
            orbitSpeed=Mathf.Clamp(orbitSpeed,.25f,4f);panSpeed=Mathf.Clamp(panSpeed,.25f,4f);
            float pitch=Mathf.SmoothDamp(angles.x,targetAngles.x,ref velocity.x,.22f/orbitSpeed,45*orbitSpeed,dt);
            float yaw=Mathf.SmoothDamp(angles.y,targetAngles.y,ref velocity.y,.22f/orbitSpeed,45*orbitSpeed,dt);
            motion.orbitVelocity=velocity;
            nextRotation=Quaternion.Euler(pitch,yaw,0);
            // Rotate about the picked point. In camera coordinates its offset
            // stays fixed during orbit; only the gradual pan moves it to center.
            var offset=Quaternion.Inverse(rotation)*(pivot-target);
            offset=Vector3.SmoothDamp(offset,Vector3.zero,ref motion.panVelocity,.28f/panSpeed,Mathf.Max(.00001f,viewSize*1.1f)*panSpeed,dt);
            nextPivot=target+nextRotation*offset;
        }

        internal static bool AdvanceAutoTumbleView(SceneView canvas,Vector3 target,Quaternion targetRotation,float deltaTime,float tolerance,ref AutoTumbleMotion motion,float orbitSpeed=1f,float panSpeed=1f)
        {
            StepAutoTumble(canvas.pivot,canvas.rotation,target,targetRotation,canvas.size,deltaTime,ref motion,out var pivot,out var orientation,orbitSpeed,panSpeed);
            bool settled=(pivot-target).sqrMagnitude<=tolerance*tolerance&&Quaternion.Angle(orientation,targetRotation)<.05f;
            if(settled){pivot=target;orientation=targetRotation;motion=default;}
            canvas.LookAt(pivot,orientation,canvas.size,canvas.orthographic,true);return settled;
        }

        private Vector3 AutoTumbleNormal(RaycastHit hit)
        {
            var collider=hit.collider as MeshCollider;var mesh=collider?collider.sharedMesh:null;
            var normal=hit.normal;
            if(mesh)
            {
                if(autoTumbleNormalMesh!=mesh)
                {autoTumbleNormalMesh=mesh;autoTumbleNormals=mesh.normals;autoTumbleTriangles=mesh.triangles;}
                int triangle=hit.triangleIndex*3;
                if(autoTumbleNormals.Length==mesh.vertexCount&&triangle>=0&&triangle+2<autoTumbleTriangles.Length)
                {
                    var barycentric=hit.barycentricCoordinate;
                    var smooth=autoTumbleNormals[autoTumbleTriangles[triangle]]*barycentric.x
                        +autoTumbleNormals[autoTumbleTriangles[triangle+1]]*barycentric.y
                        +autoTumbleNormals[autoTumbleTriangles[triangle+2]]*barycentric.z;
                    if(smooth.sqrMagnitude>1e-12f)
                    {
                        smooth=collider.transform.TransformDirection(smooth).normalized;
                        normal=Vector3.Dot(smooth,hit.normal)>=0?smooth:-smooth;
                    }
                }
            }
            // Use the visible side even on models with reversed source normals.
            if(Vector3.Dot(normal,view.camera.transform.position-hit.point)<0)normal=-normal;
            return normal;
        }

        private void ObserveAutoTumble(MashBoxStudioViewport canvas,bool overUI)
        {
            var evt=Event.current;
            autoTumbleToolInput=false;
            Vector2 mouseDelta=(evt.type==EventType.MouseMove||evt.type==EventType.MouseDrag)?AutoTumbleMouseDelta(evt.delta):Vector2.zero;
            bool onCanvas=!overUI&&!canvas.IsOverViewportControls(evt.mousePosition)&&canvas.ModelCanvasRect.Contains(evt.mousePosition);
            int control=GUIUtility.GetControlID("RetopologyAutoTumble".GetHashCode(),FocusType.Passive);
            bool space=evt.keyCode==KeyCode.Space;
            bool spaceChanged=false;
            if(space&&evt.rawType==EventType.KeyUp&&autoTumbleSpaceDown)
            {
                autoTumbleSpaceDown=false;autoTumbleSpaceCancelled=false;
                StopAutoTumble();spaceChanged=true;
                evt.Use();canvas.Repaint();
                if(!AutoTumbleActive)return;
            }
            // Some SceneView handlers consume repeated key events before this
            // callback. They still belong to the same physical press.
            if(space&&evt.rawType==EventType.KeyDown&&autoTumbleSpaceDown)
            {
                if(autoTumbleControl!=0)HideAutoTumbleCursor(canvas);
                evt.Use();return;
            }
            bool startSpace=space&&evt.rawType==EventType.KeyDown&&!evt.shift&&!evt.control&&!evt.command&&!evt.alt&&!EditorGUIUtility.editingTextField;
            if(startSpace)
            {
                autoTumbleSpaceDown=true;autoTumbleSpaceCancelled=false;
                StopAutoTumble();spaceChanged=true;evt.Use();canvas.Repaint();
                if(!AutoTumbleActive)return;
            }
            if(autoTumbleControl!=0&&evt.type==EventType.KeyDown&&evt.keyCode==KeyCode.Escape)
            {
                if(evt.keyCode==KeyCode.Escape){autoTumble=false;autoTumbleSpaceCancelled=autoTumbleSpaceDown;EditorPrefs.SetBool("MashBox.Retopology.AutoTumble",false);}
                StopAutoTumble();evt.Use();canvas.Repaint();return;
            }
            bool captured=autoTumbleControl!=0;
            bool manual=evt.alt||(!captured&&evt.rawType==EventType.ScrollWheel)||
                (evt.rawType==EventType.MouseDown&&evt.button!=0);
            if(Active==null||!AutoTumbleActive||(!captured&&!onCanvas)||manual||
                !UnityEditorInternal.InternalEditorUtility.isApplicationActive||canvas.in2DMode||canvas.isRotationLocked||
                EditorGUIUtility.editingTextField||evt.rawType==EventType.Ignore)
            {StopAutoTumble();return;}
            if(captured&&evt.type==EventType.ScrollWheel)
            {
                ZoomAutoTumbleView(canvas,autoTumblePoint,evt.delta.y);
                // Wheel input changes distance only. Do not resume an old orbit
                // or let residual pointer smoothing acquire a different surface.
                autoTumbleTarget=false;autoTumbleMotion=default;
                autoTumbleAimDelta=Vector2.zero;
                autoTumbleAimTime=EditorApplication.timeSinceStartup;
                autoTumbleRotation=canvas.rotation;
                autoTumbleToolInput=true;
                autoTumbleToolPointer=HandleUtility.WorldToGUIPoint(autoTumblePoint);
                if(autoTumbleDrawing)autoTumbleDrawPointer=autoTumbleToolPointer;
                HideAutoTumbleCursor(canvas);
                evt.mousePosition=autoTumbleToolPointer;
                evt.Use();canvas.MarkDirty(MashBoxStudioViewport.DirtyState.Camera);
                canvas.Repaint();return;
            }
            bool down=evt.type==EventType.MouseDown&&evt.button==0;
            bool up=evt.rawType==EventType.MouseUp&&evt.button==0;
            bool pointTool=activeTool==RetopoTool.Polygon||activeTool==RetopoTool.Patch||activeTool==RetopoTool.Tube;
            if(captured&&down&&!pointTool)
            {
                autoTumbleDrawing=true;
                autoTumbleDrawPointer=HandleUtility.WorldToGUIPoint(autoTumblePoint);
                autoTumbleAimDelta=Vector2.zero;
            }
            if(captured&&autoTumbleDrawing)
            {
                if(evt.type==EventType.MouseDrag)autoTumbleDrawPointer+=mouseDelta;
                autoTumbleToolPointer=autoTumbleDrawPointer;
                if(up)autoTumbleDrawing=false;
            }
            else
            {
                double now=EditorApplication.timeSinceStartup;
                float dt=autoTumbleAimTime==0?1f/60:(float)(now-autoTumbleAimTime);
                bool move=evt.type==EventType.MouseMove||evt.type==EventType.MouseDrag;
                if(move)autoTumbleAimDelta+=mouseDelta;
                bool update=spaceChanged||move||evt.type==EventType.Repaint;
                Vector2 pointer=captured?HandleUtility.WorldToGUIPoint(autoTumblePoint):evt.mousePosition;
                if(update)
                {
                    autoTumbleAimTime=now;
                    var step=SmoothAutoTumbleInput(ref autoTumbleAimDelta,dt);
                    pointer+=step;
                    var rect=canvas.ModelCanvasRect;
                    pointer=new Vector2(Mathf.Clamp(pointer.x,rect.xMin,rect.xMax),Mathf.Clamp(pointer.y,rect.yMin,rect.yMax));
                    // Camera-only repaints must not acquire a different surface.
                    if(!captured||step.sqrMagnitude>0)
                    {
                        bool found=AutoTumbleSurfaceHit(pointer,step,captured,out var hit,out var normal);
                        if(found)
                        {
                            if(!autoTumbleTarget){autoTumbleTime=now;autoTumbleMotion=default;autoTumbleReadyTime=now;}
                            autoTumblePoint=hit.point;
                            autoTumbleRotation=AutoTumbleTargetRotation(normal,canvas.rotation);
                            autoTumbleView=canvas;autoTumbleTarget=true;
                            if(!captured)
                            {
                                autoTumbleControl=control;GUIUtility.hotControl=control;
                                canvas.Focus();HideAutoTumbleCursor(canvas);captured=true;
                            }
                        }
                    }
                }
                if(captured)autoTumbleToolPointer=HandleUtility.WorldToGUIPoint(autoTumblePoint);
            }
            if(!captured)return;
            autoTumbleToolInput=true;
            HideAutoTumbleCursor(canvas);
            evt.mousePosition=autoTumbleToolPointer;
            if(autoTumbleAimDelta.sqrMagnitude>0.0001f)canvas.Repaint();
        }

        internal static void ZoomAutoTumbleView(SceneView canvas,Vector3 anchor,float wheelDelta)
        {
            float oldSize=Mathf.Max(.0001f,canvas.size);
            float size=Mathf.Clamp(oldSize*Mathf.Exp(Mathf.Clamp(wheelDelta*.03f,-2,2)),.0001f,1000000f);
            // Scale the entire camera-to-anchor vector, including its off-axis
            // component. This keeps the reticle fixed in either projection.
            var pivot=anchor+(canvas.pivot-anchor)*(size/oldSize);
            canvas.LookAt(pivot,canvas.rotation,size,canvas.orthographic,true);
        }
        // Preserve total travel while filtering high-frequency mouse jitter.
        internal static Vector2 SmoothAutoTumbleInput(ref Vector2 pending,float deltaTime)
        {
            float blend=1-Mathf.Exp(-Mathf.Clamp(deltaTime,0,.05f)/.035f);
            var step=pending*blend;
            pending-=step;
            if(pending.sqrMagnitude<.0001f){step+=pending;pending=Vector2.zero;}
            return step;
        }
        private void TickAutoTumble()
        {
            var canvas=autoTumbleView;
            // A transient focus change must not re-arm Space while it is still
            // held: OS key repeat would otherwise toggle the mode repeatedly.
#if UNITY_EDITOR_WIN
            if(autoTumbleSpaceDown&&(GetAsyncKeyState(0x20)&0x8000)==0)
            {autoTumbleSpaceDown=false;autoTumbleSpaceCancelled=false;StopAutoTumble();view?.Repaint();}
#endif
            if(autoTumbleControl==0&&!autoTumbleTarget)return;
            if(!AutoTumbleActive||!canvas||canvas!=view||!canvas.IsShowingScene(Scene)||EditorWindow.focusedWindow!=canvas||
                !UnityEditorInternal.InternalEditorUtility.isApplicationActive||EditorApplication.isCompiling||EditorApplication.isUpdating||
                canvas.in2DMode||canvas.isRotationLocked||EditorGUIUtility.editingTextField)
            {StopAutoTumble();return;}
            if(!autoTumbleTarget)return;
            if(autoTumbleDrawing||quadDragHeld||stripStroke||relaxStroke||resolutionStroke||selectingVertices)
            {autoTumbleTime=EditorApplication.timeSinceStartup;return;}
            double now=EditorApplication.timeSinceStartup;
            float deltaTime=(float)(now-autoTumbleTime);
            autoTumbleTime=now;
            if(now<autoTumbleReadyTime)return;
            float epsilon=Mathf.Max(.000001f,Active!=null&&Active.reference?Active.reference.bounds.size.magnitude*.00001f:.00001f);
            // Keep zoom and projection unchanged while moving the view pivot.
            bool settled=AdvanceAutoTumbleView(canvas,autoTumblePoint,autoTumbleRotation,deltaTime,epsilon,ref autoTumbleMotion,autoTumbleOrbitSpeed,autoTumblePanSpeed);
            canvas.MarkDirty(MashBoxStudioViewport.DirtyState.Camera);
            if(settled)autoTumbleTarget=false;
        }
    }

    public sealed partial class MashBoxStudioViewport
    {
        internal bool IsShowingScene(UnityEngine.SceneManagement.Scene scene)=>customScene==scene;
    }
}

using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private bool autoTumble=EditorPrefs.GetBool("MashBox.Retopology.AutoTumble",false);
        private bool autoTumbleTarget;
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
            StopAutoTumble();repaint();
        }

        private void StopAutoTumble()
        {autoTumbleTarget=false;autoTumbleView=null;autoTumbleTime=0;autoTumbleReadyTime=0;autoTumbleMotion=default;}

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

        internal static void StepAutoTumble(Vector3 pivot,Quaternion rotation,Vector3 target,Quaternion targetRotation,float viewSize,float deltaTime,ref AutoTumbleMotion motion,out Vector3 nextPivot,out Quaternion nextRotation)
        {
            float dt=Mathf.Clamp(deltaTime,0,.05f);
            if(dt==0){nextPivot=pivot;nextRotation=rotation;return;}
            var angles=AutoTumbleAngles(rotation);var targetAngles=AutoTumbleAngles(targetRotation);
            targetAngles.y=angles.y+Mathf.DeltaAngle(angles.y,targetAngles.y);
            var velocity=motion.orbitVelocity;
            float pitch=Mathf.SmoothDamp(angles.x,targetAngles.x,ref velocity.x,.65f,18,dt);
            float yaw=Mathf.SmoothDamp(angles.y,targetAngles.y,ref velocity.y,.65f,18,dt);
            motion.orbitVelocity=velocity;
            nextRotation=Quaternion.Euler(pitch,yaw,0);
            // Rotate about the picked point. In camera coordinates its offset
            // stays fixed during orbit; only the gradual pan moves it to center.
            var offset=Quaternion.Inverse(rotation)*(pivot-target);
            offset=Vector3.SmoothDamp(offset,Vector3.zero,ref motion.panVelocity,.85f,Mathf.Max(.00001f,viewSize*.35f),dt);
            nextPivot=target+nextRotation*offset;
        }

        internal static bool AdvanceAutoTumbleView(SceneView canvas,Vector3 target,Quaternion targetRotation,float deltaTime,float tolerance,ref AutoTumbleMotion motion)
        {
            StepAutoTumble(canvas.pivot,canvas.rotation,target,targetRotation,canvas.size,deltaTime,ref motion,out var pivot,out var orientation);
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
            bool onCanvas=!overUI&&!canvas.IsOverViewportControls(evt.mousePosition)&&canvas.ModelCanvasRect.Contains(evt.mousePosition);
            bool busy=GUIUtility.hotControl!=0||contextHeld||adjustingRetopoBrush||quadDragHeld||stripStroke||relaxStroke||resolutionStroke||selectingVertices||EditorGUIUtility.editingTextField;
            bool manual=evt.alt||evt.rawType==EventType.MouseDown||evt.rawType==EventType.MouseDrag||evt.rawType==EventType.MouseUp||evt.rawType==EventType.ScrollWheel||evt.rawType==EventType.KeyDown;
            if(Active==null||!CanAutoTumble(autoTumble,onCanvas,busy,manual,UnityEditorInternal.InternalEditorUtility.isApplicationActive)||canvas.in2DMode||canvas.isRotationLocked||evt.rawType==EventType.MouseLeaveWindow||evt.rawType==EventType.Ignore)
            {StopAutoTumble();return;}
            // Latch world-space targets only on real pointer movement. Recasting
            // during camera repaint would chase a moving ray and drift forever.
            if(evt.type!=EventType.MouseMove)return;
            if(!ShouldRetargetAutoTumble(evt.mousePosition,autoTumblePointer,autoTumbleView==canvas))return;
            RaycastHit hit=default;
            bool found=Active.showReference&&DrawingHit(evt.mousePosition,out hit);
            if(!found)
            {
                hit=default;
                if(Active.showOutput&&Document.selected<outputs.Count)
                {
                    var collider=outputs[Document.selected].GetComponent<MeshCollider>();
                    found=collider&&collider.Raycast(HandleUtility.GUIPointToWorldRay(evt.mousePosition),out hit,float.MaxValue);
                }
            }
            if(!found){StopAutoTumble();return;}
            var normal=AutoTumbleNormal(hit);
            if(normal.sqrMagnitude<1e-12f){StopAutoTumble();return;}
            double now=EditorApplication.timeSinceStartup;
            if(!autoTumbleTarget){autoTumbleTime=now;autoTumbleMotion=default;autoTumbleReadyTime=now+.2;}
            else if(now<autoTumbleReadyTime)autoTumbleReadyTime=now+.2;
            autoTumblePoint=hit.point;autoTumbleRotation=AutoTumbleTargetRotation(normal,canvas.rotation);
            autoTumblePointer=evt.mousePosition;
            // Preserve velocity and the update clock when retargeting. Resetting
            // time on every MouseMove used to starve interpolation and stutter.
            autoTumbleView=canvas;autoTumbleTarget=true;
            canvas.Repaint();
        }

        private void TickAutoTumble()
        {
            var canvas=autoTumbleView;
            if(!autoTumbleTarget)return;
            if(!autoTumble||!canvas||canvas!=view||!canvas.IsShowingScene(Scene)||EditorWindow.mouseOverWindow!=canvas||
                !UnityEditorInternal.InternalEditorUtility.isApplicationActive||EditorApplication.isCompiling||EditorApplication.isUpdating||
                canvas.in2DMode||canvas.isRotationLocked||EditorGUIUtility.editingTextField)
            {StopAutoTumble();return;}
            double now=EditorApplication.timeSinceStartup;
            float deltaTime=(float)(now-autoTumbleTime);
            autoTumbleTime=now;
            if(now<autoTumbleReadyTime)return;
            float epsilon=Mathf.Max(.000001f,Active!=null&&Active.reference?Active.reference.bounds.size.magnitude*.00001f:.00001f);
            // Keep zoom and projection unchanged while moving the view pivot.
            bool settled=AdvanceAutoTumbleView(canvas,autoTumblePoint,autoTumbleRotation,deltaTime,epsilon,ref autoTumbleMotion);
            canvas.MarkDirty(MashBoxStudioViewport.DirtyState.Camera);
            if(settled)autoTumbleTarget=false;
        }
    }

    public sealed partial class MashBoxStudioViewport
    {
        internal bool IsShowingScene(UnityEngine.SceneManagement.Scene scene)=>customScene==scene;
    }
}

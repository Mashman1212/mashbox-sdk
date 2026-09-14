using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [Serializable] private sealed class BikeCurve { public string name; public Vector3[] points; }
        [Serializable] private sealed class BikeCurves { public BikeCurve[] curves; }
        private static BikeCurves bikeCurves;
        [SerializeField] private bool editBikeHandleLayout;
        private StudioHandleOffset BikeHandle(int index) => take?.handleOffsets?.FirstOrDefault(o=>o.control==BikeControlNames[index]);
        private Vector3 BikeHandlePosition(int index,Transform joint) => joint.TransformPoint(BikeHandle(index)?.position ?? Vector3.zero);
        private Quaternion RestWorldRotation(Transform bone)
        {
            Quaternion q=Quaternion.identity;
            for(var t=bone;t;t=t.parent)
            { int i=Array.IndexOf(rig.Bones,t); if(i<0) break; q=rig.Rest.bones[i].rotation*q; }
            return q;
        }
        private void SaveBikeHandle(int index,Vector3 position,Vector3 rotation)
        {
            Undo.RecordObject(take,"Edit bike handle layout");
            take.handleOffsets=take.handleOffsets ?? new System.Collections.Generic.List<StudioHandleOffset>();
            var offset=BikeHandle(index);
            if(offset==null) { offset=new StudioHandleOffset { control=BikeControlNames[index] }; take.handleOffsets.Add(offset); }
            offset.position=position; offset.rotation=rotation; Save(); viewport?.Repaint();
        }
        private Vector2 bikeDragMouse;
        private Vector3 bikeDragOrigin, bikeDragTangent;
        private float bikeDragAngle, bikeDragRadius;
        private int bikeDragControl;
        private bool DrawMayaBikeCurve(int index,Transform joint)
        {
            if(bikeCurves==null)
            {
                var data=AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath("a8ec29e72a6d447580d5b3ebfeec906a"));
                bikeCurves=data ? JsonUtility.FromJson<BikeCurves>(data.text) : new BikeCurves { curves=Array.Empty<BikeCurve>() };
            }
            var shape=bikeCurves.curves.FirstOrDefault(c=>c.name==BikeControlNames[index]);
            if(shape==null && (index==0 || index==8)) return false;
            var offset=BikeHandle(index);
            // The ASCII extraction omitted the root's driven 35 cm translation. Do not add it twice.
            var correction=Quaternion.Inverse(RestWorldRotation(joint))*(rig.Rest.bones[0].rotation*Vector3.up)*.35f;
            Vector3[] points;
            if(shape!=null)
                points=shape.points.Select(p=>joint.TransformPoint((offset?.position ?? Vector3.zero)+Quaternion.Euler(offset?.rotation ?? Vector3.zero)*(p-correction))).ToArray();
            else
            {
                var center=BikeHandlePosition(index,joint);
                var axis=joint.TransformDirection(BikeAxis(index)).normalized;
                var u=Vector3.Cross(axis,Mathf.Abs(Vector3.Dot(axis,Vector3.up))>.9f ? Vector3.right : Vector3.up).normalized;
                var v=Vector3.Cross(axis,u);
                float radius=HandleUtility.GetHandleSize(center)*.085f*controlSize*1.1f;
                points=new Vector3[65];
                for(int i=0;i<points.Length;i++)
                { float angle=i*Mathf.PI*2/(points.Length-1); points[i]=center+radius*(u*Mathf.Cos(angle)+v*Mathf.Sin(angle)); }
            }
            foreach(var point in points) RegisterRigPoint(index,false,false,point);
            int id=GUIUtility.GetControlID(78200+index,FocusType.Passive);
            if(Event.current.type==EventType.Layout && !Event.current.alt && !playing)
            {
                float distance=float.MaxValue;
                for(int p=1;p<points.Length;p++) distance=Mathf.Min(distance,HandleUtility.DistanceToLine(points[p-1],points[p]));
                HandleUtility.AddControl(id,Mathf.Max(0,distance-5));
            }
            if(Event.current.type==EventType.Repaint)
            { Handles.color=RigControlSelected(index) ? Color.white : new Color(.9f,.4f,1); Handles.DrawAAPolyLine(RigControlSelected(index)?3:2,points); }
            if(Event.current.type==EventType.MouseDown && Event.current.button==0 && !Event.current.alt && !playing && GUIUtility.hotControl==0 && HandleUtility.nearestControl==id)
            {
                FinishSceneEdit(); PickRigControl(index); control=index; ikMode=false;
                if(index>0 && index<8 && !editBikeHandleLayout && !Event.current.shift && rigSelection.Count<=1)
                {
                    // Drag the authored curve itself along its projected rotational tangent.
                    var center=BikeHandlePosition(index,joint);
                    var axis=joint.TransformDirection(BikeAxis(index)).normalized;
                    var nearest=points.OrderBy(p=>(HandleUtility.WorldToGUIPoint(p)-Event.current.mousePosition).sqrMagnitude).First();
                    var radial=Vector3.ProjectOnPlane(nearest-center,axis);
                    bikeDragRadius=Mathf.Max(radial.magnitude,HandleUtility.GetHandleSize(center)*.085f*controlSize);
                    bikeDragTangent=Vector3.Cross(axis,radial.normalized);
                    if(bikeDragTangent.sqrMagnitude<.001f ||
                        (HandleUtility.WorldToGUIPoint(nearest+bikeDragTangent*bikeDragRadius)-HandleUtility.WorldToGUIPoint(nearest)).sqrMagnitude<4)
                        bikeDragTangent=BikeViewTangent(axis);
                    bikeDragOrigin=nearest; bikeDragMouse=Event.current.mousePosition;
                    bikeDragAngle=BikeAngle(index); bikeDragControl=index;
                    GUIUtility.hotControl=id;
                }
                Event.current.Use(); Repaint();
            }
            if(GUIUtility.hotControl==id && Event.current.type==EventType.MouseDrag)
            {
                float distance=HandleUtility.CalcLineTranslation(bikeDragMouse,Event.current.mousePosition,bikeDragOrigin,bikeDragTangent);
                SetBikeAngle(bikeDragControl,bikeDragAngle+distance/bikeDragRadius*Mathf.Rad2Deg);
                pose=rig.Capture(); PreviewSceneEdit("Rotate bike control"); Event.current.Use();
            }
            if(GUIUtility.hotControl==id && Event.current.rawType==EventType.MouseUp)
            { GUIUtility.hotControl=0; FinishSceneEdit(); Event.current.Use(); }

            return true;
        }
        private Vector3 BikeViewTangent(Vector3 axis)
        {
            var camera=viewport.camera.transform;
            var tangent=Vector3.Cross(axis,camera.forward);
            return tangent.sqrMagnitude>.001f ? tangent.normalized : camera.right;
        }
        private static readonly string[] BikeControlNames = { "BMX_Curve", "Bars_Curve", "Frame_Curve", "DriveTrain_Curve", "LeftPedal_Curve", "RightPedal_Curve", "BackWheel", "FrontWheel", "PID balance" };
        private static readonly string[] BikeJointNames = { "Joints", "Bars_Joint", "Frame_Joint", "DriveTrain_Joint", "LeftPedal_Joint", "RightPedal_Joint", "BackWheel_Joint", "FrontWheel_Joint", "PIDBalanceControl" };
        private Transform BikeJoint(int i) => rig.Bones.FirstOrDefault(b => b.name.Split(':').Last() == BikeJointNames[i]);
        private bool IsBike => rig != null && !rig.Animator && BikeJoint(0) && BikeJoint(1) && BikeJoint(3);
        private Vector3 BikeAxis(int i) => i == 1 || i == 2 ? Vector3.up : Vector3.right;
        private void SetBikeAngle(int i, float angle)
        {
            var joint=BikeJoint(i); if(!joint) return;
            int index=Array.IndexOf(rig.Bones,joint);
            var axis=BikeAxis(i);
            Quaternion delta=Quaternion.Inverse(rig.Rest.bones[index].rotation)*joint.localRotation;
            float old = axis == Vector3.up ? Mathf.DeltaAngle(0,delta.eulerAngles.y) : Mathf.DeltaAngle(0,delta.eulerAngles.x);
            joint.localRotation=rig.Rest.bones[index].rotation*Quaternion.AngleAxis(angle,axis);
            if(i==3)
            {
                // Maya DriveTrain_CTRL: pedal local X = pedal control X - crank X.
                for(int pedal=4;pedal<=5;pedal++)
                {
                    var bone=BikeJoint(pedal); if(!bone) continue;
                    bone.localRotation=Quaternion.AngleAxis(old-angle,Vector3.right)*bone.localRotation;
                }
            }
        }
        private float BikeAngle(int i)
        {
            var bone=BikeJoint(i); int index=Array.IndexOf(rig.Bones,bone);
            var delta=Quaternion.Inverse(rig.Rest.bones[index].rotation)*bone.localRotation;
            return Mathf.DeltaAngle(0,BikeAxis(i)==Vector3.up ? delta.eulerAngles.y : delta.eulerAngles.x);
        }
        private bool DrawBikeControls()
        {
            if(!IsBike) return false;
            ikMode=false; control=Mathf.Clamp(control,0,BikeControlNames.Length-1);
            EditorGUILayout.LabelField("BMX · MAYA CONTROL MAPPING",EditorStyles.boldLabel);
            var bone=BikeJoint(control);
            if(!bone) { EditorGUILayout.HelpBox("Joint missing: "+BikeJointNames[control],MessageType.Warning); return true; }
            rotate=GUILayout.Toolbar(rotate?1:0,new[]{"Move [W]","Rotate [E]"})==1;
            EditorGUI.BeginChangeCheck();
            if(control==0)
            {
                var position=EditorGUILayout.Vector3Field("BMX position",bone.localPosition);
                var angles=EditorGUILayout.Vector3Field("BMX rotation",bone.localEulerAngles);
                if(EditorGUI.EndChangeCheck()) { bone.localPosition=position; bone.localEulerAngles=angles; pose=rig.Capture(); Commit("Edit BMX root"); }
            }
            else if(control==8)
            {
                var position=bone.localPosition;
                position.y=EditorGUILayout.FloatField("PID X control → Y",position.y);
                position.z=EditorGUILayout.FloatField("PID Z control → Z",position.z);
                if(EditorGUI.EndChangeCheck()) { bone.localPosition=position; pose=rig.Capture(); Commit("Edit bike balance channels"); }
            }
            else
            {
                float angle=EditorGUILayout.FloatField(BikeAxis(control)==Vector3.up ? "Rotate Y (degrees)" : "Rotate X (degrees)",BikeAngle(control));
                if(EditorGUI.EndChangeCheck()) { SetBikeAngle(control,angle); pose=rig.Capture(); Commit("Edit bike control"); }
            }
            using(new EditorGUI.DisabledScope(true))
            {
                if(control!=0 && control!=8) EditorGUILayout.Vector3Field("Joint position",bone.localPosition);
            }
            control=GUILayout.SelectionGrid(control,BikeControlNames,2);
            editBikeHandleLayout=EditorGUILayout.Toggle("Edit handle layout",editBikeHandleLayout);
            if(editBikeHandleLayout)
            {
                var offset=BikeHandle(control);
                EditorGUI.BeginChangeCheck();
                var p=EditorGUILayout.Vector3Field("Handle position offset",offset?.position ?? Vector3.zero);
                var r=EditorGUILayout.Vector3Field("Handle rotation offset",offset?.rotation ?? Vector3.zero);
                if(EditorGUI.EndChangeCheck()) SaveBikeHandle(control,p,r);
                if(GUILayout.Button("Reset handle offset")) SaveBikeHandle(control,Vector3.zero,Vector3.zero);
                EditorGUILayout.HelpBox("Layout mode moves the handle without moving or keying the bone. Saved with the actor and presets.",MessageType.None);
            }
            EditorGUILayout.HelpBox("BMX moves the whole joint hierarchy. Bars/frame rotate Y; crank and pedals rotate X. Crank edits counter-rotate the pedals, matching Maya's drivetrain expression. Wheel controls are additional FK controls. Values are in Unity units.",MessageType.None);
            return true;
        }
        private bool DrawBikeScene(SceneView view)
        {
            if(!IsBike) return false;
            control=Mathf.Clamp(control,0,BikeControlNames.Length-1);
            var color=Handles.color; var depth=Handles.zTest;
            Handles.zTest=UnityEngine.Rendering.CompareFunction.Always;
            try
            {
                if(!skipActorPickers)
                {
                if(showSkeleton) foreach(var bone in rig.Bones)
                    if(bone.parent && (bone.name.Contains("Joint") || bone.name.Contains("Pedal")))
                    { Handles.color=new Color(.4f,.8f,1,.65f); Handles.DrawLine(bone.parent.position,bone.position); }
                for(int i=0;i<BikeControlNames.Length;i++)
                {
                    var joint=BikeJoint(i); if(!joint) continue;
                    if(showFKControls && DrawMayaBikeCurve(i,joint)) continue;
                    if(showFKControls) RegisterRigPoint(i,false,false,BikeHandlePosition(i,joint));
                    if(showFKControls && RingButton(BikeHandlePosition(i,joint),i==0?2.5f:1.1f,new Color(.9f,.4f,1),RigControlSelected(i),view) && !playing && !Event.current.alt)
                    { FinishSceneEdit(); PickRigControl(i); control=i; ikMode=false; Repaint(); }
                }
                }
                if(actorPickersOnly || rigSelection.Count>1 || (rigSelectionExplicit && rigSelection.Count==0)) return true;
                if(playing || !showFKControls) return true;
                var selected=BikeJoint(control); if(!selected) return true;
                var handlePosition=BikeHandlePosition(control,selected);
                if(editBikeHandleLayout)
                {
                    var offset=BikeHandle(control); EditorGUI.BeginChangeCheck();
                    var p=handlePosition; var r=selected.rotation*Quaternion.Euler(offset?.rotation ?? Vector3.zero);
                    if(rotate) r=Handles.RotationHandle(r,p); else p=Handles.PositionHandle(p,selected.rotation);
                    if(EditorGUI.EndChangeCheck()) SaveBikeHandle(control,selected.InverseTransformPoint(p),(Quaternion.Inverse(selected.rotation)*r).eulerAngles);
                    return true;
                }
                if(DrawAttachmentHandle()) return true;
                Handles.Label(handlePosition,BikeControlNames[control]);
                EditorGUI.BeginChangeCheck();
                if(control==0 || control==8)
                {
                    Vector3 p=handlePosition;
                    Quaternion r=selected.rotation;
                    if(rotate && control==0) r=Handles.RotationHandle(r,p);
                    else p=Handles.PositionHandle(p,selected.parent.rotation);
                    if(EditorGUI.EndChangeCheck())
                    {
                        if(!rotate || control==8) selected.position+=p-handlePosition;
                        selected.rotation=r; pose=rig.Capture(); PreviewSceneEdit("Move bike control");
                    }
                }
                else
                {
                    // Curves and fallback wheel rings handle rotation in the picker pass.
                    EditorGUI.EndChangeCheck();
                }
                return true;
            }
            finally { Handles.color=color; Handles.zTest=depth; }
        }
    }
}

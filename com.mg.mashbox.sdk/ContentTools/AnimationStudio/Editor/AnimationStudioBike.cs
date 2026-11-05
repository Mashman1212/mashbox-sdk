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
        private static Vector3[] CenterBarsCurve(Vector3[] points)
            => CenterBikeCurve(points, Vector3.up);
        private static Vector3[] CenterBikeCurve(Vector3[] points, Vector3 axis)
        {
            if (points == null || points.Length < 3) return points;
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(point);
            var normal = Vector3.Cross(points[points.Length / 3] - points[0], points[2 * points.Length / 3] - points[0]).normalized;
            if (Vector3.Dot(normal, axis) < 0) normal = -normal;
            var rotation = normal.sqrMagnitude > .5f ? Quaternion.FromToRotation(normal, axis) : Quaternion.identity;
            return points.Select(point => rotation * (point - bounds.center)).ToArray();
        }
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
                var bars = bikeCurves.curves.FirstOrDefault(c => c.name == "Bars_Curve");
                if (bars != null) bars.points = CenterBarsCurve(bars.points);
                foreach (var curve in bikeCurves.curves)
                    if (curve.name == "DriveTrain_Curve" || curve.name == "LeftPedal_Curve" || curve.name == "RightPedal_Curve")
                        curve.points = CenterBikeCurve(curve.points, Vector3.right);
            }
            var shape=bikeCurves.curves.FirstOrDefault(c=>c.name==BikeControlNames[index]);
            if(shape==null && (index==0 || index==8)) return false;
            var offset=BikeHandle(index);
            // The ASCII extraction omitted the root's driven 35 cm translation. Do not add it twice.
            var correction=Quaternion.Inverse(RestWorldRotation(joint))*(rig.Rest.bones[0].rotation*Vector3.up)*.35f;
            if (index == 1 || (index >= 3 && index <= 5)) correction = Vector3.zero; // Centered shapes follow the driven bone directly.
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
            if(Event.current.type==EventType.Layout && !Event.current.alt && !playing && !rotationGizmoOwnsPointer)
            {
                float distance=float.MaxValue;
                for(int p=1;p<points.Length;p++) distance=Mathf.Min(distance,HandleUtility.DistanceToLine(points[p-1],points[p]));
                HandleUtility.AddControl(id,Mathf.Max(0,distance-5));
            }
            if(Event.current.type==EventType.Repaint)
            {
                bool hovered = !playing && !Event.current.alt && !rotationGizmoOwnsPointer &&
                    HandleUtility.nearestControl == id;
                Handles.color = hovered ? new Color(1f,.86f,.35f) :
                    RigControlSelected(index) ? Color.white : new Color(.9f,.4f,1);
                Handles.DrawAAPolyLine(hovered || RigControlSelected(index) ? 3 : 2,points);
            }
            if(Event.current.type==EventType.MouseDown && Event.current.button==0 && !Event.current.alt && !playing && !rotationGizmoOwnsPointer && GUIUtility.hotControl==0 && HandleUtility.nearestControl==id)
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
                // The left pedal's authored orientation reverses its drag response.
                // Keep this input correction separate from keyed angles and crank compensation.
                if (bikeDragControl == 4) distance = -distance;
                SetBikeAngle(bikeDragControl,bikeDragAngle+distance/bikeDragRadius*Mathf.Rad2Deg);
                CaptureBikeRotation(bikeDragControl); PreviewSceneEdit("Rotate bike control"); Event.current.Use();
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
        // Keep legacy indices stable for existing keys and handle offsets.
        private static readonly int[] BikeVisibleControls = { 0, 1, 3, 4, 5, 8 };
        private static readonly string[] BikeVisibleControlNames = BikeVisibleControls.Select(i => BikeControlNames[i]).ToArray();
        private static bool IsHiddenBikeControl(int index) => index == 2 || index == 6 || index == 7;
        private Transform BikeJoint(int i) => rig.Bones.FirstOrDefault(b => b.name.Split(':').Last() == BikeJointNames[i]);
        private bool IsBike => rig != null && !rig.Animator && BikeJoint(0) && BikeJoint(1) && BikeJoint(3);
        private Vector3 BikeAxis(int i) => i == 1 || i == 2 ? Vector3.up : Vector3.right;
        private void SetBikeAngle(int i, float angle)
        {
            var joint=BikeJoint(i); if(!joint) return;
            int index=Array.IndexOf(rig.Bones,joint);
            var axis=BikeAxis(i);
            Quaternion delta=Quaternion.Inverse(rig.Rest.bones[index].rotation)*joint.localRotation;
            float old = Mathf.DeltaAngle(0, 2f * Mathf.Atan2(axis == Vector3.up ? delta.y : delta.x, delta.w) * Mathf.Rad2Deg);
            joint.localRotation = i == 1
                ? joint.localRotation * Quaternion.AngleAxis(angle - old, axis)
                : rig.Rest.bones[index].rotation * Quaternion.AngleAxis(angle, axis);
            if (pose != null && index < pose.bones.Length)
            {
                var authored = pose.bones[index];
                var axes = Vector3.zero;
                if (axis == Vector3.up) axes.y = angle; else axes.x = angle;
                authored.SetRotationAngles(axes, joint.localRotation * Quaternion.Inverse(Quaternion.Euler(axes)));
                pose.bones[index] = authored;
            }
            if(i==3)
            {
                // The left pedal uses the opposite pitch direction to the right.
                for(int pedal=4;pedal<=5;pedal++)
                {
                    var bone=BikeJoint(pedal); if(!bone) continue;
                    float compensation = pedal == 4 ? angle - old : old - angle;
                    bone.localRotation=Quaternion.AngleAxis(compensation,Vector3.right)*bone.localRotation;
                    int pedalIndex=Array.IndexOf(rig.Bones,bone);
                    if (pose != null && pedalIndex >= 0 && pedalIndex < pose.bones.Length)
                    {
                        var authored=pose.bones[pedalIndex];
                        var angles=authored.continuousRotation ? authored.rotationAngles :
                            (Quaternion.Inverse(rig.Rest.bones[pedalIndex].rotation)*authored.rotation).eulerAngles;
                        angles.x+=compensation;
                        authored.SetRotationAngles(angles,bone.localRotation*Quaternion.Inverse(Quaternion.Euler(angles)));
                        pose.bones[pedalIndex]=authored;
                    }
                }
            }
        }
        private float BikeAngle(int i)
        {
            var bone=BikeJoint(i); int index=Array.IndexOf(rig.Bones,bone);
            if (pose != null && index >= 0 && index < pose.bones.Length && pose.bones[index].continuousRotation)
                return BikeAxis(i) == Vector3.up ? pose.bones[index].rotationAngles.y : pose.bones[index].rotationAngles.x;
            var delta=Quaternion.Inverse(rig.Rest.bones[index].rotation)*bone.localRotation;
            return Mathf.DeltaAngle(0, 2f * Mathf.Atan2(BikeAxis(i) == Vector3.up ? delta.y : delta.x, delta.w) * Mathf.Rad2Deg);
        }
        private void CaptureBikeRotation(int i)
        {
            var previous = pose;
            pose = rig.Capture();
            if (previous == null) return;
            for (int controlIndex = 1; controlIndex <= 5; controlIndex++)
            {
                var joint = BikeJoint(controlIndex);
                int boneIndex = Array.IndexOf(rig.Bones, joint);
                if (boneIndex < 0 || !previous.bones[boneIndex].continuousRotation) continue;
                var authored = previous.bones[boneIndex];
                authored.rotation = pose.bones[boneIndex].rotation;
                pose.bones[boneIndex] = authored;
            }
        }
        private bool DrawBikeControls()
        {
            if(!IsBike) return false;
            ikMode=false; control=Mathf.Clamp(control,0,BikeControlNames.Length-1);
            if (IsHiddenBikeControl(control)) control = 0;
            EditorGUILayout.LabelField("BMX · MAYA CONTROL MAPPING",StudioTheme.Styles.boldLabel);
            var bone=BikeJoint(control);
            if(!bone) { EditorGUILayout.HelpBox("Joint missing: "+BikeJointNames[control],MessageType.Warning); return true; }
            rotate=GUILayout.Toolbar(rotate?1:0,new[]{"Move [W]","Rotate [E]"})==1;
            EditorGUI.BeginChangeCheck();
            if(control==0)
            {
                var position=EditorGUILayout.Vector3Field("BMX position",bone.localPosition);
                int rootIndex=Array.IndexOf(rig.Bones,bone);
                var angles=EditorGUILayout.Vector3Field("BMX rotation",pose.bones[rootIndex].DisplayRotation);
                if(EditorGUI.EndChangeCheck()) { bone.localPosition=position; bone.localEulerAngles=angles; pose=rig.Capture(); var rootPose=pose.bones[rootIndex]; rootPose.SetRotationAngles(angles,Quaternion.identity); pose.bones[rootIndex]=rootPose; Commit("Edit BMX root"); }
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
                if(EditorGUI.EndChangeCheck()) { SetBikeAngle(control,angle); CaptureBikeRotation(control); Commit("Edit bike control"); }
            }
            using(new EditorGUI.DisabledScope(true))
            {
                if(control!=0 && control!=8) EditorGUILayout.Vector3Field("Joint position",bone.localPosition);
            }
            control=BikeVisibleControls[GUILayout.SelectionGrid(Array.IndexOf(BikeVisibleControls,control),BikeVisibleControlNames,2)];
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
            EditorGUILayout.HelpBox("BMX moves the whole joint hierarchy. Bars/frame rotate Y; crank and pedals rotate X. Crank edits counter-rotate the pedals, matching Maya's drivetrain expression. Values are in Unity units.",MessageType.None);
            return true;
        }
        private bool DrawBikeScene(SceneView view)
        {
            if(!IsBike) return false;
            control=Mathf.Clamp(control,0,BikeControlNames.Length-1);
            if (IsHiddenBikeControl(control)) control = 0;
            rigSelection.RemoveAll(item => item.Actor == take && IsHiddenBikeControl(item.Index));
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
                    if (IsHiddenBikeControl(i) || i == 8) continue; // Frame/wheel controls are unused; balance is edited in the UI.
                    var joint=BikeJoint(i); if(!joint) continue;
                    if(showVehicleControls && DrawMayaBikeCurve(i,joint)) continue;
                    if(showVehicleControls) RegisterRigPoint(i,false,false,BikeHandlePosition(i,joint));
                    if(showVehicleControls && RingButton(BikeHandlePosition(i,joint),i==0?2.5f:1.1f,new Color(.9f,.4f,1),RigControlSelected(i),view) && !playing && !Event.current.alt)
                    { FinishSceneEdit(); PickRigControl(i); control=i; ikMode=false; Repaint(); }
                }
                }
                if(actorPickersOnly || rigSelection.Count>1 || (rigSelectionExplicit && rigSelection.Count==0)) return true;
                if(playing || !showVehicleControls || control == 8) return true;
                var selected=BikeJoint(control); if(!selected) return true;
                var handlePosition=BikeHandlePosition(control,selected);
                if(editBikeHandleLayout)
                {
                    var offset=BikeHandle(control); EditorGUI.BeginChangeCheck();
                    var p=handlePosition; var r=selected.rotation*Quaternion.Euler(offset?.rotation ?? Vector3.zero);
                    if(rotate) r=StudioRotationHandle(r,p); else p=StudioUnsnappedHandles.PositionHandle(p,HandleOrientation(r));
                    if(EditorGUI.EndChangeCheck()) SaveBikeHandle(control,selected.InverseTransformPoint(p),(Quaternion.Inverse(selected.rotation)*r).eulerAngles);
                    return true;
                }
                if(DrawAttachmentHandle()) return true;
                Handles.Label(handlePosition,BikeControlNames[control]);
                if(control==0 || control==8)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 p=handlePosition;
                    Quaternion r=selected.rotation;
                    if(rotate && control==0) r=StudioRotationHandle(r,p);
                    else p=StudioUnsnappedHandles.PositionHandle(p,HandleOrientation(selected.rotation));
                    if(EditorGUI.EndChangeCheck())
                    {
                        if(!rotate || control==8) selected.position+=p-handlePosition;
                        selected.rotation=r; pose=rig.Capture(); PreviewSceneEdit("Move bike control");
                    }
                }
                return true;
            }
            finally { Handles.color=color; Handles.zTest=depth; }
        }
    }
}

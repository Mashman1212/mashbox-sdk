using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private bool showIKControls = true;
        [SerializeField] private bool fadeRigControls = true;
        [SerializeField] private bool hideInactiveControls;
        [SerializeField] private bool rigDiagram = true;
        [SerializeField] private int diagramHand;
        private static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };
        private int BoneLimb(Transform bone)
        {
            for (int i = 0; i < 4; i++)
                if (rig.Starts[i] && (bone == rig.Starts[i] || bone == rig.Middles[i] || bone == rig.Ends[i])) return i;
            return -1; // Fingers and toes remain independently editable FK controls.
        }
        private float RigAlpha(bool ik, int limb)
        {
            if (ik ? !showIKControls : !showFKControls) return 0;
            if (!fadeRigControls || limb < 0) return 1;
            float weight = Mathf.Clamp01(DisplayEffector(limb).weight);
            float amount = ik ? weight : 1-weight;
            return hideInactiveControls ? amount : Mathf.Lerp(.15f, 1, amount);
        }
        private void SetLimbBlend(int limb, float weight)
        {
            FinishSceneEdit();
            if (pose.effectors[limb].weight <= 0 && weight > 0)
                pose.effectors[limb] = rig.MatchEffector(limb, 0);
            pose.effectors[limb].weight = weight;
            Commit("Change limb IK/FK blend");
        }
        private void DrawRigDiagram()
        {
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                showIKControls = GUILayout.Toggle(showIKControls, "Show IK", EditorStyles.miniButtonLeft);
                showFKControls = GUILayout.Toggle(showFKControls, "Show FK", EditorStyles.miniButtonRight);
            }
            fadeRigControls = EditorGUILayout.Toggle("Fade by IK/FK blend", fadeRigControls);
            using (new EditorGUI.DisabledScope(!fadeRigControls)) hideInactiveControls = EditorGUILayout.Toggle("Hide inactive controls", hideInactiveControls);
            if (EditorGUI.EndChangeCheck()) viewport?.Repaint();
            rigDiagram = EditorGUILayout.Foldout(rigDiagram, "Body picker · front view", true);
            if (!rigDiagram) return;
            Rect area = GUILayoutUtility.GetRect(100, 280, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(area, new Color(.075f,.09f,.11f));
            Vector2 origin = new Vector2(area.center.x, area.y+12);
            float scale = Mathf.Min(area.width / 250f, 1);
            Handles.BeginGUI();
            DiagramBone(HumanBodyBones.Hips, 0,135,0,110, origin,scale);
            DiagramBone(HumanBodyBones.Spine, 0,110,0,90, origin,scale);
            DiagramBone(HumanBodyBones.Chest, 0,90,0,70, origin,scale);
            DiagramBone(HumanBodyBones.UpperChest, 0,70,0,51, origin,scale);
            DiagramBone(HumanBodyBones.Neck, 0,51,0,22, origin,scale);
            DiagramBone(HumanBodyBones.Head, 0,22,0,22, origin,scale);
            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? 1 : -1;
                DiagramBone(s == 0 ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder, x*25,70,0,70,origin,scale);
                DiagramBone(s == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, x*43,78,x*25,70,origin,scale);
                DiagramBone(s == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, x*65,115,x*43,78,origin,scale);
                DiagramBone(s == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, x*84,151,x*65,115,origin,scale);
                DiagramBone(s == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg, x*24,151,0,135,origin,scale);
                DiagramBone(s == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg, x*28,198,x*24,151,origin,scale);
                DiagramBone(s == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot, x*32,240,x*28,198,origin,scale);
                DiagramBone(s == 0 ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes, x*50,254,x*32,240,origin,scale);
                DiagramIK(s, origin + new Vector2(x*108,151)*scale, false);
                DiagramIK(s, origin + new Vector2(x*94,110)*scale, true);
                DiagramIK(s+2, origin + new Vector2(x*75,237)*scale, false);
                DiagramIK(s+2, origin + new Vector2(x*58,195)*scale, true);
            }
            Handles.EndGUI();
            EditorGUILayout.LabelField("Circles: FK bones   Squares: IK   Gold: poles", EditorStyles.miniLabel);
            int limb = ikMode ? Mathf.Clamp(control,0,3) : BoneLimb(rig.Bones[Mathf.Clamp(control,0,rig.Bones.Length-1)]);
            if (limb >= 0)
            {
                EditorGUILayout.LabelField(LimbNames[limb] + " chain", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(!rig.HasIK))
                {
                    EditorGUI.BeginChangeCheck();
                    float blend = EditorGUILayout.Slider("FK 0 ← blend → IK 1", pose.effectors[limb].weight,0,1);
                    if (EditorGUI.EndChangeCheck()) SetLimbBlend(limb,blend);
                }
            }
            else EditorGUILayout.LabelField("Selected bone: FK control", EditorStyles.miniLabel);
            diagramHand = GUILayout.Toolbar(diagramHand, new[] { "Left fingers", "Right fingers" });
            Rect hand = GUILayoutUtility.GetRect(100,78,GUILayout.ExpandWidth(true));
            int first = (int)(diagramHand == 0 ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
            for (int finger = 0; finger < 5; finger++)
            for (int joint = 0; joint < 3; joint++)
            {
                var human = (HumanBodyBones)(first + finger*3+joint);
                var bone = rig.Animator.GetBoneTransform(human);
                Rect button = new Rect(hand.x + finger*hand.width/5,hand.y + joint*25,hand.width/5-3,22);
                var background = GUI.backgroundColor;
                if (bone && !ikMode && rig.Bones[Mathf.Clamp(control,0,rig.Bones.Length-1)] == bone) GUI.backgroundColor = new Color(.3f,.85f,.7f);
                using (new EditorGUI.DisabledScope(!bone))
                    if (GUI.Button(button,new GUIContent(joint == 0 ? FingerNames[finger] : (joint+1).ToString(), human.ToString()),EditorStyles.miniButton)) SelectBone(bone);
                GUI.backgroundColor = background;
            }
        }
        private void DiagramBone(HumanBodyBones human, float x, float y, float px, float py, Vector2 origin, float scale)
        {
            var bone = rig.Animator.GetBoneTransform(human);
            Vector2 p = origin + new Vector2(x,y)*scale;
            Handles.color = new Color(.32f,.4f,.46f);
            Handles.DrawAAPolyLine(3,origin+new Vector2(px,py)*scale,p);
            bool selected = bone && !ikMode && rig.Bones[Mathf.Clamp(control,0,rig.Bones.Length-1)] == bone;
            float strength = bone ? RigAlpha(false, BoneLimb(bone)) : 0;
            Handles.color = !bone ? Color.gray : selected ? Color.white : Color.Lerp(new Color(.22f,.27f,.3f), new Color(.3f,.85f,.7f),strength);
            Handles.DrawSolidDisc(p, Vector3.forward,selected ? 8 : 6);
            if (bone && GUI.Button(new Rect(p.x-10,p.y-10,20,20),new GUIContent("",human.ToString()), GUIStyle.none)) SelectBone(bone);
        }
        private void DiagramIK(int limb, Vector2 p, bool pole)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = ikMode && control == limb && poleMode == pole ? Color.white : Color.Lerp(Color.gray, pole ? new Color(1,.75f,.2f) : new Color(.3f,.65f,1),RigAlpha(true,limb));
            using (new EditorGUI.DisabledScope(!rig.HasIK))
                if (GUI.Button(new Rect(p.x-9,p.y-9,18,18),new GUIContent(pole ? "·" : "",pole ? PoleNames[limb] : LimbNames[limb]),EditorStyles.miniButton))
                { FinishSceneEdit(); PickRigControl(limb,true,pole); ikMode = true; control = limb; poleMode = pole; if (pole) rotate = false; Repaint(); viewport?.Repaint(); }
            GUI.backgroundColor = old;
        }
        private static readonly HumanBodyBones[] RingBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
            HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
            HumanBodyBones.LeftToes, HumanBodyBones.RightToes
        };
        private static readonly string[] PoleNames = { "Left elbow", "Right elbow", "Left knee", "Right knee" };

        private void SelectBone(Transform bone)
        {
            FinishSceneEdit();
            int index = Array.IndexOf(rig.Bones, bone);
            if (index < 0) return;
            PickRigControl(index);
            control = index; ikMode = false; poleMode = false; rotate = true; boneSearch = "";
            Repaint(); viewport?.Repaint();
        }

        // Match the solved limb before releasing its target, so the first FK edit cannot snap.
        // Other limbs retain their pins, including both feet when editing the torso.
        private void PrepareFKEdit(int index)
        {
            Transform bone = rig.Bones[index];
            for (int limb = 0; limb < 4; limb++)
            {
                if (!rig.Starts[limb] || pose.effectors[limb].weight <= 0 ||
                    (bone != rig.Starts[limb] && !bone.IsChildOf(rig.Starts[limb]))) continue;
                for (int i = 0; i < rig.Bones.Length; i++)
                    if (rig.Bones[i] == rig.Starts[limb] || rig.Bones[i].IsChildOf(rig.Starts[limb]))
                        pose.bones[i] = BonePose.Read(rig.Bones[i]);
                pose.effectors[limb].weight = 0;
            }
        }

        private bool RingButton(Vector3 position, float scale, Color color, bool selected, SceneView view)
        {
            float radius = HandleUtility.GetHandleSize(position) * 0.085f * controlSize * scale;
            Handles.color = selected ? new Color(1,1,1,color.a) : color;
            // Circle caps use a filled picking area, with a consistently sized screen-space radius.
            return Handles.Button(position, view.camera.transform.rotation, radius, radius,
                Handles.CircleHandleCap);
        }

        private void DrawRigPickers(SceneView view)
        {
            var oldColor = Handles.color;
            var oldDepth = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            var evt = Event.current;
            bool canPick = !playing && !evt.alt && (evt.button == 0 || evt.type == EventType.Layout || evt.type == EventType.Repaint);
            try
            {
                if (showSkeleton && showFKControls)
                {
                    foreach (var bone in rig.DisplayBones)
                    {
                        if (!bone.parent || bone == rig.Root.transform || !rig.DisplayBones.Contains(bone.parent)) continue;
                        float alpha = RigAlpha(false, BoneLimb(bone.parent));
                        if (alpha <= .001f) continue;
                        // The segment leaving a joint selects that joint, as in a DCC skeleton.
                        int id = GUIUtility.GetControlID(bone.GetInstanceID(), FocusType.Passive);
                        int boneIndex=Array.IndexOf(rig.Bones,bone.parent);
                        RegisterRigPoint(boneIndex,false,false,bone.parent.position);
                        bool selected = RigControlSelected(boneIndex);
                        if (evt.type == EventType.Layout && canPick)
                            HandleUtility.AddControl(id, Mathf.Max(0, HandleUtility.DistanceToLine(bone.parent.position, bone.position) - 5));
                        if (evt.type == EventType.Repaint)
                        {
                            Handles.color = selected ? new Color(1,1,1,alpha) : new Color(0.35f, 0.75f, 0.9f, 0.8f*alpha);
                            Handles.DrawAAPolyLine(selected ? 4 : 2, bone.parent.position, bone.position);
                        }
                        if (evt.type == EventType.MouseDown && canPick && GUIUtility.hotControl == 0 && HandleUtility.nearestControl == id)
                        { SelectBone(bone.parent); evt.Use(); }
                    }
                }
                if (showFKControls && rig.Animator)
                {
                    foreach (var humanBone in RingBones)
                    {
                        var bone = rig.Animator.GetBoneTransform(humanBone);
                        if (!bone) continue;
                        float alpha = RigAlpha(false, BoneLimb(bone));
                        if (alpha <= .001f) continue;
                        bool torso = humanBone == HumanBodyBones.Hips || humanBone == HumanBodyBones.Spine ||
                            humanBone == HumanBodyBones.Chest || humanBone == HumanBodyBones.UpperChest;
                        int boneIndex=Array.IndexOf(rig.Bones,bone);
                        RegisterRigPoint(boneIndex,false,false,bone.position);
                        bool selected = RigControlSelected(boneIndex);
                        Color color = torso ? new Color(0.25f, 1f, 0.65f) : new Color(0.45f, 0.8f, 0.9f);
                        color.a = alpha;
                        if (RingButton(bone.position, torso ? 1.5f : 0.75f, color, selected, view) && canPick)
                            SelectBone(bone);
                        if (selected && rigSelection.Count <= 1) Handles.Label(bone.position, bone.name + " · FK");
                    }
                }
                if (!rig.HasIK || !showIKControls) return;
                for (int i = 0; i < 4; i++)
                {
                    if (!rig.Ends[i]) continue;
                    float alpha = RigAlpha(true,i);
                    if (alpha <= .001f) continue;
                    var displayed = DisplayEffector(i);
                    var effector = displayed.weight > 0 ? displayed : rig.MatchEffector(i, 0);
                    Vector3 target = rig.Root.transform.TransformPoint(effector.position);
                    Color color = i % 2 == 0 ? new Color(0.2f, 0.65f, 1f) : new Color(1f, 0.4f, 0.35f);
                    color.a = alpha;
                    RegisterRigPoint(i,true,false,target);
                    if (RingButton(target, 1, color, RigControlSelected(i,true,false), view) && canPick)
                    { FinishSceneEdit(); PickRigControl(i,true,false); ikMode = true; control = i; poleMode = false; Repaint(); }
                    if (pickerActorSelected && ikMode && control == i && !poleMode) Handles.Label(target, LimbNames[i] + " · IK");
                    if (!showPoles) continue;
                    Vector3 pole = rig.Root.transform.TransformPoint(effector.pole);
                    Handles.color = new Color(1, 0.8f, 0.2f, 0.65f*alpha);
                    Handles.DrawDottedLine(rig.Middles[i].position, pole, 5);
                    RegisterRigPoint(i,true,true,pole);
                    if (RingButton(pole, 0.85f, new Color(1, 0.8f, 0.2f,alpha), RigControlSelected(i,true,true), view) && canPick)
                    { FinishSceneEdit(); PickRigControl(i,true,true); ikMode = true; control = i; poleMode = true; rotate = false; Repaint(); }
                    if (pickerActorSelected && ikMode && control == i && poleMode) Handles.Label(pole, PoleNames[i]);
                }
            }
            finally { Handles.color = oldColor; Handles.zTest = oldDepth; }
        }
    }
}

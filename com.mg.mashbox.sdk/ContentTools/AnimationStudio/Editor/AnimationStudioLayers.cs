using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private int offsetHumanBone;
        private List<PoseKey> EditingKeys => take.activeLayer>=0 && take.activeLayer<take.layers.Count ? take.layers[take.activeLayer].keys : take.keys;
        private bool WriteAuthoringKey(int[] boneMask=null,int[] effectorMask=null)
        {
            try
            {
                rig.ConstrainAuthoredPose(pose);
                if(take.singleFramePose) frame=0;
                if(take.activeLayer>=0 && take.activeLayer<take.layers.Count) take.SetLayerKey(take.activeLayer,pose,frame,boneMask,effectorMask);
                else
                {
                    if(take.layers!=null && Enumerable.Range(0,take.layers.Count).Any(take.LayerEnabled))
                        throw new InvalidOperationException("Mute correction layers before keying Base motion, or select a correction layer.");
                    if(boneMask==null) take.SetKey(pose,frame); else take.SetChannelKey(pose,frame,boneMask,effectorMask);
                }
                message=null; return true;
            }
            catch(InvalidOperationException e) { message=e.Message; unkeyedPose=true; return false; }
        }
        private void RefreshLayers()
        {
            sceneDraft=false; unkeyedPose=false; playing=false;
            pose=take.Evaluate(frame); rig.Apply(pose); EvaluateConstraints(); viewport?.Repaint(); Repaint();
        }
        private void AddSelectedToLayer(StudioAnimationLayer layer)
        {
            if(!addingLayerSelection && rigSelection.Count>1)
            {
                int old=control; bool oldIK=ikMode,oldPole=poleMode; addingLayerSelection=true;
                try { foreach(var item in rigSelection.Where(s=>s.Actor==take)) { control=item.Index; ikMode=item.IK; poleMode=item.Pole; AddSelectedToLayer(layer); } }
                finally { control=old; ikMode=oldIK; poleMode=oldPole; addingLayerSelection=false; }
                return;
            }
            if(ikMode && !IsBike)
            { layer.effectors[Mathf.Clamp(control,0,3)]|=poleMode ? 12 : 15; return; }
            int i=IsBike ? Array.IndexOf(rig.Bones,BikeJoint(Mathf.Clamp(control,0,BikeJointNames.Length-1))) : Mathf.Clamp(control,0,rig.Bones.Length-1);
            if(i<0) return;
            if(!layer.bones.Contains(rig.Paths[i])) layer.bones.Add(rig.Paths[i]);
            for(int limb=0;limb<4;limb++)
                if(rig.Starts[limb] && (rig.Bones[i]==rig.Starts[limb] || rig.Bones[i].IsChildOf(rig.Starts[limb]))) layer.effectors[limb]|=8;
            if(IsBike && control==3)
                for(int pedal=4;pedal<=5;pedal++)
                { int p=Array.IndexOf(rig.Bones,BikeJoint(pedal)); if(p>=0 && !layer.bones.Contains(rig.Paths[p])) layer.bones.Add(rig.Paths[p]); }
        }
        private void DrawLayerPicker()
        {
            take.layers=take.layers ?? new List<StudioAnimationLayer>();
            var names=new[]{"Base motion"}.Concat(take.layers.Select(l=>l.name)).ToArray();
            int choice=EditorGUILayout.Popup("Key on layer",Mathf.Clamp(take.activeLayer+1,0,names.Length-1),names)-1;
            if(choice!=take.activeLayer)
                SelectAuthoringLayer(choice);
        }
        private void SelectAuthoringLayer(int choice)
        {
            if(choice < -1 || choice >= take.layers.Count) throw new ArgumentOutOfRangeException(nameof(choice));
            if(choice == take.activeLayer) return;
            FinishSceneEdit(); Undo.RecordObject(take,"Select animation layer"); take.activeLayer=choice; Save(); RefreshLayers();
        }
        private void DrawTimelineLayerPicker()
        {
            Action draw=()=>
            {
                take.layers=take.layers ?? new List<StudioAnimationLayer>();
                var names=new[]{new GUIContent("Base motion",take.name+" · Base motion")}.Concat(take.layers.Select(l=>new GUIContent(l.name,take.name+" · "+l.name))).ToArray();
                GUILayout.Label(new GUIContent("Layer", "Key on layer for the selected actor: "+take.name),StudioTheme.Styles.miniLabel,GUILayout.Width(30));
                float width=Mathf.Clamp((viewport ? viewport.position.width : 900)-640,100,210);
                var rect=GUILayoutUtility.GetRect(names[Mathf.Clamp(take.activeLayer+1,0,names.Length-1)],EditorStyles.popup,GUILayout.Width(width),GUILayout.Height(20));
                if(EditorGUI.DropdownButton(rect,names[Mathf.Clamp(take.activeLayer+1,0,names.Length-1)],FocusType.Passive,EditorStyles.popup))
                    ShowTimelineLayerMenu(rect);
            };
            if(activeActor>=0 && activeActor<actorPreviews.Count) InActor(actorPreviews[activeActor],draw);
            else draw();
        }
        private void ShowTimelineLayerMenu(Rect rect)
        {
            var target=take;
            var selected=take.activeLayer>=0 && take.activeLayer<take.layers.Count ? take.layers[take.activeLayer] : null;
            // Menus run after the temporary actor scope has ended. Restore the
            // captured actor for every command instead of modifying the primary take.
            Action<Action> run=action=>Guard(()=>
            {
                if(take==target) action();
                else
                {
                    var actor=actorPreviews.FirstOrDefault(a=>a.Take==target);
                    if(actor!=null) InActor(actor,action);
                }
            });
            var menu=new StudioMenu();
            menu.AddItem(new GUIContent("Base motion"),take.activeLayer<0,()=>run(()=>SelectAuthoringLayer(-1)));
            for(int i=0;i<take.layers.Count;i++)
            {
                var layer=take.layers[i];
                menu.AddItem(new GUIContent(layer.name.Replace('/', '∕')),take.activeLayer==i,()=>run(()=>SelectAuthoringLayer(take.layers.IndexOf(layer))));
            }
            menu.AddSeparator("");
            if(!take.singleFramePose)
            {
                menu.AddItem(new GUIContent("New layer…"),false,()=>run(()=>CreateTimelineLayer(false)));
                menu.AddItem(new GUIContent("New layer from selection…"),false,()=>run(()=>CreateTimelineLayer(true)));
            }
            else menu.AddDisabledItem(new GUIContent("New layer (disable Single Frame Pose)"));
            menu.AddItem(new GUIContent("Edit layers…"),false,()=>run(OpenLayerSettings));
            if(selected!=null && !take.singleFramePose)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Add selection to layer"),false,()=>run(()=>ChangeLayerSelection(selected,false)));
                menu.AddItem(new GUIContent("Remove selection from layer"),false,()=>run(()=>ChangeLayerSelection(selected,true)));
                menu.AddItem(new GUIContent("Mute layer"),selected.muted,()=>run(()=>
                { FinishSceneEdit(); Undo.RecordObject(take,"Mute animation layer"); selected.muted=!selected.muted; Save(); RefreshLayers(); }));
                menu.AddItem(new GUIContent("Solo layer"),selected.solo,()=>run(()=>
                { FinishSceneEdit(); Undo.RecordObject(take,"Solo animation layer"); selected.solo=!selected.solo; Save(); RefreshLayers(); }));
            }
            menu.DropDown(rect);
        }
        private void OpenLayerSettings()
        {
            activeActor=actorPreviews.FindIndex(a=>a.Take==take);
            inspectorTab=5; inspectorExpanded=true;
            viewport?.SetInspectorExpanded(true); viewport?.Repaint();
        }
        private void CreateTimelineLayer(bool fromSelection)
        {
            FinishSceneEdit(); Undo.RecordObject(take,"Create animation layer");
            var layer=new StudioAnimationLayer { name="Correction "+(take.layers.Count+1) };
            if(fromSelection) AddSelectedToLayer(layer);
            take.layers.Add(layer); take.activeLayer=take.layers.Count-1;
            Save(); RefreshLayers(); OpenLayerSettings();
        }
        private void ChangeLayerSelection(StudioAnimationLayer layer,bool remove)
        {
            if(!take.layers.Contains(layer)) return;
            FinishSceneEdit(); Undo.RecordObject(take,remove ? "Remove layer selection" : "Add layer selection");
            if(!remove) AddSelectedToLayer(layer);
            else
            {
                var mask=new StudioAnimationLayer(); AddSelectedToLayer(mask);
                layer.bones.RemoveAll(path=>mask.bones.Contains(path));
                layer.offsets.RemoveAll(offset=>mask.bones.Contains(offset.path));
                for(int limb=0;limb<4;limb++)
                {
                    layer.effectors[limb]&=~mask.effectors[limb];
                    // Other FK bones on this limb still need the blend channel.
                    if(rig.Starts[limb] && layer.bones.Any(path=>
                    { int index=Array.IndexOf(rig.Paths,path); return index>=0 && (rig.Bones[index]==rig.Starts[limb] || rig.Bones[index].IsChildOf(rig.Starts[limb])); }))
                        layer.effectors[limb]|=8;
                }
            }
            Save(); RefreshLayers();
        }
        private void DrawLayers()
        {
            if(take.singleFramePose) { EditorGUILayout.HelpBox("Single Frame Pose edits the base pose. Disable it under Clip to add motion layers.",MessageType.Info); return; }
            EditorGUILayout.LabelField(take.name+" · ANIMATION LAYERS",StudioTheme.Styles.boldLabel);
            DrawLayerPicker();
            if(GUILayout.Button("Add correction layer + selected control"))
            {
                FinishSceneEdit(); Undo.RecordObject(take,"Add animation layer");
                var layer=new StudioAnimationLayer { name="Correction "+(take.layers.Count+1) };
                AddSelectedToLayer(layer); take.layers.Add(layer); take.activeLayer=take.layers.Count-1; Save(); RefreshLayers();
            }
            if(take.activeLayer<0 || take.activeLayer>=take.layers.Count)
            { EditorGUILayout.HelpBox("Base motion contains the imported keys. Add a layer to make corrections without replacing them.",MessageType.Info); return; }
            var selected=take.layers[take.activeLayer];
            if(selected.limitRange) EditorGUILayout.LabelField("Active frames",selected.firstFrame+"–"+selected.lastFrame);
            EditorGUI.BeginChangeCheck();
            string name=EditorGUILayout.TextField("Layer name",selected.name);
            StudioLayerMode mode=(StudioLayerMode)EditorGUILayout.EnumPopup("Blend mode",selected.mode);
            float weight=EditorGUILayout.Slider("Layer weight",selected.weight,0,1);
            bool mute=EditorGUILayout.Toggle("Mute",selected.muted),solo=EditorGUILayout.Toggle("Solo",selected.solo);
            if(EditorGUI.EndChangeCheck())
            { Undo.RecordObject(take,"Edit animation layer"); take.ConvertLayerMode(take.activeLayer,mode); selected.name=name; selected.weight=weight; selected.muted=mute; selected.solo=solo; Save(); RefreshLayers(); }
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Move earlier") && take.activeLayer>0)
                { Undo.RecordObject(take,"Reorder animation layer"); int i=take.activeLayer; take.layers.RemoveAt(i); take.layers.Insert(i-1,selected); take.activeLayer--; Save(); RefreshLayers(); }
                if(GUILayout.Button("Move later") && take.activeLayer<take.layers.Count-1)
                { Undo.RecordObject(take,"Reorder animation layer"); int i=take.activeLayer; take.layers.RemoveAt(i); take.layers.Insert(i+1,selected); take.activeLayer++; Save(); RefreshLayers(); }
            }
            if(GUILayout.Button("Add selected bone / IK control to layer"))
                ChangeLayerSelection(selected,false);
            if(GUILayout.Button("Remove selected bone / IK control from layer"))
                ChangeLayerSelection(selected,true);
            EditorGUILayout.LabelField("Affected bones",StudioTheme.Styles.boldLabel);
            for(int i=0;i<selected.bones.Count;i++)
                using(new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(selected.bones[i].Length==0 ? "Actor root" : selected.bones[i],StudioTheme.Styles.wordWrappedMiniLabel);
                    if(GUILayout.Button("×",GUILayout.Width(24)))
                    { Undo.RecordObject(take,"Remove layer bone"); selected.bones.RemoveAt(i); Save(); RefreshLayers(); break; }
                }
            for(int i=0;i<4;i++)
                if(selected.effectors[i]!=0)
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(LimbNames[i]+(selected.effectors[i]==8 ? " · IK/FK weight" : " · IK control"));
                        if(GUILayout.Button("×",GUILayout.Width(24))) { Undo.RecordObject(take,"Remove layer IK"); selected.effectors[i]=0; Save(); RefreshLayers(); }
                    }
            EditorGUILayout.LabelField(selected.keys.Count+" layer keys · S / Shift+W / Shift+E");
            if(GUILayout.Button("Delete layer key at frame "+frame))
            { Undo.RecordObject(take,"Delete layer key"); selected.keys.RemoveAll(k=>k.frame==frame); Save(); RefreshLayers(); }
            if(rig.Animator) DrawHumanOffsets(selected);
            if(GUILayout.Button("Delete selected layer"))
            { Undo.RecordObject(take,"Delete animation layer"); take.layers.RemoveAt(take.activeLayer); take.activeLayer=Mathf.Min(take.activeLayer,take.layers.Count-1); Save(); RefreshLayers(); }
            EditorGUILayout.HelpBox("Layers evaluate from earlier to later. Key the latest enabled layer, or mute layers above the one you edit. Changing Additive/Override resamples the layer at the take frame rate. Key changes before switching layers; whole-clip offsets are saved settings, independent of Auto Key. IK pins and attachments evaluate afterward.",MessageType.None);
        }
        private void DrawHumanOffsets(StudioAnimationLayer layer)
        {
            EditorGUILayout.Space(6); EditorGUILayout.LabelField("HUMAN · WHOLE-CLIP ROTATION OFFSETS",StudioTheme.Styles.boldLabel);
            var mapped=Enumerable.Range(0,(int)HumanBodyBones.LastBone).Select(i=>(HumanBodyBones)i).Where(b=>rig.Animator.GetBoneTransform(b)).ToArray();
            offsetHumanBone=EditorGUILayout.Popup("Human joint",Mathf.Clamp(offsetHumanBone,0,mapped.Length-1),mapped.Select(b=>b.ToString()).ToArray());
            if(GUILayout.Button("Use selected joint"))
            {
                var selected=ikMode ? rig.Middles[Mathf.Clamp(control,0,3)] : rig.Bones[Mathf.Clamp(control,0,rig.Bones.Length-1)];
                int found=Array.FindIndex(mapped,b=>rig.Animator.GetBoneTransform(b)==selected); if(found>=0) offsetHumanBone=found;
            }
            var bone=rig.Animator.GetBoneTransform(mapped[offsetHumanBone]); int index=Array.IndexOf(rig.Bones,bone); string path=rig.Paths[index];
            var offset=layer.offsets.FirstOrDefault(o=>o.path==path); var angles=offset?.angles ?? Vector3.zero;
            string jointName=mapped[offsetHumanBone].ToString();
            bool limb=jointName.Contains("Arm") || jointName.Contains("Shoulder") || jointName.Contains("Leg");
            EditorGUI.BeginChangeCheck();
            angles.x=EditorGUILayout.Slider(limb ? "Lift / lower" : "Bend forward / back",angles.x,-90,90);
            angles.y=EditorGUILayout.Slider(limb ? "Forward / back" : "Side bend",angles.y,-90,90);
            angles.z=EditorGUILayout.Slider("Twist",angles.z,-90,90);
            bool changed=EditorGUI.EndChangeCheck();
            if(GUILayout.Button("Reset this joint offset")) { angles=Vector3.zero; changed=true; }
            if(!changed) return;
            Undo.RecordObject(take,"Adjust human offset");
            if(offset==null)
            {
                var inverse=Quaternion.Inverse(RestWorldRotation(bone)); var rootRotation=rig.Rest.bones[0].rotation;
                Vector3 right=rootRotation*Vector3.right, up=rootRotation*Vector3.up, forward=rootRotation*Vector3.forward;
                Vector3 direction=jointName.StartsWith("Left",StringComparison.Ordinal) ? -right : right;
                offset=new StudioJointOffset { path=path,axisX=inverse*(limb ? Vector3.Cross(direction,up) : right),
                    axisY=inverse*(limb ? Vector3.Cross(direction,forward) : forward),axisZ=inverse*(limb ? direction : up) };
                layer.offsets.Add(offset);
            }
            offset.angles=angles; if(!layer.bones.Contains(path)) layer.bones.Add(path);
            Save(); RefreshLayers();
        }
    }
}

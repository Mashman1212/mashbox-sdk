using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private bool inspectorExpanded;
        [SerializeField] private bool compactSettingsInitialized;
        [SerializeField] private bool displayOptions;
        [SerializeField] private int inspectorTab;
        private Vector2 inspectorScroll;
        private bool selectionTransformsDrawn;
        private bool selectedTransformEditing;
        private static Vector3 SelectedTransformVector(string label, Vector3 value)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(65));
                for (int axis = 0; axis < 3; axis++)
                {
                    GUILayout.Label(axis == 0 ? "X" : axis == 1 ? "Y" : "Z", GUILayout.Width(12));
                    EditorGUI.BeginChangeCheck();
                    float next = EditorGUILayout.FloatField(Mathf.Abs(value[axis]) < .000001f ? 0 : value[axis], GUILayout.MinWidth(42));
                    if (EditorGUI.EndChangeCheck()) value[axis] = next;
                }
            }
            return value;
        }
        private void DrawViewportSelectedTransform()
        {
            if (!take || rig == null || pose == null) return;
            if (quickCorrectionMode != QuickCorrectionMode.None) return;
            var actor = activeActor >= 0 && activeActor < actorPreviews.Count ? actorPreviews[activeActor] : null;
            if (rigSelectionExplicit && rigSelection.Count == 0)
            {
                pendingRigChannels.Clear();
                System.Action drawChannels = () =>
                {
                    if (IsBike) DrawCompactBikeBalance();
                    else DrawViewportRigChannels();
                };
                if (actor == null) drawChannels();
                else InActor(actor, drawChannels);
                ApplyPendingRigChannels();
                return;
            }
            bool drewRigChannels = false;
            System.Action edit = null;
            System.Action draw = () =>
            {
                using (new GUILayout.VerticalScope(StudioTheme.Styles.helpBox))
                {
                    string name = IsBike ? BikeControlNames[Mathf.Clamp(control, 0, BikeControlNames.Length - 1)] :
                        ikMode ? (poleMode ? PoleNames : LimbNames)[Mathf.Clamp(control, 0, 3)] : rig.Bones[Mathf.Clamp(control, 0, rig.Bones.Length - 1)].name;
                    GUILayout.Label(name, StudioTheme.Styles.boldLabel);
                    using (new EditorGUI.DisabledScope(playing))
                    {
                        if (IsBike)
                        {
                            int selected = control; var joint = BikeJoint(selected); if (!joint) return;
                            EditorGUI.BeginChangeCheck();
                            Vector3 p = joint.localPosition;
                            if (selected == 0) p = SelectedTransformVector("Position", p);
                            var r = selected == 0 ? pose.bones[System.Array.IndexOf(rig.Bones,joint)].DisplayRotation : joint.localEulerAngles; float angle = 0;
                            if (selected == 0) r = SelectedTransformVector("Rotation", r);
                            else if (selected > 0 && selected < 8)
                                angle = EditorGUILayout.FloatField(BikeAxis(selected) == Vector3.up ? "Rotation Y" : "Rotation X", BikeAngle(selected));
                            if (EditorGUI.EndChangeCheck()) edit = () =>
                            {
                                if (selected == 0) { joint.localPosition = p; joint.localRotation = Quaternion.Euler(r); }
                                else if (selected < 8) SetBikeAngle(selected, angle);
                                if (selected == 0) { pose = rig.Capture(); var rootPose=pose.bones[System.Array.IndexOf(rig.Bones,joint)]; rootPose.SetRotationAngles(r,Quaternion.identity); pose.bones[System.Array.IndexOf(rig.Bones,joint)]=rootPose; }
                                else CaptureBikeRotation(selected);
                            };
                        }
                        else if (ikMode)
                        {
                            int selected = Mathf.Clamp(control, 0, 3); bool pole = poleMode;
                            var e = pose.effectors[selected];
                            if (rig.HasIK)
                            {
                                EditorGUI.BeginChangeCheck();
                                var p = SelectedTransformVector("Position", pole ? e.pole : e.position);
                                Vector3 r = e.DisplayRotation;
                                if (!pole) r = SelectedTransformVector("Rotation", r);
                                if (EditorGUI.EndChangeCheck()) edit = () =>
                                {
                                    if (pole) { e.pole = p; if (e.pinPole) e.pinnedPole = rig.Root.transform.TransformPoint(p); }
                                    else
                                    {
                                        e.position = p; e.SetRotationAngles(r);
                                        if (e.pinPosition) e.pinnedPosition = rig.Root.transform.TransformPoint(p);
                                        if (e.pinRotation) e.pinnedRotation = rig.Root.transform.rotation * e.rotation;
                                    }
                                    e.weight = 1; pose.effectors[selected] = e;
                                };
                            }
                        }
                        else
                        {
                            int selected = Mathf.Clamp(control, 0, rig.Bones.Length - 1);
                            var bone = rig.Bones[selected]; int kind = BodyIKKind(selected);
                            var original = BonePose.Read(bone);
                            EditorGUI.BeginChangeCheck(); Vector3 p = original.position;
                            if (CanTranslateBone(selected)) p = SelectedTransformVector("Position", p);
                            var r = SelectedTransformVector("Rotation", pose.bones[selected].DisplayRotation);
                            if (EditorGUI.EndChangeCheck()) edit = () =>
                            {
                                if (kind >= 0)
                                {
                                    if (p != original.position) rig.PoseBodyControl(pose, kind, bone.parent ? bone.parent.TransformPoint(p) : p, bone.rotation, false);
                                    if (r != pose.bones[selected].DisplayRotation)
                                        rig.PoseBodyControl(pose, kind, bone.position, (bone.parent ? bone.parent.rotation : Quaternion.identity) * Quaternion.Euler(r), true);
                                    var keyed=pose.bones[selected]; keyed.SetRotationAngles(r,Quaternion.identity); pose.bones[selected]=keyed;
                                }
                                else { PrepareFKEdit(selected); original.position = p; original.SetRotationAngles(r,Quaternion.identity); pose.bones[selected] = original; }
                            };
                        }
                    }
                }
                if (IsBike)
                {
                    pendingRigChannels.Clear();
                    drewRigChannels = true;
                    DrawCompactBikeBalance();
                }
                else
                {
                    pendingRigChannels.Clear();
                    drewRigChannels = true;
                    DrawViewportRigChannels();
                }
            };
            float labelWidth = EditorGUIUtility.labelWidth;
            try
            {
                EditorGUIUtility.labelWidth = 100;
                if (actor == null) draw(); else InActor(actor, draw);
                if (edit != null)
                {
                    if (!selectedTransformEditing)
                    {
                        FinishSceneEdit(); FinishActorEdits();
                        if (!autoKey) CaptureUnkeyedPoseUndo();
                        selectedTransformEditing = true;
                    }
                    System.Action apply = () => { edit(); PreviewSceneEdit("Edit control transform"); };
                    if (actor == null) Guard(apply); else InActor(actor, () => Guard(apply));
                }
                if (selectedTransformEditing &&
                    (Event.current.rawType == EventType.MouseUp || GUIUtility.hotControl == 0))
                {
                    if (actor == null) FinishSceneEdit(); else InActor(actor, FinishSceneEdit);
                    if (!autoKey) RecordUnkeyedPoseUndo();
                    selectedTransformEditing = false;
                }
                if (drewRigChannels) ApplyPendingRigChannels();
            }
            finally { EditorGUIUtility.labelWidth = labelWidth; }
        }
        [SerializeField] private bool workspaceExpanded;
        private static readonly string[] InspectorTabs = { "Session", "Selection", "Outfit", "Clip", "Actors", "Layers" };
        private static readonly string[] PrivateInspectorTabs = { "Session", "Selection", "Outfit", "Clip", "Actors", "Layers", "Generate" };

        private void DrawSelectionTransform()
        {
            if(IsBike) return; // Bike's axis-specific channels lead DrawBikeControls.
            EditorGUILayout.LabelField(take.name+" · "+(ikMode ? (poleMode ? PoleNames : LimbNames)[Mathf.Clamp(control,0,3)] : rig.Bones[Mathf.Clamp(control,0,rig.Bones.Length-1)].name),StudioTheme.Styles.boldLabel);
            EditorGUI.BeginChangeCheck();
            if(ikMode)
            {
                control=Mathf.Clamp(control,0,3); var e=pose.effectors[control];
                var p=EditorGUILayout.Vector3Field(poleMode ? "Pole position" : "Position",poleMode ? e.pole : e.position);
                var q=EditorGUILayout.Vector3Field("Rotation",e.DisplayRotation);
                if(EditorGUI.EndChangeCheck()) { if(poleMode) e.pole=p; else e.position=p; e.SetRotationAngles(q); e.weight=1; pose.effectors[control]=e; Commit("Edit selected IK transform"); }
            }
            else
            {
                control=Mathf.Clamp(control,0,rig.Bones.Length-1); var b=BonePose.Read(rig.Bones[control]);
                using(new EditorGUI.DisabledScope(!CanTranslateBone(control)))
                    b.position=EditorGUILayout.Vector3Field("Position",b.position);
                var angles=EditorGUILayout.Vector3Field("Rotation",pose.bones[control].DisplayRotation);
                if(EditorGUI.EndChangeCheck()) { b.SetRotationAngles(angles,Quaternion.identity); PrepareFKEdit(control); pose.bones[control]=b; Commit("Edit selected FK transform"); }
            }
        }
        private void DrawViewportInspector()
        {
            if (!this) return;
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            bool previousWide = EditorGUIUtility.wideMode;
            EditorGUIUtility.labelWidth = 135;
            EditorGUIUtility.wideMode = false;
            EditorGUI.BeginChangeCheck();
            try
            {
                using (new GUILayout.VerticalScope(StudioTheme.Styles.inspectorDefaultMargins))
                {
                    if (TryDrawFocusedCorrectionPanel()) return;
                    using (new GUILayout.HorizontalScope())
                    {
                        bool expanded = GUILayout.Toggle(inspectorExpanded, new GUIContent("Animation settings " + (inspectorExpanded ? "▴" : "▾"), EditorGUIUtility.IconContent("SettingsIcon").image, "Expand animation settings"), StudioTheme.Styles.overlayButton, GUILayout.Height(24));
                        if (expanded != inspectorExpanded)
                        { inspectorExpanded = expanded; viewport.SetInspectorExpanded(expanded); }
                    }
                    if (!inspectorExpanded) return;
                    DrawActorControlVisibility();
                    var tabs = MotionGenerationAvailable ? PrivateInspectorTabs : InspectorTabs;
                    if(rig==null || !take) inspectorTab=0;
                    inspectorTab = GUILayout.SelectionGrid(Mathf.Clamp(inspectorTab,0,tabs.Length-1),tabs,4,StudioTheme.Styles.miniButton);
                    inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
                    string tab=tabs[inspectorTab];
                    if(tab=="Session") DrawWorkspace(false);
                    else if(rig!=null && pose!=null && take)
                    {
                        EnsureActors();
                        if(tab=="Selection")
                        {
                            System.Action draw=()=>
                            {
                                DrawQuickCorrections();
                                if (quickCorrectionMode != QuickCorrectionMode.None) return;
                                DrawSelectionTransform();
                                DrawLayerPicker();
                                selectionTransformsDrawn=true;
                                try { DrawControls(); } finally { selectionTransformsDrawn=false; }
                                if(GUILayout.Button("Key pose [S]")) Commit("Key pose",true);
                                EditorGUILayout.LabelField(unkeyedPose ? "Unkeyed changes" : autoKey ? "Auto Key ON" : "Auto Key OFF",StudioTheme.Styles.miniLabel);
                            };
                            if(activeActor>=0) InActor(actorPreviews[activeActor],draw); else draw();
                        }
                        else if(tab=="Actors") DrawActors();
                        else if(tab=="Layers")
                        { if(activeActor>=0) InActor(actorPreviews[activeActor],DrawLayers); else DrawLayers(); }
                        else if(tab=="Outfit")
                        {
                            DrawDefaultCharacter();
                            if(!OutfitRig.Animator) DrawVehicleOutfit();
                            else if(activeActor<0)
                            {
                                body=(GameObject)EditorGUILayout.ObjectField("Preview body / bust",body,typeof(GameObject),false);
                                DrawClothing(); DrawActorPresets();
                            }
                            else EditorGUILayout.HelpBox("This character uses the outfit stored in its actor preset. Save a dressed primary character as a preset, then add it from Actors.",MessageType.Info);
                        }
                        else if(tab=="Generate") DrawGeneration();
                        else { DrawTimeline(); DrawClipTools(); }
                    }
                    if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Error);
                    EditorGUILayout.EndScrollView();
                }
                HandleKeys(Event.current);
            }
            finally
            {
                if (EditorGUI.EndChangeCheck()) SaveAnimationConfiguration();
                EditorGUIUtility.labelWidth = previousLabelWidth;
                EditorGUIUtility.wideMode = previousWide;
            }
        }
    }
}

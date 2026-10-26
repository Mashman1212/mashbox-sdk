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
                var q=EditorGUILayout.Vector3Field("Rotation",e.rotation.eulerAngles);
                if(EditorGUI.EndChangeCheck()) { if(poleMode) e.pole=p; else e.position=p; e.rotation=Quaternion.Euler(q); e.weight=1; pose.effectors[control]=e; Commit("Edit selected IK transform"); }
            }
            else
            {
                control=Mathf.Clamp(control,0,rig.Bones.Length-1); var b=BonePose.Read(rig.Bones[control]);
                using(new EditorGUI.DisabledScope(!CanTranslateBone(control)))
                    b.position=EditorGUILayout.Vector3Field("Position",b.position);
                b.rotation=Quaternion.Euler(EditorGUILayout.Vector3Field("Rotation",b.rotation.eulerAngles));
                if(EditorGUI.EndChangeCheck()) { PrepareFKEdit(control); pose.bones[control]=b; Commit("Edit selected FK transform"); }
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

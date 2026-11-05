using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private GUIContent RigIcon(string name, string tooltip) => new GUIContent(EditorGUIUtility.IconContent(name).image, tooltip);

        private void DrawRigOrientationDropdown(Rect rect)
        {
            if (!TimelineButton(rect, "✣   " + (localHandleSpace ? "Local" : "Global") + "          ▾", "Transform handle orientation")) return;
            var menu = new StudioMenu();
            menu.AddItem(new GUIContent("Local"), localHandleSpace, () => SetHandleSpace(true));
            menu.AddItem(new GUIContent("Global"), !localHandleSpace, () => SetHandleSpace(false));
            menu.DropDown(rect);
        }
        private bool RigToolbarIcon(Rect rect, Texture icon, string fallback, string tooltip, bool selected = false)
        {
            bool clicked = TimelineButton(rect, "", tooltip, selected);
            if (Event.current.type == EventType.Repaint)
            {
                var previous = GUI.color;
                // Keep active symbols white against the gold toggle background.
                GUI.color = selected ? Color.white : TimelineText;
                if (fallback == "Wheel")
                {
                    var old=Handles.color;
                    Handles.BeginGUI(); Handles.color=GUI.color;
                    var center=new Vector3(rect.center.x,rect.center.y,0);
                    Handles.DrawWireDisc(center,Vector3.forward,6);
                    Handles.DrawSolidDisc(center,Vector3.forward,1.5f);
                    for(int spoke=0;spoke<6;spoke++)
                    {
                        float angle=spoke*Mathf.PI/3;
                        Handles.DrawAAPolyLine(1.5f,center,
                            center+new Vector3(Mathf.Cos(angle)*5,Mathf.Sin(angle)*5,0));
                    }
                    Handles.color=old; Handles.EndGUI();
                }
                else if (icon) GUI.DrawTexture(new Rect(rect.x+7,rect.y+5,rect.width-14,rect.height-10),icon,ScaleMode.ScaleToFit,true);
                else
                {
                    var style = new GUIStyle(TimelineStyle()) { fontSize = 14, fontStyle = FontStyle.Bold };
                    style.normal.textColor = GUI.color;
                    GUI.Label(rect,fallback,style);
                }
                GUI.color = previous;
            }
            return clicked;
        }
        private void DrawCompactRigToolbar()
        {
            var bar = GUILayoutUtility.GetRect(1,34,GUILayout.ExpandWidth(true));
            TimelineRect(bar,TimelineToolbarBackground);
            TimelineRect(new Rect(bar.x,bar.yMax-1,bar.width,1),TimelineBorder);
            float x=bar.x+12, y=bar.y+4;
            DrawRigOrientationDropdown(new Rect(x,y,126,25)); x+=137;
            TimelineDivider(x-6,y); x+=4;
            bool changed=false;
            var selectedRig=OutfitRig;
            string ikTip = selectedRig != null && !selectedRig.HasIK ? selectedRig.SolverStatus : "Show inverse-kinematics controls";
            if (TimelineButton(new Rect(x,y,36,25),"IK",ikTip,showIKControls,showIKControls))
            {
                if (selectedRig != null && !selectedRig.HasIK) ShowNotification(new GUIContent(selectedRig.SolverStatus), 5);
                else { showIKControls=!showIKControls; changed=true; }
            } x+=36;
            if (TimelineButton(new Rect(x,y,36,25),"FK","Show forward-kinematics controls",showFKControls,showFKControls))
            { showFKControls=!showFKControls; changed=true; } x+=46;
            if (RigToolbarIcon(new Rect(x,y,30,25),null,"Wheel","Show vehicle rig controls",showVehicleControls))
            { showVehicleControls=!showVehicleControls; changed=true; } x+=34;
            Texture2D bonesIcon;
            if (showSkeleton) StudioSymbolIcons.TryGetWhite("Animation/Rig Bones",out bonesIcon);
            else StudioSymbolIcons.TryGet("Animation/Rig Bones",out bonesIcon);
            if (RigToolbarIcon(new Rect(x,y,30,25),bonesIcon,"⌁","Show full rig bones; FK guides remain visible with FK on",showSkeleton))
            { showSkeleton=!showSkeleton; changed=true; } x+=39;
            TimelineDivider(x-5,y); x+=4;
            using (new EditorGUI.DisabledScope(selectedRig==null || !selectedRig.SupportsHumanLimits))
            {
                bool limits=selectedRig!=null && selectedRig.HumanLimitsEnabled;
                if (TimelineButton(new Rect(x,y,94,25),"Human Limits",
                    "Constrain this humanoid to avatar joint ranges. Session only.",limits,limits) && selectedRig!=null)
                {
                    Guard(() =>
                    {
                        FinishSceneEdit(); FinishActorEdits();
                        selectedRig.HumanLimitsEnabled=!limits;
                        System.Action apply=()=>rig.Apply(pose);
                        if(activeActor>=0 && activeActor<actorPreviews.Count) InActor(actorPreviews[activeActor],apply);
                        else apply();
                        EvaluateConstraints(); viewport?.Repaint(); Repaint();
                    });
                }
            }
            x+=103;
            if (TimelineButton(new Rect(x,y,30,25),"▥","Fade FK controls by blend",fadeRigControls,fadeRigControls))
            { fadeRigControls=!fadeRigControls; if(!fadeRigControls) hideInactiveControls=false; viewport?.Repaint(); } x+=34;
            if (TimelineButton(new Rect(x,y,30,25),"◉","Hide inactive IK/FK controls",hideInactiveControls,hideInactiveControls))
            { hideInactiveControls=!hideInactiveControls; if(hideInactiveControls) fadeRigControls=true; viewport?.Repaint(); } x+=34;
            using (new EditorGUI.DisabledScope(!OutfitActor || OutfitRig==null))
                if (RigToolbarIcon(new Rect(x,y,30,25),EditorGUIUtility.IconContent("Refresh").image,"↻","Reset selected actor to rest pose")) ResetRestPose();
            x+=34;
            using (new EditorGUI.DisabledScope(!take))
                if (RigToolbarIcon(new Rect(x,y,30,25),null,"▲","Single Frame Pose",take.singleFramePose))
                    Guard(()=>SetSingleFramePoseMode(!take.singleFramePose));
            x+=39;
            TimelineDivider(x-5,y); x+=4;
            if (RigToolbarIcon(new Rect(x,y,30,25),EditorGUIUtility.IconContent("SettingsIcon").image,"⚙","Studio appearance and preferences"))
                StudioSettingsWindow.Open();
            if(changed) { SaveAnimationConfiguration(); viewport?.Repaint(); }
        }

    }
}

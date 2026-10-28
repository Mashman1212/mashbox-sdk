using System;
using UnityEditor;
using UnityEngine;
using Lesson=MashBoxSDK.AnimationStudio.StudioRetopologyLessons.Lesson;
using Step=MashBoxSDK.AnimationStudio.StudioRetopologyLessons.Step;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        // Toolbar and tutorial use the same glyph IDs and the same vector renderer.
        private static int ToolIconIndex(RetopoTool tool)=>tool==RetopoTool.Polygon?21:tool==RetopoTool.Strip?14:
            tool==RetopoTool.RelaxBrush?13:tool==RetopoTool.Resolution?11:
            tool==RetopoTool.Tube?7:tool==RetopoTool.TargetWeld?15:
            tool==RetopoTool.EdgeBridge?16:(int)tool;

        private void DrawTutorialControl(int icon,bool active,string label,Action action,bool toggle=false)
        {
            using(new GUILayout.HorizontalScope())
            {
                bool clicked=IconButton(icon,active,label+" — same icon as the viewport toolbar");
                string caption=toggle?label+(active?"  ·  On":"  ·  Off"):
                    active?label+"  ·  Active":"Activate "+label;
                clicked|=GUILayout.Button(caption,GUILayout.Height(24),GUILayout.ExpandWidth(true));
                if(clicked){action();ObserveTutorialSettings();repaint();}
            }
        }

        private void DrawTutorialControls(Lesson lesson,Step step)
        {
            if(!string.IsNullOrEmpty(step.tool))
            {
                string name=step.tool;int icon;bool active;
                if(name=="Vertices"||name=="Edges"||name=="Faces")
                {
                    var mode=(SelectionMode)Enum.Parse(typeof(SelectionMode),name);
                    icon=8+(int)mode;active=activeTool==RetopoTool.Select&&selectionMode==mode;
                }
                else if(Enum.TryParse(name,out RetopoTool tool))
                {icon=ToolIconIndex(tool);active=activeTool==tool;}
                else return;
                DrawTutorialControl(icon,active,ToolName(name),()=>{if(!active)ActivateTutorialTool(name);});
            }
            if(step.check=="event:referenceTexture")
                DrawTutorialControl(19,referenceTextured,"Reference Textured",()=>ToggleTextured(true),true);
            if(step.check=="event:workingTexture")
                DrawTutorialControl(20,workingTextured,"Working Mesh Textured",()=>ToggleTextured(false),true);
            if(step.check=="event:referenceChecker")
                DrawTutorialControl(17,referenceChecker,"Reference Checker",()=>ToggleChecker(true),true);
            if(step.check=="event:workingChecker")
                DrawTutorialControl(18,workingChecker,"Working Mesh Checker",()=>ToggleChecker(false),true);
            if(step.check=="referenceWire")
                DrawTutorialControl(5,referenceWire,"Reference Wire",()=>
                {referenceWire=!referenceWire;EditorPrefs.SetBool("MashBox.Retopology.ReferenceWire",referenceWire);},true);
            if(step.check=="xray")
                DrawTutorialControl(6,workingXray,"X-ray Display",ToggleWorkingXray,true);
            if(step.check=="symmetry"||(lesson.id=="symmetry"&&step.id=="mirror"))
                DrawTutorialControl(4,Document.symmetry,"Mirror Drawing X",()=>Document.symmetry=!Document.symmetry,true);
            // This control is a checkbox in Tool settings, so show that same form here.
            if(step.check=="event:continuePatch")
            {
                bool enabled=GUILayout.Toggle(continuousPatch,"Continuous patch",GUILayout.Height(24));
                if(enabled!=continuousPatch)ToggleContinuousPatch();
            }
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private int ToolbarIntSlider(string label,int value,int min,int max)
        {
            GUILayout.Label(label,StudioTheme.Styles.miniLabel,GUILayout.Width(42),GUILayout.Height(24));
            var rect=GUILayoutUtility.GetRect(174,24,GUILayout.Width(174),GUILayout.Height(24));
            rect.y+=4;rect.height=18;
            return EditorGUI.IntSlider(rect,value,min,max);
        }
        private void DrawDensityToolbar()
        {
            FinishDensityUndo();
            if(activeTool==RetopoTool.Select)
            {
                DrawBridgeBands();return;
            }
            if(activeTool!=RetopoTool.Patch&&activeTool!=RetopoTool.Tube)return;
            GUILayout.Space(4);
            using(new GUILayout.HorizontalScope())
            using(new EditorGUI.DisabledScope(Active==null||!Active.reference))
            {
                float oldSize=patchQuadSize;int oldSegments=patchSegments;bool oldAuto=patchAutoDensity;
                EditorGUI.BeginChangeCheck();
                if(activeTool==RetopoTool.Patch)
                {
                    if(patchAutoDensity&&Active!=null&&Active.reference)
                    {
                        // Relative edge density stays useful on models imported at different scales.
                        float span=Mathf.Max(.0001f,Active.reference.bounds.size.magnitude);
                        float density=span/Mathf.Max(.0001f,PatchQuadSize);
                        GUILayout.Label(new GUIContent("Density","Drag right for smaller, denser quads. Shared borders retain their vertex counts."),StudioTheme.Styles.miniLabel,GUILayout.Width(48),GUILayout.Height(24));
                        var rect=GUILayoutUtility.GetRect(174,24,GUILayout.Width(174),GUILayout.Height(24));rect.y+=4;rect.height=18;
                        EditorGUI.BeginChangeCheck();
                        float next=EditorGUI.Slider(rect,density,4,192);
                        if(EditorGUI.EndChangeCheck())patchQuadSize=Mathf.Max(.0001f,span/next);
                    }
                    else
                    {
                        patchSegments=ToolbarIntSlider("Divisions",patchSegments,1,24);
                    }
                    GUILayout.Space(10);
                    patchAutoDensity=GUILayout.Toggle(patchAutoDensity,new GUIContent("Auto size","Adapt rows and columns to patch size; shared borders stay welded."),GUILayout.Width(78),GUILayout.Height(24));
                    bool nextContinuous=GUILayout.Toggle(continuousPatch,new GUIContent("Continuous","Continue with two new corners after the first patch. Escape ends the chain."),GUILayout.Width(96),GUILayout.Height(24));
                    if(nextContinuous!=continuousPatch)ToggleContinuousPatch();
                }
                else
                {
                    tubeSides=ToolbarIntSlider("Sides",tubeSides,4,64);
                    GUILayout.Space(12);
                    tubeDivisions=ToolbarIntSlider("Rows",tubeDivisions,1,64);
                }
                if(EditorGUI.EndChangeCheck())
                {
                    if(activeTool==RetopoTool.Tube&&corners.Count==2)Run(UpdateTubePreview);
                    if(activeTool==RetopoTool.Patch&&(oldAuto!=patchAutoDensity||oldSegments!=patchSegments||!Mathf.Approximately(oldSize,patchQuadSize)))
                        Run(UpdateLastPatchDensity);
                    ObserveTutorialSettings();repaint();
                }
                if(activeTool==RetopoTool.Tube)
                {
                    GUILayout.Space(10);
                    using(new EditorGUI.DisabledScope(tubePreview==null||corners.Count!=2))
                        if(GUILayout.Button(new GUIContent("Create Tube","Commit the preview (Enter). Escape cancels."),GUILayout.Width(100),GUILayout.Height(24)))Run(CreateTube);
                }
                if(activeTool==RetopoTool.Patch)
                {
                    GUILayout.Space(10);
                    string feedback=lastDensityPatch!=null&&lastDensityPatch.owner==Active
                        ?"Last patch: "+(lastDensityPatch.after.faces.Count-lastDensityPatch.before.faces.Count)+" faces"
                        :"Next patch";
                    GUILayout.Label(new GUIContent(feedback,status),StudioTheme.Styles.miniLabel,GUILayout.Height(24));
                }
                GUILayout.FlexibleSpace();
            }
        }
    }
}

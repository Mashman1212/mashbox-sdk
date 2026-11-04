using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private ToolChannelPane circularizePane;
        private const string CircularizeTool = "Circularize";
        private bool CanCircularize => mode != ComponentMode.Object && ActiveMesh && CanEditComponents() &&
            !Document.nodes[Active].skinned && ActiveMesh.blendShapeCount == 0 && ActiveMesh.bindposes.Length == 0 && Components.Count > 0;
        private Vector4 CircularizeParameters => new Vector4(
            ToolChannelPane.GetFloatDefault(CircularizeTool,"RadialOffset",0),
            ToolChannelPane.GetFloatDefault(CircularizeTool,"Twist",0),
            ToolChannelPane.GetFloatDefault(CircularizeTool,"NormalOrientation",0),
            ToolChannelPane.GetFloatDefault(CircularizeTool,"Alignment",1));
        private Vector3 CircularizeExtra => new Vector3(ToolChannelPane.GetFloatDefault(CircularizeTool,"Divisions",0),
            ToolChannelPane.GetFloatDefault(CircularizeTool,"SupportingEdges",0),0);
        private void CloseCircularizeTool() { if(circularizePane)circularizePane.Close();circularizePane=null; }
        private void OpenCircularizeTool()
        {
            FinishToolsForSwitch(circularizePane);
            if(circularizePane){circularizePane.Focus();return;}
            try { PrepareMeshSelection(ComponentMode.Vertex,()=>CanCircularize,
                "Circularize: select at least three vertices, closed edge loops, or connected faces on editable static meshes.",
                ComponentMode.Vertex,ComponentMode.Edge,ComponentMode.Face); }
            catch(InvalidOperationException e) { error=e.Message; }
            circularizePane=ToolChannelPane.Open(CircularizeTool,DrawCircularizeTool,()=>circularizePane=null,
                execute:()=>ApplyLiveTool(LiveTool.Circularize),canExecute:()=>CanRunLiveTool(LiveTool.Circularize),
                failed:ex=>{error=ex.Message;repaint();},owner:ToolChannelPaneOwner,
                preview:()=>QueueLiveTool(LiveTool.Circularize),cancelPreview:()=>CancelLiveTool(LiveTool.Circularize),
                history:PerformWorkspaceHistory,repeat:PerformRepeatLastAction);
            if(CanCircularize)QueueLiveTool(LiveTool.Circularize);
        }
        private static Vector4 DrawCircularizeFields(Vector4 p,ref Vector3 extra)
        {
            p.x=StudioSceneUnits.Field(new GUIContent("Radial Offset","Added to the fitted mesh-local radius; interior points scale with the circle."),p.x);
            p.z=EditorGUILayout.Popup("Normal Orientation",Mathf.RoundToInt(p.z),new[]{"Face","Edge Loop"});
            p.y=EditorGUILayout.FloatField("Twist",p.y);
            p.w=EditorGUILayout.Popup("Alignment",Mathf.RoundToInt(p.w),new[]{"Automatic","Surface (average)","Surface (vertex)"});
            extra.x=Mathf.Clamp(EditorGUILayout.IntField("Add Divisions",Mathf.RoundToInt(extra.x)),0,32);
            extra.y=EditorGUILayout.Popup("Supporting Edges",Mathf.RoundToInt(extra.y),new[]{"Off","Inside","Outside","Both"});
            return p;
        }
        private void DrawCircularizeTool()
        {
            var extra=CircularizeExtra;EditorGUI.BeginChangeCheck();var p=DrawCircularizeFields(CircularizeParameters,ref extra);
            if(EditorGUI.EndChangeCheck())
            {
                ToolChannelPane.SetFloatDefault(CircularizeTool,"RadialOffset",p.x);
                ToolChannelPane.SetFloatDefault(CircularizeTool,"Twist",p.y);
                ToolChannelPane.SetFloatDefault(CircularizeTool,"NormalOrientation",p.z);
                ToolChannelPane.SetFloatDefault(CircularizeTool,"Alignment",p.w);
                ToolChannelPane.SetFloatDefault(CircularizeTool,"Divisions",extra.x);
                ToolChannelPane.SetFloatDefault(CircularizeTool,"SupportingEdges",extra.y);
            }
            GUILayout.Label("Faces use each connected region's perimeter. Edges require closed loops. Twist is in degrees. Divisions split existing perimeter edges; supporting edges triangulate the chosen side.",StudioTheme.Styles.wordWrappedMiniLabel);
        }
    }
}

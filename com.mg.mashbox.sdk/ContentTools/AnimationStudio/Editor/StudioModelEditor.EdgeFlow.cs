using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshToolPreview
    {
        internal sealed partial class Target
        { internal StudioMeshCuts.EdgeFlowPlan EdgeFlowPlan; }
    }
    internal sealed partial class StudioModelEditor
    {
        private const string EdgeFlowTool = "Edit Edge Flow";
        private ToolChannelPane edgeFlowToolPane;
        private Vector3 liveFlowParameters;
        private bool CanEditEdgeFlow => CanBevelEdges;
        private static bool BevelEdgeFlow => ToolChannelPane.GetFloatDefault(BevelTool,"EdgeFlow",0)>=.5f;
        private Vector3 FlowParameters(LiveTool kind) => repeatingAction != null && repeatingAction.kind == RepeatKind.Live && repeatingAction.live == kind ? repeatingAction.flow : kind==LiveTool.Bevel
            ? new Vector3(BevelEdgeFlow?1:0,ToolChannelPane.GetFloatDefault(BevelTool,"FlowInfluence",.5f),ToolChannelPane.GetFloatDefault(BevelTool,"FlowIterations",4)) : Vector3.zero;
        private void CloseEdgeFlowToolWindow(){if(edgeFlowToolPane)edgeFlowToolPane.Close();edgeFlowToolPane=null;}
        private void OpenEdgeFlowTool()
        {
            PrepareMeshSelection(ComponentMode.Edge, () => CanEditEdgeFlow,
                "Select edges on an editable static mesh to edit edge flow.", ComponentMode.Edge);
            if(edgeFlowToolPane){QueueLiveTool(LiveTool.EdgeFlow);edgeFlowToolPane.Focus();return;}
            edgeFlowToolPane=ToolChannelPane.Open(EdgeFlowTool,DrawEdgeFlowTool,()=>edgeFlowToolPane=null,
                new Vector2(350,235),new Vector2(310,215),
                execute:()=>ApplyLiveTool(LiveTool.EdgeFlow),canExecute:()=>CanRunLiveTool(LiveTool.EdgeFlow),
                failed:ex=>{error=ex.Message;repaint();},owner:ToolChannelPaneOwner,
                preview:()=>QueueLiveTool(LiveTool.EdgeFlow),cancelPreview:()=>CancelLiveTool(LiveTool.EdgeFlow),history:PerformWorkspaceHistory,repeat:PerformRepeatLastAction);
            QueueLiveTool(LiveTool.EdgeFlow);
        }
        private static void DrawFlowParameters(string tool,bool bevel)
        {
            string strength=bevel?"FlowInfluence":"Influence",passes=bevel?"FlowIterations":"Iterations";
            EditorGUI.BeginChangeCheck();
            float influence=EditorGUILayout.Slider(new GUIContent("Influence","Positive fits the surrounding profile; negative reverses the displacement for concave shaping."),ToolChannelPane.GetFloatDefault(tool,strength,.5f),-1,1);
            if(EditorGUI.EndChangeCheck())ToolChannelPane.SetFloatDefault(tool,strength,influence);
            ToolChannelPane.IntParameter(tool,passes,new GUIContent("Iterations","Repeated blending toward the fixed neighboring profile. No vertices are added."),4,1,64);
        }
        private void DrawEdgeFlowTool()
        {
            EditorGUIUtility.labelWidth=145;DrawFlowParameters(EdgeFlowTool,false);
            GUILayout.Label("Fits across the selected edges using neighboring surface tangents. Unselected vertices stay fixed; edges do not shrink along their length. Open strip ends and branch junctions stay fixed; a single edge can adjust both ends.",StudioTheme.Styles.wordWrappedMiniLabel);
            GUILayout.Label("Changes preview immediately. Apply keeps the pane open; Execute finishes. Cancel or Escape cancels the unapplied preview.",StudioTheme.Styles.wordWrappedMiniLabel);
        }
        private void DrawBevelFlowOptions()
        {
            EditorGUI.BeginChangeCheck();bool enabled=EditorGUILayout.Toggle(new GUIContent("Edge Flow","Fit the resulting selected bevel rails to neighboring surface curvature. Requires at least one subdivision. Disabled by default; does not change bevel connectivity."),BevelEdgeFlow);
            if(EditorGUI.EndChangeCheck())ToolChannelPane.SetFloatDefault(BevelTool,"EdgeFlow",enabled?1:0);
            if(enabled)DrawFlowParameters(BevelTool,true);
        }
        private bool EvaluateEdgeFlow(StudioMeshToolPreview.Target target,float influence,int iterations)
        {
            if(target.EdgeFlowPlan==null)target.EdgeFlowPlan=target.Topology.CreateEdgeFlowPlan(target.Source,liveEdges,weld);
            var points=target.EdgeFlowPlan.Evaluate(influence,iterations);
            bool changed=!points.SequenceEqual(target.Topology.Positions);
            if(!changed){target.PublishedPositions=points;target.VertexUpdateStart=0;return false;}
            target.Work.mesh.vertices=points;RecalculateGroupedNormals(target.Work.mesh,target.Work.normalGroups);
            if(target.Work.mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))target.Work.mesh.RecalculateTangents();
            target.Work.mesh.RecalculateBounds();target.PublishedPositions=points;target.VertexUpdateStart=0;
            return changed;
        }
        private void SmoothBevelOutput(StudioMeshToolPreview.Target target,StudioMeshCuts.BevelResult bevel,Vector3 settings)
        {
            if(bevel==null || bevel.RenderSelection.Count==0 || settings.y==0)return;
            var topology=new StudioMeshCuts(target.Work);
            var fixedPoints=new HashSet<Vector3>(target.Topology.Positions);
            var plan=topology.CreateEdgeFlowPlan(target.Work.mesh,bevel.RenderSelection,true,fixedPoints);
            var points=plan.Evaluate(settings.y,Mathf.Clamp(Mathf.RoundToInt(settings.z),1,64));
            if(points.SequenceEqual(topology.Positions))return;
            target.Work.mesh.vertices=points;RecalculateGroupedNormals(target.Work.mesh,target.Work.normalGroups);
            if(target.Work.mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))target.Work.mesh.RecalculateTangents();
            target.Work.mesh.RecalculateBounds();
            // The bevel packet contains the unsmoothed source trajectory. Publish the
            // smoothed streams through the existing generic path, without feeding the
            // modified points back into the immutable bevel/wavefront cache.
            target.VertexUpdate=null;target.Welds=null;target.VertexUpdateStart=0;target.PublishedPositions=points;
        }
    }
}


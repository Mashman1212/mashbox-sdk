using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private const string BridgeTool="Bridge Edges";
        private ToolChannelPane bridgeToolPane;
        private Vector4 BridgeParameters=>repeatingAction!=null&&repeatingAction.kind==RepeatKind.Bridge?repeatingAction.parameters:
            new Vector4(ToolChannelPane.GetFloatDefault(BridgeTool,"Divisions",0),ToolChannelPane.GetFloatDefault(BridgeTool,"Curvature",0),0,0);
        private void CloseBridgeToolWindow(){if(bridgeToolPane)bridgeToolPane.Close();bridgeToolPane=null;}
        private void OpenBridgeTool()
        {
            PrepareMeshSelection(ComponentMode.Edge,()=>true,"",ComponentMode.Edge);
            if(bridgeToolPane){if(CanBridgeEdges)QueueLiveTool(LiveTool.Bridge);bridgeToolPane.Focus();return;}
            bridgeToolPane=ToolChannelPane.Open(BridgeTool,DrawBridgeTool,()=>bridgeToolPane=null,
                new Vector2(350,235),new Vector2(310,210),execute:()=>ApplyLiveTool(LiveTool.Bridge),
                canExecute:()=>CanRunLiveTool(LiveTool.Bridge),failed:ex=>{error=ex.Message;repaint();},owner:ToolChannelPaneOwner,
                preview:()=>QueueLiveTool(LiveTool.Bridge),cancelPreview:()=>CancelLiveTool(LiveTool.Bridge),history:PerformWorkspaceHistory,repeat:PerformRepeatLastAction);
            if(CanBridgeEdges)QueueLiveTool(LiveTool.Bridge);else error="Select two non-touching boundary edge groups with equal edge counts on one mesh.";FocusToolOptions?.Invoke();repaint();
        }
        private void DrawBridgeTool()
        {
            EditorGUIUtility.labelWidth=145;
            ToolChannelPane.IntParameter(BridgeTool,"Divisions",new GUIContent("Divisions","Additional rows across the gap. Zero creates one flat band."),0,0,64);
            EditorGUI.BeginChangeCheck();
            float curve=EditorGUILayout.Slider(new GUIContent("Curvature","Signed profile following neighboring surface tangents: -1 concave, 0 flat, +1 convex."),ToolChannelPane.GetFloatDefault(BridgeTool,"Curvature",0),-1,1);
            if(EditorGUI.EndChangeCheck())ToolChannelPane.SetFloatDefault(BridgeTool,"Curvature",curve);
            GUILayout.Label("Select two non-touching boundary groups with equal edge counts on one mesh. Add divisions for a curved profile; source borders stay fixed.",StudioTheme.Styles.wordWrappedMiniLabel);
            GUILayout.Label("Changes preview immediately. Apply keeps the pane open; Execute finishes. Cancel restores the unapplied bridge.",StudioTheme.Styles.wordWrappedMiniLabel);
        }
        private bool EvaluateBridge(StudioMeshToolPreview.Target target,Vector4 parameters)
        {
            int divisions=Mathf.Clamp(Mathf.RoundToInt(parameters.x),0,64);float curvature=Mathf.Clamp(parameters.y,-1,1);
            var profile=target.Topology.LastBridgeProfile;
            if(liveEvaluated&&profile!=null&&profile.Divisions==divisions)
            {target.PublishedPositions=profile.Apply(target.Work.mesh,curvature);target.VertexUpdateStart=target.Topology.Positions.Length;return true;}
            target.Reset();target.Topology.BridgeEdges(target.Work,liveEdges,divisions,curvature);
            target.PublishedPositions=target.Work.mesh.vertices;target.VertexUpdateStart=target.Topology.Positions.Length;return true;
        }
        private bool CanBridgeEdges=>Document&&mode==ComponentMode.Edge&&ComponentTargetNodes(true).Count==1;
        private void BridgeSelectedEdges()
        {
            PrepareMeshSelection(ComponentMode.Edge, () => CanBridgeEdges,
                "Select two non-touching boundary edge groups with equal edge counts on one mesh. Bridge does not combine objects.", ComponentMode.Edge);
            int targetNode=ComponentTargetNodes(true).Single();
            var before=CaptureSelection();
            (int a,int b)[] selected;int[] targetSelection;
            using(new ComponentTargetScope(this,targetNode))
            {
                var edges=Edges();selected=Components.Where(i=>i>=0&&i<edges.Count).Select(i=>edges[i]).ToArray();targetSelection=Components.ToArray();
            }
            using(var transaction=new StudioMeshToolPreview(Document,new[]{targetNode}))
            {
                var target=transaction.Targets[0];var parameters=BridgeParameters;target.Topology.BridgeEdges(target.Work,selected,Mathf.Clamp(Mathf.RoundToInt(parameters.x),0,64),Mathf.Clamp(parameters.y,-1,1));
                PrepareIsolationTopologyEdit();StopCutTool();StopTargetWeld();EndPivotSnap();CancelMarkingMenu();
                var previous=livePreview;livePreview=transaction;
                try
                {
                    transaction.Publish(true);RefreshLiveMeshes();ComponentTargetAt(targetNode).selection.Clear();
                    transaction.Commit("Bridge edges",()=>{
                        var operation=new StudioModifierOperation{kind="Bridge",name="Bridge",parameters=parameters,edges=selected.SelectMany(e=>new[]{e.a,e.b}).ToArray()};
                        var source=target.ModifierSourceNode();operation.Bind(source);AppendModifier(Document.nodes[targetNode],operation,StudioModifierSource.Capture(source));
                        RecordSelectionChange(before,false,"Bridge edges");});RefreshLiveMeshes();
                }
                catch{transaction.Restore();var selection=ComponentTargetAt(targetNode).selection;selection.Clear();selection.UnionWith(targetSelection);RefreshLiveMeshes(false);throw;}
                finally{livePreview=previous;}
            }
            EstablishSelectionBaseline();RememberAction(RepeatKind.Bridge,BridgeParameters);error=null;repaint();
        }
    }
}

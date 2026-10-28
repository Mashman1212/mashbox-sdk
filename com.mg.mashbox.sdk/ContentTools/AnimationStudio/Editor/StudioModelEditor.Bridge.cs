using System;
using System.Linq;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool CanBridgeEdges=>Document&&mode==ComponentMode.Edge&&ComponentTargetNodes(true).Count==1;
        private void BridgeSelectedEdges()
        {
            if(!Document||mode!=ComponentMode.Edge||ComponentTargetNodes(true).Count!=1)
                throw new InvalidOperationException("Select two non-touching edge groups on one mesh. Bridge does not combine objects.");
            int targetNode=ComponentTargetNodes(true).Single();
            var before=CaptureSelection();
            (int a,int b)[] selected;int[] targetSelection;
            using(new ComponentTargetScope(this,targetNode))
            {
                var edges=Edges();selected=Components.Where(i=>i>=0&&i<edges.Count).Select(i=>edges[i]).ToArray();targetSelection=Components.ToArray();
            }
            using(var transaction=new StudioMeshToolPreview(Document,new[]{targetNode}))
            {
                var target=transaction.Targets[0];target.Topology.BridgeEdges(target.Work,selected);
                PrepareIsolationTopologyEdit();StopCutTool();StopTargetWeld();EndPivotSnap();CancelMarkingMenu();
                var previous=livePreview;livePreview=transaction;
                try
                {
                    transaction.Publish(true);RefreshLiveMeshes();ComponentTargetAt(targetNode).selection.Clear();
                    transaction.Commit("Bridge edges",()=>RecordSelectionChange(before,false,"Bridge edges"));RefreshLiveMeshes();
                }
                catch{transaction.Restore();var selection=ComponentTargetAt(targetNode).selection;selection.Clear();selection.UnionWith(targetSelection);RefreshLiveMeshes(false);throw;}
                finally{livePreview=previous;}
            }
            EstablishSelectionBaseline();RememberAction(RepeatKind.Bridge);error=null;repaint();
        }
    }
}

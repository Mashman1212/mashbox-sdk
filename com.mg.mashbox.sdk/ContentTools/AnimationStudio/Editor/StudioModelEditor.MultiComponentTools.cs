using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private sealed class ComponentLiveTarget { internal int node;internal ComponentLiveState state=new ComponentLiveState(); }
        private List<ComponentLiveTarget> componentLiveTargets;
        private SelectionSnapshot componentLiveBefore;
        private bool dispatchingComponentTool;
        private SelectionSnapshot SourceLiveSelection(SelectionSnapshot selection)
        {
            selection.mode=liveMode;selection.components=(int[])liveSelection.Clone();
            if(selection.targets!=null)foreach(var record in selection.targets)if(record.node==Active){record.mode=(int)liveMode;record.components=(int[])liveSelection.Clone();}
            return selection;
        }
        private bool EligibleComponentTool(LiveTool kind) => kind==LiveTool.Bevel?CanBevelEdges:kind==LiveTool.Extrude?CanExtrude:kind==LiveTool.Merge?CanMergeVertices:CanAdjustEdgeNormals;
        private bool EvaluateComponentLiveTools(LiveTool kind)
        {
            if(componentLiveTargets==null)
            {
                if(mode==ComponentMode.Object)return false;
                var nodes=ComponentTargetNodes(true);if(nodes.Count<2)return false;
                // Every selected target participates. Fail visibly rather than silently
                // applying a destructive tool only to the active object.
                foreach(int node in nodes)using(new ComponentTargetScope(this,node))if(!EligibleComponentTool(kind))throw new InvalidOperationException("The tool requires a compatible component selection on every selected mesh.");
                FinishLiveTool();componentLiveBefore=CaptureSelection();
                componentLiveTargets=nodes.GroupBy(i=>Document.nodes[i].mesh).Select(g=>new ComponentLiveTarget{node=g.Contains(Active)?Active:g.First()}).ToList();
                foreach(var target in componentLiveTargets)
                {
                    var primary=ComponentTargetAt(target.node);
                    foreach(int other in nodes.Where(i=>i!=target.node&&Document.nodes[i].mesh==Document.nodes[target.node].mesh))
                    {
                        var secondary=ComponentTargetAt(other);
                        if(secondary.mode!=primary.mode){componentLiveTargets=null;componentLiveBefore=null;throw new InvalidOperationException("Shared mesh instances must use the same component type for a topology tool.");}
                        primary.selection.UnionWith(secondary.selection);
                    }
                }
            }
            if(componentLiveTargets.Any(t=>t.state.livePreview!=null&&t.state.liveTool!=kind)){CommitComponentLiveTools(true);return EvaluateComponentLiveTools(kind);}
            dispatchingComponentTool=true;
            try
            {
                foreach(var target in componentLiveTargets)using(new ComponentTargetScope(this,target.node))
                {
                    LoadComponentLiveState(target.state);
                    try{EvaluateLiveTool(kind);MirrorComponentLiveSelection(target.node);}finally{SaveComponentLiveState(target.state);}
                }
            }
            catch
            {
                dispatchingComponentTool=false;CancelComponentLiveTools(false);throw;
            }
            finally{dispatchingComponentTool=false;LoadPrimaryComponentLiveState();}
            return true;
        }
        private void LoadPrimaryComponentLiveState()
        {
            if(componentLiveTargets!=null&&componentLiveTargets.Count>0)LoadComponentLiveState((componentLiveTargets.FirstOrDefault(t=>t.node==Active)??componentLiveTargets[0]).state);
        }
        private void MirrorComponentLiveSelection(int node)
        {
            var target=ComponentTargetAt(node);
            foreach(int index in objects)if(index!=node&&Document.nodes[index].mesh==Document.nodes[node].mesh)
            {var shared=ComponentTargetAt(index);shared.mode=target.mode;shared.selection.Clear();shared.selection.UnionWith(target.selection);}
        }
        private StudioMeshToolPreview.Target ComponentBevelPublication()
        {
            if(componentLiveTargets==null)return null;
            foreach(var target in componentLiveTargets)
                if(target.state.liveTool==LiveTool.Bevel&&target.state.livePreview!=null)
                    foreach(var publication in target.state.livePreview.Targets)
                        if(publication.Live==ActiveMesh&&publication.PublishedPositions!=null&&publication.PublishedPositions.Count==publication.Live.vertexCount)return publication;
            return null;
        }
        private bool CommitComponentLiveTools(bool closePane)
        {
            if(componentLiveTargets==null)return false;
            if(componentLiveTargets.Any(t=>!t.state.liveValid))throw new InvalidOperationException("Correct the tool values or cancel the preview.");
            var targets=componentLiveTargets;var kind=targets[0].state.liveTool;
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();dispatchingComponentTool=true;
            try
            {
                foreach(var target in targets)using(new ComponentTargetScope(this,target.node))
                {
                    LoadComponentLiveState(target.state);try{CommitLiveTool(false);MirrorComponentLiveSelection(target.node);}finally{SaveComponentLiveState(target.state);}
                }
                Undo.CollapseUndoOperations(group);
            }
            catch{Undo.RevertAllDownToGroup(group);throw;}
            finally{dispatchingComponentTool=false;componentLiveTargets=null;componentLiveBefore=null;ClearLiveToolState();}
            if(closePane)CloseLiveToolPane(kind);repaint();return true;
        }
        private bool CancelComponentLiveTools(bool afterUndo)
        {
            if(componentLiveTargets==null)return false;
            var targets=componentLiveTargets;componentLiveTargets=null;dispatchingComponentTool=true;RemoveQueuedPreview();
            try
            {
                foreach(var target in targets)
                {
                    if(target.node<0||target.node>=Document.nodes.Count)
                    { target.state.livePreview?.Restore(afterUndo);target.state.livePreview?.Dispose();continue; }
                    using(new ComponentTargetScope(this,target.node))
                    {LoadComponentLiveState(target.state);CancelLiveTool(afterUndo);}
                }
            }
            finally
            {
                if(!afterUndo&&componentLiveBefore!=null)RestoreComponentTargets(componentLiveBefore.targets);
                dispatchingComponentTool=false;componentLiveBefore=null;ClearLiveToolState();
            }
            return true;
        }
        internal void DeleteSelection()
        {
            if(mode==ComponentMode.Object){DeleteSelectedObjects();return;}
            FinishLiveTool();var nodes=ComponentTargetNodes(true);if(nodes.Count==0)return;
            foreach(int node in nodes)using(new ComponentTargetScope(this,node))if(!CanEditComponents())throw new InvalidOperationException("Every selected mesh must be editable.");
            var before=CaptureSelection();Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
            try
            {
                foreach(var shared in nodes.GroupBy(i=>Document.nodes[i].mesh))
                {
                    int node=shared.First();var target=ComponentTargetAt(node);
                    foreach(int other in shared.Skip(1))
                    {
                        var otherTarget=ComponentTargetAt(other);
                        if(otherTarget.mode!=target.mode)throw new InvalidOperationException("Shared mesh instances must use the same component type for deletion.");
                        target.selection.UnionWith(otherTarget.selection);
                    }
                    using(new ComponentTargetScope(this,node))DeleteActiveComponents();
                    foreach(int other in shared)ComponentTargetAt(other).selection.Clear();
                }
                Rebuild();Changed();RecordSelectionChange(before,false,"Delete model components");Undo.CollapseUndoOperations(group);
            }
            catch{Undo.RevertAllDownToGroup(group);RestoreComponentTargets(before.targets);Rebuild();throw;}
        }
        private bool CanDeleteComponentTargets()
        {
            var nodes=ComponentTargetNodes(true);if(nodes.Count==0)return false;
            foreach(int node in nodes)using(new ComponentTargetScope(this,node))if(!CanEditComponents())return false;
            return true;
        }
    }
}

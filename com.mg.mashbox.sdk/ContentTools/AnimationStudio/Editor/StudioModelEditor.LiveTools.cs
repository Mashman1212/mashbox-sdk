using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal void ReloadStoredToolDefaults()
        {
            CancelLiveTool(); LoadDisplayConfiguration();
            edgeLoopCount = Mathf.Clamp(Mathf.RoundToInt(StudioPreferenceStore.GetFloat("Model.EdgeLoopCount", 1)), 1, MaximumEdgeLoops);

            extrusionDistance = ToolChannelPane.GetFloatDefault("Extrude", "Distance", .01f);
            extrusionInset = ToolChannelPane.GetFloatDefault("Extrude", "Inset", 0);
            extrudeFacesTogether = ToolChannelPane.GetFloatDefault("Extrude", "Together", 1) >= .5f;
        }
        private enum LiveTool { Bevel, Merge, Soften, Extrude, EdgeFlow }
        private StudioMeshToolPreview livePreview;
        private LiveTool liveTool, queuedTool;
        private bool previewQueued, liveValid, liveEvaluated;
        private Vector4 liveParameters;
        private ComponentMode liveMode;
        private int[] liveSelection, liveFaces;
        private (int a, int b)[] liveEdges;
        private HashSet<(int, int)>[] liveNormalEdges;
        private HashSet<Vector3> livePoints;
        private HashSet<(Vector3, Vector3)> liveEdgePoints;
        private Matrix4x4 liveWorld;
        private bool liveMergePair;
        private StudioMeshCuts.ExtrusionResult liveExtrusion;
        private Vector3[] liveExtrusionDirections, liveExtrusionPositions;
        private bool liveExtrusionTogether;
        private List<Vector2> liveExtrusionUV = new List<Vector2>();
        private int[] liveWallTips;
        private int liveUVDimension;
        private List<Vector3> liveExtrusionUV3 = new List<Vector3>();
        private List<Vector4> liveExtrusionUV4 = new List<Vector4>();
        private void RepaintLiveToolPanes()
        {
            if (bevelToolPane) bevelToolPane.Repaint();
            if (softenToolPane) softenToolPane.Repaint();
            if (extrudeToolPane) extrudeToolPane.Repaint();
            if (edgeFlowToolPane) edgeFlowToolPane.Repaint();
            if (combineToolPane) combineToolPane.Repaint();
        }
        private void CloseLiveToolPane(LiveTool kind)
        {
            if (kind == LiveTool.Bevel) CloseBevelToolWindow();
            else if (kind == LiveTool.Merge) CloseMergeToolWindow();
            else if (kind == LiveTool.Soften) CloseNormalsToolWindow();
            else if (kind == LiveTool.EdgeFlow) CloseEdgeFlowToolWindow();
            else CloseExtrudeToolWindow();
        }
        private void CloseLiveToolPanes()
        { CloseBevelToolWindow(); CloseMergeToolWindow(); CloseNormalsToolWindow(); CloseExtrudeToolWindow(); CloseEdgeFlowToolWindow(); }
        private bool HasLiveTool(LiveTool kind) => livePreview != null && liveTool == kind;
        private bool CanRunLiveTool(LiveTool kind) => HasLiveTool(kind) || (kind == LiveTool.Bevel ? CanBevelEdges :
            kind == LiveTool.Merge ? CanMergeVertices : kind == LiveTool.Soften ? CanAdjustEdgeNormals : kind == LiveTool.EdgeFlow ? CanEditEdgeFlow : CanExtrude);
        private Vector4 LiveParameters(LiveTool kind)
        {
            if (repeatingAction != null && repeatingAction.kind == RepeatKind.Live && repeatingAction.live == kind) return repeatingAction.parameters;
            if (kind == LiveTool.Bevel) return new Vector4(ToolChannelPane.GetFloatDefault(BevelTool, "Width", .1f),
                ToolChannelPane.GetFloatDefault(BevelTool, "Subdivisions", 1), ToolChannelPane.GetFloatDefault(BevelTool, "Tension", 1), ToolChannelPane.GetFloatDefault(BevelTool, "Junction", 0));
            if (kind == LiveTool.EdgeFlow) return new Vector4(ToolChannelPane.GetFloatDefault(EdgeFlowTool,"Influence",.5f),ToolChannelPane.GetFloatDefault(EdgeFlowTool,"Iterations",4),0,0);
            if (kind == LiveTool.Merge) return Vector4.zero;
            if (kind == LiveTool.Soften) return new Vector4(ToolChannelPane.GetFloatDefault(SoftenTool, "Angle", 45), 0, 0, 0);
            return new Vector4(extrusionDistance, extrudeFacesTogether ? 1 : 0, extrusionInset, 0);
        }
        private void QueueLiveTool(LiveTool kind)
        {
            if (!CanRunLiveTool(kind)) return;
            queuedTool = kind;
            if (previewQueued) return;
            // Inspector delayCall delivery can throttle a floating pane's drag to
            // inspector refresh cadence. Run once on the next editor update,
            // outside IMGUI, and coalesce all parameter edits until then.
            previewQueued = true; EditorApplication.update += UpdateQueuedLiveTool;
        }
        private void RemoveQueuedPreview()
        { EditorApplication.update -= UpdateQueuedLiveTool; previewQueued = false; }
        private void UpdateQueuedLiveTool()
        {
            var kind = queuedTool; RemoveQueuedPreview();
            try { if (CanRunLiveTool(kind)) EvaluateLiveTool(kind); }
            catch (Exception exception) { liveValid = false; error = exception.Message; repaint(); }
        }
        private void StartLiveTool(LiveTool kind)
        {
            if (HasLiveTool(kind)) return;
            FinishLiveTool();
            if (!CanRunLiveTool(kind)) throw new InvalidOperationException("Select editable mesh components for this tool.");
            PrepareIsolationTopologyEdit();
            FinishChannelEdit(); StopTargetWeld(); StopCutTool(); CancelMarkingMenu(); EndPivotSnap();
            liveMode = mode; liveSelection = Components.OrderBy(i => i).ToArray();
            liveWorld = Active >= 0 ? Transforms[Active].localToWorldMatrix : Matrix4x4.identity;
            liveMergePair = kind == LiveTool.Merge;
            liveEdges = mode == ComponentMode.Edge ? liveSelection.Select(i => Edges()[i]).ToArray() : null;
            liveFaces = mode == ComponentMode.Face ? liveSelection : null;
            livePoints = new HashSet<Vector3>(); liveEdgePoints = new HashSet<(Vector3, Vector3)>();
            if (ActiveMesh)
            {
                var points = ActiveMesh.vertices;
                if (mode == ComponentMode.Vertex) foreach (int i in liveSelection) livePoints.Add(points[i]);
                if (liveEdges != null) foreach (var edge in liveEdges) liveEdgePoints.Add(NormalEdgePositionKey(points[edge.a], points[edge.b]));
            }
            livePreview = new StudioMeshToolPreview(Document, kind == LiveTool.Soften ? NormalTargets() : new[] { Active });
            liveTool = kind; liveEvaluated = liveValid = false;
            liveNormalEdges = kind == LiveTool.Soften ? livePreview.Targets.Select(t => SelectedNormalEdges(t.Topology)).ToArray() : null;
            liveExtrusion = null; liveExtrusionDirections = liveExtrusionPositions = null;
        }
#if MASHBOX_STUDIO_TESTS
        internal static Action<string> LiveProfileStage;
#endif
        private void EvaluateLiveTool(LiveTool kind)
        {
            if (!dispatchingComponentTool && EvaluateComponentLiveTools(kind)) return;
            StartLiveTool(kind);
            var parameters = LiveParameters(kind);
            var flowParameters = FlowParameters(kind);
            if (liveEvaluated && liveValid && parameters.Equals(liveParameters) && flowParameters.Equals(liveFlowParameters)) return;
#if MASHBOX_STUDIO_TESTS
            LiveProfileStage?.Invoke("live.start");
#endif
            liveValid = false;
            foreach (float value in new[] { parameters.x, parameters.y, parameters.z, parameters.w, flowParameters.x, flowParameters.y, flowParameters.z })
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Tool values must be finite.");
            StudioMeshCuts.BevelResult bevel = null;
            StudioMeshCuts.MergeResult merge = null;
            bool changed = false;
            for (int index = 0; index < livePreview.Targets.Length; index++)
            {
                var target = livePreview.Targets[index];
                if (kind == LiveTool.Extrude && liveExtrusion != null && liveExtrusionTogether == extrudeFacesTogether && (Mathf.Abs(parameters.x) >= 1e-5f || Mathf.Abs(parameters.z) >= 1e-5f) && (parameters.z == 0 || liveExtrusion.InsetDirections != null))
                {
                    // Distance/inset edits retain the index buffer, cached wavefront and expanded topology.
                    liveExtrusion.ValidateInset(parameters.z);
                    for (int i = 0; i < liveExtrusionPositions.Length; i++) liveExtrusionPositions[i] = liveExtrusion.SourceVertices[i] + liveExtrusion.NormalDirections[i] * parameters.x + (liveExtrusion.InsetDirections == null ? Vector3.zero : liveExtrusion.InsetDirections[i] * parameters.z);
                    target.Work.mesh.vertices = liveExtrusionPositions;
                    UpdateExtrusionUV(target.Work.mesh);
                    target.Work.mesh.RecalculateNormals(); target.Work.mesh.RecalculateTangents(); target.Work.mesh.RecalculateBounds();
                    changed = true; continue;
                }
                // Bevel owns an immutable attribute/source cache. Repeated Width
                // samples update its working result instead of restoring/replacing
                // the entire source mesh. Zero width still restores the source.
                bool reset = kind != LiveTool.Bevel || !liveEvaluated || parameters.x <= 0 || flowParameters.x>=.5f || liveFlowParameters.x>=.5f;
#if MASHBOX_STUDIO_TESTS
                reset |= target.Topology.UseReferenceBevelWriter;
#endif
                if (reset) target.Reset();
#if MASHBOX_STUDIO_TESTS
                LiveProfileStage?.Invoke("live.reset");
#endif
                if (kind == LiveTool.Bevel)
                {
                    bevel = target.Topology.BevelEdges(target.Work, liveEdges, new StudioMeshCuts.BevelOptions {
                        Width = parameters.x, Subdivisions = Mathf.RoundToInt(parameters.y), Tension = parameters.z,
                        Junction = (StudioMeshCuts.BevelJunction)Mathf.RoundToInt(parameters.w) });
                    changed |= bevel != null;
                    target.IsBevelPreview=bevel!=null; target.VertexUpdate=bevel?.VertexUpdate; target.Welds=bevel?.Welds; target.VertexUpdateStart = bevel?.VertexUpdateStart ?? 0; target.PublishedPositions = bevel?.PublishedPositions;
                    if(flowParameters.x>=.5f&&Mathf.RoundToInt(parameters.y)>0)SmoothBevelOutput(target,bevel,flowParameters);
                }
                else if (kind == LiveTool.EdgeFlow)
                { changed |= EvaluateEdgeFlow(target,parameters.x,Mathf.Clamp(Mathf.RoundToInt(parameters.y),1,64)); }
                else if (kind == LiveTool.Merge)
                { merge = target.Topology.MergeVertices(target.Work, liveSelection, liveMergePair, parameters.x, liveWorld); changed |= merge != null; }
                else if (kind == LiveTool.Soften)
                { target.Topology.SetEdgeNormals(target.Work, liveNormalEdges[index], parameters.x, false); changed = true; }
                else
                {
                    // Zero distance and zero inset together are a reversible no-op.
                    liveExtrusion = null;
                    if (Mathf.Abs(parameters.x) < 1e-5f && Mathf.Abs(parameters.z) < 1e-5f) continue;
                    liveExtrusion = target.Topology.Extrude(target.Work, liveFaces, liveEdges, parameters.x, extrudeFacesTogether, weld, inset: parameters.z);
                    target.Work.normalGroups = null; liveExtrusionTogether = extrudeFacesTogether;
                    liveExtrusionPositions = target.Work.mesh.vertices;
                    liveExtrusionDirections = liveExtrusion.NormalDirections;
                    liveUVDimension = target.Work.mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord0);
                    if (liveUVDimension == 2) target.Work.mesh.GetUVs(0, liveExtrusionUV);
                    else if (liveUVDimension == 3) target.Work.mesh.GetUVs(0, liveExtrusionUV3);
                    else target.Work.mesh.GetUVs(0, liveExtrusionUV4);
                    var wallTopology = new StudioMeshCuts(target.Work); var tips = new HashSet<int>();
                    for (int f = target.Topology.Faces.Count; f < wallTopology.Faces.Count; f++)
                    { var v = wallTopology.Faces[f].Vertices; if (v.Count == 4) { tips.Add(v[2]); tips.Add(v[3]); } }
                    liveWallTips = tips.ToArray(); changed = true;
                }
            }
            // No target is published until all targets have evaluated successfully.
            livePreview.Publish(changed);
#if MASHBOX_STUDIO_TESTS
            LiveProfileStage?.Invoke("live.publish");
#endif
            RefreshLiveMeshes();
#if MASHBOX_STUDIO_TESTS
            LiveProfileStage?.Invoke("live.refresh");
#endif
            Components.Clear(); mode = liveMode;
            if (!changed) foreach (int i in liveSelection) Components.Add(i);
            else if (kind == LiveTool.Bevel)
            {
                Edges(); // Build the render-edge lookup only when topology changes.
                foreach (var pair in bevel.RenderSelection)
                    if (cachedEdgeIndices.TryGetValue(pair,out int selected)) Components.Add(selected);
#if MASHBOX_STUDIO_TESTS
                // The canonical writer intentionally publishes a different layout.
                if (livePreview.Targets[0].Topology.UseReferenceBevelWriter)
                {
                    var points = ActiveMesh.vertices; var edges = Edges();
                    for (int i=0;i<edges.Count;i++) if (bevel.Selection.Contains(StudioMeshCuts.NormalEdgePositionKeyForBevel(points[edges[i].a],points[edges[i].b]))) Components.Add(i);
                }
#endif
            }
            else if (kind == LiveTool.Merge) foreach (int i in merge.Selection) Components.Add(i);
            else if (kind == LiveTool.EdgeFlow) Components.UnionWith(liveSelection);
            else if (kind == LiveTool.Extrude) SelectExtrusionResult(liveExtrusion, liveFaces != null);
            else RestoreLiveComponentSelection();
#if MASHBOX_STUDIO_TESTS
            LiveProfileStage?.Invoke("live.selection");
#endif
            liveParameters = parameters; liveFlowParameters = flowParameters; liveEvaluated = liveValid = true;
            repaint();
        }
        private StudioMeshToolPreview.Target ActiveBevelPublication()
        {
            var cohort = ComponentBevelPublication(); if (cohort != null) return cohort;
            if (livePreview==null || liveTool!=LiveTool.Bevel) return null;
            foreach (var target in livePreview.Targets)
                if (target.Live==ActiveMesh && target.PublishedPositions!=null && target.PublishedPositions.Count==target.Live.vertexCount) return target;
            return null;
        }
        private IList<Vector3> ComponentPositions() => ActiveBevelPublication()?.PublishedPositions ?? ActiveMesh.vertices;
        private void ExpandWeldedSelection(HashSet<int> selection, IList<Vector3> positions)
        {
            var publication=ActiveBevelPublication();
            if (publication?.Welds!=null) { publication.Welds.Expand(selection); return; }
            var points=new HashSet<Vector3>(selection.Select(i=>positions[i]));
            for (int i=0;i<positions.Count;i++) if (points.Contains(positions[i])) selection.Add(i);
        }
        private void UpdateExtrusionUV(Mesh mesh)
        {
            float Length(int i) => Vector3.Distance(liveExtrusionPositions[i], liveExtrusion.SourceVertices[i]);
            if (liveUVDimension == 2)
            { foreach (int i in liveWallTips) { var value = liveExtrusionUV[i]; value.y = Length(i); liveExtrusionUV[i] = value; } mesh.SetUVs(0, liveExtrusionUV); }
            else if (liveUVDimension == 3)
            { foreach (int i in liveWallTips) { var value = liveExtrusionUV3[i]; value.y = Length(i); liveExtrusionUV3[i] = value; } mesh.SetUVs(0, liveExtrusionUV3); }
            else
            { foreach (int i in liveWallTips) { var value = liveExtrusionUV4[i]; value.y = Length(i); liveExtrusionUV4[i] = value; } mesh.SetUVs(0, liveExtrusionUV4); }
        }
        private void RestoreLiveComponentSelection()
        {
            if (liveMode == ComponentMode.Face) { foreach (int i in liveSelection) Components.Add(i); }
            else if (liveMode == ComponentMode.Vertex)
            { var points = ActiveMesh.vertices; for (int i = 0; i < points.Length; i++) if (livePoints.Contains(points[i])) Components.Add(i); }
            else if (liveMode == ComponentMode.Edge)
            { var points = ActiveMesh.vertices; var edges = Edges(); for (int i = 0; i < edges.Count; i++) if (liveEdgePoints.Contains(NormalEdgePositionKey(points[edges[i].a], points[edges[i].b]))) Components.Add(i); }
        }
        private void RefreshLiveMeshes(bool markDirty = true)
        {
            RefreshOtherComponentCaches(livePreview);
            bool structureChanged = livePreview.Targets.Any(t => t.StructureChanged);
            if (structureChanged)
            {
                edgeMesh = null; cachedEdges = null; faceNode = null; cachedFaces = null; cachedFaceTriangles = null;
                ClearComponentDrawing();
            }
            else
            {
                var activeTarget = livePreview.Targets.FirstOrDefault(t => t.Live == ActiveMesh);
                RefreshComponentPositions(activeTarget?.VertexUpdateStart ?? 0, activeTarget?.PublishedPositions);
            }
            cutTopology = null; cutMesh = null; componentFrameValid = false;
            pivotTargetCache.Clear(); pivotHover = null;
            for (int nodeIndex=0;nodeIndex<Document.nodes.Count;nodeIndex++)
            {
                var node=Document.nodes[nodeIndex];
                var target = livePreview.Targets.FirstOrDefault(t => t.Live == node.mesh);
                if (target == null) continue;
                if (nodeIndex<Transforms.Length && Transforms[nodeIndex]) target.UseGpuPreview(Transforms[nodeIndex].GetComponent<MeshFilter>());
                if (selectionWireframes.TryGetValue(node, out var wire))
                {
                    if (target.StructureChanged || wire.baked)
                    { if (wire.baked) UnityEngine.Object.DestroyImmediate(wire.baked); ReleaseWireBatch(wire); selectionWireframes.Remove(node); }
                    else
                    {
                        int start = target.VertexUpdateStart;
                        if (target.PublishedPositions != null && target.PublishedPositions.Count == wire.vertices.Count)
                            for (int i = start; i < wire.vertices.Count; i++) wire.vertices[i] = target.PublishedPositions[i];
                        else { node.mesh.GetVertices(wire.vertices); start = 0; }
                        wire.vertexUpdateStart = wire.batchPositionsDirty ? Math.Min(wire.vertexUpdateStart, start) : start;
                        wire.batchPositionsDirty = true; wire.linesDirty = true;
                    }
                }
                if (target.StructureChanged) statisticsCache.Remove(node);
                undoMeshVersions[node.mesh] = EditorUtility.GetDirtyCount(node.mesh);
            }
            if (markDirty) EditorUtility.SetDirty(Document); WorkspaceSaveStateChanged?.Invoke(); repaint();
        }
        private void ApplyLiveTool(LiveTool kind)
        {
            RemoveQueuedPreview(); EvaluateLiveTool(kind);
            CommitLiveTool(false);
        }
        private void FinishLiveTool() => CommitLiveTool(true);
        private void CommitLiveTool(bool closePane)
        {
            if (previewQueued)
            {
                var kind = queuedTool; RemoveQueuedPreview();
                EvaluateLiveTool(kind);
            }
            if (!dispatchingComponentTool && CommitComponentLiveTools(closePane)) return;
            if (livePreview == null) return;
            if (!liveValid) throw new InvalidOperationException("Correct the tool values or close its pane to cancel the preview.");
            string label = liveTool == LiveTool.Bevel ? "Bevel edges" : liveTool == LiveTool.Merge ? "Merge vertices to center" : liveTool == LiveTool.Soften ? "Soften edges" : liveTool == LiveTool.EdgeFlow ? "Edit edge flow" : "Extrude mesh components";
            var finishedTool = liveTool;
            var selectionBefore = CaptureSelection();
            selectionBefore = dispatchingComponentTool ? componentLiveBefore : SourceLiveSelection(selectionBefore);
            bool changed = livePreview.HasChanges;
            if (changed)
            {
                if (liveTool == LiveTool.Bevel) Components.Clear();
                livePreview.Commit(label, () => RecordSelectionChange(selectionBefore, false, label));
            }
            else livePreview.Restore();
            // Completion keeps the object and component mode active, but a bevel
            // must not leave generated edges armed for an accidental second bevel.
            bool clearBevelSelection = changed && liveTool == LiveTool.Bevel;
            if (changed && !dispatchingComponentTool) RememberLiveAction(finishedTool,liveParameters,liveFlowParameters);
            RefreshLiveMeshes(changed); livePreview.Dispose(); ClearLiveToolState();
            if (clearBevelSelection) { Components.Clear(); ClearComponentDrawing(); }
            error = changed ? label + " applied." : "No mesh changes to apply.";
            if (closePane) CloseLiveToolPane(finishedTool);
            repaint();
        }
        private void ClearLiveToolState()
        {
            liveFlowParameters = Vector3.zero;
            livePreview = null; liveSelection = liveFaces = liveWallTips = null; liveEdges = null;
            liveNormalEdges = null; livePoints = null; liveEdgePoints = null;
            liveExtrusion = null; liveExtrusionDirections = liveExtrusionPositions = null;
            liveExtrusionUV.Clear(); liveExtrusionUV3.Clear(); liveExtrusionUV4.Clear();
        }
        private void CancelLiveTool(LiveTool kind)
        {
            if (previewQueued && queuedTool == kind) RemoveQueuedPreview();
            if (!HasLiveTool(kind)) return;
            CancelLiveTool();
        }
        private void CancelLiveTool(bool afterUndo = false)
        {
            if (!dispatchingComponentTool && CancelComponentLiveTools(afterUndo)) return;
            RemoveQueuedPreview(); if (livePreview == null) return;
            var transaction = livePreview;
            try
            {
                transaction.Restore(afterUndo);
                // Native Undo already changed document nodes/topology. OnUndo
                // rebuilds render caches; refreshing old caches here is unsafe.
                if (!afterUndo)
                {
                    mode = liveMode; Components.Clear(); foreach (int i in liveSelection) Components.Add(i);
                    RefreshLiveMeshes(false);
                }
            }
            finally { transaction.Dispose(); ClearLiveToolState(); repaint(); }
        }
    }
}

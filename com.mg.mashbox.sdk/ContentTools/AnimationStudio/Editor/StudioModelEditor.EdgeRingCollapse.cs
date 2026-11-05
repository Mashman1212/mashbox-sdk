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
        private const string EdgeRingCollapseLabel = "Collapse Edge Ring";
        private void CollapseSelectedEdgeRings() => ChooseSelectionAction(EdgeRingCollapseLabel, CollapseEdgeRings,
            "Select edges across a quad band. Each ring edge collapses to its own midpoint; crushed faces are removed.");

        private void CollapseEdgeRings()
        {
            var previousTool = tool;
            PrepareMeshSelection(ComponentMode.Edge,
                () => Document && mode == ComponentMode.Edge && ComponentTargetNodes(true).Count > 0,
                "Select edges to collapse their quad rings.", ComponentMode.Edge);
            FlushModifierEdits();
            var selection = new Dictionary<int, List<(int a, int b)>>();
            foreach (int i in ComponentTargetNodes(true)) using (new ComponentTargetScope(this, i))
            {
                var node = Document.nodes[i];
                if (mode != ComponentMode.Edge || !CanEditComponents() || Document.IsBone(i) || !string.IsNullOrEmpty(node.anchorType))
                    throw new InvalidOperationException("Collapse Edge Ring requires editable static mesh edges on every selected mesh.");
                if (node.modifiers?.operations.Any(o => !string.IsNullOrEmpty(o.failure)) == true)
                    throw new InvalidOperationException("Repair the broken modifier or Clear History before collapsing edge rings.");
                var edges = Edges(); var seeds = new List<(int, int)>();
                foreach (int id in Components.OrderBy(id => id))
                {
                    if (id < 0 || id >= edges.Count) throw new InvalidOperationException("Select valid edges to collapse their rings.");
                    if (IsolationAllowsComponent(id)) seeds.Add(edges[id]);
                }
                if (seeds.Count > 0) selection.Add(i, seeds);
            }
            if (selection.Count == 0) throw new InvalidOperationException("Select edges to collapse their quad rings.");
            PrepareIsolationTopologyEdit(); var before = CaptureSelection();
            var regions = selection.GroupBy(p => Document.nodes[p.Key].mesh)
                .ToDictionary(g => g.Key, g => g.SelectMany(p => p.Value).Distinct().ToArray());
            using (var preview = new StudioMeshToolPreview(Document, selection.Keys))
            {
                // All targets finish evaluation off-scene before anything publishes.
                foreach (var target in preview.Targets) target.Topology.CollapseEdgeRings(target.Work, regions[target.Live]);
                preview.HasChanges = true; writingModifiers = true;
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                try
                {
                    preview.Commit(EdgeRingCollapseLabel, () => {
                        foreach (var target in preview.Targets)
                        {
                            var node = Document.nodes.First(n => n.mesh == target.Live); var source = target.ModifierSourceNode();
                            var operation = new StudioModifierOperation { kind = "EdgeRingCollapse", name = EdgeRingCollapseLabel,
                                edges = regions[target.Live].SelectMany(e => new[] { e.a, e.b }).ToArray() };
                            operation.Bind(source); AppendModifier(node, operation, StudioModifierSource.Capture(source));
                        }
                        // Collapsed edges no longer exist. Keep Edge mode without stale IDs.
                        ClearAllComponentSelections(); RecordSelectionChange(before, false, EdgeRingCollapseLabel);
                    });
                }
                catch { Undo.RevertAllDownToGroup(group); preview.Restore(); throw; }
                finally { writingModifiers = false; }
            }
            tool = previousTool == Tool.Rotate || previousTool == Tool.Scale ? previousTool : Tool.Move;
            Rebuild(); Changed(); EstablishSelectionBaseline(); RememberAction(RepeatKind.EdgeRingCollapse); repaint();
        }
    }
}

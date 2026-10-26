using System;

using System.Collections.Generic;

using UnityEditor;

using UnityEngine;



namespace MashBoxSDK.AnimationStudio

{

    internal sealed partial class StudioModelEditor

    {

        private ToolChannelPane bevelToolPane;

        private const string BevelTool = "Bevel Edges";

        private static readonly GUIContent BevelWidthContent = new GUIContent("Width offset", "Offset in mesh-local model units. 0 does nothing. Width is not normalized to nearby edge lengths or limited to 1; clipping updates affected topology as supporting surfaces meet.");

        private static readonly GUIContent BevelSubdivisionsContent = new GUIContent("Subdivisions", "Internal profile loops. 0 creates one chamfer strip; each additional subdivision adds a loop. Maximum 32.");

        private static readonly GUIContent BevelTensionContent = new GUIContent("Profile tension", "0 creates a linear chamfer; positive values bulge toward the original edge; negative values inset the profile.");

        private static readonly GUIContent BevelJunctionContent = new GUIContent("Intersect junction style", "Grid (Uniform) connects subdivided edge profiles through a rounded corner patch. Cap (N-gon) keeps one corner face. Zero subdivisions always creates a flat chamfer.");

        // Runtime import Read/Write flags do not determine editor eligibility:
        // previews obtain readable working copies through the Editor snapshot API.
        private bool CanBevelEdges => mode == ComponentMode.Edge && Components.Count > 0 && ActiveMesh && CanEditComponents() &&

            !Document.nodes[Active].skinned && Document.nodes[Active].blendShape == null && ActiveMesh.blendShapeCount == 0 && ActiveMesh.bindposes.Length == 0;

        private void CloseBevelToolWindow() { if (bevelToolPane) bevelToolPane.Close(); bevelToolPane = null; }

        private void OpenBevelTool()

        {

            if (!CanBevelEdges) throw new InvalidOperationException("Select edges on an editable static mesh to bevel.");

            if (bevelToolPane) { QueueLiveTool(LiveTool.Bevel); bevelToolPane.Repaint(); bevelToolPane.Focus(); return; }

            bevelToolPane = ToolChannelPane.Open("Bevel", DrawBevelTool, () => bevelToolPane = null,

                new Vector2(340, 235), new Vector2(310, 215),

                execute: () => ApplyLiveTool(LiveTool.Bevel), canExecute: () => CanRunLiveTool(LiveTool.Bevel), failed: ex => { error = ex.Message; repaint(); }, owner: ToolChannelPaneOwner,

                preview: () => QueueLiveTool(LiveTool.Bevel), cancelPreview: () => CancelLiveTool(LiveTool.Bevel), history: PerformWorkspaceHistory);

            QueueLiveTool(LiveTool.Bevel);

        }

        private void DrawBevelTool()

        {

            EditorGUIUtility.labelWidth = 145;

            ToolChannelPane.FloatParameter(BevelTool, "Width", BevelWidthContent, .1f, 0);

            ToolChannelPane.IntParameter(BevelTool, "Subdivisions", BevelSubdivisionsContent, 1, 0, 32);

            ToolChannelPane.FloatParameter(BevelTool, "Tension", BevelTensionContent, 1, -1, 1);

            ToolChannelPane.EnumParameter(BevelTool, "Junction", BevelJunctionContent, StudioMeshCuts.BevelJunction.Uniform);

            GUILayout.Label("Changes preview immediately. Apply keeps the pane open; Execute finishes. The header X or Escape cancels the unapplied preview.", StudioTheme.Styles.wordWrappedMiniLabel);

        }

        private void ApplyBevelEdges() => ApplyLiveTool(LiveTool.Bevel);

    }

}


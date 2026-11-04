using System;

using System.Collections.Generic;

using UnityEditor;

using UnityEngine;



namespace MashBoxSDK.AnimationStudio

{

    internal sealed partial class StudioModelEditor

    {

        private ToolChannelPane bevelToolPane;

        // Parameter W retains the junction in bit 0 and range unlocking in bit 1.
        private const int BevelUnlockRangeFlag=2;
        private static bool BevelRangeUnlocked(Vector4 p)=>(Mathf.RoundToInt(p.w)&BevelUnlockRangeFlag)!=0;
        private const string BevelTool = "Bevel Edges";

        private static readonly GUIContent BevelWidthContent = new GUIContent("Width", "Fraction of this selection's available bevel range. 0 is no bevel; 1 is the maximum contact limit while locked; Unlock width range permits larger values. Independent of scene units.");

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
            FinishToolsForSwitch(bevelToolPane);

            PrepareMeshSelection(ComponentMode.Edge, () => CanBevelEdges,
                "Select edges on an editable static mesh to bevel.", ComponentMode.Edge);

            if (bevelToolPane) { QueueLiveTool(LiveTool.Bevel); bevelToolPane.Repaint(); bevelToolPane.Focus(); return; }

            bevelToolPane = ToolChannelPane.Open("Bevel", DrawBevelTool, () => bevelToolPane = null,

                new Vector2(340, 285), new Vector2(310, 235),

                execute: () => ApplyLiveTool(LiveTool.Bevel), canExecute: () => CanRunLiveTool(LiveTool.Bevel), failed: ex => { error = ex.Message; repaint(); }, owner: ToolChannelPaneOwner,

                preview: () => QueueLiveTool(LiveTool.Bevel), cancelPreview: () => CancelLiveTool(LiveTool.Bevel), history: PerformWorkspaceHistory, repeat: PerformRepeatLastAction);

            QueueLiveTool(LiveTool.Bevel);

        }

        private void DrawBevelTool()

        {

            EditorGUIUtility.labelWidth = 145;

            bool unlocked=ToolChannelPane.GetFloatDefault(BevelTool,"UnlockWidthRange",0)>=.5f;
            bool editedUnlock=EditorGUILayout.Toggle(new GUIContent("Unlock width range","Allow width above 1 and bypass automatic contact clamping. Geometry validity checks still apply."),unlocked);
            if(editedUnlock!=unlocked){unlocked=editedUnlock;ToolChannelPane.SetFloatDefault(BevelTool,"UnlockWidthRange",unlocked?1:0);}
            if(!unlocked)ToolChannelPane.SetFloatDefault(BevelTool,"WidthFraction",Mathf.Clamp01(ToolChannelPane.GetFloatDefault(BevelTool,"WidthFraction",.1f)));
            ToolChannelPane.FloatParameter(BevelTool, "WidthFraction", BevelWidthContent, .1f, 0, unlocked?float.PositiveInfinity:1);

            ToolChannelPane.IntParameter(BevelTool, "Subdivisions", BevelSubdivisionsContent, 1, 0, 32);

            ToolChannelPane.FloatParameter(BevelTool, "Tension", BevelTensionContent, 1, -1, 1);

            ToolChannelPane.EnumParameter(BevelTool, "Junction", BevelJunctionContent, StudioMeshCuts.BevelJunction.Uniform);

            DrawBevelFlowOptions();

            GUILayout.Label("Changes preview immediately. Apply keeps the pane open; Execute finishes. Cancel or Escape cancels the unapplied preview.", StudioTheme.Styles.wordWrappedMiniLabel);

        }

        private void ApplyBevelEdges() => ApplyLiveTool(LiveTool.Bevel);

    }

}


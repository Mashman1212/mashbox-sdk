using System.Collections.Generic;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private sealed class ComponentLiveState
        {
            internal StudioMeshToolPreview livePreview;
            internal LiveTool liveTool;
            internal bool liveValid;
            internal bool liveEvaluated;
            internal bool liveMergePair;
            internal bool liveExtrusionTogether;
            internal Vector4 liveParameters;
            internal Vector3 liveFlowParameters;
            internal ComponentMode liveMode;
            internal int[] liveSelection;
            internal int[] liveFaces;
            internal int[] liveWallTips;
            internal (int a,int b)[] liveEdges;
            internal HashSet<(int,int)>[] liveNormalEdges;
            internal HashSet<Vector3> livePoints;
            internal HashSet<(Vector3,Vector3)> liveEdgePoints;
            internal Matrix4x4 liveWorld;
            internal StudioMeshCuts.ExtrusionResult liveExtrusion;
            internal Vector3[] liveExtrusionDirections;
            internal Vector3[] liveExtrusionPositions;
            internal int liveUVDimension;
            internal List<Vector2> liveExtrusionUV = new List<Vector2>();
            internal List<Vector3> liveExtrusionUV3 = new List<Vector3>();
            internal List<Vector4> liveExtrusionUV4 = new List<Vector4>();
        }
        private void SaveComponentLiveState(ComponentLiveState state)
        {
            state.livePreview = livePreview;
            state.liveTool = liveTool;
            state.liveValid = liveValid;
            state.liveEvaluated = liveEvaluated;
            state.liveMergePair = liveMergePair;
            state.liveExtrusionTogether = liveExtrusionTogether;
            state.liveParameters = liveParameters;
            state.liveFlowParameters = liveFlowParameters;
            state.liveMode = liveMode;
            state.liveSelection = liveSelection;
            state.liveFaces = liveFaces;
            state.liveWallTips = liveWallTips;
            state.liveEdges = liveEdges;
            state.liveNormalEdges = liveNormalEdges;
            state.livePoints = livePoints;
            state.liveEdgePoints = liveEdgePoints;
            state.liveWorld = liveWorld;
            state.liveExtrusion = liveExtrusion;
            state.liveExtrusionDirections = liveExtrusionDirections;
            state.liveExtrusionPositions = liveExtrusionPositions;
            state.liveUVDimension = liveUVDimension;
            state.liveExtrusionUV = liveExtrusionUV;
            state.liveExtrusionUV3 = liveExtrusionUV3;
            state.liveExtrusionUV4 = liveExtrusionUV4;
        }
        private void LoadComponentLiveState(ComponentLiveState state)
        {
            livePreview = state.livePreview;
            liveTool = state.liveTool;
            liveValid = state.liveValid;
            liveEvaluated = state.liveEvaluated;
            liveMergePair = state.liveMergePair;
            liveExtrusionTogether = state.liveExtrusionTogether;
            liveParameters = state.liveParameters;
            liveFlowParameters = state.liveFlowParameters;
            liveMode = state.liveMode;
            liveSelection = state.liveSelection;
            liveFaces = state.liveFaces;
            liveWallTips = state.liveWallTips;
            liveEdges = state.liveEdges;
            liveNormalEdges = state.liveNormalEdges;
            livePoints = state.livePoints;
            liveEdgePoints = state.liveEdgePoints;
            liveWorld = state.liveWorld;
            liveExtrusion = state.liveExtrusion;
            liveExtrusionDirections = state.liveExtrusionDirections;
            liveExtrusionPositions = state.liveExtrusionPositions;
            liveUVDimension = state.liveUVDimension;
            liveExtrusionUV = state.liveExtrusionUV;
            liveExtrusionUV3 = state.liveExtrusionUV3;
            liveExtrusionUV4 = state.liveExtrusionUV4;
        }
    }
}
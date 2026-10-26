using System.Collections.Generic;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        // Rebind existing caches when visiting another selected mesh; retain GPU
        // buffers and face/edge indices rather than rebuilding them on every repaint.
        private sealed class ComponentDrawCache
        {
            internal ComponentWireSubset selectedWireSubset=new ComponentWireSubset(),hoverWireSubset=new ComponentWireSubset();
            internal Mesh componentSource;
            internal Mesh componentLines;
            internal Mesh componentDots;
            internal Mesh componentHighlight;
            internal Mesh componentHover;
            internal Mesh edgeMesh;
            internal Mesh selectedStatisticsMesh;
            internal Vector3[] componentPositions;
            internal List<Vector3> componentPositionBuffer = new List<Vector3>();
            internal List<OverlayGeometry> componentGeometry = new List<OverlayGeometry>();
            internal HashSet<int> drawnComponents = new HashSet<int>();
            internal HashSet<int> selectedStatisticsComponents = new HashSet<int>();
            internal int hoveredComponent = -1;
            internal int drawnHover = -1;
            internal ComponentMode hoverMode;
            internal ComponentMode drawnMode;
            internal ComponentMode selectedStatisticsMode;
            internal UnityEngine.GraphicsBuffer markerPositions;
            internal UnityEngine.GraphicsBuffer markerSelection;
            internal int[] markerSelectionIds = System.Array.Empty<int>();
            internal int markerSelectionCount;
            internal int selectedStatisticsVersion;
            internal List<(int a,int b)> cachedEdges;
            internal Dictionary<(int,int),int> cachedEdgeIndices;
            internal StudioModelDocument.Node faceNode;
            internal List<StudioMeshCuts.Face> cachedFaces;
            internal List<StudioMeshCuts.Face> componentFaceUseSource;
            internal List<int>[] cachedFaceTriangles;
            internal int[] componentFaceOffsets;
            internal int[] componentFaceUses;
            internal bool selectedStatisticsWeld;
            internal long[] cachedSelectedStatistics;
            internal Mesh handleFrameMesh;
            internal int handleFrameNode;
            internal int handleFrameVersion;
            internal ComponentMode handleFrameMode;
            internal bool handleFrameWeld;
            internal Matrix4x4 handleFrameMatrix;
            internal HashSet<int> handleFrameSelection = new HashSet<int>();
            internal HashSet<int> handleFrameVertices;
            internal IList<Vector3> handleFramePositions;
            internal Vector3 handleFrameCenter;
        }
        private void SaveComponentDrawCache(ComponentDrawCache cache)
        {
            cache.selectedWireSubset=selectedWireSubset;cache.hoverWireSubset=hoverWireSubset;
            cache.componentSource = componentSource;
            cache.componentLines = componentLines;
            cache.componentDots = componentDots;
            cache.componentHighlight = componentHighlight;
            cache.componentHover = componentHover;
            cache.edgeMesh = edgeMesh;
            cache.selectedStatisticsMesh = selectedStatisticsMesh;
            cache.componentPositions = componentPositions;
            cache.componentPositionBuffer = componentPositionBuffer;
            cache.componentGeometry = componentGeometry;
            cache.drawnComponents = drawnComponents;
            cache.selectedStatisticsComponents = selectedStatisticsComponents;
            cache.hoveredComponent = hoveredComponent;
            cache.drawnHover = drawnHover;
            cache.hoverMode = hoverMode;
            cache.drawnMode = drawnMode;
            cache.selectedStatisticsMode = selectedStatisticsMode;
            cache.markerPositions = markerPositions;
            cache.markerSelection = markerSelection;
            cache.markerSelectionIds = markerSelectionIds;
            cache.markerSelectionCount = markerSelectionCount;
            cache.selectedStatisticsVersion = selectedStatisticsVersion;
            cache.cachedEdges = cachedEdges;
            cache.cachedEdgeIndices = cachedEdgeIndices;
            cache.faceNode = faceNode;
            cache.cachedFaces = cachedFaces;
            cache.componentFaceUseSource = componentFaceUseSource;
            cache.cachedFaceTriangles = cachedFaceTriangles;
            cache.componentFaceOffsets = componentFaceOffsets;
            cache.componentFaceUses = componentFaceUses;
            cache.selectedStatisticsWeld = selectedStatisticsWeld;
            cache.cachedSelectedStatistics = cachedSelectedStatistics;
            cache.handleFrameMesh = handleFrameMesh;
            cache.handleFrameNode = handleFrameNode;
            cache.handleFrameVersion = handleFrameVersion;
            cache.handleFrameMode = handleFrameMode;
            cache.handleFrameWeld = handleFrameWeld;
            cache.handleFrameMatrix = handleFrameMatrix;
            cache.handleFrameSelection = handleFrameSelection;
            cache.handleFrameVertices = handleFrameVertices;
            cache.handleFramePositions = handleFramePositions;
            cache.handleFrameCenter = handleFrameCenter;
        }
        private void LoadComponentDrawCache(ComponentDrawCache cache)
        {
            selectedWireSubset=cache.selectedWireSubset;hoverWireSubset=cache.hoverWireSubset;
            componentSource = cache.componentSource;
            componentLines = cache.componentLines;
            componentDots = cache.componentDots;
            componentHighlight = cache.componentHighlight;
            componentHover = cache.componentHover;
            edgeMesh = cache.edgeMesh;
            selectedStatisticsMesh = cache.selectedStatisticsMesh;
            componentPositions = cache.componentPositions;
            componentPositionBuffer = cache.componentPositionBuffer;
            componentGeometry = cache.componentGeometry;
            drawnComponents = cache.drawnComponents;
            selectedStatisticsComponents = cache.selectedStatisticsComponents;
            hoveredComponent = cache.hoveredComponent;
            drawnHover = cache.drawnHover;
            hoverMode = cache.hoverMode;
            drawnMode = cache.drawnMode;
            selectedStatisticsMode = cache.selectedStatisticsMode;
            markerPositions = cache.markerPositions;
            markerSelection = cache.markerSelection;
            markerSelectionIds = cache.markerSelectionIds;
            markerSelectionCount = cache.markerSelectionCount;
            selectedStatisticsVersion = cache.selectedStatisticsVersion;
            cachedEdges = cache.cachedEdges;
            cachedEdgeIndices = cache.cachedEdgeIndices;
            faceNode = cache.faceNode;
            cachedFaces = cache.cachedFaces;
            componentFaceUseSource = cache.componentFaceUseSource;
            cachedFaceTriangles = cache.cachedFaceTriangles;
            componentFaceOffsets = cache.componentFaceOffsets;
            componentFaceUses = cache.componentFaceUses;
            selectedStatisticsWeld = cache.selectedStatisticsWeld;
            cachedSelectedStatistics = cache.cachedSelectedStatistics;
            handleFrameMesh = cache.handleFrameMesh;
            handleFrameNode = cache.handleFrameNode;
            handleFrameVersion = cache.handleFrameVersion;
            handleFrameMode = cache.handleFrameMode;
            handleFrameWeld = cache.handleFrameWeld;
            handleFrameMatrix = cache.handleFrameMatrix;
            handleFrameSelection = cache.handleFrameSelection;
            handleFrameVertices = cache.handleFrameVertices;
            handleFramePositions = cache.handleFramePositions;
            handleFrameCenter = cache.handleFrameCenter;
        }
        private static void ReleaseComponentDrawCache(ComponentDrawCache cache)
        {
            cache.selectedWireSubset.Dispose();cache.hoverWireSubset.Dispose();
            cache.markerPositions?.Dispose();cache.markerSelection?.Dispose();
            foreach(var geometry in cache.componentGeometry)if(geometry.mesh)Object.DestroyImmediate(geometry.mesh);
        }
    }
}
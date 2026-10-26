using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private Mesh handleFrameMesh;
        private int handleFrameNode, handleFrameVersion;
        private ComponentMode handleFrameMode;
        private bool handleFrameWeld;
        private Matrix4x4 handleFrameMatrix;
        private HashSet<int> handleFrameSelection = new HashSet<int>();
        private HashSet<int> handleFrameVertices;
        private IList<Vector3> handleFramePositions;
        private Vector3 handleFrameCenter;
#if MASHBOX_STUDIO_TESTS
        internal bool DisableHandleFrameCache;
        internal int HandleFrameBuilds;
#endif
        private void InvalidateHandleComponentFrame()
        {
            handleFrameMesh = null; handleFrameVertices = null; handleFramePositions = null;
            handleFrameSelection.Clear();
        }
        private void GetHandleComponentFrame(out HashSet<int> vertices, out IList<Vector3> positions, out Vector3 center)
        {
            if (!ActiveMesh)
            {
                InvalidateHandleComponentFrame(); vertices = new HashSet<int>();
                positions = System.Array.Empty<Vector3>(); center = Transforms[Active].position; return;
            }
#if MASHBOX_STUDIO_TESTS
            if (DisableHandleFrameCache)
            {
                vertices = SelectedVertices(); positions = ComponentPositions();
                center = ComponentSelectionCenter(positions, vertices); return;
            }
#endif
            var mesh = ActiveMesh;
            int version = EditorUtility.GetDirtyCount(mesh);
            var matrix = Transforms[Active].localToWorldMatrix;
            if (handleFrameMesh != mesh || handleFrameNode != Active || handleFrameVersion != version ||
                handleFrameMode != mode || handleFrameWeld != weld || !handleFrameMatrix.Equals(matrix) ||
                !handleFrameSelection.SetEquals(Components))
            {
                handleFrameVertices = SelectedVertices(); handleFramePositions = ComponentPositions();
                handleFrameCenter = ComponentSelectionCenter(handleFramePositions, handleFrameVertices);
                handleFrameSelection.Clear(); handleFrameSelection.UnionWith(Components);
                handleFrameMesh = mesh; handleFrameNode = Active; handleFrameVersion = version;
                handleFrameMode = mode; handleFrameWeld = weld; handleFrameMatrix = matrix;
#if MASHBOX_STUDIO_TESTS
                HandleFrameBuilds++;
#endif
            }
            vertices = handleFrameVertices; positions = handleFramePositions; center = handleFrameCenter;
        }
    }
}
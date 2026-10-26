using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private sealed class MoveDrag
        {
            internal int control, node;
            internal ComponentMode mode;
            internal Mesh mesh;
            internal Vector3 center, position;
            internal Quaternion orientation;
            internal Matrix4x4 worldToLocal;
            internal HashSet<int> componentIndices;
            internal int[] vertices;
            internal Vector3[] sourceVertices, workingVertices;
            internal bool undoRegistered;
        }
        private MoveDrag moveDrag;
        private MoveDrag CaptureMoveDrag(int control, Vector3 center, Quaternion orientation,
            HashSet<int> indices, Vector3[] vertices)
        {
            return new MoveDrag { control=control, node=Active, mode=mode, mesh=ActiveMesh, center=center, position=center,
                orientation=orientation, worldToLocal=Transforms[Active].worldToLocalMatrix,
                componentIndices=new HashSet<int>(indices), vertices=indices.OrderBy(i=>i).ToArray(),
                sourceVertices=vertices, workingVertices=(Vector3[])vertices.Clone() };
        }
        private bool ApplyMoveDrag(Vector3 position)
        {
            var drag=moveDrag;
            if(drag==null || drag.node!=Active || drag.mode!=mode || drag.mesh!=ActiveMesh || position.Equals(drag.position) || !FiniteScalePoint(position))return false;
            // Translation has one displacement for the whole selection. Capture the
            // linear transform once and never round-trip each point through native
            // Transform calls or accumulate changes into the previous sample.
            var delta=drag.worldToLocal.MultiplyVector(position-drag.center);
            if(!FiniteScalePoint(delta))return false;
            foreach(int i in drag.vertices)
            {
                var value=drag.sourceVertices[i]+delta;
                if(!FiniteScalePoint(value))return false;
                drag.workingVertices[i]=value;
            }
            if(!drag.undoRegistered)
            {
                PreserveFaceTopology();
                Undo.RegisterCompleteObjectUndo(drag.mesh,"Transform mesh components");
                drag.undoRegistered=true;
            }
            drag.mesh.vertices=drag.workingVertices;
            RecalculateEditableNormals(drag.mesh);drag.mesh.RecalculateBounds();
            if(drag.mesh.HasVertexAttribute(VertexAttribute.TexCoord0))drag.mesh.RecalculateTangents();
            EditorUtility.SetDirty(drag.mesh);
            UpdateComponentPivotAfterTransform(position,drag.position,Quaternion.identity,drag.orientation);
            drag.position=position;
            PositionsChanged(deferStatistics:GUIUtility.hotControl!=0, markerPositions:drag.workingVertices, markerIndices:drag.vertices);
            return true;
        }
    }
}

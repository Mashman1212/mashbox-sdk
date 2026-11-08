using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool preserveMoveUVs;
        private bool slideComponents;
        private bool SlideComponentsEnabled => slideComponents && !pivot && !SplineInputActive &&
            (mode==ComponentMode.Vertex||mode==ComponentMode.Edge);
        private const string PreserveUVMoveLabel="Move mesh components (preserve UVs)";
        private sealed class MoveDrag
        {
            internal int control, node;
            internal ComponentMode mode;
            internal Mesh mesh;
            internal Vector3 center, position, displayPosition;
            internal int[] slideCenterVertices;
            internal Quaternion orientation;
            internal Matrix4x4 worldToLocal, localToWorld;
            internal Dictionary<int,Vector3[]> slideRails;
            internal HashSet<int> componentIndices;
            internal int[] vertices;
            internal Vector3[] sourceVertices, workingVertices;
            internal bool undoRegistered;
            internal StudioMoveUVPreservation uvs;
            internal StudioMeshSymmetry.Deformation symmetry;
        }
        private MoveDrag moveDrag;
        // Capture representatives once: seam multiplicity and later coincident
        // endpoints must not change the center weighting during the gesture.
        private static int[] SlideCenterVertices(Vector3[] source, IEnumerable<int> indices)
            => indices.GroupBy(i=>source[i]).Select(g=>g.First()).ToArray();
        internal static Vector3 SlideDisplayPosition(Vector3 center, Vector3[] source, Vector3[] working,
            int[] representatives, Matrix4x4 localToWorld)
        {
            if(representatives.Length==0)return center;
            var delta=Vector3.zero;
            foreach(int i in representatives)delta+=working[i]-source[i];
            return center+localToWorld.MultiplyVector(delta/representatives.Length);
        }

        // Capture authored adjacency once. Render diagonals and coincident detached
        // islands must never become slide rails. Selected neighbors are excluded.
        private Dictionary<int, Vector3[]> CaptureSlideRails(Vector3[] source, IEnumerable<int> selection)
        {
            var selected=new HashSet<int>(selection);
            var identities=weld?SelectionWeldIdentities(source):Enumerable.Range(0,source.Length).ToArray();
            var selectedIds=new HashSet<int>(selected.Select(i=>identities[i]));
            var neighbors=new Dictionary<int,HashSet<int>>();
            void Add(int a,int b)
            {
                int id=identities[a];
                if(!selectedIds.Contains(id)||selectedIds.Contains(identities[b]))return;
                if(!neighbors.TryGetValue(id,out var values))neighbors.Add(id,values=new HashSet<int>());
                values.Add(b);
            }
            foreach(var edge in Edges()){Add(edge.a,edge.b);Add(edge.b,edge.a);}
            var result=new Dictionary<int,Vector3[]>();
            foreach(int i in selected)
                result[i]=neighbors.TryGetValue(identities[i],out var values)
                    ?values.OrderBy(v=>v).Select(v=>source[v]-source[i]).Where(v=>v.sqrMagnitude>1e-20f).ToArray()
                    :Array.Empty<Vector3>();
            return result;
        }
        internal static Vector3 SlideDisplacement(Vector3[] rails, Matrix4x4 localToWorld, Vector3 worldDelta)
        {
            float best=0;var result=Vector3.zero;
            foreach(var rail in rails)
            {
                var world=localToWorld.MultiplyVector(rail);float length=world.magnitude;
                if(length<=1e-10f)continue;
                float along=Vector3.Dot(worldDelta,world/length);
                // The best forward rail follows the gesture; clamp at its next
                // vertex without merging topology or walking onto another edge.
                if(along<=best)continue;
                best=along;result=rail*Mathf.Clamp01(along/length);
            }
            return result;
        }
        private bool SlideHandleMouseDown => !pivot && (tool==Tool.Move||tool==Tool.Rotate||tool==Tool.Scale) &&
            (mode==ComponentMode.Vertex||mode==ComponentMode.Edge) &&
            Event.current.type==EventType.MouseDown && Event.current.button==0 &&
            Event.current.shift && (Event.current.control||Event.current.command) && !Event.current.alt;

        private MoveDrag CaptureMoveDrag(int control, Vector3 center, Quaternion orientation,
            HashSet<int> indices, Vector3[] vertices, bool slide=false)
        {
            var selected=indices.OrderBy(i=>i).ToArray();
            var symmetry=CaptureSymmetryDeformation(vertices, selected);
            return new MoveDrag { control=control, node=Active, mode=mode, mesh=ActiveMesh, center=center, position=center, displayPosition=center,
                slideCenterVertices=slide?SlideCenterVertices(vertices,selected):null,
                slideRails=slide?CaptureSlideRails(vertices,selected):null, localToWorld=Transforms[Active].localToWorldMatrix,
                orientation=orientation, worldToLocal=Transforms[Active].worldToLocalMatrix,
                componentIndices=new HashSet<int>(indices), vertices=selected, symmetry=symmetry,
                uvs=preserveMoveUVs&&mode!=ComponentMode.Object&&!pivot?StudioMoveUVPreservation.Capture(ActiveMesh,vertices,symmetry?.Affected ?? selected):null,
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
            bool moved=false;
            foreach(int i in drag.vertices)
            {
                var value=drag.sourceVertices[i]+(drag.slideRails!=null
                    ?SlideDisplacement(drag.slideRails[i],drag.localToWorld,position-drag.center):delta);
                if(!FiniteScalePoint(value))return false;
                moved|=!drag.workingVertices[i].Equals(value);
                drag.workingVertices[i]=value;
            }
            if(!moved){drag.position=position;return false;}
            drag.symmetry?.Apply(drag.workingVertices);
            drag.uvs?.Evaluate(drag.workingVertices);
            if(!drag.undoRegistered)
            {
                PreserveFaceTopology();
                Undo.RegisterCompleteObjectUndo(drag.mesh,drag.slideRails!=null?"Slide mesh components":drag.uvs!=null?PreserveUVMoveLabel:"Transform mesh components");
                drag.undoRegistered=true;
            }
            drag.mesh.vertices=drag.workingVertices;
            drag.uvs?.Publish(drag.mesh);
            RecalculateEditableNormals(drag.mesh);drag.mesh.RecalculateBounds();
            if(drag.mesh.HasVertexAttribute(VertexAttribute.TexCoord0))drag.mesh.RecalculateTangents();
            EditorUtility.SetDirty(drag.mesh);
            var displayPosition=drag.slideRails!=null
                ?SlideDisplayPosition(drag.center,drag.sourceVertices,drag.workingVertices,drag.slideCenterVertices,drag.localToWorld):position;
            UpdateComponentPivotAfterTransform(displayPosition,drag.displayPosition,Quaternion.identity,drag.orientation);
            drag.displayPosition=displayPosition;
            drag.position=position;
            PositionsChanged(deferStatistics:GUIUtility.hotControl!=0, markerPositions:drag.workingVertices, markerIndices:drag.symmetry?.Affected ?? drag.vertices);
            return true;
        }
    }
}

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
        private sealed class MultiTransformTarget
        {
            internal int node;internal Mesh mesh;internal int[] indices;internal Vector3[] source,working;
            internal Matrix4x4 localToWorld,worldToLocal;
        }
        private sealed class MultiComponentGesture
        {
            internal int control;internal Tool tool;internal Vector3 center,position,factor=Vector3.one;
            internal Quaternion orientation,rotation;internal bool registered,pendingExtrude,failed;
            internal readonly List<MultiTransformTarget> targets=new List<MultiTransformTarget>();
        }
        private MultiComponentGesture multiComponentGesture;
        private Vector3 MultiComponentCenter(List<int> nodes)
        {
            var center=Vector3.zero;int count=0;
            foreach(int node in nodes)using(new ComponentTargetScope(this,node))
            {
                GetHandleComponentFrame(out var indices,out var positions,out var current);
                var target=ComponentTargetAt(node);
                if(!ReferenceEquals(target.frameVertices,indices))
                {
                    componentCenterPoints.Clear();foreach(int index in indices)componentCenterPoints.Add(positions[index]);
                    target.frameVertices=indices;target.frameWeight=componentCenterPoints.Count;
                }
                center+=current*target.frameWeight;count+=target.frameWeight;
            }
            return count==0?Transforms[Active].position:center/count;
        }
        private MultiComponentGesture CaptureMultiComponentGesture(List<int> nodes,Vector3 center,Quaternion orientation,int control)
        {
            var gesture=new MultiComponentGesture{control=control,tool=tool,center=center,position=center,orientation=orientation,rotation=orientation};
            var byMesh=new Dictionary<Mesh,MultiTransformTarget>();
            foreach(int node in nodes.OrderBy(i=>i==Active?0:1))using(new ComponentTargetScope(this,node))
            {
                if(!CanEditComponents())throw new InvalidOperationException("Every selected component target must be editable.");
                var indices=SelectedVertices();
                if(byMesh.TryGetValue(ActiveMesh,out var shared)){shared.indices=shared.indices.Concat(indices).Distinct().OrderBy(i=>i).ToArray();continue;}
                var source=ComponentPositions().ToArray();
                var target=new MultiTransformTarget{node=node,mesh=ActiveMesh,indices=indices.OrderBy(i=>i).ToArray(),source=source,working=(Vector3[])source.Clone(),localToWorld=Transforms[node].localToWorldMatrix,worldToLocal=Transforms[node].worldToLocalMatrix};
                gesture.targets.Add(target);byMesh.Add(target.mesh,target);
            }
            return gesture;
        }
        private bool ApplyMultiComponentGesture(MultiComponentGesture gesture,Vector3 position,Quaternion rotation,Vector3 factor)
        {
            factor=ConstrainComponentScale(factor);
            if(!FiniteScalePoint(position)||!InvertibleScale(factor)||gesture.position.Equals(position)&&gesture.rotation.Equals(rotation)&&gesture.factor.Equals(factor))return false;
            if(gesture.failed)return false;
            if(gesture.pendingExtrude)
            {
                gesture.pendingExtrude=false;gesture.failed=true;
                ExtrudeMultiComponentGesture(gesture,position,rotation,factor);gesture.failed=false;return true;
            }
            var turn=rotation*Quaternion.Inverse(gesture.orientation);var inverse=Quaternion.Inverse(gesture.orientation);
            foreach(var target in gesture.targets)foreach(int index in target.indices)
            {
                var world=target.localToWorld.MultiplyPoint3x4(target.source[index]);
                if(gesture.tool==Tool.Move)world+=position-gesture.center;
                else if(gesture.tool==Tool.Rotate)world=gesture.center+turn*(world-gesture.center);
                else world=gesture.center+gesture.orientation*Vector3.Scale(inverse*(world-gesture.center),factor);
                var local=target.worldToLocal.MultiplyPoint3x4(world);if(!FiniteScalePoint(local))return false;target.working[index]=local;
            }
            if(!gesture.registered)
            {
                foreach(var target in gesture.targets)using(new ComponentTargetScope(this,target.node))PreserveFaceTopology();
                Undo.RegisterCompleteObjectUndo(gesture.targets.Select(t=>(UnityEngine.Object)t.mesh).ToArray(),"Transform mesh components");gesture.registered=true;
            }
            foreach(var target in gesture.targets)using(new ComponentTargetScope(this,target.node))
            {
                target.mesh.vertices=target.working;RecalculateEditableNormals(target.mesh);target.mesh.RecalculateBounds();
                if(target.mesh.HasVertexAttribute(VertexAttribute.TexCoord0))target.mesh.RecalculateTangents();EditorUtility.SetDirty(target.mesh);
                PositionsChanged(deferStatistics:GUIUtility.hotControl!=0,markerPositions:target.working,markerIndices:target.indices);
            }
            UpdateComponentPivotAfterTransform(position,gesture.position,rotation,gesture.rotation);
            gesture.position=position;gesture.rotation=rotation;gesture.factor=factor;return true;
        }
        private void ExtrudeMultiComponentGesture(MultiComponentGesture gesture,Vector3 position,Quaternion rotation,Vector3 factor)
        {
            var before=CaptureSelection();
            foreach(var target in gesture.targets)using(new ComponentTargetScope(this,target.node))if(!CanExtrude)throw new InvalidOperationException("Shift-drag requires faces or open boundary edges on every selected mesh.");
            using var preview=new StudioMeshToolPreview(Document,gesture.targets.Select(t=>t.node));
            var results=new List<StudioMeshCuts.ExtrusionResult>();
            for(int i=0;i<gesture.targets.Count;i++)
            {
                var target=gesture.targets[i];using(new ComponentTargetScope(this,target.node))
                {
                    Vector3 TransformPoint(Vector3 local)
                    {
                        var world=target.localToWorld.MultiplyPoint3x4(local);
                        if(gesture.tool==Tool.Move)world+=position-gesture.center;
                        else if(gesture.tool==Tool.Rotate)world=gesture.center+rotation*Quaternion.Inverse(gesture.orientation)*(world-gesture.center);
                        else world=gesture.center+gesture.orientation*Vector3.Scale(Quaternion.Inverse(gesture.orientation)*(world-gesture.center),factor);
                        return target.worldToLocal.MultiplyPoint3x4(world);
                    }
                    var faces=mode==ComponentMode.Face?Components.OrderBy(c=>c).ToArray():null;
                    var edges=mode==ComponentMode.Edge?Components.OrderBy(c=>c).Select(c=>Edges()[c]).ToArray():null;
                    var work=preview.Targets[i];
                    var result=work.Topology.Extrude(work.Work,faces,edges,.1f,extrudeFacesTogether,weld,TransformPoint);results.Add(result);
                }
            }
            // Evaluate every mesh before publishing or registering Undo.
            preview.Publish(true);
            var previous=livePreview;livePreview=preview;
            try
            {
                for(int i=0;i<gesture.targets.Count;i++)using(new ComponentTargetScope(this,gesture.targets[i].node))
                {
                    RefreshLiveMeshes();Components.Clear();SelectExtrusionResult(results[i],mode==ComponentMode.Face);
                    var target=gesture.targets[i];target.indices=results[i].MovingVertices.ToArray();target.source=results[i].SourceVertices;target.working=target.mesh.vertices;
                }
                preview.Commit("Extrude mesh components",()=>RecordSelectionChange(before,false,"Extrude mesh components"));
                foreach(var target in gesture.targets)using(new ComponentTargetScope(this,target.node))RefreshLiveMeshes();
            }
            catch{preview.Restore();RestoreComponentTargets(before.targets);throw;}
            finally{livePreview=previous;}
            gesture.registered=true;gesture.position=position;gesture.rotation=rotation;gesture.factor=factor;Changed();
        }
        private bool DrawMultiComponentHandle()
        {
            if(mode==ComponentMode.Object)return false;
            var nodes=ComponentTargetNodes(true);if(nodes.Count==0||nodes.Count==1&&nodes[0]==Active){multiComponentGesture=null;return false;}
            var evt=Event.current;bool released=evt.rawType==EventType.MouseUp;
            bool moveEvent=evt.type==EventType.MouseDrag;var movePointer=evt.mousePosition;
            bool shiftDown=(mode==ComponentMode.Edge||mode==ComponentMode.Face)&&evt.type==EventType.MouseDown&&evt.button==0&&evt.shift&&!evt.alt&&!evt.control&&!evt.command;
            if(multiComponentGesture!=null&&(multiComponentGesture.tool!=tool||!released&&multiComponentGesture.control!=GUIUtility.hotControl))multiComponentGesture=null;
            var gesture=multiComponentGesture;
            var center=gesture?.center??(HasComponentPivot?componentPivot.position:MultiComponentCenter(nodes));
            var orientation=gesture?.orientation??(local?PivotOrientation:Quaternion.identity);
            var position=gesture?.position??center;var rotation=gesture?.rotation??orientation;var factor=Vector3.one;
            float size=StudioSettings.Current.handleSize;
            EditorGUI.BeginChangeCheck();
            using(new Handles.DrawingScope(Matrix4x4.identity))
            {
#if MASHBOX_STUDIO_TESTS
                TransformHandleProbe?.Invoke(tool,center,orientation,HandleUtility.GetHandleSize(center)*size);
#endif
                if(tool==Tool.Move)position=DrawMoveHandle(position,orientation,size);
                else if(tool==Tool.Rotate)
                {
                    var modifiers=evt.modifiers;if(evt.control||evt.command)evt.modifiers=modifiers&~EventModifiers.Shift;
                    try{rotation=DrawSizedRotationHandle(Handles.RotationHandleIds.@default,rotation,center,size);}finally{evt.modifiers=modifiers;}
                }
                else factor=Handles.ScaleHandle(Vector3.one,center,orientation,HandleUtility.GetHandleSize(center)*size);
                AntialiasTransformGizmo(center,HandleUtility.GetHandleSize(center)*size*1.35f);
            }
            bool changed=EditorGUI.EndChangeCheck();
            if(gesture==null&&GUIUtility.hotControl!=0){multiComponentGesture=gesture=CaptureMultiComponentGesture(nodes,center,orientation,GUIUtility.hotControl);gesture.pendingExtrude=shiftDown;}
            if(tool==Tool.Move&&moveEvent&&moveDragControl!=0&&moveDragControl==GUIUtility.hotControl)
            {
                position=ResolveMoveSnap(SceneView.currentDrawingSceneView,movePointer,position,gesture?.position??center);
                if(pivotSnapHeld||gridSnapHeld)changed=(position-(gesture?.position??center)).sqrMagnitude>1e-12f;
            }
            if(changed)
            {
                if(gesture==null)multiComponentGesture=gesture=CaptureMultiComponentGesture(nodes,center,orientation,GUIUtility.hotControl);
                Run(()=>ApplyMultiComponentGesture(gesture,position,rotation,factor));
            }
            if(released&&GUIUtility.hotControl==0)multiComponentGesture=null;
            return true;
        }
    }
}

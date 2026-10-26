using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private sealed class ComponentTarget
        {
            internal ComponentMode mode;
            internal HashSet<int> selection = new HashSet<int>();
            internal ComponentDrawCache drawing = new ComponentDrawCache();
            internal HashSet<int> frameVertices;internal int frameWeight;
        }
        private readonly Dictionary<StudioModelDocument.Node,ComponentTarget> componentTargets = new Dictionary<StudioModelDocument.Node,ComponentTarget>();
        private ComponentTarget boundComponentTarget;
        private ComponentDrawCache unboundComponentDrawing = new ComponentDrawCache();
        private readonly HashSet<int> unboundComponents = new HashSet<int>();
        private int activeComponentNode = -1;
        private int? requestedModeTarget;
        private int markingModeTarget = -1;

        private void SyncComponentTarget()
        {
            if(boundComponentTarget!=null){boundComponentTarget.mode=mode;boundComponentTarget.selection=Components;SaveComponentDrawCache(boundComponentTarget.drawing);}
            else SaveComponentDrawCache(unboundComponentDrawing);
        }
        private ComponentTarget ComponentTargetAt(int index,bool create=true)
        {
            if(!Document||index<0||index>=Document.nodes.Count)return null;
            var node=Document.nodes[index];
            if(!componentTargets.TryGetValue(node,out var target)&&create){target=new ComponentTarget{mode=mode};componentTargets.Add(node,target);}
            return target;
        }
        private void BindComponentTarget(int index)
        {
            if(activeComponentNode==index&&ReferenceEquals(boundComponentTarget,ComponentTargetAt(index,false)))return;
            SyncComponentTarget();activeComponentNode=index;boundComponentTarget=ComponentTargetAt(index);
            Components=boundComponentTarget?.selection??unboundComponents;
            LoadComponentDrawCache(boundComponentTarget?.drawing??unboundComponentDrawing);
        }
        private struct ComponentTargetScope : IDisposable
        {
            private readonly StudioModelEditor editor;private readonly int active;private readonly ComponentMode mode;
            internal ComponentTargetScope(StudioModelEditor editor,int node)
            {
                this.editor=editor;active=editor.Active;mode=editor.mode;editor.Active=node;
                editor.mode=editor.ComponentTargetAt(node).mode;
            }
            public void Dispose(){editor.SyncComponentTarget();editor.Active=active;editor.mode=mode;}
        }
        private List<int> ComponentTargetNodes(bool selectedOnly=false)
        {
            SyncComponentTarget();var result=new List<int>();
            if(!Document)return result;
            foreach(int index in objects.OrderBy(i=>i))
            {
                if(index<0||index>=Transforms.Length||!Document.nodes[index].mesh||!Transforms[index].gameObject.activeInHierarchy)continue;
                var target=ComponentTargetAt(index,false);
                if(target!=null&&target.mode!=ComponentMode.Object&&(!selectedOnly||target.selection.Count>0))result.Add(index);
            }
            return result;
        }
        private void ClearAllComponentSelections()
        {
            foreach(var loop in loopSelections.Values)loop.Reset();
            SyncComponentTarget();foreach(var target in componentTargets.Values)target.selection.Clear();unboundComponents.Clear();
        }
        private void ResetComponentTargets()
        {
            ClearAllComponentDrawCaches();componentTargets.Clear();boundComponentTarget=null;activeComponentNode=-1;
            unboundComponents.Clear();Components=unboundComponents;multiComponentGesture=null;
        }
        private void ReconcileComponentTargets()
        {
            var current=Document?new HashSet<StudioModelDocument.Node>(Document.nodes):new HashSet<StudioModelDocument.Node>();
            foreach(var node in componentTargets.Keys.Where(n=>!current.Contains(n)).ToArray())componentTargets.Remove(node);
            Active=Active;
            foreach(int index in ComponentTargetNodes())using(new ComponentTargetScope(this,index))Components.RemoveWhere(i=>i<0||i>=ComponentCount());
            multiComponentGesture=null;
        }
        private void ClearAllComponentDrawCaches()
        {
            SyncComponentTarget();
            foreach(var target in componentTargets.Values){ReleaseComponentDrawCache(target.drawing);target.drawing=new ComponentDrawCache();}
            ReleaseComponentDrawCache(unboundComponentDrawing);unboundComponentDrawing=new ComponentDrawCache();
            LoadComponentDrawCache(boundComponentTarget?.drawing??unboundComponentDrawing);
            paintPickIndex=null;InvalidateHandleComponentFrame();
        }
        private void ResetComponentHover()
        {
            hoveredComponent=-1;
            foreach(var target in componentTargets.Values)target.drawing.hoveredComponent=-1;
        }
        private int MeshAtModePointer(SceneView view,Vector2 point)
        {
            if(!view||!Document)return -1;
            var canvas=view is MashBoxStudioViewport studio?studio.ModelCanvasRect:new Rect(Vector2.zero,view.position.size);
            if(!canvas.Contains(point)||(view is MashBoxStudioViewport overlay&&overlay.IsOverViewportControls(point)))return -1;
            var ray=HandleUtility.GUIPointToWorldRay(point);int hit=-1;float nearest=float.PositiveInfinity;
            for(int node=0;node<Transforms.Length;node++)
            {
                var mesh=Document.nodes[node].mesh;if(!mesh||!Transforms[node].gameObject.activeInHierarchy)continue;
                var vertices=PickVertices(node);var triangles=mesh.triangles;var matrix=Transforms[node].localToWorldMatrix;
                for(int i=0;i<triangles.Length;i+=3)
                    if(RayTriangle(ray,matrix.MultiplyPoint3x4(vertices[triangles[i]]),matrix.MultiplyPoint3x4(vertices[triangles[i+1]]),matrix.MultiplyPoint3x4(vertices[triangles[i+2]]),out float distance)&&distance<nearest){nearest=distance;hit=node;}
            }
            return hit;
        }
        private void ActivateComponentTarget(int node,ComponentMode kind)
        {
            var target=ComponentTargetAt(node);
            if(!objects.Contains(node))target.selection.Clear();
            Active=node;objects.Add(node);
            if(target.mode!=kind)target.selection.Clear();
            target.mode=mode=kind;Components=target.selection;
        }
        private void ChangeComponentMode(ComponentMode next,SceneView view)
        {
            StopTargetWeld();
            FinishSelectionPaint();
            if(next==ComponentMode.Object){try{FinishLiveTool();}catch(InvalidOperationException){CancelLiveTool();}CloseLiveToolPanes();}
            else FinishLiveTool();
            var before=CaptureSelection();
            int target=requestedModeTarget??(view&&Event.current!=null?MeshAtModePointer(view,Event.current.mousePosition):-1);
            if(next==ComponentMode.Object)
            {
                ClearAllComponentSelections();mode=next;foreach(var item in componentTargets.Values)item.mode=ComponentMode.Object;
                if(target>=0){Active=target;objects.Add(target);}mode=next;
            }
            else
            {
                if(target>=0)ActivateComponentTarget(target,next);
                foreach(int node in objects.ToArray())
                {
                    var item=ComponentTargetAt(node);if(item==null||!Document.nodes[node].mesh)continue;
                    if(item.mode!=next)item.selection.Clear();item.mode=next;
                }
                mode=next;
            }
            pivot=false;SyncComponentTarget();RecordSelectionChange(before);repaint();
        }
        private void ExecuteModeTargetAction(MarkingAction action,SceneView view,int target)
        {
            var old=requestedModeTarget;requestedModeTarget=target;
            try{ExecuteMarkingAction(action,view);}finally{requestedModeTarget=old;}
        }
        private void PickCore(SceneView view,Rect rect,bool click)
        {
            if(mode==ComponentMode.Object){PickSingleComponentTarget(view,rect,click);return;}
            if(click)
            {
                int hit=MeshAtModePointer(view,end);
                if(hit<0){if(!additive&&!subtract)ClearAllComponentSelections();return;}
                if(Active>=0&&hit!=Active&&!additive&&!subtract)
                {
                    // A plain click on another mesh chooses that object. Hover
                    // and explicit component-mode acquisition remain independent.
                    FinishLiveTool(); CloseLiveToolPanes(); ClearAllComponentSelections();
                    mode=ComponentMode.Object;foreach(var item in componentTargets.Values)item.mode=ComponentMode.Object;
                    objects.Clear();Active=hit;objects.Add(hit);pivot=false;return;
                }
                var kind=mode;
                ActivateComponentTarget(hit,kind);PickSingleComponentTarget(view,rect,true);return;
            }
            var nodes=ComponentTargetNodes();if(!additive&&!subtract)ClearAllComponentSelections();
            foreach(int node in nodes)using(new ComponentTargetScope(this,node))PickSingleComponentTarget(view,rect,false);
        }
        private void DrawComponents(SceneView view)
        {
            if(Event.current.type!=EventType.Repaint||!Document||mode==ComponentMode.Object)return;
            var nodes=ComponentTargetNodes();var depth=Handles.zTest;var color=Handles.color;bool faces=false;
            try
            {
                Handles.zTest=displayXray?UnityEngine.Rendering.CompareFunction.Always:UnityEngine.Rendering.CompareFunction.LessEqual;
                foreach(int node in nodes)using(new ComponentTargetScope(this,node)){DrawComponentBatches(view);faces|=mode==ComponentMode.Face;}
                if(faces)
                {
                    DrawSceneWireframes(view.camera);
                    foreach(int node in nodes)using(new ComponentTargetScope(this,node))if(mode==ComponentMode.Face&&drawnHover>=0)DrawHoveredFaceBorder(drawnHover,2.5f);
                }
            }
            finally{Handles.zTest=depth;Handles.color=color;}
        }
        private long[] CountAllSelectedComponents()
        {
            var total=new long[4];foreach(int node in ComponentTargetNodes(true))using(new ComponentTargetScope(this,node)){var counts=CountSelectedComponents();for(int i=0;i<4;i++)total[i]+=counts[i];}return total;
        }
        private bool TrySelectedComponentBounds(out Bounds bounds)
        {
            bounds=default;bool found=false;
            foreach(int node in ComponentTargetNodes(true))using(new ComponentTargetScope(this,node))
            {
                var points=ComponentPositions();var matrix=Transforms[node].localToWorldMatrix;
                foreach(int index in SelectedVertices())
                {
                    var point=matrix.MultiplyPoint3x4(points[index]);
                    if(!found){bounds=new Bounds(point,Vector3.zero);found=true;}else bounds.Encapsulate(point);
                }
            }
            if(found)bounds.size=Vector3.Max(bounds.size,Vector3.one*.05f);return found;
        }
        private void RefreshOtherComponentCaches(StudioMeshToolPreview preview)
        {
            foreach(int index in objects.ToArray())
            {
                if(index==Active||index<0||index>=Document.nodes.Count||ComponentTargetAt(index,false)==null)continue;
                var publication=preview.Targets.FirstOrDefault(t=>t.Live==Document.nodes[index].mesh);if(publication==null)continue;
                using(new ComponentTargetScope(this,index))
                {
                    if(publication.StructureChanged)
                    {edgeMesh=null;cachedEdges=null;faceNode=null;cachedFaces=null;cachedFaceTriangles=null;ClearComponentDrawing();}
                    else RefreshComponentPositions(publication.VertexUpdateStart,publication.PublishedPositions);
                }
            }
        }
    }
}

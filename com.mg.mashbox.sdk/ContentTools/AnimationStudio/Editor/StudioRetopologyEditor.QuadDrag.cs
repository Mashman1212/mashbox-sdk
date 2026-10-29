using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private bool quadDragHeld,quadDragMoved;
        private int quadDragControl,quadDragUndo;
        private Vector2 quadDragMouse;
        private Vector3 quadDragStart,quadDragNormal,quadDragRight,quadDragUp;
        private Vector3[] quadDragPreview;

        private void BeginQuadDrag(Vector3 start,Vector3 normal,Vector2 mouse,int control)
        {
            // A short click records exactly the same first point as before.
            // Only movement beyond the click threshold turns it into a rectangle.
            AddConstructionPoint(start,normal);quadDragUndo=Undo.GetCurrentGroup();
            quadDragStart=start;quadDragNormal=normal;quadDragMouse=mouse;
            quadDragRight=view.camera.transform.right;quadDragUp=view.camera.transform.up;
            PrepareQuadDragWelding();quadDragSnaps=null;quadDragWarning=null;
            quadDragHeld=true;quadDragMoved=false;quadDragPreview=null;quadDragControl=control;
            GUIUtility.hotControl=control;GUIUtility.keyboardControl=0;EditorGUIUtility.editingTextField=false;
        }

        private void ReleaseQuadDrag()
        {
            if(quadDragHeld&&GUIUtility.hotControl==quadDragControl)GUIUtility.hotControl=0;
            quadDragHeld=quadDragMoved=false;quadDragPreview=null;
            quadDragSnaps=null;quadDragBorderVertices=null;quadDragWarning=null;
        }

        internal static Vector3[] DragQuadCorners(Vector3 start,Vector3 opposite,Vector3 normal,Vector3 cameraRight,Vector3 cameraUp)
        {
            if(normal.sqrMagnitude<1e-12f)throw new InvalidOperationException("Choose a surface with a valid normal.");
            normal.Normalize();var right=Vector3.ProjectOnPlane(cameraRight,normal);
            if(right.sqrMagnitude<1e-8f)right=Vector3.Cross(cameraUp,normal);
            if(right.sqrMagnitude<1e-8f)throw new InvalidOperationException("Orbit the view to face this surface before dragging a quad.");
            right.Normalize();var up=Vector3.Cross(normal,right).normalized;
            if(Vector3.Dot(up,cameraUp)<0)up=-up;
            var delta=opposite-start;float width=Vector3.Dot(delta,right),height=Vector3.Dot(delta,up);
            float epsilon=Mathf.Max(.000001f,delta.magnitude*.00001f);
            if(Mathf.Abs(width)<=epsilon||Mathf.Abs(height)<=epsilon)
                throw new InvalidOperationException("Drag diagonally from the first corner to give the quad width and height.");
            return new[]{start,start+right*width,opposite,start+up*height};
        }

        private void UpdateQuadDrag(Vector2 mouse)
        {
            quadDragPreview=null;quadDragSnaps=null;quadDragWarning=null;
            Vector3 opposite;
            int snap=PickQuadDragBorder(mouse);
            if(snap>=0)opposite=Active.vertices[snap];
            else if(DrawingHit(mouse,out var hit))
            {
                opposite=hit.point+hit.normal*Document.offset;
            }
            else
            {
                // Keep the rectangle responsive just beyond the silhouette.
                var plane=new Plane(quadDragNormal,quadDragStart);var ray=HandleUtility.GUIPointToWorldRay(mouse);
                if(!plane.Raycast(ray,out float distance)||distance<0)return;
                opposite=ray.GetPoint(distance);
                if(Document.project)opposite=Surface.ProjectAlong(opposite,quadDragNormal,Document.offset,Vector3.Distance(quadDragStart,opposite));
            }
            try
            {
                var points=DragQuadCorners(quadDragStart,opposite,quadDragNormal,quadDragRight,quadDragUp);
                bool Visible(int id)=>Active.showOutput&&view.camera.WorldToViewportPoint(Active.vertices[id]).z>0&&IsSelectionPointVisible(Active.vertices[id],false);
                quadDragSnaps=SnapDraggedQuad(points,Active.vertices,quadDragBorderVertices??Array.Empty<int>(),HandleUtility.WorldToGUIPoint,24,Visible);
                ProjectQuadDragCorners(points,quadDragNormal);
                quadDragSnaps=SnapDraggedQuad(points,Active.vertices,quadDragBorderVertices??Array.Empty<int>(),HandleUtility.WorldToGUIPoint,24,Visible);
                if(!ValidDraggedQuad(points))return;
                quadDragWarning=DraggedQuadAttachmentWarning(Active,points,quadDragNormal,Mathf.Max(.000001f,Active.reference.bounds.size.magnitude*.00001f));
                quadDragPreview=points;
            }
            catch(InvalidOperationException){quadDragPreview=null;}
        }

        private void ProjectQuadDragCorners(Vector3[] points,Vector3 normal)
        {
            if(!Document.project)return;
            float span=Vector3.Distance(points[0],points[2]);
            if(quadDragSnaps==null||quadDragSnaps[1]<0)points[1]=Surface.ProjectAlong(points[1],normal,Document.offset,span);
            if(quadDragSnaps==null||quadDragSnaps[3]<0)points[3]=Surface.ProjectAlong(points[3],normal,Document.offset,span);
        }

        internal static bool ValidDraggedQuad(Vector3[] points)
        {
            if(points==null||points.Length!=4)return false;
            float span=(points[2]-points[0]).sqrMagnitude;if(span<1e-12f)return false;
            var a=Vector3.Cross(points[1]-points[0],points[2]-points[0]);
            var b=Vector3.Cross(points[2]-points[0],points[3]-points[0]);
            if(a.sqrMagnitude<=span*span*1e-10f||b.sqrMagnitude<=span*span*1e-10f||Vector3.Dot(a,b)<=0)return false;
            var normal=(a+b).normalized;
            for(int i=0;i<4;i++)
                if(Vector3.Dot(Vector3.Cross(points[(i+1)%4]-points[i],points[(i+2)%4]-points[(i+1)%4]),normal)<=span*1e-7f)return false;
            return true;
        }

        private void CreateDraggedQuad(Vector3[] points,Vector3 normal,int undoGroup)
        {
            if(!ValidDraggedQuad(points))throw new InvalidOperationException("Drag out a non-folded quad with width and height.");
            var warning=DraggedQuadAttachmentWarning(Active,points,normal,Mathf.Max(.000001f,Active.reference.bounds.size.magnitude*.00001f));
            if(warning!=null)throw new InvalidOperationException(warning);
            var first=corners[0];var firstNormal=cornerNormal;
            try
            {
                corners.Clear();corners.AddRange(points);cornerNormal=normal;StoreToolState();
                var grid=new StudioRetopologyDocument.Part{vertices=points.ToList(),uv=new System.Collections.Generic.List<Vector2>{Vector2.zero,Vector2.right,Vector2.one,Vector2.up}};
                grid.faces.Add(new StudioRetopologyDocument.Face(new[]{0,1,2,3}));
                Change("Draw projected quad",()=>
                {
                    AppendDraggedQuadGrid(grid,normal,false);
                    if(Document.symmetry&&!points.All(p=>Mathf.Abs(p.x)<.000001f))
                    {
                        grid.vertices=grid.vertices.Select(p=>new Vector3(-p.x,p.y,p.z)).ToList();
                        Array.Reverse(grid.faces[0].vertices);
                        AppendDraggedQuadGrid(grid,new Vector3(-normal.x,normal.y,normal.z),true);
                    }
                    Active.needsUV=true;
                });
                status="Quad created from the dragged corner. Undo reverses the whole drag.";
            }
            catch
            {
                corners.Clear();corners.Add(first);cornerNormal=firstNormal;StoreToolState();throw;
            }
            finally {Undo.CollapseUndoOperations(undoGroup);}
        }

        private bool HandleQuadDrag(Event evt,MashBoxStudioViewport canvas,bool overUI)
        {
            int control=GUIUtility.GetControlID("Retopology quad drag".GetHashCode(),FocusType.Passive);
            if(!quadDragHeld){quadDragControl=control;return false;}
            if(Active==null||activeTool!=RetopoTool.Quad||GUIUtility.hotControl!=quadDragControl||evt.type==EventType.Ignore||evt.type==EventType.MouseLeaveWindow)
            {ReleaseQuadDrag();repaint();return false;}
            if(evt.type==EventType.KeyDown&&evt.keyCode==KeyCode.Escape)
            {ReleaseQuadDrag();CancelConstruction();evt.Use();return true;}
            bool usable=!overUI&&!evt.alt&&canvas.ModelCanvasRect.Contains(evt.mousePosition);
            if(evt.type==EventType.MouseDrag&&evt.button==0)
            {
                if((evt.mousePosition-quadDragMouse).sqrMagnitude>=16)quadDragMoved=true;
                if(quadDragMoved)
                {if(usable)UpdateQuadDrag(evt.mousePosition);else quadDragPreview=null;}
                evt.Use();repaint();return true;
            }
            if(evt.type==EventType.MouseUp&&evt.button==0)
            {
                bool dragged=quadDragMoved||(evt.mousePosition-quadDragMouse).sqrMagnitude>=16;
                if(dragged&&usable)UpdateQuadDrag(evt.mousePosition);
                var points=dragged&&usable&&quadDragWarning==null?quadDragPreview:null;var normal=quadDragNormal;int undoGroup=quadDragUndo;
                var warning=quadDragWarning;
                ReleaseQuadDrag();
                if(points!=null)Run(()=>CreateDraggedQuad(points,normal,undoGroup));
                else if(dragged)status=warning??"No quad created. The first point is kept; drag diagonally across the reference to give it width and height.";
                evt.Use();repaint();return true;
            }
            if(evt.type==EventType.Repaint)
            {
                var color=Handles.color;var depth=Handles.zTest;
                try
                {
                    Handles.zTest=CompareFunction.Always;
                    if(quadDragPreview!=null)
                    {
                        Handles.color=quadDragWarning==null?new Color(.4f,1f,.8f,.18f):new Color(1,.25f,.15f,.18f);Handles.DrawAAConvexPolygon(quadDragPreview);
                        Handles.color=new Color(.4f,1f,.8f);var p=quadDragPreview;
                        Handles.DrawAAPolyLine(2,p[0],p[1],p[2],p[3],p[0]);
                        DrawQuadDragWeldPreview(p);
                    }
                    else
                    {
                        Handles.color=new Color(.4f,1f,.8f);
                        Handles.DotHandleCap(0,quadDragStart,Quaternion.identity,HandleUtility.GetHandleSize(quadDragStart)*.035f,EventType.Repaint);
                        if(quadDragMoved)Handles.Label(quadDragStart,"Drag diagonally to give the quad width and height");
                    }
                }
                finally{Handles.color=color;Handles.zTest=depth;}
            }
            return true;
        }
    }
}

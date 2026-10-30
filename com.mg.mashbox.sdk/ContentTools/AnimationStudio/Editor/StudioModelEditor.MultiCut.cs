using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private struct MultiPoint { internal int Face;internal Vector3 Local;internal bool Boundary,Preview; }
        private readonly List<MultiPoint> multiPoints=new List<MultiPoint>();
        private readonly List<List<StudioMeshCuts.FaceStroke>> multiLinks=new List<List<StudioMeshCuts.FaceStroke>>();
        private List<StudioMeshCuts.FaceStroke> multiSlicePreview=new List<StudioMeshCuts.FaceStroke>();
        private MultiPoint? multiHover;
        private Vector2 multiMouse,multiSliceStart;
        private Plane multiSlicePlane;
        private bool multiSlicing;
        private bool MultiLoopClosed => multiPoints.Count >= 4 && SameMultiPoint(multiPoints[0],multiPoints[multiPoints.Count-1]);
        private bool SameMultiPoint(MultiPoint a,MultiPoint b)
        { float tolerance=Mathf.Max(1e-7f,ActiveMesh.bounds.size.magnitude*1e-6f);return (a.Local-b.Local).sqrMagnitude<=tolerance*tolerance; }
        private bool CanCloseMultiLoop()
        {
            var unique=new List<MultiPoint>();
            foreach(var p in multiPoints)if(!unique.Any(q=>SameMultiPoint(p,q)))unique.Add(p);
            return unique.Count>=3;
        }
        private bool HasMultiCutGesture=>multiPoints.Count>0||multiSlicing;
        private void ReleaseMultiCutPointer()
        { if(cutTool!=CutTool.MultiCut)return;if(cutControl!=0&&GUIUtility.hotControl==cutControl)GUIUtility.hotControl=0;multiSlicing=false;multiSlicePreview.Clear(); }
        private void ResetMultiCutGesture()
        { if(GUIUtility.hotControl==cutControl)GUIUtility.hotControl=0;multiPoints.Clear();multiLinks.Clear();multiSlicePreview.Clear();multiHover=null;multiSlicing=false; }
        private bool MultiCutKey(Event evt)
        {
            if(cutTool!=CutTool.MultiCut)return false;
            if(evt.keyCode==KeyCode.W&&!evt.alt&&!evt.control&&!evt.command&&!evt.shift)
            {
                bool complete=!HasMultiCutGesture;
                if(multiPoints.Count>0)Run(()=>{CommitMultiCut(CollectMultiStrokes());complete=true;});
                if(complete)ActivateTool(Tool.Move);
                evt.Use();repaint();return true;
            }
            return false;
        }
        private void RemoveMultiPoint()
        { if(multiPoints.Count>0)multiPoints.RemoveAt(multiPoints.Count-1);if(multiLinks.Count>=multiPoints.Count&&multiLinks.Count>0)multiLinks.RemoveAt(multiLinks.Count-1);repaint(); }
        private void CommitPendingCutGesture()
        {
            if(cutTool==CutTool.Bisect){Run(ApplyBisect);return;}
            if(cutTool==CutTool.MultiCut&&multiPoints.Count>0)Run(()=>CommitMultiCut(CollectMultiStrokes()));
            else Run(()=>CommitCuts(new List<StudioMeshCuts.Cut>(pendingCuts)));
        }
        private List<StudioMeshCuts.FaceStroke> CollectMultiStrokes()
        {
            if(multiPoints.Count<2||(!multiPoints[multiPoints.Count-1].Boundary&&!multiPoints[multiPoints.Count-1].Preview))throw new InvalidOperationException("Finish the path on a perimeter or a pending cut vertex or line before pressing W.");
            var result=new List<StudioMeshCuts.FaceStroke>();
            foreach(var link in multiLinks)foreach(var part in link)
            {
                if(result.Count>0&&result[result.Count-1].Face==part.Face&&(result[result.Count-1].Points.Last()-part.Points[0]).sqrMagnitude<1e-10f)
                    result[result.Count-1].Points.AddRange(part.Points.Skip(1));
                else {var copy=new StudioMeshCuts.FaceStroke(part.Face);copy.Points.AddRange(part.Points);result.Add(copy);}
            }
            return result;
        }
        private void CommitMultiCut(IList<StudioMeshCuts.FaceStroke> strokes)
        {
            if(!CanCutMesh)throw new InvalidOperationException("Select an editable static mesh before applying a cut.");
            if(cutTopology==null||cutMesh!=ActiveMesh){cutTopology=new StudioMeshCuts(Document.nodes[Active]);cutMesh=ActiveMesh;}
            int group=-1;
            try
            {
                cutTopology.ApplyMultiCut(Document.nodes[Active],strokes,()=>{Undo.IncrementCurrentGroup();group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Multi-Cut");Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[]{Document,ActiveMesh},"Multi-Cut");});EditorUtility.SetDirty(ActiveMesh);EditorUtility.SetDirty(Document);
                ResetCutGesture();MeshTopologyChanged();cutTopology=new StudioMeshCuts(Document.nodes[Active]);cutMesh=ActiveMesh;error=null;
            }
            catch { if(group>=0)Undo.RevertAllDownToGroup(group);throw; }
            finally { if(group>=0)Undo.CollapseUndoOperations(group); }
        }
        private MultiPoint? MultiSurface(SceneView view,Vector2 mouse,bool snap=true)
        {
            var ray=PaintPointerRay(view.camera,mouse);var transform=Transforms[Active];var triangles=cutTopology.BevelSourceTriangles(ActiveMesh);
            float nearest=float.MaxValue;int face=-1;
            for(int f=0;f<triangles.Length;f++)for(int i=0;i+2<triangles[f].Count;i+=3)
                if(RayTriangle(ray,transform.TransformPoint(cutTopology.Positions[triangles[f][i]]),transform.TransformPoint(cutTopology.Positions[triangles[f][i+1]]),transform.TransformPoint(cutTopology.Positions[triangles[f][i+2]]),out float d)&&d<nearest)
                {nearest=d;face=f;}
            if(face<0)
            {
                if(!snap)return null;
                float best=10,depth=float.PositiveInfinity;MultiPoint? edgePoint=null;
                for(int f=0;f<cutTopology.Faces.Count;f++)
                {
                    var v=cutTopology.Faces[f].Vertices;
                    for(int e=0;e<v.Count;e++)
                    {
                        int a=v[e],b=v[(e+1)%v.Count];float t=MultiEdgeFraction(view,mouse,a,b);
                        var localEdge=Vector3.Lerp(cutTopology.Positions[a],cutTopology.Positions[b],t);var world=transform.TransformPoint(localEdge);
                        float z=view.camera.WorldToViewportPoint(world).z;if(z<=0)continue;
                        float d=Vector2.Distance(mouse,PaintWorldToGUI(view.camera,world));
                        if(d<best-.01f||(Mathf.Abs(d-best)<.01f&&z<depth))
                        {
                            if(Vector2.Distance(mouse,PaintWorldToGUI(view.camera,transform.TransformPoint(cutTopology.Positions[a])))<7)localEdge=cutTopology.Positions[a];
                            else if(Vector2.Distance(mouse,PaintWorldToGUI(view.camera,transform.TransformPoint(cutTopology.Positions[b])))<7)localEdge=cutTopology.Positions[b];
                            best=d;depth=z;edgePoint=new MultiPoint{Face=f,Local=localEdge,Boundary=true};
                        }
                    }
                }
                return edgePoint;
            }
            Vector3 local=transform.InverseTransformPoint(ray.GetPoint(nearest));
            if(snap)
            {
                float best=10;var v=cutTopology.Faces[face].Vertices;
                for(int i=0;i<v.Count;i++)
                {
                    int a=v[i],b=v[(i+1)%v.Count];float t=MultiEdgeFraction(view,mouse,a,b);
                    var pa=cutTopology.Positions[a];var pb=cutTopology.Positions[b];
                    if(Vector2.Distance(mouse,PaintWorldToGUI(view.camera,transform.TransformPoint(pa)))<7)t=0;
                    else if(Vector2.Distance(mouse,PaintWorldToGUI(view.camera,transform.TransformPoint(pb)))<7)t=1;
                    var p=Vector3.Lerp(pa,pb,t);float distance=Vector2.Distance(mouse,PaintWorldToGUI(view.camera,transform.TransformPoint(p)));
                    if(distance<best){best=distance;local=p;}
                }
            }
            return new MultiPoint{Face=face,Local=local,Boundary=cutTopology.OnCutBoundary(face,local,out _)};
        }
        private MultiPoint? SnapMultiPreview(SceneView view,Vector2 mouse,MultiPoint? surface)
        {
            if(!surface.HasValue||multiPoints.Count==0)return surface;
            var transform=Transforms[Active];MultiPoint? nearest=null;float best=7;
            bool Visible(int face,Vector3 local) => view.camera.WorldToViewportPoint(transform.TransformPoint(local)).z>0 &&
                (face==surface.Value.Face||cutTopology.OnCutBoundary(surface.Value.Face,local,out _));
            void Vertex(int face,Vector3 local)
            {
                if(!Visible(face,local))return;
                float distance=Vector2.Distance(mouse,PaintWorldToGUI(view.camera,transform.TransformPoint(local)));
                if(distance>=best)return;best=distance;
                nearest=new MultiPoint{Face=face,Local=local,Boundary=cutTopology.OnCutBoundary(face,local,out _),Preview=true};
            }
            foreach(var point in multiPoints)Vertex(point.Face,point.Local);
            foreach(var link in multiLinks)foreach(var part in link)foreach(var point in part.Points)Vertex(part.Face,point);
            if(nearest.HasValue)return nearest;
            best=10;
            foreach(var link in multiLinks)foreach(var part in link)for(int i=0;i+1<part.Points.Count;i++)
            {
                var a=part.Points[i];var b=part.Points[i+1];
                var local=Vector3.Lerp(a,b,MultiSegmentFraction(view,mouse,a,b));
                if(!Visible(part.Face,local))continue;
                float distance=Vector2.Distance(mouse,PaintWorldToGUI(view.camera,transform.TransformPoint(local)));
                if(distance>=best)continue;best=distance;
                nearest=new MultiPoint{Face=part.Face,Local=local,Boundary=cutTopology.OnCutBoundary(part.Face,local,out _),Preview=true};
            }
            return nearest??surface;
        }
        private bool MultiFaceAtMouse(SceneView view,int face,Vector2 mouse,out Vector3 local)
        {
            var transform=Transforms[Active];var ray=PaintPointerRay(view.camera,mouse);
            var origin=transform.InverseTransformPoint(ray.origin);var direction=transform.InverseTransformVector(ray.direction);
            var plane=new Plane(cutTopology.CutFaceNormal(face),cutTopology.Positions[cutTopology.Faces[face].Vertices[0]]);
            float denominator=Vector3.Dot(plane.normal,direction);
            if(Mathf.Abs(denominator)<1e-9f){local=default;return false;}
            float t=-plane.GetDistanceToPoint(origin)/denominator;local=origin+direction*t;return t>=0;
        }
        private List<StudioMeshCuts.FaceStroke> TraceMultiSegment(SceneView view,MultiPoint a,MultiPoint b)
        {
            var transform=Transforms[Active];Vector2 start=PaintWorldToGUI(view.camera,transform.TransformPoint(a.Local)),end=PaintWorldToGUI(view.camera,transform.TransformPoint(b.Local));
            var delta=end-start;if(delta.sqrMagnitude<.01f)throw new InvalidOperationException("Choose a different point for the next segment.");
            var fractions=new List<float>{0,1};
            // Screen-line crossings partition the path into visible source-face intervals.
            foreach(var face in cutTopology.Faces)for(int e=0;e<face.Vertices.Count;e++)
            {
                var p=PaintWorldToGUI(view.camera,transform.TransformPoint(cutTopology.Positions[face.Vertices[e]]));
                var q=PaintWorldToGUI(view.camera,transform.TransformPoint(cutTopology.Positions[face.Vertices[(e+1)%face.Vertices.Count]]));
                var side=q-p;float den=delta.x*side.y-delta.y*side.x;if(Mathf.Abs(den)<1e-7f)continue;
                float t=((p.x-start.x)*side.y-(p.y-start.y)*side.x)/den,u=((p.x-start.x)*delta.y-(p.y-start.y)*delta.x)/den;
                if(t>1e-5f&&t<1-1e-5f&&u>=0&&u<=1)fractions.Add(t);
            }
            fractions.Sort();var sorted=new List<float>();foreach(float t in fractions)if(sorted.Count==0||t-sorted.Last()>1e-5f)sorted.Add(t);
            var result=new List<StudioMeshCuts.FaceStroke>();
            for(int i=0;i+1<sorted.Count;i++)
            {
                var hit=MultiSurface(view,Vector2.Lerp(start,end,(sorted[i]+sorted[i+1])*.5f),false);
                if(!hit.HasValue)throw new InvalidOperationException("The path leaves the mesh silhouette; place points along adjacent faces.");
                int face=hit.Value.Face;
                if(!MultiFaceAtMouse(view,face,Vector2.Lerp(start,end,sorted[i]),out var entry)||!MultiFaceAtMouse(view,face,Vector2.Lerp(start,end,sorted[i+1]),out var exit))throw new InvalidOperationException("Cannot project this segment onto its face.");
                if(i==0){if(face!=a.Face&&!cutTopology.OnCutBoundary(face,a.Local,out _))throw new InvalidOperationException("The next segment is not on an adjacent face.");entry=a.Local;}
                if(i==sorted.Count-2){if(face!=b.Face&&!cutTopology.OnCutBoundary(face,b.Local,out _))throw new InvalidOperationException("The next segment is occluded by another face.");exit=b.Local;}
                if(result.Count>0)
                {
                    var previous=result.Last();var join=previous.Points.Last();float tolerance=Mathf.Max(1e-6f,ActiveMesh.bounds.size.magnitude*1e-5f);
                    if(previous.Face==face)
                    {
                        if((join-entry).sqrMagnitude>tolerance*tolerance)throw new InvalidOperationException("The screen path crosses disconnected or occluded surfaces.");
                        previous.Points[previous.Points.Count-1]=exit;continue;
                    }
                    // Resolve transitions on the actual shared authored edge, not two
                    // independently rounded ray/plane intersection positions.
                    if(!MultiSharedCrossing(view,previous.Face,face,Vector2.Lerp(start,end,sorted[i]),out join))
                        throw new InvalidOperationException("Faces do not share this crossing; place a point on their shared edge.");
                    previous.Points[previous.Points.Count-1]=join;entry=join;
                }
                result.Add(new StudioMeshCuts.FaceStroke(face,entry,exit));
            }
            return result;
        }
        private bool MultiSharedCrossing(SceneView view,int from,int to,Vector2 screen,out Vector3 point)
        {
            var a=cutTopology.Faces[from].Vertices;var b=cutTopology.Faces[to].Vertices;var transform=Transforms[Active];
            for(int i=0;i<a.Count;i++)for(int j=0;j<b.Count;j++)
            {
                int x=a[i],y=a[(i+1)%a.Count];
                if(cutTopology.NormalEdgeKey(x,y)!=cutTopology.NormalEdgeKey(b[j],b[(j+1)%b.Count]))continue;
                float t=MultiEdgeFraction(view,screen,x,y);var p=Vector3.Lerp(cutTopology.Positions[x],cutTopology.Positions[y],t);
                if(Vector2.Distance(screen,PaintWorldToGUI(view.camera,transform.TransformPoint(p)))>.75f)continue;
                point=p;return true;
            }
            point=default;return false;
        }
        private Rect MultiProjectedBounds(SceneView view)
        {
            var transform=Transforms[Active];var min=new Vector2(float.PositiveInfinity,float.PositiveInfinity);var max=-min;
            // The projected local bounding box can extend far beyond a rotated
            // object's visible extent. Use the actual source vertices instead.
            var vertices=bisectPreview!=null?bisectPreview.Targets[0].Source.vertices:cutTopology?.Positions??ActiveMesh.vertices;
            foreach(var vertex in vertices)
            {var p=PaintWorldToGUI(view.camera,transform.TransformPoint(vertex));min=Vector2.Min(min,p);max=Vector2.Max(max,p);}
            return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
        }
        // Exact triangle hits, without the edge/vertex snap radius. Empty space
        // inside the projected bounds is a valid through-slice endpoint.
        private bool SlicePointerOnMesh(SceneView view,Vector2 pointer)
        {
            var mesh=bisectPreview!=null?bisectPreview.Targets[0].Source:ActiveMesh;
            var vertices=mesh.vertices;var triangles=mesh.triangles;var transform=Transforms[Active];
            var ray=PaintPointerRay(view.camera,pointer);
            for(int i=0;i<triangles.Length;i+=3)
                if(RayTriangle(ray,transform.TransformPoint(vertices[triangles[i]]),transform.TransformPoint(vertices[triangles[i+1]]),transform.TransformPoint(vertices[triangles[i+2]]),out _))return true;
            return false;
        }
        private List<StudioMeshCuts.FaceStroke> MultiSlice(SceneView view,Vector2 a,Vector2 b)
            =>SliceSurfaceStrokes(view,a,b,cutTopology,out multiSlicePlane);
        private bool SliceSegmentCrossesMesh(SceneView view,Vector2 a,Vector2 b)
        {
            var source=bisectPreview!=null?bisectPreview.Targets[0].ModifierSourceNode():Document.nodes[Active];
            return SliceSurfaceStrokes(view,a,b,new StudioMeshCuts(source),out _).Count>0;
        }
        private List<StudioMeshCuts.FaceStroke> SliceSurfaceStrokes(SceneView view,Vector2 a,Vector2 b,StudioMeshCuts topology,out Plane localPlane)
        {
            localPlane=default;
            if((a-b).sqrMagnitude<9)return new List<StudioMeshCuts.FaceStroke>();
            var transform=Transforms[Active];
            if(!TryCameraSlicePlane(view,a,b,out var worldPlane))return new List<StudioMeshCuts.FaceStroke>();
            var matrix=transform.localToWorldMatrix;var normal=matrix.transpose.MultiplyVector(worldPlane.normal);float length=normal.magnitude;
            if(length<1e-12f)return new List<StudioMeshCuts.FaceStroke>();
            localPlane=new Plane(normal/length,(Vector3.Dot(worldPlane.normal,matrix.MultiplyPoint3x4(Vector3.zero))+worldPlane.distance)/length);
            var delta=b-a;
            return topology.SurfaceSlice(localPlane).Where(stroke=>stroke.Points.All(point=>
            {var projected=PaintWorldToGUI(view.camera,transform.TransformPoint(point));float t=Vector2.Dot(projected-a,delta)/delta.sqrMagnitude;return t>=-1e-5f&&t<=1+1e-5f;})).ToList();
        }
        private static bool TryCameraSlicePlane(SceneView view,Vector2 a,Vector2 b,out Plane plane)
        {
            var ra=PaintPointerRay(view.camera,a);var rb=PaintPointerRay(view.camera,b);
            var normal=view.orthographic?Vector3.Cross(rb.origin-ra.origin,ra.direction):Vector3.Cross(ra.direction,rb.direction);
            plane=new Plane(normal.normalized,view.orthographic?ra.origin:view.camera.transform.position);return normal.sqrMagnitude>1e-12f;
        }
        private void CommitMultiSlice(Plane plane)
        {
            if(!CanCutMesh)throw new InvalidOperationException("Select an editable static mesh before slicing.");
            var topology=new StudioMeshCuts(Document.nodes[Active]);int group=-1;
            try
            {
                bool changed=topology.ApplyPlaneSlice(Document.nodes[Active],plane,()=>{Undo.IncrementCurrentGroup();group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Multi-Cut");Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[]{Document,ActiveMesh},"Multi-Cut");});
                if(!changed)throw new InvalidOperationException("The cutting plane does not cross the active mesh.");
                EditorUtility.SetDirty(ActiveMesh);EditorUtility.SetDirty(Document);ResetCutGesture();MeshTopologyChanged();cutTopology=new StudioMeshCuts(Document.nodes[Active]);cutMesh=ActiveMesh;error=null;
            }
            catch{if(group>=0)Undo.RevertAllDownToGroup(group);throw;}
            finally{if(group>=0)Undo.CollapseUndoOperations(group);}
        }
        private float MultiEdgeFraction(SceneView view,Vector2 mouse,int a,int b)
        {
            return MultiSegmentFraction(view,mouse,cutTopology.Positions[a],cutTopology.Positions[b]);
        }
        private float MultiSegmentFraction(SceneView view,Vector2 mouse,Vector3 a,Vector3 b)
        {
            var transform=Transforms[Active];var wa=transform.TransformPoint(a);var wb=transform.TransformPoint(b);
            var pa=PaintWorldToGUI(view.camera,wa);var pb=PaintWorldToGUI(view.camera,wb);var delta=pb-pa;
            float t=delta.sqrMagnitude<.001f?.5f:Mathf.Clamp01(Vector2.Dot(mouse-pa,delta)/delta.sqrMagnitude);
            if(!view.orthographic){float za=view.camera.WorldToViewportPoint(wa).z,zb=view.camera.WorldToViewportPoint(wb).z;t=t*za/Mathf.Max(.000001f,(1-t)*zb+t*za);}
            return t;
        }
        private bool MultiCutSceneGUI(SceneView view,bool overCanvas)
        {
            var evt=Event.current;
            var pointer=PaintInputPosition(view);var rootPointer=view is MashBoxStudioViewport viewport?pointer+viewport.CanvasInputOrigin:pointer;
            var canvas=view is MashBoxStudioViewport studio?studio.ModelCanvasRect:new Rect(Vector2.zero,view.position.size);
            overCanvas=canvas.Contains(rootPointer)&&(!(view is MashBoxStudioViewport overlay)||!overlay.IsOverViewportControls(rootPointer));
            if((overCanvas||multiSlicing)&&(evt.type==EventType.MouseMove||evt.type==EventType.MouseDrag||evt.type==EventType.MouseDown||evt.type==EventType.MouseUp))
            {
                multiMouse=PaintInputPosition(view);multiHover=SnapMultiPreview(view,multiMouse,MultiSurface(view,multiMouse));
                if(multiSlicing)multiSlicePreview=MultiSlice(view,multiSliceStart,multiMouse);
                repaint();
            }
            if(overCanvas&&evt.type==EventType.MouseDown&&evt.button==0&&GUIUtility.hotControl==0)
            {
                GUIUtility.hotControl=cutControl;
                if(multiPoints.Count==0&&!SlicePointerOnMesh(view,multiMouse))
                {multiSlicing=true;multiSliceStart=multiMouse;multiSlicePreview.Clear();error=null;}
                else if(multiHover.HasValue)
                {
                    var point=multiHover.Value;
                    if(multiPoints.Count==0)
                    { if(point.Boundary){multiPoints.Add(point);error=null;}else error="Start a cut on an edge or vertex."; }
                    else if(MultiLoopClosed)error="Loop is closed. Press W to apply, or Backspace to reopen.";
                    else if(SameMultiPoint(point,multiPoints[0])&&!CanCloseMultiLoop())error="A closed loop needs at least three distinct vertices.";
                    else Run(()=>{var link=TraceMultiSegment(view,multiPoints.Last(),point);multiLinks.Add(link);multiPoints.Add(point);error=MultiLoopClosed?"Loop closed. Press W to apply, or Backspace to reopen.":null;});
                }

                evt.Use();return true;
            }
            if(evt.type==EventType.MouseDrag&&GUIUtility.hotControl==cutControl){evt.Use();return true;}
            if(evt.type==EventType.MouseUp&&evt.button==0&&GUIUtility.hotControl==cutControl)
            {
                GUIUtility.hotControl=0;
                if(multiSlicing)
                {
                    if(SlicePointerOnMesh(view,PaintInputPosition(view)))error="Release in empty space off the active mesh to finish the slice.";
                    else if(multiSlicePreview.Count==0)error="The drawn segment does not cross the active mesh.";
                    else Run(()=>CommitMultiSlice(multiSlicePlane));
                    multiSlicing=false;multiSlicePreview.Clear();
                }
                evt.Use();repaint();return true;
            }
            if(evt.type==EventType.MouseDown&&evt.button==1&&overCanvas){CommitPendingCutGesture();suppressModelContext=true;evt.Use();return true;}
            if(evt.type==EventType.Repaint)DrawMultiCutPreview(view);
            return true;
        }
        private void DrawMultiCutPreview(SceneView view)
        {
            var color=Handles.color;var depth=Handles.zTest;var transform=Transforms[Active];
            try
            {
                Handles.color=CutPreviewColor;Handles.zTest=displayXray||multiSlicing?UnityEngine.Rendering.CompareFunction.Always:UnityEngine.Rendering.CompareFunction.LessEqual;
                foreach(var link in multiLinks)foreach(var part in link)Handles.DrawAAPolyLine(3,part.Points.Select(p=>transform.TransformPoint(p)).ToArray());
                foreach(var part in multiSlicePreview)Handles.DrawAAPolyLine(3,part.Points.Select(p=>transform.TransformPoint(p)).ToArray());
                foreach(var p in multiPoints){var world=transform.TransformPoint(p.Local);Handles.DotHandleCap(0,world,Quaternion.identity,HandleUtility.GetHandleSize(world)*.035f,EventType.Repaint);}
                if(multiHover.HasValue&&!MultiLoopClosed){var world=transform.TransformPoint(multiHover.Value.Local);Handles.color=CutHoverColor;Handles.DotHandleCap(0,world,Quaternion.identity,HandleUtility.GetHandleSize(world)*.035f,EventType.Repaint);}
                if((multiPoints.Count>0&&!MultiLoopClosed)||multiSlicing)
                {
                    var start=multiSlicing?multiSliceStart:PaintWorldToGUI(view.camera,transform.TransformPoint(multiPoints.Last().Local));
                    var offset=HandleUtility.WorldToGUIPoint(transform.position)-PaintWorldToGUI(view.camera,transform.position);start+=offset;var end=(!multiSlicing&&multiHover.HasValue?PaintWorldToGUI(view.camera,transform.TransformPoint(multiHover.Value.Local)):multiMouse)+offset;Handles.BeginGUI();try{Handles.color=CutPreviewColor;Handles.DrawAAPolyLine(2,new Vector3(start.x,start.y),new Vector3(end.x,end.y));}finally{Handles.EndGUI();}
                }
            }
            finally{Handles.color=color;Handles.zTest=depth;}
        }
    }
}

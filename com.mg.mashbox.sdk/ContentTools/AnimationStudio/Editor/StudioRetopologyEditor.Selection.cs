using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private enum SelectionMode { Vertices, Edges, Faces }
        private SelectionMode selectionMode;
        private bool xraySelection=EditorPrefs.GetBool("MashBox.Retopology.XraySelection",false);
        private void ToggleXraySelection()
        {
            xraySelection=!xraySelection;
            EditorPrefs.SetBool("MashBox.Retopology.XraySelection",xraySelection);repaint();
        }
        private bool IsSelectionPointVisible(Vector3 point, bool allowThroughSelection = true)
        {
            if(allowThroughSelection && xraySelection)return true;
            var ray=HandleUtility.GUIPointToWorldRay(HandleUtility.WorldToGUIPoint(point));
            float distance=Vector3.Dot(point-ray.origin,ray.direction);
            float size=Active.reference?Active.reference.bounds.size.magnitude:1f;
            float epsilon=Mathf.Max(.00001f,size*.00001f);
            if(distance<=0)return false;
            int index=Document.selected;
            // Test the working surface separately: display X-ray must not select its far side.
            if(index>=0&&index<outputs.Count&&outputs[index])
            {
                var collider=outputs[index].GetComponent<MeshCollider>();
                if(collider&&collider.Raycast(ray,out var hit,distance)&&hit.distance<distance-epsilon)return false;
            }
            if(Active.showReference&&index>=0&&index<references.Count&&references[index])
            {
                var collider=references[index].GetComponent<MeshCollider>();
                float allowance=epsilon;
                if(workingXray&&Active.reference)
                {
                    var bounds=Active.reference.bounds.size;
                    allowance+=Mathf.Max(bounds.x,Mathf.Max(bounds.y,bounds.z))*xrayDepthPercent*.01f;
                }
                if(collider&&collider.Raycast(ray,out var hit,distance))
                {
                    float depth=view.camera.WorldToViewportPoint(point).z-view.camera.WorldToViewportPoint(hit.point).z;
                    if(depth>allowance)return false;
                }
            }
            return true;
        }
        private readonly HashSet<int> selectedFaces=new HashSet<int>();
        private readonly HashSet<(int,int)> selectedEdges=new HashSet<(int,int)>();
        private void ClearComponents(){ResetLoopDirection();selected.Clear();selectedFaces.Clear();selectedEdges.Clear();}
        private void SyncComponentVertices()
        {
            selected.Clear();
            if(selectionMode==SelectionMode.Faces)
                foreach(int f in selectedFaces)foreach(int v in Active.faces[f].vertices)selected.Add(v);
            else foreach(var edge in selectedEdges){selected.Add(edge.Item1);selected.Add(edge.Item2);}
        }
        private void SetSelectionMode(SelectionMode mode)
        {
            CancelVertexSelection();ClearComponents();ClearPatchCorners();selectionMode=mode;activeTool=RetopoTool.Select;repaint();
        }
        private void DrawSelectionMode()
        {
            if(IconButton(8,activeTool==RetopoTool.Select&&selectionMode==SelectionMode.Vertices,"Select vertices. Click a vertex, then double-click its neighbor to follow that loop. Drag a box; Shift adds, Ctrl removes."))SetSelectionMode(SelectionMode.Vertices);
            if(IconButton(9,activeTool==RetopoTool.Select&&selectionMode==SelectionMode.Edges,"Select edges. Double-click selects an edge loop or border; Shift adds, Ctrl removes."))SetSelectionMode(SelectionMode.Edges);
            if(IconButton(10,activeTool==RetopoTool.Select&&selectionMode==SelectionMode.Faces,"Select faces. Double-click the same face for its connected hull. Click a face, then double-click its neighbor to follow that row. Shift adds, Ctrl removes. Delete removes selected faces."))SetSelectionMode(SelectionMode.Faces);
            GUILayout.Space(6);
        }
        private void SelectAllComponents()
        {
            if(Active==null)return;RecordToolAction("Select all retopology components");ClearComponents();
            if(selectionMode==SelectionMode.Faces)for(int i=0;i<Active.faces.Count;i++)selectedFaces.Add(i);
            else if(selectionMode==SelectionMode.Edges)selectedEdges.UnionWith(StudioRetopologyOperations.Edges(Active).Keys);
            if(selectionMode==SelectionMode.Vertices)for(int i=0;i<Active.vertices.Count;i++)selected.Add(i);
            else SyncComponentVertices();
            StoreToolState();
        }
        private bool PointInMarquee(Vector3 point, Rect rect)
        {
            return view.camera.WorldToViewportPoint(point).z>0 &&
                rect.Contains(HandleUtility.WorldToGUIPoint(point)) && IsSelectionPointVisible(point);
        }
        private bool EdgeOverlapsMarquee(Vector3 a,Vector3 b,Rect rect)
        {
            if(PointInMarquee(a,rect)||PointInMarquee(b,rect))return true;
            float za=view.camera.WorldToViewportPoint(a).z,zb=view.camera.WorldToViewportPoint(b).z;
            if(za<=0||zb<=0)return false;
            Vector2 start=HandleUtility.WorldToGUIPoint(a),delta=HandleUtility.WorldToGUIPoint(b)-start;
            float lo=0,hi=1;
            bool Clip(float p,float q)
            {
                if(Mathf.Abs(p)<1e-7f)return q>=0;
                float t=q/p;
                if(p<0){if(t>hi)return false;lo=Mathf.Max(lo,t);}
                else {if(t<lo)return false;hi=Mathf.Min(hi,t);}
                return true;
            }
            if(!Clip(-delta.x,start.x-rect.xMin)||!Clip(delta.x,rect.xMax-start.x)||
                !Clip(-delta.y,start.y-rect.yMin)||!Clip(delta.y,rect.yMax-start.y))return false;
            // Test the portion inside the box, not the full edge's midpoint.
            for(int i=0;i<3;i++)
            {
                float t=Mathf.Lerp(lo,hi,(i+1)*.25f);
                if(!view.camera.orthographic)t=(t/zb)/((1-t)/za+t/zb);
                if(IsSelectionPointVisible(Vector3.Lerp(a,b,t)))return true;
            }
            return false;
        }
        private bool FaceOverlapsMarquee(int face,Rect rect)
        {
            var ids=Active.faces[face].vertices;
            for(int i=0;i<ids.Length;i++)
                if(EdgeOverlapsMarquee(Active.vertices[ids[i]],Active.vertices[ids[(i+1)%ids.Length]],rect))return true;
            // A non-planar quad's average can lie behind its own triangles.
            for(int i=1;i+1<ids.Length;i++)
                if(PointInMarquee((Active.vertices[ids[0]]+Active.vertices[ids[i]]+Active.vertices[ids[i+1]])/3,rect))return true;
            return false;
        }
        private void FinishComponentSelection()
        {
            bool click=(selectionEnd-selectionStart).sqrMagnitude<16;
            var rect=VertexSelectionRect(selectionStart,selectionEnd);
            if(!click)ResetLoopDirection();

            if(selectionMode==SelectionMode.Faces)
            {
                var hits=new HashSet<int>();
                if(click){int face=PickFace(selectionEnd);if(face>=0)hits.UnionWith(ClickFaceLoop(face,selectionClicks>=2));else ResetLoopDirection();}
                else for(int i=0;i<Active.faces.Count;i++)
                {
                    if(FaceOverlapsMarquee(i,rect))hits.Add(i);
                }
                if(!click)
                {
                    int enclosed=PickFace(rect.center);
                    if(enclosed>=0)hits.Add(enclosed);
                }
                if(!selectionAdd&&!selectionSubtract)selectedFaces.Clear();
                if(selectionSubtract)selectedFaces.ExceptWith(hits);else selectedFaces.UnionWith(hits);
            }
            else
            {
                var hits=new HashSet<(int,int)>();var edges=StudioRetopologyOperations.Edges(Active);
                if(click)
                {
                    var edge=PickEdge(selectionEnd,edges.Keys);
                    if(edge.Item1>=0)
                    {
                        hits.Add(edge);
                        if(selectionClicks>=2)
                        {
                            hits.UnionWith(ConnectedEdgeLoop(Active,edge));
                            if(!selectionSubtract&&hits.Count>1)RecordTutorialAction("selectEdgeLoop");
                        }
                    }
                }
                else foreach(var edge in edges.Keys)if(EdgeOverlapsMarquee(Active.vertices[edge.Item1],Active.vertices[edge.Item2],rect))hits.Add(edge);
                if(!selectionAdd&&!selectionSubtract)selectedEdges.Clear();
                if(selectionSubtract)selectedEdges.ExceptWith(hits);else selectedEdges.UnionWith(hits);
            }
            SyncComponentVertices();
        }
        private static HashSet<(int,int)> ConnectedEdgeLoop(StudioRetopologyDocument.Part part,(int,int) seed)
        {
            var result=new HashSet<(int,int)>();
            if(part==null)return result;
            seed=StudioRetopologyOperations.Edge(seed.Item1,seed.Item2);
            var edges=StudioRetopologyOperations.Edges(part);
            if(!edges.TryGetValue(seed,out var seedFaces))return result;
            result.Add(seed);
            if(seedFaces.Count<1||seedFaces.Count>2)return result;
            bool border=seedFaces.Count==1;
            var incident=new Dictionary<int,List<(int,int)>>();
            foreach(var edge in edges.Keys)
                foreach(int vertex in new[]{edge.Item1,edge.Item2})
                {
                    if(!incident.TryGetValue(vertex,out var list))incident[vertex]=list=new List<(int,int)>();
                    list.Add(edge);
                }
            void Walk(int vertex)
            {
                var previous=seed;
                while(incident.TryGetValue(vertex,out var around))
                {
                    if(around.Any(e=>edges[e].Count>2))return;
                    (int,int) next;
                    if(border)
                    {
                        var boundary=around.Where(e=>edges[e].Count==1).ToArray();
                        if(boundary.Length!=2)return; // Stop at branching borders.
                        next=boundary[0]==previous?boundary[1]:boundary[0];
                    }
                    else
                    {
                        // At a regular quad vertex, the loop continues along the
                        // edge sharing neither incident face with the incoming edge.
                        // Crossing to the opposite edge of a face would select a ring.
                        if(around.Count!=4||around.Any(e=>edges[e].Count!=2))return;
                        var faces=around.SelectMany(e=>edges[e]).Distinct().ToArray();
                        if(faces.Length!=4||faces.Any(f=>part.faces[f].vertices.Length!=4))return;
                        var candidates=around.Where(e=>e!=previous&&!edges[e].Intersect(edges[previous]).Any()).ToArray();
                        if(candidates.Length!=1)return;
                        next=candidates[0];
                    }
                    if(!result.Add(next))return;
                    vertex=next.Item1==vertex?next.Item2:next.Item1;
                    previous=next;
                }
            }
            Walk(seed.Item1);Walk(seed.Item2);
            return result;
        }
        private (int,int) PickEdge(Vector2 mouse,IEnumerable<(int,int)> edges)
        {
            var best=(-1,-1);float distance=10,depth=float.MaxValue;
            foreach(var edge in edges)
            {
                var a=Active.vertices[edge.Item1];var b=Active.vertices[edge.Item2];
                if(view.camera.WorldToViewportPoint(a).z<=0||view.camera.WorldToViewportPoint(b).z<=0)continue;
                var sa=HandleUtility.WorldToGUIPoint(a);var sb=HandleUtility.WorldToGUIPoint(b);
                float d=HandleUtility.DistancePointToLineSegment(mouse,sa,sb);
                var segment=sb-sa;
                float along=segment.sqrMagnitude>0?Mathf.Clamp01(Vector2.Dot(mouse-sa,segment)/segment.sqrMagnitude):0;
                if(d>distance||!IsSelectionPointVisible(Vector3.Lerp(a,b,along)))continue;
                float z=view.camera.WorldToViewportPoint((a+b)*.5f).z;
                if(d<distance-.01f||(Mathf.Abs(d-distance)<.01f&&z<depth)){distance=d;depth=z;best=edge;}
            }
            return best;
        }
        private int PickFace(Vector2 mouse)
        {
            var ray=HandleUtility.GUIPointToWorldRay(mouse);int best=-1;float nearest=float.MaxValue;
            for(int i=0;i<Active.faces.Count;i++)
            {
                var ids=Active.faces[i].vertices;
                for(int j=1;j+1<ids.Length;j++)
                {
                    var a=Active.vertices[ids[0]];var e1=Active.vertices[ids[j]]-a;var e2=Active.vertices[ids[j+1]]-a;
                    var p=Vector3.Cross(ray.direction,e2);float det=Vector3.Dot(e1,p);if(Mathf.Abs(det)<1e-9f)continue;
                    var t=ray.origin-a;float u=Vector3.Dot(t,p)/det;if(u<0||u>1)continue;
                    var q=Vector3.Cross(t,e1);float v=Vector3.Dot(ray.direction,q)/det;if(v<0||u+v>1)continue;
                    float d=Vector3.Dot(e2,q)/det;if(d>=0&&d<nearest){nearest=d;best=i;}
                }
            }
            return best>=0&&IsSelectionPointVisible(ray.GetPoint(nearest))?best:-1;
        }
        private void DrawComponentSelection(bool allowHover)
        {
            if(activeTool!=RetopoTool.Select||!Active.showOutput)return;
            var depth=Handles.zTest;var color=Handles.color;
            var selectedColor=StudioFaceSelectionStyle.Selected;
            var hoverColor=StudioFaceSelectionStyle.Hover;
            void Vertex(int index,Color tint,float size)
            {
                if(index<0||index>=Active.vertices.Count)return;
                var point=Active.vertices[index];if(view.camera.WorldToViewportPoint(point).z<=0)return;
                float scale=HandleUtility.GetHandleSize(point);
                Handles.color=new Color(.02f,.03f,.03f,1);
                Handles.DotHandleCap(0,point,Quaternion.identity,scale*(size+.014f),EventType.Repaint);
                Handles.color=tint;Handles.DotHandleCap(0,point,Quaternion.identity,scale*size,EventType.Repaint);
            }
            void Edge((int,int) edge,Color tint,float width)
            {
                if(edge.Item1<0||edge.Item2>=Active.vertices.Count)return;
                Handles.color=tint;Handles.DrawAAPolyLine(width,Active.vertices[edge.Item1],Active.vertices[edge.Item2]);
            }
            void Face(int face,Color tint,float opacity)
            {
                if(face<0||face>=Active.faces.Count)return;
                var ids=Active.faces[face].vertices;Handles.color=new Color(tint.r,tint.g,tint.b,opacity);
                for(int i=1;i+1<ids.Length;i++)Handles.DrawAAConvexPolygon(Active.vertices[ids[0]],Active.vertices[ids[i]],Active.vertices[ids[i+1]]);
                for(int i=0;i<ids.Length;i++)Edge((ids[i],ids[(i+1)%ids.Length]),tint,2.5f);
            }
            try
            {
                Handles.zTest=UnityEngine.Rendering.CompareFunction.LessEqual;
                if(selectionMode==SelectionMode.Vertices && !DrawTopologyVertices(false,new Color(.48f,.72f,.78f,.9f),3,UnityEngine.Rendering.CompareFunction.LessEqual))
                    for(int i=0;i<Active.vertices.Count;i++)Vertex(i,new Color(.48f,.72f,.78f,.9f),.022f);
                // Explicit selected/hovered components remain readable through the reference.
                Handles.zTest=workingXray?UnityEngine.Rendering.CompareFunction.Always:UnityEngine.Rendering.CompareFunction.LessEqual;
                if(selectionMode==SelectionMode.Vertices)
                { if(!DrawTopologyVertices(true,selectedColor,6,Handles.zTest))foreach(int i in selected)Vertex(i,selectedColor,.045f); }
                else if(!DrawTopologySelectedSurfaces(selectedColor,Handles.zTest))
                {
                    if(selectionMode==SelectionMode.Edges)foreach(var edge in selectedEdges)Edge(edge,selectedColor,3.5f);
                    else foreach(int face in selectedFaces)Face(face,selectedColor,StudioFaceSelectionStyle.SelectedOpacity);
                }
                if(!allowHover)return;
                var mouse=Event.current.mousePosition;
                if(selectionMode==SelectionMode.Vertices)
                {
                    int vertex=PickVertex(mouse);if(vertex<0)return;
                    Vertex(vertex,selected.Contains(vertex)?selectedColor:hoverColor,.06f);
                    var point=Active.vertices[vertex];Handles.DrawWireDisc(point,view.camera.transform.forward,HandleUtility.GetHandleSize(point)*.055f);
                }
                else if(selectionMode==SelectionMode.Edges)
                {
                    var edge=PickEdge(mouse,StudioRetopologyOperations.Edges(Active).Keys);
                    if(edge.Item1>=0)Edge(edge,selectedEdges.Contains(edge)?selectedColor:hoverColor,5f);
                }
                else
                {
                    int face=PickFace(mouse);if(face>=0)Face(face,selectedFaces.Contains(face)?selectedColor:hoverColor,StudioFaceSelectionStyle.HoverOpacity);
                }
            }
            finally{Handles.color=color;Handles.zTest=depth;}
        }
        private void DeleteComponents()
        {
            if(selectionMode==SelectionMode.Vertices){DeleteSelection();return;}
            if(selectionMode==SelectionMode.Faces&&selectedFaces.Count>0)
                Change("Delete selected faces",()=>{for(int i=Active.faces.Count-1;i>=0;i--)if(selectedFaces.Contains(i))Active.faces.RemoveAt(i);StudioRetopologyOperations.Compact(Active);});
            else if(selectionMode==SelectionMode.Edges&&selectedEdges.Count>0)
                Change("Delete faces at selected edges",()=>{Active.faces.RemoveAll(f=>Enumerable.Range(0,f.vertices.Length).Any(i=>selectedEdges.Contains(StudioRetopologyOperations.Edge(f.vertices[i],f.vertices[(i+1)%f.vertices.Length]))));StudioRetopologyOperations.Compact(Active);});
        }
    }
}

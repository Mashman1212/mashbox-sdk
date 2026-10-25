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
        private bool IsSelectionPointVisible(Vector3 point)
        {
            if(xraySelection)return true;
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
        private void ClearComponents(){selected.Clear();selectedFaces.Clear();selectedEdges.Clear();}
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
            if(IconButton(8,activeTool==RetopoTool.Select&&selectionMode==SelectionMode.Vertices,"Select vertices. Click or drag a box; Shift adds, Ctrl removes."))SetSelectionMode(SelectionMode.Vertices);
            if(IconButton(9,activeTool==RetopoTool.Select&&selectionMode==SelectionMode.Edges,"Select edges. Double-click a boundary edge for its loop; Shift adds another loop. Bridge connects matching borders."))SetSelectionMode(SelectionMode.Edges);
            if(IconButton(10,activeTool==RetopoTool.Select&&selectionMode==SelectionMode.Faces,"Select faces. Click or drag a box, then Delete removes selected faces."))SetSelectionMode(SelectionMode.Faces);
            GUILayout.Space(6);
        }
        private void SelectAllComponents()
        {
            ClearComponents();if(Active==null)return;
            if(selectionMode==SelectionMode.Faces)for(int i=0;i<Active.faces.Count;i++)selectedFaces.Add(i);
            else if(selectionMode==SelectionMode.Edges)selectedEdges.UnionWith(StudioRetopologyOperations.Edges(Active).Keys);
            if(selectionMode==SelectionMode.Vertices)for(int i=0;i<Active.vertices.Count;i++)selected.Add(i);
            else SyncComponentVertices();
        }
        private void FinishComponentSelection()
        {
            bool click=(selectionEnd-selectionStart).sqrMagnitude<16;
            var rect=VertexSelectionRect(selectionStart,selectionEnd);
            bool InBox(Vector3 p)=>view.camera.WorldToViewportPoint(p).z>0&&rect.Contains(HandleUtility.WorldToGUIPoint(p))&&IsSelectionPointVisible(p);
            if(selectionMode==SelectionMode.Faces)
            {
                var hits=new HashSet<int>();
                if(click){int face=PickFace(selectionEnd);if(face>=0)hits.Add(face);}
                else for(int i=0;i<Active.faces.Count;i++)
                {
                    var center=Vector3.zero;foreach(int v in Active.faces[i].vertices)center+=Active.vertices[v];
                    if(InBox(center/Active.faces[i].vertices.Length))hits.Add(i);
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
                        if(selectionClicks==2&&edges[edge].Count==1)
                        {
                            var loop=StudioRetopologyLoops.Boundaries(Active).FirstOrDefault(l=>Enumerable.Range(0,l.Length).Any(i=>StudioRetopologyOperations.Edge(l[i],l[(i+1)%l.Length])==edge));
                            if(loop!=null)for(int i=0;i<loop.Length;i++)hits.Add(StudioRetopologyOperations.Edge(loop[i],loop[(i+1)%loop.Length]));
                        }
                    }
                }
                else foreach(var edge in edges.Keys)if(InBox((Active.vertices[edge.Item1]+Active.vertices[edge.Item2])*.5f))hits.Add(edge);
                if(!selectionAdd&&!selectionSubtract)selectedEdges.Clear();
                if(selectionSubtract)selectedEdges.ExceptWith(hits);else selectedEdges.UnionWith(hits);
            }
            SyncComponentVertices();
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
            var selectedColor=new Color(1f,.72f,.18f,1f);
            var hoverColor=new Color(.55f,1f,.95f,1f);
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
                if(selectionMode==SelectionMode.Vertices)
                    for(int i=0;i<Active.vertices.Count;i++)Vertex(i,new Color(.48f,.72f,.78f,.9f),.022f);
                // Explicit selected/hovered components remain readable through the reference.
                Handles.zTest=workingXray?UnityEngine.Rendering.CompareFunction.Always:UnityEngine.Rendering.CompareFunction.LessEqual;
                if(selectionMode==SelectionMode.Vertices)foreach(int i in selected)Vertex(i,selectedColor,.045f);
                else if(selectionMode==SelectionMode.Edges)foreach(var edge in selectedEdges)Edge(edge,selectedColor,3.5f);
                else foreach(int face in selectedFaces)Face(face,selectedColor,.28f);
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
                    int face=PickFace(mouse);if(face>=0)Face(face,selectedFaces.Contains(face)?selectedColor:hoverColor,.4f);
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

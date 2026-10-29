using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private int[] quadDragSnaps,quadDragBorderVertices;
        private string quadDragWarning;

        private void AppendDraggedQuadGrid(Part grid,Vector3 normal,bool mirrored)
        {
            int firstNewVertex=Active.vertices.Count;
            AppendPolygonGrid(grid,normal,4,mirrored);
            for(int i=firstNewVertex;i<Active.vertices.Count;i++)
            {
                int corner=grid.vertices.FindIndex(p=>p==Active.vertices[i]);
                if(corner>=0)Active.uv[i]=grid.uv[corner];
            }
        }

        internal static int[] SnapDraggedQuad(Vector3[] points,IReadOnlyList<Vector3> vertices,IEnumerable<int> borderVertices,Func<Vector3,Vector2> screen,float radius,Func<int,bool> visible)
        {
            var result=new[]{-1,-1,-1,-1};var used=new HashSet<int>();var candidates=borderVertices.ToArray();
            // The grabbed corner and the cursor's opposite corner take priority.
            foreach(int corner in new[]{0,2,1,3})
            {
                var pointer=screen(points[corner]);float best=radius*radius;int selected=-1;
                foreach(int id in candidates)
                {
                    if(used.Contains(id))continue;
                    float distance=(screen(vertices[id])-pointer).sqrMagnitude;
                    if(distance<best&&visible(id)){selected=id;best=distance;}
                }
                if(selected<0)continue;
                result[corner]=selected;used.Add(selected);points[corner]=vertices[selected];
            }
            return result;
        }

        private void PrepareQuadDragWelding()
        {
            var edges=StudioRetopologyOperations.Edges(Active);
            quadDragBorderVertices=edges.Where(e=>e.Value.Count==1).SelectMany(e=>new[]{e.Key.Item1,e.Key.Item2}).Distinct().ToArray();
            float tolerance=Mathf.Max(.000001f,Active.reference.bounds.size.magnitude*.00001f);
            int start=Active.vertices.FindIndex(p=>(p-quadDragStart).sqrMagnitude<=tolerance*tolerance);
            if(start<0)return;
            // Follow the existing grid at a grabbed border corner, instead of
            // deriving an unrelated rectangle solely from the camera's roll/yaw.
            var reference=Vector3.ProjectOnPlane(quadDragRight,quadDragNormal).normalized;
            float best=0;Vector3 right=reference;
            foreach(var edge in edges.Where(e=>e.Value.Count==1&&(e.Key.Item1==start||e.Key.Item2==start)))
            {
                int other=edge.Key.Item1==start?edge.Key.Item2:edge.Key.Item1;
                var direction=Vector3.ProjectOnPlane(Active.vertices[other]-quadDragStart,quadDragNormal).normalized;
                if(direction.sqrMagnitude<.5f)continue;
                if(Mathf.Abs(Vector3.Dot(direction,reference))<.7071f)direction=Vector3.Cross(direction,quadDragNormal).normalized;
                float alignment=Vector3.Dot(direction,reference);
                if(Mathf.Abs(alignment)<=best)continue;
                best=Mathf.Abs(alignment);right=alignment<0?-direction:direction;
            }
            if(best>0)quadDragRight=right;
        }

        private int PickQuadDragBorder(Vector2 mouse)
        {
            int result=-1;float best=24*24;
            if(!Active.showOutput)return result;
            foreach(int id in quadDragBorderVertices??Array.Empty<int>())
            {
                if(view.camera.WorldToViewportPoint(Active.vertices[id]).z<=0)continue;
                float distance=(HandleUtility.WorldToGUIPoint(Active.vertices[id])-mouse).sqrMagnitude;
                if(distance<best&&IsSelectionPointVisible(Active.vertices[id],false)){result=id;best=distance;}
            }
            return result;
        }

        internal static string DraggedQuadAttachmentWarning(Part part,Vector3[] points,Vector3 normal,float tolerance)
        {
            try
            {
                var sides=StudioRetopologyPatchBorders.Find(part,points,tolerance);
                if(sides.Any(side=>side!=null&&side.Length>2))
                    return "This border has multiple edges. Use Draw Patch or Draw Polygon to match its divisions.";
                int columns=1,rows=1;bool reverse=false;
                StudioRetopologyPatchBorders.Match(part,sides,ref columns,ref rows,ref reverse);
                var edges=StudioRetopologyOperations.Edges(part);var center=points.Aggregate(Vector3.zero,(sum,p)=>sum+p)/4;
                var ids=points.Select(point=>part.vertices.FindIndex(p=>(p-point).sqrMagnitude<=tolerance*tolerance)).ToArray();
                for(int i=0;i<4;i++)
                {
                    int a=ids[i],b=ids[(i+1)%4];
                    if(a<0||b<0||!edges.TryGetValue(StudioRetopologyOperations.Edge(a,b),out var adjacent))continue;
                    if(adjacent.Count!=1)return "That edge is already occupied on both sides.";
                    var face=part.faces[adjacent[0]].vertices;
                    var occupied=face.Aggregate(Vector3.zero,(sum,id)=>sum+part.vertices[id])/face.Length;
                    var edge=part.vertices[b]-part.vertices[a];
                    float oldSide=Vector3.Dot(Vector3.Cross(edge,occupied-part.vertices[a]),normal);
                    float newSide=Vector3.Dot(Vector3.Cross(edge,center-part.vertices[a]),normal);
                    if(oldSide*newSide>tolerance*tolerance*tolerance*tolerance)return "The quad overlaps an existing face. Drag toward the empty side of its border.";
                }
                return null;
            }
            catch(InvalidOperationException error){return error.Message;}
        }

        private void DrawQuadDragWeldPreview(Vector3[] points)
        {
            if(quadDragSnaps==null)return;
            var edges=StudioRetopologyOperations.Edges(Active);
            Handles.color=quadDragWarning==null?new Color(1,.8f,.2f):new Color(1,.3f,.2f);
            int count=0;
            for(int i=0;i<4;i++)
            {
                if(quadDragSnaps[i]<0)continue;
                count++;
                Handles.DrawWireDisc(points[i],view.camera.transform.forward,HandleUtility.GetHandleSize(points[i])*.055f);
                int next=(i+1)%4;
                if(quadDragSnaps[next]>=0&&edges.TryGetValue(StudioRetopologyOperations.Edge(quadDragSnaps[i],quadDragSnaps[next]),out var faces)&&faces.Count==1)
                    Handles.DrawAAPolyLine(5,points[i],points[next]);
            }
            Handles.Label(points[2],quadDragWarning??(count>0?"Release: weld "+count+" corners · Escape: cancel":"Release: create quad · Escape: cancel"));
        }
    }
}

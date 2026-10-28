using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private bool HandlePolygonEscape(Event evt)
        {
            if(evt.type!=EventType.KeyDown||evt.keyCode!=KeyCode.Escape||
                activeTool!=RetopoTool.Polygon||corners.Count==0||
                EditorGUIUtility.editingTextField||contextHeld)return false;
            CancelConstruction();
            status="Polygon outline cancelled.";
            evt.Use();return true;
        }
        private void ClearPolygonDraft()
        {Document.polygonPoints.Clear();Document.polygonPart=-1;Document.polygonNormal=Vector3.zero;Document.toolState=null;}

        private void RestorePolygonDraft()
        {
            RestoreToolState();
        }

        private void RecordPolygonDraft(string label)
        {
            RecordToolAction(label);
        }

        private void StorePolygonDraft()
        {
            StoreToolState();
        }

        private void AddPolygonPoint(Vector3 point,Vector3 normal)
        {
            AddConstructionPoint(point,normal);
        }

        private void RemovePolygonPoint()
        {
            RemoveConstructionPoint();
        }

        internal static bool PolygonClosesOnBorder(StudioRetopologyDocument.Part part,Vector3[] points,float tolerance)
        {
            if(part==null||points.Length<3)return false;
            try
            {
                var routes=StudioRetopologyPatchBorders.Find(part,points,tolerance);
                // Reconnecting to a local open border closes the draft. Merely
                // tracing an existing perimeter must not fill its faces again.
                if(routes[points.Length-1]==null||routes.All(route=>route!=null))return false;
                var outline=new List<Vector3>();
                for(int i=0;i<points.Length;i++)
                {
                    if(routes[i]==null)outline.Add(points[i]);
                    else outline.AddRange(routes[i].Take(routes[i].Length-1).Select(id=>part.vertices[id]));
                }
                TriangulatePolygon(outline.ToArray());
                return true;
            }
            catch(InvalidOperationException){return false;}
        }
        private void CreatePolygon()
        {
            if(corners.Count<3)throw new InvalidOperationException("Place at least three points, then press Enter to fill the outline.");
            var points=PreparePolygonOutline(corners.ToArray(),out var fixedEdges,out var requiredSeams,out var layoutCorners);var normal=Vector3.zero;
            for(int i=0;i<points.Length;i++)normal+=Vector3.Cross(points[i]-points[0],points[(i+1)%points.Length]-points[0]);
            normal=normal.sqrMagnitude>1e-12f?normal.normalized:cornerNormal;
            if(Vector3.Dot(normal,cornerNormal)<0)normal=-normal;
            var grid=BuildPolygonGrid(points,patchAutoDensity?PatchQuadSize:0,patchSegments,fixedEdges,layoutCorners);
            // Keep the complete draft in the same Undo snapshot as the fill.
            StorePolygonDraft();
            Change("Fill polygon patch",()=>
            {
                AppendPolygonGrid(grid,normal,points.Length);
                if(Document.symmetry&&!points.All(p=>Mathf.Abs(p.x)<.000001f))
                {
                    grid.vertices=grid.vertices.Select(p=>new Vector3(-p.x,p.y,p.z)).ToList();
                    foreach(var face in grid.faces)Array.Reverse(face.vertices);
                    AppendPolygonGrid(grid,new Vector3(-normal.x,normal.y,normal.z),points.Length,true);
                }
                var edges=StudioRetopologyOperations.Edges(Active);
                if(requiredSeams.Any(edge=>!edges.TryGetValue(edge,out var adjacent)||adjacent.Count!=2))
                    throw new InvalidOperationException("The polygon could not seal every selected border edge. No patch was kept; adjust the outline and try again.");
                Active.needsUV=true;
            });
            status="Polygon quad patch created. Undo reopens the outline; keep undoing to remove its points.";
        }

        internal static int[] TriangulatePolygon(Vector3[] points)
        {
            if(points==null||points.Length<3)throw new InvalidOperationException("A polygon needs at least three points.");
            Vector3 normal=Vector3.zero;
            for(int i=0;i<points.Length;i++)normal+=Vector3.Cross(points[i]-points[0],points[(i+1)%points.Length]-points[0]);
            if(normal.sqrMagnitude<1e-20f)throw new InvalidOperationException("The outline is flat or crosses itself. Adjust its points before filling.");
            normal.Normalize();
            var axis=Vector3.Cross(normal,Mathf.Abs(normal.y)<.9f?Vector3.up:Vector3.right).normalized;
            var up=Vector3.Cross(normal,axis);
            var p=points.Select(v=>new Vector2(Vector3.Dot(v-points[0],axis),Vector3.Dot(v-points[0],up))).ToArray();
            float extent=p.Max(v=>v.magnitude),epsilon=Mathf.Max(1e-14f,extent*extent*1e-7f);
            float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
            float Turn(int a,int b,int c)=>Cross(p[b]-p[a],p[c]-p[a]);
            bool On(Vector2 a,Vector2 b,Vector2 v)=>Mathf.Abs(Cross(b-a,v-a))<=epsilon&&Vector2.Dot(v-a,v-b)<=epsilon;
            bool Intersects(int a,int b,int c,int d)
            {
                float x=Turn(a,b,c),y=Turn(a,b,d),z=Turn(c,d,a),w=Turn(c,d,b);
                return ((x>epsilon&&y< -epsilon||x< -epsilon&&y>epsilon)&&(z>epsilon&&w< -epsilon||z< -epsilon&&w>epsilon))||
                    On(p[a],p[b],p[c])||On(p[a],p[b],p[d])||On(p[c],p[d],p[a])||On(p[c],p[d],p[b]);
            }
            for(int i=0;i<p.Length;i++)
            {
                if((p[i]-p[(i+1)%p.Length]).sqrMagnitude<=epsilon)throw new InvalidOperationException("Two outline points are too close together.");
                for(int j=i+1;j<p.Length;j++)
                {
                    if(j==i+1||i==0&&j==p.Length-1)continue;
                    if(Intersects(i,(i+1)%p.Length,j,(j+1)%p.Length))throw new InvalidOperationException("The outline crosses or touches itself. Backspace removes the last point.");
                }
            }
            var remaining=Enumerable.Range(0,p.Length).ToList();var result=new List<int>();
            while(remaining.Count>3)
            {
                bool clipped=false;
                for(int i=0;i<remaining.Count;i++)
                {
                    int a=remaining[(i+remaining.Count-1)%remaining.Count],b=remaining[i],c=remaining[(i+1)%remaining.Count];
                    if(Turn(a,b,c)<=epsilon)continue;
                    if(remaining.Any(v=>v!=a&&v!=b&&v!=c&&Turn(a,b,v)>= -epsilon&&Turn(b,c,v)>= -epsilon&&Turn(c,a,v)>= -epsilon))continue;
                    result.AddRange(new[]{a,b,c});remaining.RemoveAt(i);clipped=true;break;
                }
                if(!clipped)throw new InvalidOperationException("This outline cannot be filled safely. Remove overlapping or collinear points.");
            }
            if(Turn(remaining[0],remaining[1],remaining[2])<=epsilon)throw new InvalidOperationException("The last polygon corner is degenerate. Adjust the outline.");
            result.AddRange(remaining);return result.ToArray();
        }

        private bool DrawingHit(Vector2 mouse,out RaycastHit hit)
        {
            if(ReferenceHit(mouse,out hit))return true;
            // Search only a small GUI-point radius, nearest rings first. The
            // actual collider hit supplies depth and normal; never guess a plane
            // or reuse a stale point when the cursor is far off the model.
            for(int radius=2;radius<=14;radius+=2)
            {
                bool found=false;float depth=float.PositiveInfinity;RaycastHit closest=default;
                for(int i=0;i<16;i++)
                {
                    float angle=i*Mathf.PI/8;
                    if(!ReferenceHit(mouse+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius,out var candidate))continue;
                    float distance=Vector3.Distance(view.camera.transform.position,candidate.point);
                    if(distance<depth){depth=distance;closest=candidate;found=true;}
                }
                if(found)
                {
                    // Refine toward the missed cursor ray to land on the
                    // silhouette instead of a visibly inset sample-ring point.
                    Vector2 inside=HandleUtility.WorldToGUIPoint(closest.point),outside=mouse;
                    for(int i=0;i<6;i++)
                    {
                        var middle=(inside+outside)*.5f;
                        if(ReferenceHit(middle,out var refined)){closest=refined;inside=middle;}else outside=middle;
                    }
                    hit=closest;return true;
                }
            }
            return false;
        }
    }
}

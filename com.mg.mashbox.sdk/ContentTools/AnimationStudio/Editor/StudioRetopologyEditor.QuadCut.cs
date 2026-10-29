using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private bool QuadEdgeSnap(Vector2 mouse,out Vector3 point)
        {
            point=default;if(!Active.showOutput)return false;
            int vertex=PickVertex(mouse,10f);if(vertex>=0){point=Active.vertices[vertex];return true;}
            float best=10f;bool found=false;
            var ray=HandleUtility.GUIPointToWorldRay(mouse);
            foreach(var edge in StudioRetopologyOperations.Edges(Active).Keys)
            {
                var a=Active.vertices[edge.Item1];var b=Active.vertices[edge.Item2];
                if(view.camera.WorldToViewportPoint(a).z<=0||view.camera.WorldToViewportPoint(b).z<=0)continue;
                var pa=HandleUtility.WorldToGUIPoint(a);var pb=HandleUtility.WorldToGUIPoint(b);var delta=pb-pa;
                float screenT=delta.sqrMagnitude>0?Mathf.Clamp01(Vector2.Dot(mouse-pa,delta)/delta.sqrMagnitude):0;
                float distance=Vector2.Distance(mouse,pa+delta*screenT);if(distance>=best)continue;
                // Recover the world-space fraction, accounting for perspective.
                var d=b-a;var w=a-ray.origin;float dd=Vector3.Dot(d,d),dr=Vector3.Dot(d,ray.direction);
                float denominator=dd-dr*dr;
                float t=denominator>1e-12f?Mathf.Clamp01((dr*Vector3.Dot(ray.direction,w)-Vector3.Dot(d,w))/denominator):screenT;
                var candidate=Vector3.Lerp(a,b,t);if(!IsSelectionPointVisible(candidate,false))continue;
                best=distance;point=candidate;found=true;
            }
            return found;
        }
        private Vector3 DrawingPlacement(Vector2 mouse,RaycastHit hit,out int snap,out bool edgeSnap)
        {
            snap=PickVertex(mouse,24f);edgeSnap=false;
            var point=snap>=0?Active.vertices[snap]:hit.point+hit.normal*Document.offset;
            if((activeTool==RetopoTool.Quad||activeTool==RetopoTool.Polygon)&&QuadEdgeSnap(mouse,out var edgePoint))
            {
                point=edgePoint;edgeSnap=true;
                float tolerance=StudioRetopologyQuadCut.Tolerance(Active);
                snap=Active.vertices.FindIndex(v=>(v-point).sqrMagnitude<=tolerance*tolerance);
            }
            return point;
        }
        private bool TryQuadCut(Vector3 point)
        {
            if(activeTool!=RetopoTool.Quad||corners.Count!=1||StudioRetopologyQuadCut.FindFace(Active,corners[0],point)<0)return false;
            var start=corners[0];var normal=cornerNormal;
            ChangeCore("Cut retopology face",()=>StudioRetopologyQuadCut.Apply(Active,start,point),true);
            // ChangeCore clears construction after committing geometry. Retain
            // the endpoint in that same undo action, rather than recording an
            // extra point-placement action between each connected cut.
            float tolerance=StudioRetopologyQuadCut.Tolerance(Active);
            int endpoint=Active.vertices.FindIndex(v=>(v-point).sqrMagnitude<=tolerance*tolerance);
            corners.Add(endpoint>=0?Active.vertices[endpoint]:point);
            cornerNormal=normal;StoreToolState();EditorUtility.SetDirty(Document);
            status="Continue cutting from the last point. Escape ends the chain.";
            return true;
        }
    }
}

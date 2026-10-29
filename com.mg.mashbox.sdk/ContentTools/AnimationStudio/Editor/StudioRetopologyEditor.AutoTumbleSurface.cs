using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private Collider autoTumbleSurface;
        private Vector3 autoTumbleSurfaceNormal;

        // Follow a local surface instead of searching a wider screen-space
        // circle, which only returns the same silhouette point over and over.
        internal static bool WalkAutoTumbleSurface(Collider surface,Vector3 point,Vector3 normal,
            Vector3 screenTravel,Vector3 cameraForward,out RaycastHit hit)
        {
            hit=default;
            if(!surface||normal.sqrMagnitude<.5f||screenTravel.sqrMagnitude<1e-14f)return false;
            normal.Normalize();
            float distance=screenTravel.magnitude;
            var direction=screenTravel/distance;
            var tangent=Vector3.ProjectOnPlane(direction,normal);
            // Outward input at the silhouette means continue around the side,
            // away from the camera, rather than into empty screen space.
            float outward=Mathf.Clamp01(Vector3.Dot(direction,normal));
            if(outward>.5f&&Vector3.Dot(tangent,cameraForward)<0)tangent=-tangent;
            tangent+=Vector3.ProjectOnPlane(cameraForward,normal)*outward;
            if(tangent.sqrMagnitude<1e-8f)return false;
            tangent.Normalize();
            float scale=Mathf.Max(.00001f,surface.bounds.size.magnitude);
            distance=Mathf.Min(distance,scale*.02f);
            float reach=Mathf.Max(distance*3,scale*.001f);
            var axis=Vector3.Cross(normal,tangent).normalized;
            // Subdivide large moves and sweep a small normal fan. Candidates
            // must stay local, face the previous surface, and advance in the
            // intended direction; never jump across the gap to the other leg.
            for(int size=0;size<3;size++)
            {
                float travel=distance/(1<<size);
                var predicted=point+tangent*travel;
                float best=float.PositiveInfinity;bool found=false;
                for(int i=0;i<5;i++)
                {
                    var probeNormal=Quaternion.AngleAxis(i*15,axis)*normal;
                    if(!surface.Raycast(new Ray(predicted+probeNormal*reach,-probeNormal),out var candidate,reach*2))continue;
                    var delta=candidate.point-point;
                    if(delta.magnitude>travel*2.5f||Vector3.Dot(delta,tangent)<travel*.1f||Vector3.Dot(candidate.normal,normal)<.25f)continue;
                    float score=(candidate.point-predicted).sqrMagnitude;
                    if(score>=best)continue;
                    best=score;hit=candidate;found=true;
                }
                if(found)return true;
            }
            return false;
        }

        private bool AutoTumbleSurfaceHit(Vector2 pointer,Vector2 step,bool captured,out RaycastHit hit,out Vector3 normal)
        {
            hit=default;normal=default;
            var ray=HandleUtility.GUIPointToWorldRay(pointer);
            bool found=Active.showReference&&ReferenceHit(pointer,out hit);
            if(!found&&Active.showOutput&&Document.selected<outputs.Count)
            {
                var collider=outputs[Document.selected].GetComponent<MeshCollider>();
                found=collider&&collider.Raycast(ray,out hit,float.MaxValue);
            }
            Vector3 travel=Vector3.zero;
            if(captured&&autoTumbleSurface)
            {
                var plane=new Plane(view.camera.transform.forward,autoTumblePoint);
                if(plane.Raycast(ray,out float t))travel=ray.GetPoint(t)-autoTumblePoint;
                float localLimit=Mathf.Max(travel.magnitude*6,autoTumbleSurface.bounds.size.magnitude*.002f);
                if(found&&(hit.collider!=autoTumbleSurface||Vector3.Distance(hit.point,autoTumblePoint)>localLimit))found=false;
                if(!found&&step.sqrMagnitude>0)
                    found=WalkAutoTumbleSurface(autoTumbleSurface,autoTumblePoint,autoTumbleSurfaceNormal,
                        travel,view.camera.transform.forward,out hit);
            }
            if(!found)return false;
            normal=AutoTumbleNormal(hit);
            // A local walk can move just beyond the silhouette. Preserve its
            // outward orientation so the camera turns toward that new surface.
            if(captured&&Vector3.Dot(normal,autoTumbleSurfaceNormal)<0)normal=-normal;
            autoTumbleSurface=hit.collider;autoTumbleSurfaceNormal=normal;
            return true;
        }
    }
}

using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private int hoverPreviewNode=-1,hoverPreviewComponent=-1;
        private bool HoverBoundsContain(SceneView view,int node,Vector2 pointer)
        {
            var renderer=Transforms[node].GetComponent<Renderer>();
            if(!renderer||!renderer.enabled)return false;
            var bounds=renderer.bounds;var min=new Vector2(float.PositiveInfinity,float.PositiveInfinity);var max=-min;
            for(int i=0;i<8;i++)
            {
                var p=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                if(view.camera.WorldToViewportPoint(p).z<=0)return true;
                var screen=HandleUtility.WorldToGUIPoint(p);min=Vector2.Min(min,screen);max=Vector2.Max(max,screen);
            }
            float padding=mode==ComponentMode.Face?0:ComponentPickRadius;
            return pointer.x>=min.x-padding&&pointer.x<=max.x+padding&&pointer.y>=min.y-padding&&pointer.y<=max.y+padding;
        }
        private void UpdateSelectedComponentHover(SceneView view,Vector2 pointer)
        {
            OcclusionMesh[] occluders=null;float bestScreen=float.PositiveInfinity,bestDepth=float.PositiveInfinity;
            int nodeHit=-1,componentHit=-1;var ray=HandleUtility.GUIPointToWorldRay(pointer);
            foreach(int node in ComponentTargetNodes())using(new ComponentTargetScope(this,node))
            {
                if(!CanEditComponents()||!HoverBoundsContain(view,node,pointer))continue;
                var vertices=ActiveMesh.vertices;int hit=FindComponentAt(view,pointer,vertices,ref occluders);
                if(hit<0)continue;
                var matrix=Transforms[node];float screen=0,depth;
                if(mode==ComponentMode.Face)
                {RayFace(ray,cachedFaceTriangles[hit],vertices,matrix,out depth);}
                else
                {
                    Vector3 world;
                    if(mode==ComponentMode.Vertex){world=matrix.TransformPoint(vertices[hit]);screen=Vector2.Distance(pointer,HandleUtility.WorldToGUIPoint(world));}
                    else
                    {
                        var edge=Edges()[hit];var a=matrix.TransformPoint(vertices[edge.a]);var b=matrix.TransformPoint(vertices[edge.b]);world=(a+b)*.5f;
                        screen=HandleUtility.DistancePointLine(pointer,HandleUtility.WorldToGUIPoint(a),HandleUtility.WorldToGUIPoint(b));
                    }
                    depth=Vector3.Dot(world-ray.origin,ray.direction);
                }
                if(screen>bestScreen+.001f || Mathf.Abs(screen-bestScreen)<=.001f&&depth>=bestDepth)continue;
                bestScreen=screen;bestDepth=depth;nodeHit=node;componentHit=hit;
            }
            if(nodeHit<0)return;
            using(new ComponentTargetScope(this,nodeHit))
            {
                hoveredComponent=componentHit;
                if(mode!=ComponentMode.Face&&Components.Contains(componentHit))return;
                hoverPreviewNode=nodeHit;hoverPreviewComponent=componentHit;
            }
        }
    }
}

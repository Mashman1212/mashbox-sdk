using System.Collections.Generic;
using UnityEngine;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private int[] FindStripTriangle(Vector3 point,Vector3 normal,bool attached)
        {
            var edges=StudioRetopologyOperations.Edges(Active);
            var neighbors=new Dictionary<int,List<int>>();
            foreach(var e in edges)
            {
                if(e.Value.Count!=1)continue;
                int a=e.Key.Item1,b=e.Key.Item2;
                if(!neighbors.ContainsKey(a))neighbors[a]=new List<int>();
                if(!neighbors.ContainsKey(b))neighbors[b]=new List<int>();
                neighbors[a].Add(b);neighbors[b].Add(a);
            }
            foreach(var entry in neighbors)
            {
                int a=entry.Key;
                if(entry.Value.Count!=2)continue;
                int b=entry.Value[0],c=entry.Value[1];
                if(a>b||a>c||neighbors[b].Count!=2||neighbors[c].Count!=2||!neighbors[b].Contains(c))continue;
                if(attached&&!(new List<int>{a,b,c}.Contains(stripLeft)&&new List<int>{a,b,c}.Contains(stripRight)))continue;
                var ids=new[]{a,b,c};bool? reverse=null;bool valid=true;
                for(int i=0;i<3;i++)
                {
                    int from=ids[i],to=ids[(i+1)%3];
                    var face=Active.faces[edges[StudioRetopologyOperations.Edge(from,to)][0]].vertices;
                    if(face.Length==3&&System.Array.IndexOf(face,a)>=0&&System.Array.IndexOf(face,b)>=0&&System.Array.IndexOf(face,c)>=0){valid=false;break;}
                    bool forward=false;for(int j=0;j<face.Length;j++)if(face[j]==from&&face[(j+1)%face.Length]==to)forward=true;
                    if(reverse.HasValue&&reverse.Value!=forward){valid=false;break;}reverse=forward;
                }
                if(!valid)continue;
                if(reverse==true)System.Array.Reverse(ids);
                var p=Active.vertices[ids[0]];var u=Active.vertices[ids[1]]-p;var v=Active.vertices[ids[2]]-p;
                var cross=Vector3.Cross(u,v);float area=cross.magnitude;
                if(area<.00000001f||Vector3.Dot(cross.normalized,normal)<.1f)continue;
                if(Mathf.Max(u.magnitude,Mathf.Max(v.magnitude,(u-v).magnitude))>stripWidth*3)continue;
                var delta=point-p;if(Mathf.Abs(Vector3.Dot(delta,cross.normalized))>stripWidth*.5f)continue;
                float uu=Vector3.Dot(u,u),uv=Vector3.Dot(u,v),vv=Vector3.Dot(v,v);
                float du=Vector3.Dot(delta,u),dv=Vector3.Dot(delta,v),denom=uu*vv-uv*uv;
                float s=(du*vv-dv*uv)/denom,t=(dv*uu-du*uv)/denom;
                if(s<-.02f||t<-.02f||s+t>1.02f)continue;
                return ids;
            }
            return null;
        }
        private bool TryFillStripTriangle(Vector3 point,Vector3 normal,bool attached)
        {
            var hole=FindStripTriangle(point,normal,attached);if(hole==null)return false;
            var mirror=Document.symmetry?FindStripTriangle(new Vector3(-point.x,point.y,point.z),new Vector3(-normal.x,normal.y,normal.z),false):null;
            Change("Fill strip triangle",()=>
            {
                Active.faces.Add(new Face(hole));
                if(mirror!=null&&System.Array.IndexOf(hole,mirror[0])<0)Active.faces.Add(new Face(mirror));
                Active.needsUV=true;
            });
            stripClosed=true;ResetStrip();status="Triangular hole filled and welded.";return true;
        }
    }
}

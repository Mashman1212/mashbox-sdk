using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private (int,int)? bridgeSource;
        private void StartEdgeBridge()
        {
            CancelVertexSelection();ClearComponents();ClearPatchCorners();
            activeTool=RetopoTool.EdgeBridge;repaint();
        }
        private static Face EdgeBridgeFace(Part part,(int,int) first,(int,int) second)
        {
            var edges=StudioRetopologyOperations.Edges(part);
            (int,int) Directed((int,int) edge)
            {
                edge=StudioRetopologyOperations.Edge(edge.Item1,edge.Item2);
                if(!edges.TryGetValue(edge,out var owners)||owners.Count!=1)
                    throw new InvalidOperationException("Choose two open border edges.");
                var ids=part.faces[owners[0]].vertices;
                for(int i=0;i<ids.Length;i++)if(StudioRetopologyOperations.Edge(ids[i],ids[(i+1)%ids.Length])==edge)return(ids[i],ids[(i+1)%ids.Length]);
                throw new InvalidOperationException("The border edge is no longer available.");
            }
            var a=Directed(first);var b=Directed(second);
            var quad=new[]{a.Item2,a.Item1,b.Item2,b.Item1};
            if(quad.Distinct().Count()!=4)throw new InvalidOperationException("Choose separate edges. Use Draw Quad to fill a triangular gap.");
            if(part.faces.Any(f=>f.vertices.Length==4&&f.vertices.All(quad.Contains)))
                throw new InvalidOperationException("These edges already belong to the same face.");
            var p=quad.Select(i=>part.vertices[i]).ToArray();
            var n1=Vector3.Cross(p[1]-p[0],p[2]-p[0]);var n2=Vector3.Cross(p[2]-p[0],p[3]-p[0]);
            float size=p.Select(v=>(v-p[0]).sqrMagnitude).Max();
            if(n1.sqrMagnitude<size*size*1e-10f||n2.sqrMagnitude<size*size*1e-10f||Vector3.Dot(n1,n2)<=0)
                throw new InvalidOperationException("These edges would create a twisted or collapsed bridge. Choose aligned borders.");
            for(int i=0;i<4;i++)
            {
                int from=quad[i],to=quad[(i+1)%4];var key=StudioRetopologyOperations.Edge(from,to);
                if(!edges.TryGetValue(key,out var owners))continue;
                if(owners.Count!=1)throw new InvalidOperationException("This bridge would create a non-manifold edge.");
                var ids=part.faces[owners[0]].vertices;
                if(Enumerable.Range(0,ids.Length).Any(j=>ids[j]==from&&ids[(j+1)%ids.Length]==to))
                    throw new InvalidOperationException("The border directions are incompatible.");
            }
            return new Face(quad,part.faces[edges[StudioRetopologyOperations.Edge(first.Item1,first.Item2)][0]].material);
        }
        private static void BridgeEdgePair(Part part,(int,int) first,(int,int) second)
        {var face=EdgeBridgeFace(part,first,second);part.faces.Add(face);part.needsUV=true;}

        private bool HandleEdgeBridge(Event evt,MashBoxStudioViewport canvas,bool overUI)
        {
            if(activeTool!=RetopoTool.EdgeBridge)return false;
            var borders=StudioRetopologyOperations.Edges(Active).Where(e=>e.Value.Count==1).Select(e=>e.Key).ToArray();
            if(bridgeSource.HasValue&&!borders.Contains(bridgeSource.Value))bridgeSource=null;
            bool usable=Active.showOutput&&!overUI&&!contextHeld&&!evt.alt&&canvas.ModelCanvasRect.Contains(evt.mousePosition);
            var hover=usable?PickEdge(evt.mousePosition,borders):(-1,-1);
            Face preview=null;string problem=null;
            if(bridgeSource.HasValue&&hover.Item1>=0&&hover!=bridgeSource.Value)
            {try{preview=EdgeBridgeFace(Active,bridgeSource.Value,hover);}catch(InvalidOperationException e){problem=e.Message;}}
            if(evt.type==EventType.MouseMove)canvas.Repaint();
            if(evt.type==EventType.Repaint&&Active.showOutput)
            {
                var old=Handles.color;var depth=Handles.zTest;
                try
                {
                    Handles.zTest=workingXray?UnityEngine.Rendering.CompareFunction.Always:UnityEngine.Rendering.CompareFunction.LessEqual;
                    void Edge((int,int) edge,Color color){Handles.color=color;Handles.DrawAAPolyLine(4,Active.vertices[edge.Item1],Active.vertices[edge.Item2]);}
                    if(bridgeSource.HasValue)Edge(bridgeSource.Value,new Color(1,.75f,.12f));
                    if(hover.Item1>=0)Edge(hover,problem!=null?new Color(1,.35f,.2f):new Color(.4f,1,.7f));
                    if(preview!=null)
                    {
                        var points=preview.vertices.Select(i=>Active.vertices[i]).ToArray();
                        Handles.color=new Color(.4f,1,.7f,.2f);Handles.DrawAAConvexPolygon(points);
                        Handles.color=new Color(.4f,1,.7f);Handles.DrawAAPolyLine(2,points.Concat(new[]{points[0]}).ToArray());
                    }
                    if(hover.Item1>=0)Handles.Label((Active.vertices[hover.Item1]+Active.vertices[hover.Item2])*.5f,
                        problem??(bridgeSource.HasValue?"Click to bridge":"Click first border edge"));
                }
                finally{Handles.color=old;Handles.zTest=depth;}
            }
            if(usable&&evt.type==EventType.Layout)HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if(usable&&evt.type==EventType.MouseDown&&evt.button==0&&GUIUtility.hotControl==0)
            {
                if(hover.Item1>=0)
                {
                    if(!bridgeSource.HasValue)SetBridgeSource(hover);
                    else if(bridgeSource.Value==hover)SetBridgeSource(null);
                    else if(preview!=null)
                    {var first=bridgeSource.Value;Run(()=>Change("Bridge edges",()=>BridgeEdgePair(Active,first,hover)));status="Edges bridged. Choose the next pair.";}
                    else canvas.ShowNotification(new GUIContent(problem));
                }
                evt.Use();canvas.Repaint();return true;
            }
            return false;
        }
    }
}

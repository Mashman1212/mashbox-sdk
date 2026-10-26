using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private int weldSource=-1;
        private void StartTargetWeld()
        {
            CancelVertexSelection();ClearComponents();ClearPatchCorners();
            activeTool=RetopoTool.TargetWeld;repaint();
        }
        private static void WeldVertexToTarget(StudioRetopologyDocument.Part part,int source,int target)
        {
            if(part==null||source<0||target<0||source>=part.vertices.Count||target>=part.vertices.Count)
                throw new InvalidOperationException("Choose two working-mesh vertices.");
            if(source==target)return;
            var faces=new List<StudioRetopologyDocument.Face>();
            var signatures=new HashSet<string>();
            foreach(var face in part.faces)
            {
                var ids=new List<int>();
                foreach(int vertex in face.vertices)
                {
                    int mapped=vertex==source?target:vertex;
                    if(ids.Count==0||ids[ids.Count-1]!=mapped)ids.Add(mapped);
                }
                if(ids.Count>1&&ids[0]==ids[ids.Count-1])ids.RemoveAt(ids.Count-1);
                if(ids.Count<3)continue; // Collapsed triangle disappears; a quad becomes a triangle.
                if(ids.Distinct().Count()!=ids.Count)
                    throw new InvalidOperationException("Cannot weld opposite corners of the same face. Split the face first.");
                string signature=string.Join(",",ids.OrderBy(v=>v));
                if(!signatures.Add(signature))throw new InvalidOperationException("This weld would create overlapping duplicate faces.");
                faces.Add(new StudioRetopologyDocument.Face(ids.ToArray(),face.material));
            }
            var candidate=new StudioRetopologyDocument.Part { faces=faces };
            if(StudioRetopologyOperations.Edges(candidate).Any(e=>e.Value.Count>2))
                throw new InvalidOperationException("This weld would leave a non-manifold edge. Choose another target.");
            // Reuse the target index and its exact position/UV; never average the seam.
            part.faces=faces;part.needsUV=true;
            StudioRetopologyOperations.Compact(part);
        }
        private bool HandleTargetWeld(Event evt,MashBoxStudioViewport canvas,bool overUI)
        {
            if(activeTool!=RetopoTool.TargetWeld)return false;
            if(weldSource>=Active.vertices.Count)weldSource=-1;
            bool usable=!overUI&&!contextHeld&&!evt.alt&&canvas.ModelCanvasRect.Contains(evt.mousePosition);
            int hover=usable?PickVertex(evt.mousePosition,18):-1;
            if(evt.type==EventType.MouseMove)canvas.Repaint();
            if(evt.type==EventType.Repaint&&Active.showOutput)
            {
                var old=Handles.color;var depth=Handles.zTest;
                try
                {
                    Handles.zTest=workingXray?UnityEngine.Rendering.CompareFunction.Always:UnityEngine.Rendering.CompareFunction.LessEqual;
                    for(int i=0;i<Active.vertices.Count;i++)
                    {
                        if(i!=weldSource&&!IsSelectionPointVisible(Active.vertices[i],false))continue;
                        var point=Active.vertices[i];float size=HandleUtility.GetHandleSize(point);
                        Handles.color=Color.black;Handles.DotHandleCap(0,point,Quaternion.identity,size*.055f,EventType.Repaint);
                        Handles.color=i==weldSource?new Color(1,.7f,.15f):i==hover?Color.yellow:new Color(.45f,1,.85f);
                        Handles.DotHandleCap(0,point,Quaternion.identity,size*(i==hover||i==weldSource?.045f:.028f),EventType.Repaint);
                    }
                    if(weldSource>=0&&hover>=0&&hover!=weldSource)
                    {Handles.color=Color.yellow;Handles.DrawDottedLine(Active.vertices[weldSource],Active.vertices[hover],4);}
                }
                finally{Handles.color=old;Handles.zTest=depth;}
            }
            if(usable&&evt.type==EventType.Layout)HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if(usable&&evt.type==EventType.MouseDown&&evt.button==0&&GUIUtility.hotControl==0)
            {
                if(hover>=0)
                {
                    if(weldSource<0)weldSource=hover;
                    else if(weldSource==hover)weldSource=-1;
                    else
                    {
                        int source=weldSource,target=hover;
                        Run(()=>Change("Target weld vertex",()=>WeldVertexToTarget(Active,source,target)));
                    }
                }
                evt.Use();canvas.Repaint();return true;
            }
            return false;
        }
    }
}

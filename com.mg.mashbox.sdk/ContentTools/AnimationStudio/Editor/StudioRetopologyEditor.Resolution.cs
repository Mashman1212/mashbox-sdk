using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private float resolutionRadius=64;
        private bool resolutionStroke,resolutionReduce;
        private int resolutionControl;
        private readonly HashSet<int> resolutionFaces=new HashSet<int>();
        private void StartResolution(){activeTool=RetopoTool.Resolution;ClearPatchCorners();repaint();}
        private void CancelResolution()
        {if(resolutionStroke&&GUIUtility.hotControl==resolutionControl)GUIUtility.hotControl=0;resolutionStroke=false;resolutionFaces.Clear();}
        private void DrawResolutionSettings()
        {
            if(activeTool!=RetopoTool.Resolution)return;
            GUILayout.Space(6);GUILayout.Label("RESOLUTION",StudioTheme.Styles.boldLabel);
            resolutionRadius=EditorGUILayout.Slider("Brush radius (px)",resolutionRadius,12,200);
            GUILayout.Label("Paint: increase. Ctrl-paint: reduce. Ctrl + MMB drag sideways or [ / ] changes radius. Each stroke applies one level on release; Escape cancels. Increase follows quad strips to avoid cracks. Reduce merges clean 2 x 2 quad blocks while keeping shared border vertices and quad faces.",StudioTheme.Styles.wordWrappedMiniLabel);
        }
        private void CollectResolutionFaces(Vector2 mouse)
        {
            int hit=PickFace(mouse);if(hit<0)return;
            var face=Active.faces[hit];var center=face.vertices.Select(i=>Active.vertices[i]).Aggregate(Vector3.zero,(a,b)=>a+b)/face.vertices.Length;
            var plane=new Plane(-view.camera.transform.forward,center);var ray=HandleUtility.GUIPointToWorldRay(mouse);
            if(!plane.Raycast(ray,out float distance))return;center=ray.GetPoint(distance);
            var side=HandleUtility.GUIPointToWorldRay(mouse+Vector2.right*resolutionRadius);
            if(!plane.Raycast(side,out float reach))return;float radius=(side.GetPoint(reach)-center).magnitude;
            var adjacency=StudioRetopologyOperations.Edges(Active);var queue=new Queue<int>();var visited=new HashSet<int>();queue.Enqueue(hit);
            while(queue.Count>0)
            {
                int index=queue.Dequeue();if(!visited.Add(index))continue;var ids=Active.faces[index].vertices;
                var p=ids.Select(i=>Active.vertices[i]).Aggregate(Vector3.zero,(a,b)=>a+b)/ids.Length;
                if(index!=hit&&(p-center).sqrMagnitude>radius*radius)continue;
                resolutionFaces.Add(index);
                for(int i=0;i<ids.Length;i++)foreach(int next in adjacency[StudioRetopologyOperations.Edge(ids[i],ids[(i+1)%ids.Length])])if(!visited.Contains(next))queue.Enqueue(next);
            }
        }
        private bool HandleResolution(Event evt,MashBoxStudioViewport canvas,bool overUI)
        {
            int control=GUIUtility.GetControlID("Retopology resolution".GetHashCode(),FocusType.Passive);
            if(resolutionStroke)
            {
                if(evt.type==EventType.KeyDown&&evt.keyCode==KeyCode.Escape){CancelResolution();evt.Use();repaint();return true;}
                if(GUIUtility.hotControl!=resolutionControl||evt.type==EventType.Ignore||evt.type==EventType.MouseLeaveWindow){CancelResolution();repaint();return true;}
                if(evt.type==EventType.MouseDrag&&evt.button==0){if(!overUI)CollectResolutionFaces(evt.mousePosition);evt.Use();repaint();}
                if(evt.type==EventType.MouseUp&&evt.button==0)
                {
                    var painted=new HashSet<int>(resolutionFaces);bool reduce=resolutionReduce;CancelResolution();
                    if(painted.Count>0)Run(()=>{
                        int count=0;
                        Change(reduce?"Reduce topology resolution":"Increase topology resolution",()=>count=reduce?StudioRetopologyResolution.Decrease(Active,painted):StudioRetopologyResolution.Increase(Active,painted,Document.project?Surface:null,Document.offset));
                        status=count>0?$"{(reduce?"Reduced quad blocks":"Refined faces")}: {count}. Review UVs after topology changes.":"No suitable quad blocks under this stroke. Reduce needs all four faces of a clean 2 x 2 block.";
                    });evt.Use();repaint();return true;
                }
            }
            if(activeTool!=RetopoTool.Resolution)return false;
            if(!overUI&&!contextHeld&&!adjustingRetopoBrush&&!evt.alt)
            {
                if(evt.type==EventType.KeyDown&&!EditorGUIUtility.editingTextField&&(evt.keyCode==KeyCode.LeftBracket||evt.keyCode==KeyCode.RightBracket))
                {resolutionRadius=Mathf.Clamp(resolutionRadius*(evt.keyCode==KeyCode.RightBracket?1.15f:1/1.15f),12,200);evt.Use();}
                if(evt.type==EventType.MouseDown&&evt.button==0&&GUIUtility.hotControl==0&&Active.showOutput&&canvas.ModelCanvasRect.Contains(evt.mousePosition))
                {resolutionStroke=true;resolutionReduce=evt.control||evt.command;resolutionControl=control;GUIUtility.hotControl=control;resolutionFaces.Clear();CollectResolutionFaces(evt.mousePosition);evt.Use();repaint();}
                if(evt.type==EventType.MouseMove)repaint();
                if(evt.type==EventType.Repaint)
                {
                    var color=Handles.color;var depth=Handles.zTest;
                    try{
                        Handles.zTest=UnityEngine.Rendering.CompareFunction.LessEqual;Handles.color=new Color(1,.7f,.2f,.3f);
                        foreach(int index in resolutionFaces)if(index<Active.faces.Count){var ids=Active.faces[index].vertices;for(int i=1;i+1<ids.Length;i++)Handles.DrawAAConvexPolygon(Active.vertices[ids[0]],Active.vertices[ids[i]],Active.vertices[ids[i+1]]);}
                        Handles.BeginGUI();try{bool reduce=resolutionStroke?resolutionReduce:evt.control||evt.command;Handles.color=!Active.showOutput||PickFace(evt.mousePosition)<0?StudioBrushCursor.Inactive:reduce?new Color(1,.65f,.25f):new Color(.5f,1,.87f);Handles.DrawWireDisc(evt.mousePosition,Vector3.forward,resolutionRadius);GUI.Label(new Rect(evt.mousePosition.x+15,evt.mousePosition.y+15,160,20),reduce?"Resolution -":"Resolution +",EditorStyles.whiteMiniLabel);}finally{Handles.EndGUI();}
                    }finally{Handles.color=color;Handles.zTest=depth;}
                }
            }
            return resolutionStroke;
        }
    }
}

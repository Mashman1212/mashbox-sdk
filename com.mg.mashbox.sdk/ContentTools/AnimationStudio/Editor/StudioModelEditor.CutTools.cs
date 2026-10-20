using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal enum CutTool { None, EdgeLoop, MultiCut }
        private CutTool cutTool;
        private StudioMeshCuts cutTopology;
        private Mesh cutMesh;
        private readonly List<StudioMeshCuts.Cut> pendingCuts = new List<StudioMeshCuts.Cut>();
        private List<StudioMeshCuts.Cut> cutPreview = new List<StudioMeshCuts.Cut>();
        private StudioMeshCuts.Point? cutStart, cutHover, loopSeed;
        private int cutControl;
        private bool cutDragging;
        private bool CanCutMesh => !HasBlendShape && ActiveMesh && ActiveMesh.isReadable && !Document.nodes[Active].skinned && ActiveMesh.blendShapeCount == 0 && ActiveMesh.boneWeights.Length == 0;
        internal void ActivateCutTool(CutTool next)
        {
            if (!CanCutMesh) { error = "Select an editable static mesh to use the cut tools."; repaint(); return; }
            CancelMarkingMenu(); ResetCutGesture(); cutTool = next; tool = Tool.None; pivot = false;
            mode = ComponentMode.Edge; Components.Clear(); cutTopology = new StudioMeshCuts(Document.nodes[Active]); cutMesh = ActiveMesh;
            error = null; repaint(); toolOptions?.Repaint();
        }
        private void ResetCutGesture()
        {
            if (cutDragging && GUIUtility.hotControl == cutControl) GUIUtility.hotControl = 0;
            cutDragging = false; pendingCuts.Clear(); cutPreview.Clear(); cutStart = cutHover = loopSeed = null;
        }
        private void StopCutTool() { ResetCutGesture(); cutTool = CutTool.None; cutTopology = null; cutMesh = null; }
        private void CommitCuts(IList<StudioMeshCuts.Cut> cuts)
        {
            if (cuts.Count == 0) return;
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            string label = cutTool == CutTool.EdgeLoop || loopSeed.HasValue ? "Insert edge loop" : "Multi-Cut";
            Undo.SetCurrentGroupName(label); Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { Document, ActiveMesh }, label);
            try
            {
                cutTopology.Apply(Document.nodes[Active], cuts);
                EditorUtility.SetDirty(ActiveMesh); EditorUtility.SetDirty(Document);
                ResetCutGesture(); MeshTopologyChanged();
                cutTopology = new StudioMeshCuts(Document.nodes[Active]); cutMesh = ActiveMesh;
                error = null;
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }
        private bool CutToolSceneGUI(SceneView view)
        {
            if (cutTool == CutTool.None) return false;
            var evt = Event.current;
            if (!CanCutMesh) { StopCutTool(); return false; }
            if (cutMesh != ActiveMesh || cutTopology == null)
            { ResetCutGesture(); cutTopology = new StudioMeshCuts(Document.nodes[Active]); cutMesh = ActiveMesh; }
            cutControl = GUIUtility.GetControlID("MashBoxStudioCutTool".GetHashCode(), FocusType.Passive);
            if (HandleModelKeyboard(view)) return true;
            if (evt.alt) return true;
            if (evt.type == EventType.Layout || evt.type == EventType.MouseMove) HandleUtility.AddDefaultControl(cutControl);
            var canvas = view is MashBoxStudioViewport studio ? studio.ModelCanvasRect : new Rect(0,0,view.position.width,view.position.height);
            bool overCanvas = canvas.Contains(evt.mousePosition) && (!(view is MashBoxStudioViewport overlay) || !overlay.IsOverViewportControls(evt.mousePosition));
            bool loop = cutTool == CutTool.EdgeLoop || evt.control || evt.command || cutDragging;
            if (overCanvas && (evt.type == EventType.MouseMove || evt.type == EventType.MouseDrag || evt.type == EventType.MouseDown || evt.type == EventType.Repaint))
            {
                cutHover = PickCutPoint(view, evt.mousePosition, evt.shift, loop);
                cutPreview.Clear();
                if (cutDragging && loopSeed.HasValue)
                {
                    var seed = loopSeed.Value; seed.T = EdgeFraction(view, evt.mousePosition, seed.A, seed.B);
                    if(evt.shift)seed.T=Mathf.Round(seed.T*10)/10;seed.T=Mathf.Clamp(seed.T,.001f,.999f);
                    cutPreview = cutTopology.Loop(seed);
                }
                else if (cutHover.HasValue && loop) cutPreview = cutTopology.Loop(cutHover.Value);
                else if (cutStart.HasValue && cutHover.HasValue && cutTopology.TryCut(cutStart.Value,cutHover.Value,out var cut)) cutPreview.Add(cut);
                if(evt.type!=EventType.Repaint)repaint();
            }
            if (overCanvas && evt.type == EventType.MouseDown && evt.button == 0 && GUIUtility.hotControl == 0)
            {
                if(loop && cutHover.HasValue && cutPreview.Count>0)
                { cutDragging=true;loopSeed=cutHover;GUIUtility.hotControl=cutControl; }
                else if(!loop && cutHover.HasValue)
                {
                    if(!cutStart.HasValue)cutStart=cutHover;
                    else if(cutPreview.Count>0) { pendingCuts.Add(cutPreview[0]);cutStart=cutHover; }
                    else error="Choose another edge or vertex on the same face.";
                }
                evt.Use();repaint();
            }
            if(cutDragging && evt.type==EventType.MouseUp && evt.button==0)
            { var cuts=new List<StudioMeshCuts.Cut>(cutPreview);Run(()=>CommitCuts(cuts));ResetCutGesture();evt.Use();repaint(); }
            if(evt.type==EventType.MouseDown && evt.button==1 && overCanvas)
            { Run(()=>CommitCuts(new List<StudioMeshCuts.Cut>(pendingCuts)));ResetCutGesture();suppressModelContext=true;evt.Use();repaint(); }
            if(evt.type==EventType.MouseDrag && cutDragging)evt.Use();
            if(evt.type==EventType.Repaint)DrawCutPreview(view);
            return true;
        }
        private float EdgeFraction(SceneView view,Vector2 mouse,int a,int b)
        {
            var transform=Transforms[Active];var wa=transform.TransformPoint(cutTopology.Positions[a]);var wb=transform.TransformPoint(cutTopology.Positions[b]);
            var pa=HandleUtility.WorldToGUIPoint(wa);var pb=HandleUtility.WorldToGUIPoint(wb);var delta=pb-pa;
            float t=delta.sqrMagnitude<.001f?.5f:Mathf.Clamp01(Vector2.Dot(mouse-pa,delta)/delta.sqrMagnitude);
            if(!view.orthographic) { float za=view.camera.WorldToViewportPoint(wa).z,zb=view.camera.WorldToViewportPoint(wb).z; t=t*za/Mathf.Max(.000001f,(1-t)*zb+t*za); }
            return t;
        }
        private StudioMeshCuts.Point? PickCutPoint(SceneView view,Vector2 mouse,bool snap,bool loop)
        {
            var ray=HandleUtility.GUIPointToWorldRay(mouse);var transform=Transforms[Active];float nearest=float.MaxValue;int face=-1;
            for(int f=0;f<cutTopology.Faces.Count;f++)
            {
                var v=cutTopology.Faces[f].Vertices;
                for(int i=1;i<v.Count-1;i++)
                    if(RayTriangle(ray,transform.TransformPoint(cutTopology.Positions[v[0]]),transform.TransformPoint(cutTopology.Positions[v[i]]),transform.TransformPoint(cutTopology.Positions[v[i+1]]),out float distance)&&distance<nearest)
                    { nearest=distance;face=f; }
            }
            if(face<0)return null;
            var vertices=cutTopology.Faces[face].Vertices;float best=14;StudioMeshCuts.Point? result=null;
            for(int e=0;e<vertices.Count;e++)
            {
                int a=vertices[e],b=vertices[(e+1)%vertices.Count];float t=EdgeFraction(view,mouse,a,b);
                var p=Vector3.Lerp(cutTopology.Positions[a],cutTopology.Positions[b],t);
                float distance=Vector2.Distance(mouse,HandleUtility.WorldToGUIPoint(transform.TransformPoint(p)));
                if(distance>=best)continue;best=distance;
                if(snap)t=Mathf.Round(t*10)/10;
                if(loop)t=Mathf.Clamp(t,.001f,.999f);
                else { if(Vector2.Distance(mouse,HandleUtility.WorldToGUIPoint(transform.TransformPoint(cutTopology.Positions[a])))<8)t=0;
                       else if(Vector2.Distance(mouse,HandleUtility.WorldToGUIPoint(transform.TransformPoint(cutTopology.Positions[b])))<8)t=1; }
                result=new StudioMeshCuts.Point(a,b,t);
            }
            return result;
        }
        private void DrawCutPreview(SceneView view)
        {
            var previous=Handles.color;var depth=Handles.zTest;Handles.zTest=UnityEngine.Rendering.CompareFunction.LessEqual;
            try
            {
                var transform=Transforms[Active];
                Handles.zTest=displayXray ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.LessEqual;
                foreach(var cut in pendingCuts) { Handles.color=new Color(1,.65f,.1f);Handles.DrawAAPolyLine(3,transform.TransformPoint(cutTopology.Position(cut.A)),transform.TransformPoint(cutTopology.Position(cut.B))); }
                foreach(var cut in cutPreview) { Handles.color=Color.yellow;Handles.DrawAAPolyLine(3,transform.TransformPoint(cutTopology.Position(cut.A)),transform.TransformPoint(cutTopology.Position(cut.B))); }
                foreach(var point in new[]{cutStart,cutHover})if(point.HasValue)
                { var world=transform.TransformPoint(cutTopology.Position(point.Value));Handles.color=Color.yellow;Handles.DotHandleCap(0,world,Quaternion.identity,HandleUtility.GetHandleSize(world)*.035f,EventType.Repaint); }
                Handles.BeginGUI();
                var rect=view is MashBoxStudioViewport studio?studio.ModelCanvasRect:new Rect(0,0,view.position.width,view.position.height);
                GUI.Label(new Rect(rect.x+8,rect.y+38,rect.width-16,40),cutTool==CutTool.EdgeLoop?"Insert Edge Loop · drag an edge · Shift: snap · "+StudioShortcuts.Label(StudioShortcut.Cancel)+": exit":"Multi-Cut · "+StudioShortcuts.Label(StudioShortcut.CommitCut)+"/"+StudioShortcuts.Label(StudioShortcut.MarkingMenu)+": apply · Ctrl: loop · "+StudioShortcuts.Label(StudioShortcut.Cancel)+": cancel",StudioTheme.Styles.wordWrappedMiniLabel);
                Handles.EndGUI();
            }
            finally { Handles.color=previous;Handles.zTest=depth; }
        }
    }
}

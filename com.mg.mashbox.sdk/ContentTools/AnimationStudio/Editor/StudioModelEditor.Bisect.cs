using System.Collections.Generic;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private const string BisectToolName = "Bisect";
        private ToolChannelPane bisectPane;
        private StudioMeshToolPreview bisectPreview;
        private Plane bisectPlane;
        private Matrix4x4 bisectWorld;
        private bool bisectPlaced, bisectDragging, bisectValid, bisectQueued;
        private Vector2 bisectStart, bisectEnd;
        private int bisectNode = -1;
        private HashSet<int> bisectPositiveFaces;
        private Vector3 bisectSourcePivot,bisectSplitPivot;
        private Vector4? bisectRepeatedParameters;
        private float BisectDistance => bisectRepeatedParameters?.x ?? Mathf.Max(0, ToolChannelPane.GetFloatDefault(BisectToolName,"Bisect Distance",.01f));
        private bool BisectFill => (bisectRepeatedParameters?.y ?? ToolChannelPane.GetFloatDefault(BisectToolName,"Fill",1)) >= .5f;

        private void OpenBisectTool()
        {
            if (bisectPane) { bisectPane.Focus(); return; }
            FinishChannelEdit(); FinishLiveTool(); CloseLiveToolPanes(); StopCutTool(); StopTargetWeld(); EndPivotSnap();
            PrepareMeshSelection(ComponentMode.Object,()=>true,"",ComponentMode.Object,ComponentMode.Edge,ComponentMode.Face,ComponentMode.Vertex);
            bisectPane=ToolChannelPane.Open(BisectToolName,DrawBisectOptions,()=>{bisectPane=null;CancelBisectPreview();if(cutTool==CutTool.Bisect)cutTool=CutTool.None;repaint();},
                new Vector2(350,220),new Vector2(310,200),execute:ApplyBisect,
                canExecute:()=>CanCutMesh && bisectPlaced,failed:ex=>{error=ex.Message;repaint();},owner:ToolChannelPaneOwner,
                preview:QueueBisectPreview,cancelPreview:()=>CancelBisectPreview(),history:PerformWorkspaceHistory,repeat:PerformRepeatLastAction);
            bisectRepeatedParameters=repeatingAction?.kind==RepeatKind.Bisect?repeatingAction.parameters:(Vector4?)null;
            cutTool=CutTool.Bisect;tool=Tool.None;pivot=false;
            error=CanCutMesh?null:"Select an editable static mesh, then drag a line across it to bisect.";
            FocusToolOptions?.Invoke();repaint();
        }
        private void DrawBisectOptions()
        {
            EditorGUIUtility.labelWidth=145;
            EditorGUI.BeginChangeCheck();
            float distance=EditorGUILayout.FloatField(new GUIContent("Bisect Distance","Full world-space gap in meters. 0.01 is one centimeter; zero separates without a gap."),BisectDistance);
            bool fill=EditorGUILayout.Toggle("Fill",BisectFill);
            if(EditorGUI.EndChangeCheck()&&!float.IsNaN(distance)&&!float.IsInfinity(distance))
            {
                bisectRepeatedParameters=null;
                ToolChannelPane.SetFloatDefault(BisectToolName,"Bisect Distance",Mathf.Max(0,distance));
                ToolChannelPane.SetFloatDefault(BisectToolName,"Fill",fill?1:0);
            }
            GUILayout.Label("Press and release in empty space off the active mesh, dragging across it to place the cutting plane. Apply separates the halves into independent mesh objects. Fill caps closed sections with quads and planar UVs.",StudioTheme.Styles.wordWrappedMiniLabel);
            GUILayout.Label("Apply keeps the tool open; Execute finishes. Cancel restores the unapplied cut.",StudioTheme.Styles.wordWrappedMiniLabel);
        }
        private void CloseBisectTool()
        {
            CancelBisectPreview();var pane=bisectPane;bisectPane=null;if(pane)pane.Close();
        }
        private void QueueBisectPreview()
        {
            if(!bisectPlaced || bisectQueued)return;
            bisectQueued=true;EditorApplication.delayCall+=RunQueuedBisect;
        }
        private void RunQueuedBisect()
        {
            EditorApplication.delayCall-=RunQueuedBisect;bisectQueued=false;
            if(bisectPane && bisectPlaced)Run(EvaluateBisect,false);
        }
        private void RefreshBisect(bool dirty)
        {
            var previous=livePreview;livePreview=bisectPreview;
            try { RefreshLiveMeshes(dirty); } finally { livePreview=previous; }
        }
        private void CancelBisectPreview(bool afterUndo=false)
        {
            EditorApplication.delayCall-=RunQueuedBisect;bisectQueued=false;
            if(bisectDragging && GUIUtility.hotControl==cutControl)GUIUtility.hotControl=0;
            bisectDragging=false;bisectPlaced=false;bisectValid=false;
            if(bisectPreview!=null)
            {
                bisectPreview.Restore(afterUndo);
                if(Document)RefreshBisect(false);
                bisectPreview.Dispose();bisectPreview=null;
            }
            bisectNode=-1;
        }
        private void EvaluateBisect()
        {
            bisectValid=false;
            if(!bisectPlaced||!CanCutMesh)throw new InvalidOperationException("Select a static mesh and drag a cutting line first.");
            if(bisectPreview!=null && (Active!=bisectNode || bisectPreview.Targets[0].Live!=ActiveMesh))
            {CancelBisectPreview();throw new InvalidOperationException("The active mesh changed. Draw a new cut.");}
            if(bisectPreview==null)
            {
                FlushModifierEdits();
                if(Document.nodes[Active].modifiers?.operations.Any(o=>!string.IsNullOrEmpty(o.failure))==true)
                    throw new InvalidOperationException("Repair the broken modifier or Clear History before bisecting.");
                PrepareIsolationTopologyEdit();bisectNode=Active;bisectWorld=Transforms[Active].localToWorldMatrix;
                bisectPreview=new StudioMeshToolPreview(Document,new[]{Active});
            }
            var target=bisectPreview.Targets[0];target.Reset();
            try
            {
                bisectPositiveFaces = StudioMeshCuts.Bisect(target.Work,bisectPlane,bisectWorld,BisectDistance,BisectFill,(source,split)=>{bisectSourcePivot=source;bisectSplitPivot=split;});
                bisectPreview.Publish(true);RefreshBisect(false);bisectValid=true;error=null;
            }
            catch {bisectPreview.Restore();RefreshBisect(false);throw;}
            repaint();
        }
        private void ApplyBisect()
        {
            EditorApplication.delayCall-=RunQueuedBisect;bisectQueued=false;EvaluateBisect();
            if(!bisectValid)return;
            var before=CaptureSelection();
            int source=bisectNode;
            Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Bisect");
            try
            {
                // Extract both evaluated halves before restoring the source for Undo.
                // Restoring also removes the temporary preview from shared instances.
                var split=Document.SplitMeshes(new Dictionary<int,HashSet<int>> { [source]=bisectPositiveFaces },
                    ()=>bisectPreview.Restore(), generatedBase:true, cutPivots:new Dictionary<int,(Vector3 source,Vector3 extracted)>{[source]=(bisectSourcePivot,bisectSplitPivot)});
                bisectPreview.Dispose();bisectPreview=null;bisectPlaced=false;bisectValid=false;bisectNode=-1;
                ClearAllComponentSelections();objects.Clear();objects.Add(source);
                foreach(int node in split) objects.Add(node);
                Active=source;mode=ComponentMode.Object;tool=Tool.Move;pivot=false;
                foreach(var target in componentTargets.Values)target.mode=ComponentMode.Object;
                RecordSelectionChange(before,false,"Bisect");
                Rebuild();Changed();
            }
            finally { Undo.CollapseUndoOperations(group);Undo.IncrementCurrentGroup(); }
            EstablishSelectionBaseline();RememberAction(RepeatKind.Bisect,new Vector4(BisectDistance,BisectFill?1:0,0,0));
            UpdateSubdivision();error=null;repaint();
        }
        private void ReleaseBisectPointer()
        {if(!bisectDragging)return;bisectDragging=false;if(GUIUtility.hotControl==cutControl)GUIUtility.hotControl=0;repaint();}
        private bool BisectSceneGUI(SceneView view,bool overCanvas)
        {
            var evt=Event.current;
            var pointer=PaintInputPosition(view);var rootPointer=view is MashBoxStudioViewport viewport?pointer+viewport.CanvasInputOrigin:pointer;
            var canvas=view is MashBoxStudioViewport studio?studio.ModelCanvasRect:new Rect(Vector2.zero,view.position.size);
            overCanvas=canvas.Contains(rootPointer)&&(!(view is MashBoxStudioViewport overlay)||!overlay.IsOverViewportControls(rootPointer));
            if(bisectPreview!=null && (Active!=bisectNode || bisectPreview.Targets[0].Live!=ActiveMesh))CancelBisectPreview();
            if(evt.alt)
            {if(bisectDragging){bisectDragging=false;if(GUIUtility.hotControl==cutControl)GUIUtility.hotControl=0;}return true;}
            if(overCanvas && evt.type==EventType.MouseDown && evt.button==0 && GUIUtility.hotControl==0)
            {
                if(!CanCutMesh){error="Select an editable static mesh before drawing the cut.";repaint();return true;}
                if(SlicePointerOnMesh(view,pointer)){error="Start the bisect line in empty space off the active mesh.";repaint();evt.Use();return true;}
                CancelBisectPreview();bisectStart=bisectEnd=PaintInputPosition(view);bisectDragging=true;GUIUtility.hotControl=cutControl;evt.Use();repaint();
            }
            if(bisectDragging && evt.type==EventType.MouseDrag)
            {bisectEnd=PaintInputPosition(view);evt.Use();repaint();}
            if(bisectDragging && evt.type==EventType.MouseUp && evt.button==0)
            {
                bisectEnd=PaintInputPosition(view);bisectDragging=false;GUIUtility.hotControl=0;
                if(SlicePointerOnMesh(view,bisectEnd))error="Release in empty space off the active mesh to finish the bisect.";
                else if(!SliceSegmentCrossesMesh(view,bisectStart,bisectEnd))error="The drawn segment does not cross the active mesh.";
                else if((bisectEnd-bisectStart).sqrMagnitude>=16&&TryCameraSlicePlane(view,bisectStart,bisectEnd,out bisectPlane))
                {bisectPlaced=true;Run(EvaluateBisect,false);}
                evt.Use();repaint();
            }
            if(evt.type==EventType.Repaint && bisectDragging)
            {
                Handles.BeginGUI();var color=Handles.color;Handles.color=CutPreviewColor;
                var anchor=Transforms[Active].position;var offset=HandleUtility.WorldToGUIPoint(anchor)-PaintWorldToGUI(view.camera,anchor);var start=bisectStart+offset;var end=bisectEnd+offset;Handles.DrawAAPolyLine(3,new Vector3(start.x,start.y),new Vector3(end.x,end.y));
                Handles.color=color;Handles.EndGUI();
            }
            return true;
        }
    }
}

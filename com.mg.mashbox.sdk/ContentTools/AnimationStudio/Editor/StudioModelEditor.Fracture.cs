using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private ToolChannelPane fracturePane;
        private StudioMeshFracture.Settings fractureSettings;
        private StudioMeshFracture.Input fractureInput;
        private StudioMeshFracture.Result fractureResult;
        private StudioMeshToolPreview fracturePreview;
        private CancellationTokenSource fractureCancel;
        private Task<StudioMeshFracture.Result> fractureTask;
        private sealed class FractureProgressState { internal StudioMeshFracture.Progress Value; }
        private FractureProgressState fractureProgressState;
        private StudioMeshFracture.Progress fractureShownProgress;
        private Material fractureMaterial;
        private Material[][] fractureOriginalMaterials;
        private StudioModelDocument.Node[] fractureSharedNodes;
        private int fractureNode=-1;
        private bool fractureStale;
        private float fractureExplode=.15f;
        private bool FractureBusy=>fractureTask!=null;

        private void OpenFractureTool()
        {
            if(fracturePane){fracturePane.Focus();return;}
            FinishChannelEdit(); FinishToolsForSwitch();StopCutTool();StopTargetWeld();EndPivotSnap();
            PrepareMeshSelection(ComponentMode.Object,()=>true,"",ComponentMode.Object,ComponentMode.Edge,ComponentMode.Face,ComponentMode.Vertex);
            fractureSettings=new StudioMeshFracture.Settings();fractureMaterial=null;fractureExplode=.15f;fractureStale=false;
            fracturePane=ToolChannelPane.Open("Fracture",DrawFractureOptions,()=>{fracturePane=null;CancelFracture();repaint();},
                new Vector2(360,460),new Vector2(310,300),execute:CommitFracture,canExecute:()=>!FractureBusy&&fractureResult!=null&&!fractureStale,
                failed:ex=>{error=ex.Message;repaint();},owner:ToolChannelPaneOwner,cancelPreview:()=>CancelFracture(),history:PerformWorkspaceHistory,repeat:PerformRepeatLastAction);
            tool=Tool.None;pivot=false;FocusToolOptions?.Invoke();error=CanCutMesh?null:"Select a closed static mesh, then press Compute.";repaint();
        }
        private void DrawFractureOptions()
        {
            EditorGUIUtility.labelWidth=145;
            using(new EditorGUI.DisabledScope(FractureBusy))
            {
                EditorGUI.BeginChangeCheck();
                fractureSettings.Count=EditorGUILayout.IntField("Piece count",fractureSettings.Count);
                fractureSettings.Seed=EditorGUILayout.IntField("Random seed",fractureSettings.Seed);
                fractureSettings.Variation=EditorGUILayout.Slider("Size variation",fractureSettings.Variation,0,1);
                fractureSettings.Direction=EditorGUILayout.Vector3Field("Bias direction",fractureSettings.Direction);
                fractureSettings.Bias=EditorGUILayout.Slider("Directional bias",fractureSettings.Bias,0,1);
                fractureSettings.SimplifyInterior=EditorGUILayout.Toggle(new GUIContent("Simplify Interior","Replace dense rough interior patches with planar center fans. Extra centers partition concave sections and holes without overlaps; boundary connectivity is retained."),fractureSettings.SimplifyInterior);
                using(new EditorGUI.DisabledScope(fractureSettings.SimplifyInterior))
                {
                fractureSettings.Roughness=StudioSceneUnits.Field(new GUIContent("Fracture roughness","Mesh-local displacement amplitude. Exterior geometry remains fixed."),fractureSettings.Roughness);
                fractureSettings.Detail=StudioSceneUnits.Field(new GUIContent("Detail scale","Mesh-local quad-patch sampling and roughness wavelength. Smaller values cost more."),fractureSettings.Detail);
                }
                fractureMaterial=(Material)EditorGUILayout.ObjectField("Interior material",fractureMaterial,typeof(Material),false);
                fractureSettings.UVScale=EditorGUILayout.FloatField(new GUIContent("Interior UV scale","UV repeats per mesh-local meter."),fractureSettings.UVScale);
                fractureSettings.WeldTolerance=StudioSceneUnits.Field(new GUIContent("Weld tolerance","Weld nearby source boundary identities and clean compatible patch samples; UV and normal seams remain separate."),fractureSettings.WeldTolerance);
                var sourceBounds=fractureInput!=null?new Bounds((fractureInput.Positions.Aggregate(Vector3.Min)+fractureInput.Positions.Aggregate(Vector3.Max))*.5f,fractureInput.Positions.Aggregate(Vector3.Max)-fractureInput.Positions.Aggregate(Vector3.Min)):ActiveMesh?ActiveMesh.bounds:new Bounds(Vector3.zero,Vector3.one);
                var size=sourceBounds.size;float gapLimit=Mathf.Max(0,Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.02f);
                fractureSettings.Gap=StudioSceneUnits.Internal(EditorGUILayout.Slider(new GUIContent("Piece Gap ("+StudioSceneUnits.Symbol+")","Slightly shrink each fragment to open clearance, including its exterior. Included in commit and export. Range: zero to 2% of the source's shortest bounding dimension; thin fragments may require less."),StudioSceneUnits.Display(fractureSettings.Gap),0,StudioSceneUnits.Display(gapLimit)));
                if(EditorGUI.EndChangeCheck())fractureStale=fractureResult!=null;
                if(GUILayout.Button(fractureInput==null?"Compute":"Recompute from Original"))Run(StartFracture,false);
                EditorGUI.BeginChangeCheck();fractureExplode=EditorGUILayout.Slider("Exploded inspection",fractureExplode,0,1);
                if(EditorGUI.EndChangeCheck()&&fractureResult!=null)Run(PublishFractureInspection,false);
            }
            if(FractureBusy)
            {
                var progress=fractureProgressState?.Value;var rect=GUILayoutUtility.GetRect(1,20,GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(rect,progress?.Fraction??0,progress?.Stage??"Starting computation");
                if(GUILayout.Button("Cancel computation"))CancelFracture();
            }
            else if(fractureResult!=null)GUILayout.Label(fractureStale?"Settings changed. Recompute before committing.":fractureResult.Pieces.Count+" fragments ready. Execute or Apply commits Piece Gap, without exploded-inspection offsets.",StudioTheme.Styles.wordWrappedMiniLabel);
            GUILayout.Label("Closed manifold static solids; 2–64 seeded pieces. Compute alone generates geometry. Cancel restores the original. Outputs start independent history bases; Undo restores the source stack.",StudioTheme.Styles.wordWrappedMiniLabel);
        }
        private void StartFracture()
        {
            if(FractureBusy)return;
            if(!CanCutMesh)throw new InvalidOperationException("Select a closed static mesh before computing Fracture.");
            if(fracturePreview!=null&&(Active!=fractureNode||ActiveMesh!=fracturePreview.Targets[0].Live))CancelFracture();
            if(fracturePreview==null)
            {
                FlushModifierEdits();var node=Document.nodes[Active];
                if(node.modifiers?.operations.Any(o=>!string.IsNullOrEmpty(o.failure))==true)throw new InvalidOperationException("Repair the broken modifier or Clear History before fracturing.");
                if(Document.IsBone(Active)||!string.IsNullOrEmpty(node.anchorType)||Mathf.Abs(Transforms[Active].localToWorldMatrix.determinant)<1e-12f)throw new InvalidOperationException("Fracture requires an ordinary static object with a nonsingular transform.");
                PrepareIsolationTopologyEdit();fractureNode=Active;fracturePreview=new StudioMeshToolPreview(Document,new[]{Active});
                fractureSharedNodes=Document.nodes.Where(n=>n.mesh==ActiveMesh).ToArray();fractureOriginalMaterials=fractureSharedNodes.Select(n=>n.materials.ToArray()).ToArray();
                try{fractureInput=StudioMeshFracture.Capture(fracturePreview.Targets[0].Work);}catch{CancelFracture();throw;}
            }
            RestoreFractureInspection();fractureResult=null;fractureStale=false;
            fractureCancel=new CancellationTokenSource();var token=fractureCancel.Token;var settings=fractureSettings.Copy();var input=fractureInput;
            var progressState=new FractureProgressState{Value=new StudioMeshFracture.Progress("Starting computation",0)};fractureProgressState=progressState;fractureShownProgress=null;
            fractureTask=Task.Run(()=>StudioMeshFracture.Compute(input,settings,token,p=>Volatile.Write(ref progressState.Value,p)),token);
            EditorApplication.update+=PollFracture;repaint();
        }
        private void PollFracture()
        {
            if(fractureTask==null){EditorApplication.update-=PollFracture;return;}
            if(!fracturePane||!Document||Active!=fractureNode||ActiveMesh!=fracturePreview?.Targets[0].Live){CancelFracture();return;}
            var progress=fractureProgressState?.Value;
            if(!ReferenceEquals(progress,fractureShownProgress)){fractureShownProgress=progress;repaint();}
            if(!fractureTask.IsCompleted)return;
            var task=fractureTask;fractureTask=null;EditorApplication.update-=PollFracture;fractureCancel.Dispose();fractureCancel=null;
            fractureProgressState=null;fractureShownProgress=null;
            if(task.IsCanceled){CancelFracture();return;}
            if(task.IsFaulted){var message=task.Exception.GetBaseException().Message;CancelFracture();error=message;repaint();return;}
            fractureResult=task.Result;
            try{PublishFractureInspection();error=null;}catch(Exception ex){CancelFracture();error=ex.Message;}
            repaint();
        }
        private void RefreshFracture()
        {
            if(!Document||fracturePreview==null)return;var previous=livePreview;livePreview=fracturePreview;
            try{RefreshLiveMeshes(false);ApplyDisplayMaterials();}finally{livePreview=previous;}
        }
        private void RestoreFractureInspection(bool afterUndo=false)
        {
            if(fracturePreview==null)return;
            fracturePreview.Restore(afterUndo);
            if(!afterUndo&&fractureSharedNodes!=null)for(int i=0;i<fractureSharedNodes.Length;i++)fractureSharedNodes[i].materials=fractureOriginalMaterials[i];
            RefreshFracture();
        }
        private void PublishFractureInspection()
        {
            if(fractureResult==null||fracturePreview==null)return;
            StudioModelDocument.Node combined=null;
            try
            {
                combined=StudioMeshFracture.PublishInspection(fractureResult,fractureExplode);
                var target=fracturePreview.Targets[0];StudioMeshToolPreview.CopyMesh(combined.mesh,target.Work.mesh);
                target.Work.cutPolygons=combined.cutPolygons;target.Work.cutTriangles=combined.cutTriangles;target.Work.geometryVertices=combined.geometryVertices;target.Work.normalGroups=combined.normalGroups;
                int subCount=fractureResult.Source.Submeshes+1;
                for(int i=0;i<fractureSharedNodes.Length;i++){var materials=new Material[subCount];Array.Copy(fractureOriginalMaterials[i],materials,Math.Min(subCount-1,fractureOriginalMaterials[i].Length));materials[subCount-1]=fractureMaterial;fractureSharedNodes[i].materials=materials;}
                fracturePreview.Publish(true);RefreshFracture();repaint();
            }
            finally{if(combined?.mesh)UnityEngine.Object.DestroyImmediate(combined.mesh);}
        }
        private void CommitFracture()
        {
            if(FractureBusy||fractureResult==null||fractureStale)throw new InvalidOperationException("Compute the current Fracture settings before committing.");
            if(Active!=fractureNode||ActiveMesh!=fracturePreview.Targets[0].Live)throw new InvalidOperationException("The fracture source selection changed; recompute first.");
            var before=CaptureSelection();Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
            try
            {
                var selected=Document.FractureMesh(fractureNode,fractureResult,fractureMaterial,()=>RestoreFractureInspection());
                fracturePreview.Dispose();fracturePreview=null;fractureInput=null;fractureResult=null;fractureSharedNodes=null;fractureOriginalMaterials=null;fractureNode=-1;
                ClearAllComponentSelections();objects.Clear();foreach(int node in selected)objects.Add(node);Active=selected[0];mode=ComponentMode.Object;tool=Tool.Move;pivot=false;
                RecordSelectionChange(before,false,"Fracture");Rebuild();Changed();
            }
            finally{Undo.CollapseUndoOperations(group);Undo.IncrementCurrentGroup();}
            EstablishSelectionBaseline();UpdateSubdivision();
            lastRepeatAction=new RepeatAction{kind=RepeatKind.Fracture,fracture=fractureSettings.Copy(),fractureMaterial=fractureMaterial,welded=weld};error=null;repaint();
        }
        private void CancelFracture(bool afterUndo=false)
        {
            EditorApplication.update-=PollFracture;
            if(fractureTask!=null)
            {
                var task=fractureTask;var cancellation=fractureCancel;fractureTask=null;fractureCancel=null;cancellation.Cancel();
                // Observe worker failures and release its token after it exits; no Unity API in this continuation.
                task.ContinueWith(t=>{if(t.IsFaulted){var ignored=t.Exception;}cancellation.Dispose();},TaskScheduler.Default);
            }
            RestoreFractureInspection(afterUndo);fracturePreview?.Dispose();fracturePreview=null;fractureInput=null;fractureResult=null;fractureSharedNodes=null;fractureOriginalMaterials=null;fractureNode=-1;fractureStale=false;
            fractureProgressState=null;fractureShownProgress=null;repaint();
        }
        private void CloseFractureTool()
        {var pane=fracturePane;fracturePane=null;if(pane)pane.Close();else if(fracturePreview!=null||FractureBusy)CancelFracture();}
    }
}


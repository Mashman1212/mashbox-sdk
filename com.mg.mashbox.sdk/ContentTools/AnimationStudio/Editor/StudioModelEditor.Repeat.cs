using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal static readonly StudioShortcut RepeatActionShortcut = StudioModelCommands.StableId("Edit/Repeat Last Action");
        private enum RepeatKind { Live, Combine, Split, Duplicate, Delete, Primitive, Empty, CenterPivot, Freeze, Soften, Harden, Blend, Bake, Cut, TargetWeld, Bridge, Group, Bisect, RecalculateNormals, BakePivot, Fracture, MaterialSlot, DuplicateFace, DetachFace, Poke, FillHole, EdgeRingCollapse }
        private sealed class RepeatAction
        {
            internal StudioMeshFracture.Settings fracture; internal Material fractureMaterial;
            internal RepeatKind kind; internal LiveTool live; internal Vector4 parameters; internal Vector3 flow; internal bool welded;
        }
        private RepeatAction lastRepeatAction, repeatingAction;
        private bool CanRepeatLastAction => Document && lastRepeatAction != null;
        private void RememberAction(RepeatKind kind, Vector4 parameters = default)
        {
            if (repeatingAction != null) return;
            lastRepeatAction = new RepeatAction { kind = kind, parameters = parameters, welded = weld };
        }
        private void RememberLiveAction(LiveTool kind, Vector4 parameters, Vector3 flow)
        {
            if (repeatingAction != null) return;
            lastRepeatAction = new RepeatAction { kind = RepeatKind.Live, live = kind, parameters = parameters, flow = flow, welded = weld };
        }
        private void PerformRepeatLastAction() => Run(RepeatLastAction, false);
        private void RepeatLastAction()
        {
            // G finishes an existing preview once, without doubling that operation.
            if(FractureBusy) throw new InvalidOperationException("Wait for Fracture to finish or cancel computation.");
            if(fractureResult!=null){CommitFracture();return;}
            if(bisectPreview!=null){ApplyBisect();return;}
            if (livePreview != null || componentLiveTargets != null || previewQueued) { FinishLiveTool(); return; }
            if (!CanRepeatLastAction) throw new InvalidOperationException("There is no completed modeling action to repeat in this workspace.");
            var action = lastRepeatAction; var oldWeld = weld;
            float oldDistance = extrusionDistance, oldInset = extrusionInset; bool oldTogether = extrudeFacesTogether;
            repeatingAction = action; weld = action.welded;
            try
            {
                switch (action.kind)
                {
                    case RepeatKind.FillHole: FillSelectedHoles(); break;
                    case RepeatKind.EdgeRingCollapse: CollapseSelectedEdgeRings(); break;
                    case RepeatKind.Poke: PokeSelectedFaces(); break;
                    case RepeatKind.DuplicateFace: EditSelectedFaces(true,action.parameters.x>=.5f); break;
                    case RepeatKind.DetachFace: EditSelectedFaces(false); break;
                    case RepeatKind.MaterialSlot: AssignSelectionMaterialSlot(Mathf.RoundToInt(action.parameters.x)); break;
                    case RepeatKind.Live:
                        if (!EligibleComponentTool(action.live)) throw new InvalidOperationException("The last action is unavailable for the current component selection.");
                        if (action.live == LiveTool.Extrude) { extrusionDistance = action.parameters.x; extrudeFacesTogether = action.parameters.y >= .5f; extrusionInset = action.parameters.z; }
                        ApplyLiveTool(action.live); break;
                    case RepeatKind.Fracture: OpenFractureTool(); fractureSettings=action.fracture.Copy(); fractureMaterial=action.fractureMaterial; break;
                    case RepeatKind.Bisect: OpenBisectTool(); break;
                    case RepeatKind.Group: GroupSelectedObjects(); break;
                    case RepeatKind.Combine: CombineSelectedObjects(); break;
                    case RepeatKind.Bridge: ChooseSelectionAction("Bridge Edges", BridgeSelectedEdges, "Select two non-touching boundary edge groups on one mesh, with equal edge counts."); break;
                    case RepeatKind.Split:
                        if (!CanSplitSelection) throw new InvalidOperationException("Select components to repeat Split Selection.");
                        SplitSelectedComponents(); break;
                    case RepeatKind.Duplicate:
                        if (!CanDuplicateObjects) throw new InvalidOperationException("Select objects to repeat Duplicate.");
                        DuplicateSelectedObjects(); break;
                    case RepeatKind.Delete:
                        if (!CanDeleteSelection) throw new InvalidOperationException("Select objects or components to repeat Delete.");
                        DeleteSelection(); break;
                    case RepeatKind.Primitive: CreatePrimitive((PrimitiveType)Mathf.RoundToInt(action.parameters.x),commandView,commandLoaded); break;
                    case RepeatKind.Empty:
                        if (action.parameters.x >= .5f && Active < 0) throw new InvalidOperationException("Select a parent to repeat Empty Child.");
                        CreateEmptyTransform(action.parameters.x >= .5f,commandView,commandLoaded); break;
                    case RepeatKind.CenterPivot:
                        if (!CanAdjustPivot) throw new InvalidOperationException("Select an editable pivot to center.");
                        CenterEditingPivot(); break;
                    case RepeatKind.BakePivot: BakeEditingPivot(); break;
                    case RepeatKind.Freeze:
                        if (!Document || Active < 0 || mode != ComponentMode.Object) throw new InvalidOperationException("Select an object to freeze.");
                        FreezeEditingObject(); break;
                    case RepeatKind.Soften: ApplyEdgeNormals(false); break;
                    case RepeatKind.Harden: ApplyEdgeNormals(true); break;
                    case RepeatKind.RecalculateNormals: RecalculateNormals(); break;
                    case RepeatKind.Blend:
                        if (SelectedBlendMeshes.Length < 2) throw new InvalidOperationException("Select at least two compatible meshes to create a blend shape.");
                        CreateSelectedBlendShape(); break;
                    case RepeatKind.Bake:
                        if (!HasBlendShape) throw new InvalidOperationException("Select a live blend shape to bake.");
                        BakeSelectedBlendShape(); break;
                    case RepeatKind.Cut:
                        if (!CanCutMesh) throw new InvalidOperationException("Select an editable static mesh to repeat the cut tool.");
                        edgeLoopCount = Mathf.Clamp(Mathf.RoundToInt(action.parameters.y),1,MaximumEdgeLoops);
                        ActivateCutTool((CutTool)Mathf.RoundToInt(action.parameters.x)); break;
                    case RepeatKind.TargetWeld:
                        if (targetWeldActive) StopTargetWeld(); ActivateTargetWeld(); break;
                }
            }
            catch
            {
                // A failed repeat must not strand a new invalid preview or replace
                // the last successful action. Existing preview cancellation restores it.
                if (action.kind == RepeatKind.Live) CancelLiveTool();
                throw;
            }
            finally { weld = oldWeld; extrusionDistance = oldDistance; extrusionInset = oldInset; extrudeFacesTogether = oldTogether; repeatingAction = null; }
        }
    }
}

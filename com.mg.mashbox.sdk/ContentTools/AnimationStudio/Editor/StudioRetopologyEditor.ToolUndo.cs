using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        // Construction, selection and geometry share Unity's document history.
        // Never intercept Ctrl+Z with a separate stack that can get out of order.
        private void StoreToolState()
        {
            Document.polygonPoints.Clear();Document.polygonPoints.AddRange(corners);
            Document.polygonPart=Active!=null?Document.selected:-1;Document.polygonNormal=cornerNormal;
            Document.toolState=new StudioRetopologyDocument.ToolState {
                tool=(int)activeTool,selectionMode=(int)selectionMode,transformTool=(int)transformTool,
                selected=selected.ToArray(),selectedFaces=selectedFaces.ToArray(),selectedEdges=selectedEdges.SelectMany(e=>new[]{e.Item1,e.Item2}).ToArray(),
                continuation=patchOwner==Active?patchContinuation?.ToArray():null,mirrorContinuation=patchOwner==Active?mirroredPatchContinuation?.ToArray():null,
                continuousPatch=continuousPatch,autoDensity=patchAutoDensity,divisions=patchSegments,quadSize=patchQuadSize,tubeSides=tubeSides,tubeRows=tubeDivisions,
                stripWidth=stripWidth,relaxRadius=relaxRadius,relaxStrength=relaxStrength,relaxBorders=relaxBorders,resolutionRadius=resolutionRadius,
                weldSource=weldSource,bridgeA=bridgeSource?.Item1??-1,bridgeB=bridgeSource?.Item2??-1,
                stripLeft=stripLeft,stripRight=stripRight,stripMirrorLeft=stripMirrorLeft,stripMirrorRight=stripMirrorRight,
                stripCenter=stripCenter,stripNormal=stripNormal,stripSide=stripSide,stripMirror=stripMirror,stripClosed=stripClosed,stripFirstRow=stripFirstRow
            };
        }

        private void RestoreToolState()
        {
            tubePreview=null;
            if(Document.polygonPart!=Document.selected||Active==null)return;
            var state=Document.toolState;
            if(state==null)
            {
                // Workspaces saved with the initial polygon-only draft format.
                if(Document.polygonPoints.Count==0)return;
                activeTool=RetopoTool.Polygon;corners.AddRange(Document.polygonPoints);cornerNormal=Document.polygonNormal;return;
            }
            activeTool=(RetopoTool)state.tool;selectionMode=(SelectionMode)state.selectionMode;transformTool=(Tool)state.transformTool;
            corners.Clear();corners.AddRange(Document.polygonPoints);cornerNormal=Document.polygonNormal;
            if(state.selected!=null)selected.UnionWith(state.selected.Where(i=>i>=0&&i<Active.vertices.Count));
            if(state.selectedFaces!=null)selectedFaces.UnionWith(state.selectedFaces.Where(i=>i>=0&&i<Active.faces.Count));
            if(state.selectedEdges!=null)for(int i=0;i+1<state.selectedEdges.Length;i+=2)
            {int a=state.selectedEdges[i],b=state.selectedEdges[i+1];if(a>=0&&b>=0&&a<Active.vertices.Count&&b<Active.vertices.Count)selectedEdges.Add((a,b));}
            patchContinuation=state.continuation?.ToArray();mirroredPatchContinuation=state.mirrorContinuation?.ToArray();
            if(patchContinuation!=null&&patchContinuation.Any(i=>i<0||i>=Active.vertices.Count))patchContinuation=mirroredPatchContinuation=null;
            patchOwner=patchContinuation!=null?Active:null;continuousPatch=state.continuousPatch;
            patchAutoDensity=state.autoDensity;patchSegments=state.divisions;patchQuadSize=state.quadSize;tubeSides=state.tubeSides;tubeDivisions=state.tubeRows;
            stripWidth=state.stripWidth;relaxRadius=state.relaxRadius;relaxStrength=state.relaxStrength;relaxBorders=state.relaxBorders;resolutionRadius=state.resolutionRadius;
            weldSource=state.weldSource<Active.vertices.Count?state.weldSource:-1;
            bridgeSource=state.bridgeA>=0&&state.bridgeB>=0&&state.bridgeA<Active.vertices.Count&&state.bridgeB<Active.vertices.Count?((int,int)?)(state.bridgeA,state.bridgeB):null;
            stripLeft=state.stripLeft;stripRight=state.stripRight;stripMirrorLeft=state.stripMirrorLeft;stripMirrorRight=state.stripMirrorRight;
            if(new[]{stripLeft,stripRight,stripMirrorLeft,stripMirrorRight}.Any(i=>i>=Active.vertices.Count))ResetStrip();
            stripCenter=state.stripCenter;stripNormal=state.stripNormal;stripSide=state.stripSide;stripMirror=state.stripMirror;stripClosed=state.stripClosed;stripFirstRow=state.stripFirstRow;
            rotation=Quaternion.identity;scale=Vector3.one;
            if(activeTool==RetopoTool.Tube&&corners.Count==2)
            {
                // An invalid pair remains editable and undoable, without a preview.
                try{UpdateTubePreview();}catch(InvalidOperationException){tubePreview=null;}
            }
        }

        private int RecordToolAction(string label)
        {
            StoreToolState();Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(Document,label);Document.dirty=true;EditorUtility.SetDirty(Document);return group;
        }

        private void AddConstructionPoint(Vector3 point,Vector3 normal)
        {
            if(corners.Contains(point)&&!(activeTool==RetopoTool.Tube&&corners.Count>=2))return;
            int group=RecordToolAction("Place "+activeTool+" point");
            if(activeTool==RetopoTool.Tube&&corners.Count>=2){ClearPatchCorners();tubePreview=null;}
            corners.Add(point);cornerNormal=normal;StoreToolState();
            if((activeTool==RetopoTool.Quad||activeTool==RetopoTool.Patch)&&corners.Count==4)
            {
                try{CreatePatch();}finally{Undo.CollapseUndoOperations(group);}
            }
            else if(activeTool==RetopoTool.Tube&&corners.Count==2)UpdateTubePreview();
        }

        private void RemoveConstructionPoint()
        {
            if(corners.Count==0)return;
            RecordToolAction("Remove "+activeTool+" point");
            if(patchContinuation!=null&&corners.Count<=2)ClearPatchCorners();
            else corners.RemoveAt(corners.Count-1);
            StoreToolState();if(activeTool==RetopoTool.Tube)UpdateTubePreview();
        }

        private void CancelConstruction()
        {
            bool pending=corners.Count>0||weldSource>=0||bridgeSource.HasValue||stripLeft>=0||selected.Count>0||selectedFaces.Count>0||selectedEdges.Count>0;
            if(pending)RecordToolAction("Cancel "+activeTool+" action");
            ClearPatchCorners();ClearComponents();tubePreview=null;
            if(pending)StoreToolState();repaint();
        }

        private void SetWeldSource(int source)
        {if(weldSource==source)return;RecordToolAction("Choose weld source");weldSource=source;StoreToolState();}

        private void SetBridgeSource((int,int)? source)
        {if(bridgeSource==source)return;RecordToolAction("Choose bridge source");bridgeSource=source;StoreToolState();}
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private float relaxRadius=64,relaxStrength=.5f;
        private bool relaxBorders=true,relaxStroke,relaxQuadCleanup;
        private int relaxControl,relaxUndo;
        private bool relaxWeldCleanup;
        private float relaxWeldFraction=.1f;
        private Vector2 relaxLast;
        private void StartRelaxBrush(){activeTool=RetopoTool.RelaxBrush;ClearPatchCorners();repaint();}
        private void EndRelaxBrush(bool cancel=false)
        {
            if(!relaxStroke)return;
            relaxStroke=false;if(GUIUtility.hotControl==relaxControl)GUIUtility.hotControl=0;
            if(cancel)Undo.RevertAllDownToGroup(relaxUndo);else Undo.CollapseUndoOperations(relaxUndo);
            repaint();
        }
        private void DrawRelaxBrushSettings()
        {
            if(activeTool!=RetopoTool.RelaxBrush)return;
            GUILayout.Space(6);GUILayout.Label("RELAX BRUSH",StudioTheme.Styles.boldLabel);
            int mode=EditorGUILayout.Popup("Mode",relaxWeldCleanup?2:relaxQuadCleanup?1:0,new[]{"Relax","Quad Cleanup","Weld Cleanup"});
            relaxQuadCleanup=mode==1;relaxWeldCleanup=mode==2;
            if(relaxWeldCleanup)relaxWeldFraction=EditorGUILayout.Slider(new GUIContent("Weld distance (%)","Maximum distance as a percentage of the shorter local edge. Only visible border vertices inside the brush are candidates."),relaxWeldFraction*100,1,50)*.01f;
            relaxRadius=EditorGUILayout.Slider("Radius (px)",relaxRadius,12,200);
            if(!relaxWeldCleanup)relaxStrength=EditorGUILayout.Slider("Strength",relaxStrength,0,1);
            relaxBorders=true;GUILayout.Label(relaxWeldCleanup?"Joins nearby border vertices; preserves face UVs.":"Open borders stay fixed while relaxing.",StudioTheme.Styles.miniLabel);
            GUILayout.Label(relaxWeldCleanup?"Drag over nearby disconnected borders to weld them. Weld distance is relative to local edge length. Reduce it near separate surfaces. Invalid joins are skipped. One Undo per stroke; Escape cancels.":"Drag to even out edge spacing. Ctrl + MMB drag: sideways radius, up/down strength. [ / ] adjusts radius. Surface snapping keeps new positions fitted to the reference. One Undo per stroke; Escape cancels. Relax preserves connectivity. Quad Cleanup pairs triangles and improves quad edges inside the brush, protecting the region boundary and authored UV faces.",StudioTheme.Styles.wordWrappedMiniLabel);
        }
        private void RelaxDab(Vector2 mouse)
        {
            if(!relaxWeldCleanup&&relaxStrength<=0)return;
            float saved=resolutionRadius;resolutionRadius=relaxRadius;resolutionFaces.Clear();
            try{CollectResolutionFaces(mouse);}finally{resolutionRadius=saved;}
            var weights=new Dictionary<int,float>();
            foreach(int face in resolutionFaces)foreach(int i in Active.faces[face].vertices)
            {
                float t=1-Mathf.Clamp01((HandleUtility.WorldToGUIPoint(Active.vertices[i])-mouse).magnitude/relaxRadius);
                if(t>0)weights[i]=t*t*(3-2*t);
            }
            var region=new HashSet<int>(resolutionFaces);
            resolutionFaces.Clear();if(weights.Count==0)return;
            // Change supplies rollback on errors; all dabs collapse into the stroke's Undo group.
            ChangeCore(relaxWeldCleanup?"Weld cleanup brush":relaxQuadCleanup?"Quad cleanup brush":"Relax topology brush",()=>
            {
                if(relaxWeldCleanup)
                {
                    int welded=StudioRetopologyWeldCleanup.Apply(Active,weights,relaxWeldFraction);
                    status=$"Weld cleanup: {welded} border vertex pairs joined.";return;
                }
                int changes=relaxQuadCleanup?StudioRetopologyQuadCleanup.Apply(Active,region,weights):0;
                StudioRetopologyRelaxBrush.Apply(Active,weights,relaxStrength,true,Document.project?Surface:null,Document.offset);
                if(relaxQuadCleanup)status=$"Quad cleanup: {changes} connectivity improvements; region boundary preserved.";
            },relaxQuadCleanup||relaxWeldCleanup);
        }
        private bool HandleRelaxBrush(Event evt,MashBoxStudioViewport canvas,bool overUI)
        {
            int control=GUIUtility.GetControlID("Retopology relax brush".GetHashCode(),FocusType.Passive);
            if(relaxStroke)
            {
                if(evt.type==EventType.KeyDown&&evt.keyCode==KeyCode.Escape){EndRelaxBrush(true);evt.Use();return true;}
                if(GUIUtility.hotControl!=relaxControl||evt.type==EventType.Ignore||evt.type==EventType.MouseLeaveWindow){EndRelaxBrush();return true;}
                if(evt.type==EventType.MouseUp&&evt.button==0){EndRelaxBrush();evt.Use();return true;}
                if(evt.type==EventType.MouseDrag&&evt.button==0)
                {
                    if(!overUI&&!evt.alt&&(evt.mousePosition-relaxLast).magnitude>=Mathf.Max(2,relaxRadius*.08f))
                    {relaxLast=evt.mousePosition;Run(()=>RelaxDab(relaxLast));}
                    evt.Use();repaint();
                }
            }
            if(activeTool!=RetopoTool.RelaxBrush)return false;
            if(!overUI&&!contextHeld&&!adjustingRetopoBrush&&!evt.alt)
            {
                if(evt.type==EventType.KeyDown&&!EditorGUIUtility.editingTextField&&(evt.keyCode==KeyCode.LeftBracket||evt.keyCode==KeyCode.RightBracket))
                {relaxRadius=Mathf.Clamp(relaxRadius*(evt.keyCode==KeyCode.RightBracket?1.15f:1/1.15f),12,200);evt.Use();repaint();}
                if(evt.type==EventType.MouseDown&&evt.button==0&&GUIUtility.hotControl==0&&Active.showOutput&&canvas.ModelCanvasRect.Contains(evt.mousePosition))
                {
                    relaxUndo=RecordToolAction("Relax topology brush");
                    relaxStroke=true;relaxControl=control;GUIUtility.hotControl=control;relaxLast=evt.mousePosition;
                    Run(()=>RelaxDab(relaxLast));evt.Use();repaint();
                }
                if(evt.type==EventType.MouseMove)repaint();
                if(evt.type==EventType.Repaint)
                {
                    var color=Handles.color;Handles.BeginGUI();
                    try{Handles.color=Active.showOutput&&PickFace(evt.mousePosition)>=0?new Color(.5f,1,.87f):StudioBrushCursor.Inactive;Handles.DrawWireDisc(evt.mousePosition,Vector3.forward,relaxRadius);GUI.Label(new Rect(evt.mousePosition.x+16,evt.mousePosition.y+16,160,20),$"{(relaxWeldCleanup?"Weld Cleanup":relaxQuadCleanup?"Quad Cleanup":"Relax")}  {(relaxWeldCleanup?relaxWeldFraction:relaxStrength):P0}",EditorStyles.whiteMiniLabel);}
                    finally{Handles.EndGUI();Handles.color=color;}
                }
            }
            return relaxStroke;
        }
    }
}

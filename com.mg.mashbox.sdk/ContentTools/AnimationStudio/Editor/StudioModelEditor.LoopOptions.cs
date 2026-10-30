using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private const int MaximumEdgeLoops = 64;
        private int edgeLoopCount = Mathf.Clamp(Mathf.RoundToInt(StudioPreferenceStore.GetFloat("Model.EdgeLoopCount", 1)), 1, MaximumEdgeLoops);
        private static readonly Color CutPreviewColor = new Color(.4f, .65f, 1f, 1f);
        private static readonly Color CutHoverColor = new Color(1f, .95f, .5f, 1f);

        private void SetEdgeLoopCount(int value)
        {
            value = Mathf.Clamp(value, 1, MaximumEdgeLoops);
            if (edgeLoopCount == value) return;
            edgeLoopCount = value; StudioPreferenceStore.SetFloat("Model.EdgeLoopCount", value);
            var seed = cutDragging ? loopSeed : cutHover;
            bool loopContext = cutTool == CutTool.EdgeLoop || cutDragging || Event.current != null && (Event.current.control || Event.current.command);
            if (cutTopology != null && seed.HasValue && loopContext) cutPreview = BuildLoopCuts(seed.Value);
            repaint(); toolOptions?.Repaint();
        }
        private List<StudioMeshCuts.Cut> BuildLoopCuts(StudioMeshCuts.Point seed, bool centered = false)
        {
            var cuts = new List<StudioMeshCuts.Cut>();
            // Uniform spacing about the strip center. Sliding compresses the
            // group toward either edge without crossing or duplicating loops.
            float margin = .0005f * (edgeLoopCount + 1);
            float anchor = centered ? .5f : Mathf.Clamp(seed.T, margin, 1 - margin);
            for (int i = 1; i <= edgeLoopCount; i++)
            {
                float fraction = i / (float)(edgeLoopCount + 1);
                seed.T = anchor <= .5f ? fraction * 2 * anchor : 1 - (1 - fraction) * 2 * (1 - anchor);
                cuts.AddRange(cutTopology.Loop(seed));
            }
            return cuts;
        }
        internal bool WantsLoopCountWheel(Event evt) => (cutTool == CutTool.EdgeLoop || cutTool == CutTool.MultiCut) && evt.type == EventType.ScrollWheel &&
            (evt.control || evt.command) && !evt.alt && evt.delta.y != 0 &&
            (GUIUtility.hotControl == 0 || GUIUtility.hotControl == cutControl);
        private bool HandleLoopCountWheel(SceneView view, Event evt)
        {
            if (!WantsLoopCountWheel(evt)) return false;
            var canvas = view is MashBoxStudioViewport studio ? studio.ModelCanvasRect : new Rect(Vector2.zero, view.position.size);
            if (!canvas.Contains(evt.mousePosition) || view is MashBoxStudioViewport overlay && overlay.IsOverViewportControls(evt.mousePosition)) return false;
            SetEdgeLoopCount(edgeLoopCount + (evt.delta.y < 0 ? 1 : -1));
            evt.Use(); return true;
        }
        private void DrawLoopCountOptions()
        {
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.IntSlider(new GUIContent("Loop count", "Number of parallel edge loops. Ctrl/Cmd + wheel changes this in the viewport."), edgeLoopCount, 1, MaximumEdgeLoops);
            if (EditorGUI.EndChangeCheck()) SetEdgeLoopCount(value);
        }
    }
}

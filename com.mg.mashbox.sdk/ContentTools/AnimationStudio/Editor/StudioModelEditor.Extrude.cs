using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private float extrusionDistance = ToolChannelPane.GetFloatDefault("Extrude", "Distance", .1f);
        private bool extrudeFacesTogether = ToolChannelPane.GetFloatDefault("Extrude", "Together", 1) >= .5f;
        private bool CanExtrude => CanCutMesh && (mode == ComponentMode.Face || mode == ComponentMode.Edge) && Components.Count > 0;
        private int handleExtrudeControl;
        private Vector3 handleExtrudeCenter;
        private bool handleExtrudePending, handleExtrudeFailed;
        private System.Collections.Generic.HashSet<int> handleExtrudeVertices;
        private int[] handleExtrudeSourceSelection;

        private void ResetHandleExtrusion()
        {
            handleExtrudeControl = 0; handleExtrudePending = handleExtrudeFailed = false;
            handleExtrudeVertices = null; handleExtrudeSourceSelection = null;
            handleExtrudeCenter = Vector3.zero;
        }

        private void BeginHandleExtrusion(Vector3 position, Vector3 center, Quaternion rotation, Quaternion orientation, Vector3 scale)
        {
            if (!CanExtrude) throw new InvalidOperationException("Select faces or open boundary edges on an editable static mesh. Bake a live blend shape before changing its topology.");
            if (tool == Tool.Scale) scale = ConstrainComponentScale(scale);
            if (tool == Tool.Scale && !InvertibleScale(scale)) throw new InvalidOperationException("The extrusion scale must be finite and non-zero.");
            var t = Transforms[Active];
            Vector3 TransformPoint(Vector3 source)
            {
                var world = t.TransformPoint(source);
                if (tool == Tool.Move) world += position - center;
                else if (tool == Tool.Rotate) world = center + rotation * Quaternion.Inverse(orientation) * (world - center);
                else world = center + orientation * Vector3.Scale(Quaternion.Inverse(orientation) * (world - center), scale);
                return t.InverseTransformPoint(world);
            }
            var topology = new StudioMeshCuts(Document.nodes[Active]);
            int[] faces = mode == ComponentMode.Face ? Components.OrderBy(i => i).ToArray() : null;
            var edges = mode == ComponentMode.Edge ? Components.OrderBy(i => i).Select(i => Edges()[i]).ToArray() : null;
            const string label = "Extrude mesh components";
            Undo.SetCurrentGroupName(label);
            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { Document, ActiveMesh }, label);
            var result = topology.Extrude(Document.nodes[Active], faces, edges, .1f, extrudeFacesTogether, weld, TransformPoint);
            // Keep the captured handle and preview transforms alive throughout the drag.
            edgeMesh = null; cachedEdges = null; faceNode = null; cachedFaces = null;
            cutTopology = null; cutMesh = null; ClearComponentDrawing();
            Components.Clear(); SelectExtrusionResult(result, faces != null);
            handleExtrudeVertices = new System.Collections.Generic.HashSet<int>(result.MovingVertices);
            // New caps and wall tips have different vertex multiplicities from the
            // source selection. Keep the native handle's reference point continuous.
            handleExtrudeCenter = tool == Tool.Move ? position : center;
            if (tool == Tool.Scale)
            {
                scaleDrag = CaptureScaleDrag(handleExtrudeControl, center, orientation, handleExtrudeVertices, result.SourceVertices);
                scaleDrag.factor = scale;
            }
            EditorUtility.SetDirty(ActiveMesh); error = null; Changed();
        }

        private void SelectExtrusionResult(StudioMeshCuts.ExtrusionResult result, bool faces)
        {
            if (faces) foreach (int f in result.Faces) Components.Add(f);
            else
            {
                var nextEdges = Edges();
                foreach (var rim in result.Edges)
                {
                    int index = nextEdges.FindIndex(e => (e.a == rim.a && e.b == rim.b) || (e.a == rim.b && e.b == rim.a));
                    if (index >= 0) Components.Add(index);
                }
            }
        }

        private ToolChannelPane extrudeToolPane;
        private void CloseExtrudeToolWindow() { if (extrudeToolPane) extrudeToolPane.Close(); extrudeToolPane = null; }
        internal void ExtrudeSelected()
        {
            if (extrudeToolPane) { QueueLiveTool(LiveTool.Extrude); extrudeToolPane.Repaint(); extrudeToolPane.Focus(); return; }
            if (!CanExtrude) throw new InvalidOperationException("Select faces or open boundary edges on an editable static mesh.");
            extrudeToolPane = ToolChannelPane.Open("Extrude", DrawLiveExtrudeFields, () => extrudeToolPane = null,
                new Vector2(300, 160), new Vector2(260, 150),
                execute: () => ApplyLiveTool(LiveTool.Extrude), canExecute: () => CanRunLiveTool(LiveTool.Extrude),
                failed: ex => { error = ex.Message; repaint(); }, owner: ToolChannelPaneOwner,
                preview: () => QueueLiveTool(LiveTool.Extrude), cancelPreview: () => CancelLiveTool(LiveTool.Extrude), history: PerformWorkspaceHistory);
            QueueLiveTool(LiveTool.Extrude);
        }
        private void DrawLiveExtrudeFields()
        {
            EditorGUI.BeginChangeCheck();
            extrusionDistance = EditorGUILayout.FloatField(new GUIContent("Normal distance", "Distance in mesh-local units. Negative values extrude inward."), extrusionDistance);
            if (mode == ComponentMode.Face) extrudeFacesTogether = EditorGUILayout.Toggle("Keep faces together", extrudeFacesTogether);
            if (EditorGUI.EndChangeCheck()) StoreExtrusionDefaults();
            GUILayout.Label("Changes preview immediately. Apply keeps the pane open; Execute finishes. The header X or Escape cancels the unapplied preview.", StudioTheme.Styles.wordWrappedMiniLabel);
        }

        private void StoreExtrusionDefaults()
        {
            ToolChannelPane.SetFloatDefault("Extrude", "Distance", extrusionDistance);
            ToolChannelPane.SetFloatDefault("Extrude", "Together", extrudeFacesTogether ? 1 : 0);
        }

        internal bool HandleExtrudeShortcut()
        {
            var evt = Event.current;
            if (evt == null || EditorGUIUtility.editingTextField || !StudioShortcuts.Pressed(StudioShortcut.Extrude,evt)) return false;
            // Consume even an unavailable command so Unity cannot edit main-scene objects.
            if (GUIUtility.hotControl == 0) Run(ExtrudeSelected, false);
            evt.Use(); return true;
        }

        private void DrawExtrusionOptions()
        {
            if (mode != ComponentMode.Face && mode != ComponentMode.Edge) return;
            GUILayout.Space(8);
            GUILayout.Label("EXTRUDE", StudioTheme.Styles.boldLabel);
            EditorGUI.BeginChangeCheck();
            extrusionDistance = EditorGUILayout.FloatField(new GUIContent("Normal distance", "Distance in mesh-local units. Negative values extrude inward."), extrusionDistance);
            if (mode == ComponentMode.Face) extrudeFacesTogether = EditorGUILayout.Toggle("Keep faces together", extrudeFacesTogether);
            if (EditorGUI.EndChangeCheck()) { StoreExtrusionDefaults(); QueueLiveTool(LiveTool.Extrude); extrudeToolPane?.Repaint(); }
            using (new EditorGUI.DisabledScope(!CanExtrude))
                if (GUILayout.Button("Extrude Options ("+StudioShortcuts.Label(StudioShortcut.Extrude)+")")) Run(ExtrudeSelected, false);
            if (HasLiveTool(LiveTool.Extrude) || (previewQueued && queuedTool == LiveTool.Extrude))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Apply")) Run(() => ApplyLiveTool(LiveTool.Extrude), false);
                    if (GUILayout.Button("Cancel")) CancelLiveTool(LiveTool.Extrude);
                }
            GUILayout.Label(mode == ComponentMode.Face
                ? "Extrude along the selected normals, then move, rotate or scale the selected caps. Disable Keep Faces Together for separate face extrusions."
                : "Extrude open boundary edges along their surface normals. The new outer edges stay selected for further editing.", StudioTheme.Styles.wordWrappedMiniLabel);
            GUILayout.Label("Shift-drag a Move, Rotate or Scale handle to extrude using the current World/Object orientation.", StudioTheme.Styles.wordWrappedMiniLabel);
        }
    }
}

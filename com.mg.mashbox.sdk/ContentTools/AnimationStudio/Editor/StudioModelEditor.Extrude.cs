using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private float extrusionDistance = .1f;
        private bool extrudeFacesTogether = true;
        private bool CanExtrude => CanCutMesh && (mode == ComponentMode.Face || mode == ComponentMode.Edge) && Components.Count > 0;
        private int handleExtrudeControl;
        private bool handleExtrudePending, handleExtrudeFailed;
        private System.Collections.Generic.HashSet<int> handleExtrudeVertices;
        private int[] handleExtrudeSourceSelection;

        private void ResetHandleExtrusion()
        {
            handleExtrudeControl = 0; handleExtrudePending = handleExtrudeFailed = false;
            handleExtrudeVertices = null; handleExtrudeSourceSelection = null;
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

        internal void ExtrudeSelected()
        {
            if (!CanExtrude) throw new InvalidOperationException("Select faces or open boundary edges on an editable static mesh. Bake a live blend shape before changing its topology.");
            if (GUIUtility.hotControl != 0) return;
            var topology = new StudioMeshCuts(Document.nodes[Active]);
            int[] faces = mode == ComponentMode.Face ? Components.OrderBy(i => i).ToArray() : null;
            var edges = mode == ComponentMode.Edge ? Components.OrderBy(i => i).Select(i => Edges()[i]).ToArray() : null;
            StopCutTool(); CancelMarkingMenu(); EndPivotSnap();
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            const string label = "Extrude mesh components";
            Undo.SetCurrentGroupName(label);
            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { Document, ActiveMesh }, label);
            try
            {
                var result = topology.Extrude(Document.nodes[Active], faces, edges, extrusionDistance, extrudeFacesTogether, weld);
                EditorUtility.SetDirty(ActiveMesh); EditorUtility.SetDirty(Document);
                MeshTopologyChanged();
                SelectExtrusionResult(result, faces != null);
                tool = Tool.Move; pivot = false; error = null; Changed();
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }

        internal bool HandleExtrudeShortcut()
        {
            var evt = Event.current;
            if (evt == null || EditorGUIUtility.editingTextField || !StudioShortcuts.Pressed(StudioShortcut.Extrude,evt)) return false;
            // Consume even an unavailable command so Unity cannot edit main-scene objects.
            if (GUIUtility.hotControl == 0) Run(ExtrudeSelected);
            evt.Use(); return true;
        }

        private void DrawExtrusionOptions()
        {
            if (mode != ComponentMode.Face && mode != ComponentMode.Edge) return;
            GUILayout.Space(8);
            GUILayout.Label("EXTRUDE", StudioTheme.Styles.boldLabel);
            extrusionDistance = EditorGUILayout.FloatField(new GUIContent("Normal distance", "Distance in mesh-local units. Negative values extrude inward."), extrusionDistance);
            if (mode == ComponentMode.Face)
                extrudeFacesTogether = EditorGUILayout.Toggle("Keep faces together", extrudeFacesTogether);
            using (new EditorGUI.DisabledScope(!CanExtrude))
                if (GUILayout.Button("Extrude Selection ("+StudioShortcuts.Label(StudioShortcut.Extrude)+")")) Run(ExtrudeSelected);
            GUILayout.Label(mode == ComponentMode.Face
                ? "Extrude along the selected normals, then move, rotate or scale the selected caps. Disable Keep Faces Together for separate face extrusions."
                : "Extrude open boundary edges along their surface normals. The new outer edges stay selected for further editing.", StudioTheme.Styles.wordWrappedMiniLabel);
            GUILayout.Label("Shift-drag a Move, Rotate or Scale handle to extrude using the current World/Object orientation.", StudioTheme.Styles.wordWrappedMiniLabel);
        }
    }
}

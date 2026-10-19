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
                if (faces != null) foreach (int f in result.Faces) Components.Add(f);
                else
                {
                    var nextEdges = Edges();
                    foreach (var rim in result.Edges)
                    {
                        int index = nextEdges.FindIndex(e => (e.a == rim.a && e.b == rim.b) || (e.a == rim.b && e.b == rim.a));
                        if (index >= 0) Components.Add(index);
                    }
                }
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
        }
    }
}

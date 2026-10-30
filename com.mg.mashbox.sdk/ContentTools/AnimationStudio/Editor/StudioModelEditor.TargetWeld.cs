using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool targetWeldActive, targetWeldPointerValid;
        private int targetWeldControl, targetWeldNode = -1, targetWeldSource = -1, targetWeldDestination = -1, targetWeldHoverNode = -1, targetWeldHover = -1, targetWeldVersion;
        private Mesh targetWeldMesh;
        private Vector3[] targetWeldPoints;
        private SceneView targetWeldView;
        private SelectionSnapshot targetWeldBefore;
        private Vector2 targetWeldPointer;
        private readonly Vector3[] targetWeldLine = new Vector3[2];
#if MASHBOX_STUDIO_TESTS
        internal static Action TargetWeldInputProbe;
#endif

        private bool WeldNodeEditable(int index)
        {
            if (!Document || index < 0 || index >= Document.nodes.Count) return false;
            var node = Document.nodes[index]; var mesh = node.mesh;
            return mesh && mesh.isReadable && !node.skinned && node.blendShape == null && mesh.blendShapeCount == 0 && mesh.bindposes.Length == 0 && mesh.boneWeights.Length == 0;
        }
        private bool CanStartTargetWeld => Document && Enumerable.Range(0, Document.nodes.Count).Any(WeldNodeEditable);
        internal void ActivateTargetWeld()
        {
            if (targetWeldActive) { StopTargetWeld(); repaint(); return; }
            if (!CanStartTargetWeld) throw new InvalidOperationException("Target Weld requires an editable static mesh.");
            FinishSelectionPaint(); FinishLiveTool(); FinishChannelEdit(); StopCutTool(); CancelMarkingMenu(); EndPivotSnap();
            CloseLiveToolPanes(); CloseCombineToolWindow();
            ChangeComponentMode(ComponentMode.Vertex, null);
            CameraOptionsVisible = LightingOptionsVisible = false; tool = Tool.None; pivot = false;
            targetWeldActive = true; targetWeldPointerValid = false; repaint(); toolOptions?.Repaint();
        }
        private void RestoreTargetWeldSelection(SelectionSnapshot before)
        {
            if (before == null || before.document != Document) return;
            objects.Clear(); foreach (int node in before.objects) if (node >= 0 && node < Document.nodes.Count) objects.Add(node);
            RestoreComponentTargets(before.targets);
            Active = before.active >= 0 && before.active < Document.nodes.Count ? before.active : -1; mode = before.mode;
            Components.Clear(); Components.UnionWith(before.components); SelectionDisplayChanged();
        }
        private void ResetTargetWeldGesture(bool restoreSelection = true)
        {
            if (targetWeldControl != 0 && GUIUtility.hotControl == targetWeldControl) GUIUtility.hotControl = 0;
            var before = targetWeldBefore; targetWeldBefore = null;
            targetWeldSource = targetWeldDestination = targetWeldNode = targetWeldHoverNode = targetWeldHover = -1;
            targetWeldMesh = null; targetWeldPoints = null; targetWeldView = null;
            if (restoreSelection) RestoreTargetWeldSelection(before);
        }
        private void StopTargetWeld()
        { ResetTargetWeldGesture(); targetWeldActive = targetWeldPointerValid = false; }
        private int WeldVertexAt(SceneView view, Vector2 pointer, int node)
        {
            if (!WeldNodeEditable(node) || node >= Transforms.Length || !Transforms[node].gameObject.activeInHierarchy) return -1;
            using (new ComponentTargetScope(this, node))
            {
                var previous = mode; mode = ComponentMode.Vertex;
                try { return FindSelectionPaintComponent(view, PivotCameraPoint(view, pointer)); }
                finally { mode = previous; }
            }
        }
        private void UpdateTargetWeldHover(SceneView view)
        {
            targetWeldHoverNode = targetWeldHover = targetWeldDestination = -1;
            if (!targetWeldPointerValid) return;
            if (targetWeldSource >= 0)
            {
                int hit = WeldVertexAt(view, targetWeldPointer, targetWeldNode);
                if (hit >= 0 && !targetWeldPoints[hit].Equals(targetWeldPoints[targetWeldSource]))
                    targetWeldDestination = hit;
                return;
            }
            int node = MeshAtModePointer(view, targetWeldPointer);
            int vertex = WeldVertexAt(view, targetWeldPointer, node);
            if (vertex < 0 && Active >= 0) { node = Active; vertex = WeldVertexAt(view, targetWeldPointer, node); }
            if (vertex < 0)
            {
                // A silhouette vertex can be inside the pick radius while its
                // triangles miss the ray. Reuse the existing vertex snap query.
                int candidate = -1;
                if (TryPivotSnapVertex(view.camera, PivotCameraPoint(view, targetWeldPointer), EditorGUIUtility.pixelsPerPoint,
                    out _, WeldNodeEditable, snapped: (meshNode, renderVertex) => candidate = meshNode))
                { node = candidate; vertex = WeldVertexAt(view, targetWeldPointer, node); }
            }
            if (vertex >= 0) { targetWeldHoverNode = node; targetWeldHover = vertex; }
        }
        private bool BeginTargetWeld(SceneView view)
        {
            UpdateTargetWeldHover(view);
            if (targetWeldHover < 0) return false;
            targetWeldBefore = CaptureSelection();
            targetWeldNode = targetWeldHoverNode; targetWeldSource = targetWeldHover;
            ActivateComponentTarget(targetWeldNode, ComponentMode.Vertex);
            Components.Clear(); Components.Add(targetWeldSource);
            targetWeldMesh = ActiveMesh; targetWeldPoints = targetWeldMesh.vertices; targetWeldVersion = EditorUtility.GetDirtyCount(targetWeldMesh); targetWeldView = view;
            GUIUtility.hotControl = targetWeldControl; repaint(); return true;
        }
        private void CommitTargetWeld(int node, int source, int destination, SelectionSnapshot before)
        {
            try
            {
                using (new ComponentTargetScope(this, node))
                using (var preview = new StudioMeshToolPreview(Document, new[] { node }))
                {
                    var target = preview.Targets[0];
                    var result = target.Topology.MergeVertices(target.Work, new[] { source, destination }, true, 0, Matrix4x4.identity, target: destination);
                    if (result == null) { RestoreTargetWeldSelection(before); return; }
                    var previous = livePreview; livePreview = preview;
                    try
                    {
                        preview.Publish(true); RefreshLiveMeshes();
                        Components.Clear(); Components.UnionWith(result.Selection); MirrorComponentLiveSelection(node);
                        preview.Commit("Target weld vertices", ()=>{RecordDirectPreviewModifiers(preview,"Target weld vertices");RecordSelectionChange(before,false,"Target weld vertices");});
                        RefreshLiveMeshes();
                    }
                    catch { preview.Restore(); RefreshLiveMeshes(false); throw; }
                    finally { livePreview = previous; }
                }
                }
            catch { RestoreTargetWeldSelection(before); throw; }
            RememberAction(RepeatKind.TargetWeld); error = null; repaint();
        }
        private bool TargetWeldSceneGUI(SceneView view)
        {
            if (!targetWeldActive) return false;
            var evt = Event.current;
#if MASHBOX_STUDIO_TESTS
            TargetWeldInputProbe?.Invoke();
#endif
            targetWeldControl = GUIUtility.GetControlID("MashBoxStudioTargetWeld".GetHashCode(), FocusType.Passive);
            if (HandleModelKeyboard(view)) return true;
            if (targetWeldSource >= 0 && (!targetWeldMesh || targetWeldView != view || targetWeldNode >= Document.nodes.Count ||
                Document.nodes[targetWeldNode].mesh != targetWeldMesh || EditorUtility.GetDirtyCount(targetWeldMesh) != targetWeldVersion ||
                GUIUtility.hotControl != targetWeldControl || evt.type == EventType.MouseLeaveWindow)) ResetTargetWeldGesture();
            if (evt.alt) { if (targetWeldSource >= 0) ResetTargetWeldGesture(); return true; }
            var canvas = view is MashBoxStudioViewport studio ? studio.ModelCanvasRect : new Rect(Vector2.zero, view.position.size);
            bool inside = canvas.Contains(evt.mousePosition) && (!(view is MashBoxStudioViewport overlay) || !overlay.IsOverViewportControls(evt.mousePosition));
            if ((evt.type == EventType.Layout || evt.type == EventType.MouseMove) && inside) HandleUtility.AddDefaultControl(targetWeldControl);
            if (evt.isMouse && inside) { targetWeldPointer = evt.mousePosition; targetWeldPointerValid = true; UpdateTargetWeldHover(view); }
            if (evt.type == EventType.MouseDown && evt.button == 0 && inside && GUIUtility.hotControl == 0)
            { BeginTargetWeld(view); evt.Use(); repaint(); }
            else if (evt.type == EventType.MouseDrag && evt.button == 0 && targetWeldSource >= 0)
            { evt.Use(); repaint(); }
            else if (evt.type == EventType.MouseUp && evt.button == 0 && targetWeldSource >= 0)
            {
                int node = targetWeldNode, source = targetWeldSource, destination = inside ? targetWeldDestination : -1;
                var before = targetWeldBefore;
                ResetTargetWeldGesture(destination < 0);
                if (destination >= 0)
                {
                    selectionTransactionDepth++;
                    try { Run(() => CommitTargetWeld(node, source, destination, before)); }
                    finally { selectionTransactionDepth--; }
                }
                evt.Use(); repaint();
            }
            else if (evt.type == EventType.MouseDown && evt.button == 1 && inside)
            { ResetTargetWeldGesture(); suppressModelContext = true; evt.Use(); repaint(); }
            if (evt.type == EventType.MouseMove) repaint();
            if (evt.type == EventType.Repaint) { DrawComponents(view); DrawTargetWeldPreview(view); }
            return true;
        }
        private void DrawTargetWeldPreview(SceneView view)
        {
            int node = targetWeldSource >= 0 ? targetWeldNode : targetWeldHoverNode;
            int vertex = targetWeldSource >= 0 ? targetWeldSource : targetWeldHover;
            if (node < 0 || vertex < 0 || node >= Transforms.Length || !Document.nodes[node].mesh) return;
            var points = targetWeldPoints ?? Document.nodes[node].mesh.vertices; if (vertex >= points.Length) return;
            var source = Transforms[node].TransformPoint(points[vertex]);
            var color = Handles.color; var depth = Handles.zTest;
            try
            {
                Handles.zTest = CompareFunction.Always; Handles.color = new Color(1, .85f, .2f, 1);
                Handles.DrawSolidDisc(source, view.camera.transform.forward, HandleUtility.GetHandleSize(source) * .035f);
                if (targetWeldSource < 0) return;
                Vector3 end;
                if (targetWeldDestination >= 0)
                {
                    end = Transforms[node].TransformPoint(points[targetWeldDestination]); Handles.color = new Color(.35f, 1, .55f, 1);
                    Handles.DrawSolidDisc(end, view.camera.transform.forward, HandleUtility.GetHandleSize(end) * .045f);
                }
                else
                {
                    var ray = HandleUtility.GUIPointToWorldRay(targetWeldPointer);
                    end = new Plane(view.camera.transform.forward, source).Raycast(ray, out float distance) ? ray.GetPoint(distance) : source;
                }
                targetWeldLine[0] = source; targetWeldLine[1] = end; Handles.DrawAAPolyLine(2.5f, targetWeldLine);
            }
            finally { Handles.color = color; Handles.zTest = depth; }
        }
    }
}

using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Session-only tool state: supports Undo without changing or saving the model.
    internal sealed class StudioComponentPivotState : ScriptableObject
    {
        public StudioModelDocument document;
        public Mesh mesh;
        public int node, mode;
        public int[] selection;
        public bool custom;
        public Vector3 position;
        public Quaternion orientation = Quaternion.identity;
    }

    internal sealed partial class StudioModelEditor
    {
        private StudioComponentPivotState componentPivot;
        private bool HasComponentPivot
        {
            get
            {
                if (!componentPivot || !componentPivot.custom) return false;
                if (mode != ComponentMode.Object && componentPivot.document == Document && componentPivot.mesh == ActiveMesh &&
                    componentPivot.node == Active && componentPivot.mode == (int)mode && Components.SetEquals(componentPivot.selection)) return true;
                // A query can run while Undo or another component target is being
                // rebound. Only explicit user actions may invalidate saved state.
                return false;
            }
        }
        internal bool CenterMode => StudioSettings.Current.centerTransformMode;
        internal void SetCenterMode(bool enabled)
        {
            if (CenterMode == enabled || GUIUtility.hotControl != 0) return;
            if (enabled) { pivot = false; EndPivotSnap(); local = componentAxes = false; SaveDisplayConfiguration(); }
            StudioSettings.Current.centerTransformMode = enabled;
            StudioSettings.Save(); InvalidateHandleComponentFrame(); pivotHover = null;
            repaint(); toolOptions?.Repaint();
        }
        private void TogglePivotEdit(SceneView view = null)
        {
            if (CenterMode)
            {
                const string message = "Cannot edit pivot in CENTER mode";
                error = message;
                repaint(); return;
            }
            if (CanAdjustPivot) { pivot = !pivot; EndPivotSnap(); repaint(); toolOptions?.Repaint(); }
        }
        private Vector3 SelectionGeometryCenter(bool average)
        {
            if (mode != ComponentMode.Object)
            {
                var nodes = ComponentTargetNodes(true);
                return nodes.Count > 1 ? MultiComponentCenter(nodes) : ComponentSelectionCenter();
            }
            var selected = objects.Where(i => i >= 0 && i < Transforms.Length).ToArray();
            if (selected.Length == 0) return BoundsFor(Active).center;
            if (average) return selected.Aggregate(Vector3.zero, (sum, i) => sum + BoundsFor(i).center) / selected.Length;
            var bounds = BoundsFor(selected[0]);
            foreach (int i in selected.Skip(1)) bounds.Encapsulate(BoundsFor(i));
            return bounds.center;
        }
        private bool CanAdjustPivot => Document && Active >= 0 && !Document.IsBone(Active) &&
            (mode == ComponentMode.Object || (ActiveMesh && CanEditComponents() && SelectedVertices().Count > 0));

        private readonly System.Collections.Generic.HashSet<Vector3> componentCenterPoints = new System.Collections.Generic.HashSet<Vector3>();
        private Vector3 ComponentSelectionCenter() => ComponentSelectionCenter(ComponentPositions(), SelectedVertices());
        private Vector3 ComponentSelectionCenter(System.Collections.Generic.IList<Vector3> vertices, System.Collections.Generic.HashSet<int> indices)
        {
            // UV and hard-normal splits are render vertices, not extra geometric
            // points. Their multiplicity must not move the selection's center.
            componentCenterPoints.Clear();
            var sum = Vector3.zero;
            foreach (int i in indices)
                if (componentCenterPoints.Add(vertices[i])) sum += vertices[i];
            return componentCenterPoints.Count == 0 ? Transforms[Active].position :
                Transforms[Active].TransformPoint(sum / componentCenterPoints.Count);
        }
        private Vector3 UnlockedEditingPivotPosition => mode == ComponentMode.Object ? Transforms[Active].position :
            HasComponentPivot ? componentPivot.position : ComponentSelectionCenter();
        private Vector3 FocusPivotPosition => pivotLocked && lockedPivotValid && lockedPivotDocument == Document
            ? lockedPivotPosition : UnlockedEditingPivotPosition;
        internal Vector3 EditingPivotPosition => ResolveLockedPivotCenter(UnlockedEditingPivotPosition);

        private void SetComponentPivot(Vector3 position, Quaternion orientation, string undoName)
        {
            if (!componentPivot)
            {
                componentPivot = ScriptableObject.CreateInstance<StudioComponentPivotState>();
                componentPivot.hideFlags = HideFlags.HideAndDontSave;
            }
            if (!HasComponentPivot)
            {
                componentPivot.document = Document; componentPivot.mesh = ActiveMesh;
                componentPivot.node = Active; componentPivot.mode = (int)mode;
                componentPivot.selection = Components.ToArray();
                componentPivot.position = ComponentSelectionCenter(); componentPivot.orientation = PivotOrientation;
            }
            Undo.RegisterCompleteObjectUndo(componentPivot, undoName);
            componentPivot.custom = true;
            componentPivot.position = position; componentPivot.orientation = orientation.normalized;
            RememberLockedPivot(position, orientation);
            pivotHover = null; repaint();
        }
        private static bool FinitePivotOrientation(Quaternion value)
        {
            float norm = value.x*value.x + value.y*value.y + value.z*value.z + value.w*value.w;
            return !float.IsNaN(norm) && !float.IsInfinity(norm) && norm > 1e-12f;
        }
        private void BakeEditingPivot()
        {
            if(!Document||Active<0||mode!=ComponentMode.Object)throw new System.InvalidOperationException("Select an object to Bake Pivot.");
            FinishChannelEdit();FlushModifierEdits();
            var node=Document.nodes[Active];Document.ValidateBakePivot(Active,Transforms);
            if(node.modifiers?.operations.Any(o=>!string.IsNullOrEmpty(o.failure))==true)
                throw new System.InvalidOperationException("Repair the broken modifier or Clear History before baking the pivot.");
            bool rotate=!StudioModelDocument.Node.SameRotation(node.BakeRotation.normalized,Quaternion.identity);
            if(!rotate&&node.positionOffset.Equals(Vector3.zero)&&StudioModelDocument.Node.SameRotation(node.pivotRotation,Quaternion.identity)&&StudioModelDocument.Node.SameRotation(node.frozenRotation,Quaternion.identity)&&StudioModelDocument.Node.SameRotation(node.frozenPivotBaseline,Quaternion.identity))return;
            var before=rotate&&node.mesh?StudioModifierSource.Capture(node):null;
            var mesh=node.mesh;
            bool copied=rotate&&mesh&&(EditorUtility.IsPersistent(mesh)||Document.nodes.Count(n=>n.mesh==mesh)>1);
            if(copied){mesh=Object.Instantiate(mesh);mesh.name=node.mesh.name;}
            writingModifiers=true;
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
            try
            {
                Undo.RegisterCompleteObjectUndo(rotate&&mesh?new Object[]{Document,mesh}:new Object[]{Document},"Bake pivot");
                if(rotate)ClearSubdivision();
                if(copied){node.mesh=mesh;Transforms[Active].GetComponent<MeshFilter>().sharedMesh=mesh;}
                Document.BakePivot(Active,Transforms);
                if(before!=null)
                {
                    var source=before.Restore();
                    try{var operation=new StudioModifierOperation{kind="Direct",name="Bake Pivot",inputMesh=before.mesh,replacement=StudioModifierSource.Capture(node)};operation.Bind(source);AppendModifier(node,operation,before);}
                    finally{Object.DestroyImmediate(source.mesh);}
                }
                pivot=false;EndPivotSnap();RememberLockedPivot(Transforms[Active].position,Transforms[Active].rotation);
                Undo.CollapseUndoOperations(group);Undo.IncrementCurrentGroup();
                if(rotate)RefreshModifierResult();else Changed(false);
                RememberAction(RepeatKind.BakePivot);
            }
            finally{writingModifiers=false;}
        }
        private void FreezeEditingObject()
        {
            Document.Freeze(Active, Transforms);
            RememberLockedPivot(EditingPivotPosition, Quaternion.identity);
            Changed();
        }
        private void MoveEditingPivot(Vector3 position)
        {
            if (!CanAdjustPivot) return;
            if (mode != ComponentMode.Object) SetComponentPivot(position, PivotOrientation, "Move component pivot");
            else { MoveObjectPivots(position); RememberLockedPivot(position, PivotOrientation); Changed(); }
        }
        // Repivot compensates children, so selected descendants must also be
        // visited. Every selected origin receives the exact same world destination.
        private int[] ObjectPivotTargets() => objects.Contains(Active)
            ? objects.OrderBy(i => i).ToArray() : new[] { Active };
        private void MoveObjectPivots(Vector3 position)
        {
            var targets = ObjectPivotTargets();
            MoveObjectPivots(targets, targets.Select(i => position).ToArray());
        }
        private void MoveObjectPivots(int[] targets, Vector3[] destinations)
        {
            if (targets.Select((node, k) => (Transforms[node].position - destinations[k]).sqrMagnitude < 1e-12f).All(same => same)) return;
            for (int k = 0; k < targets.Length; k++) Document.ValidateRepivot(targets[k], Transforms, destinations[k]);
            var meshes = targets.Select(i => Document.nodes[i].mesh).ToArray();
            for (int k = 0; k < targets.Length; k++)
            {
                int i = targets[k]; var node = Document.nodes[i];
                // Different local frames need independent compensation. Never
                // move another instance's surface or write through a source asset.
                if (node.mesh && (EditorUtility.IsPersistent(node.mesh) || Document.nodes.Count(n => n.mesh == node.mesh) > 1))
                {
                    var mesh = Object.Instantiate(node.mesh); mesh.name = node.mesh.name;
                    meshes[k] = mesh;
                }
            }
            // One native snapshot owns document references and all compensated
            // meshes; separate registrations can restore references out of order.
            Undo.RegisterCompleteObjectUndo(new Object[] { Document }.Concat(meshes.Where(m => m).Cast<Object>()).ToArray(), "Move object pivots");
            for (int k = 0; k < targets.Length; k++)
            {
                int i = targets[k]; var node = Document.nodes[i];
                if (node.mesh != meshes[k])
                {
                    var mesh = meshes[k]; node.mesh = mesh;
                    var filter = Transforms[i].GetComponent<MeshFilter>();
                    if (filter) filter.sharedMesh = mesh;
                    var skin = Transforms[i].GetComponent<SkinnedMeshRenderer>();
                    if (skin) skin.sharedMesh = mesh;
                }
                Document.Repivot(i, Transforms, destinations[k]);
            }
        }
        private void CenterEditingPivot()
        {
            if (!CanAdjustPivot) return;
            if (mode == ComponentMode.Object)
            {
                var targets = ObjectPivotTargets();
                // Capture each item's own geometry center before compensating
                // any selected ancestor. Empty groups retain hierarchy bounds.
                var centers = targets.Select(node => {
                    var renderer = Transforms[node].GetComponent<Renderer>();
                    return renderer ? renderer.bounds.center : BoundsFor(node).center;
                }).ToArray();
                MoveObjectPivots(targets, centers);
                RememberLockedPivot(Transforms[Active].position, PivotOrientation); Changed();
            }
            else MoveEditingPivot(ComponentSelectionCenter());
            RememberAction(RepeatKind.CenterPivot);
        }
        private void UpdateComponentPivotAfterTransform(Vector3 position, Vector3 center, Quaternion rotation, Quaternion orientation)
        {
            if (HasLockedPivot || !HasComponentPivot) return;
            if (tool == Tool.Move) SetComponentPivot(componentPivot.position + position - center, componentPivot.orientation, "Transform mesh components");
            else if (tool == Tool.Rotate) SetComponentPivot(componentPivot.position,
                rotation * Quaternion.Inverse(orientation) * componentPivot.orientation, "Transform mesh components");
        }
    }
}

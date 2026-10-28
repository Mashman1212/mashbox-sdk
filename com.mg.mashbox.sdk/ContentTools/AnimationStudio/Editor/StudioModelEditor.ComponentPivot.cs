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
        private void MoveEditingPivot(Vector3 position)
        {
            if (!CanAdjustPivot) return;
            if (mode != ComponentMode.Object) SetComponentPivot(position, PivotOrientation, "Move component pivot");
            else { Document.Repivot(Active, Transforms, position); RememberLockedPivot(position, PivotOrientation); Changed(); }
        }
        private void CenterEditingPivot()
        {
            if (CanAdjustPivot) MoveEditingPivot(mode == ComponentMode.Object ? BoundsFor(Active).center : ComponentSelectionCenter());
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

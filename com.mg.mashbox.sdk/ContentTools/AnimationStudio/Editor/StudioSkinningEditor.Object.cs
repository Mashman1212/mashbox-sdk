using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private int objectUndoGroup = -1;
        private bool objectScale;
        private Vector3 objectScaleHandle = Vector3.one;
        private void FinishObjectGesture()
        {
            objectScaleHandle = Vector3.one;
            if (objectUndoGroup < 0) return;
            Undo.CollapseUndoOperations(objectUndoGroup);
            objectUndoGroup = -1;
            Undo.IncrementCurrentGroup();
        }

        private void DrawObjectModeIcon(Rect rect)
        {
            if (DrawSkinModeButton(rect, Tool == Brush.Object, "Object — position clothing before skinning (W: move, E: rotate, R: scale)"))
            { CancelBrushInteraction(); ChooseBrush(Brush.Object); }
            if (Event.current.type != EventType.Repaint) return;
            var color = Handles.color;
            try
            {
                Handles.color = Tool == Brush.Object ? new Color(.5f,1,.87f) : new Color(.75f,.81f,.81f);
                Vector3 P(float x,float y) => new Vector3(rect.center.x+x,rect.center.y+y,0);
                Handles.DrawAAPolyLine(1.6f,P(-9,-5),P(0,-10),P(9,-5),P(9,6),P(0,11),P(-9,6),P(-9,-5),P(0,0),P(9,-5));
                Handles.DrawAAPolyLine(1.6f,P(0,0),P(0,11));
            }
            finally { Handles.color = color; }
        }

        private void DrawObjectTransform(SceneView view)
        {
            var part = Part;
            if (part == null || part.referenceOnly || !part.visible || !part.mesh) return;
            if (part.bound)
            {
                Handles.Label(part.mesh.bounds.center, "Unskin this mesh to position it, then Skin to bind it again.");
                return;
            }
            var e = Event.current;
            var pivot = part.mesh.bounds.center;
            var rotation = part.objectRotation;
            if (rotation == new Quaternion(0,0,0,0)) rotation = Quaternion.identity;
            EditorGUI.BeginChangeCheck();
            var position = pivot;
            var nextRotation = rotation;
            var scale = objectScaleHandle;
            var axes = poseLocal ? rotation : Quaternion.identity;
            if (objectScale) scale = Handles.ScaleHandle(scale,pivot,axes,HandleUtility.GetHandleSize(pivot));
            else if (poseTranslate) position = Handles.PositionHandle(pivot, poseLocal ? rotation : Quaternion.identity);
            else if (poseLocal) nextRotation = Handles.RotationHandle(rotation, pivot);
            else
            {
                var next = Handles.RotationHandle(poseGlobalHandleRotation, pivot);
                nextRotation = next * Quaternion.Inverse(poseGlobalHandleRotation) * rotation;
                poseGlobalHandleRotation = next;
            }
            if (EditorGUI.EndChangeCheck())
            {
                if (objectUndoGroup < 0)
                {
                    Undo.IncrementCurrentGroup(); objectUndoGroup = Undo.GetCurrentGroup();
                    Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { Document, part.mesh }, "Transform skinning mesh");
                }
                var delta = Matrix4x4.TRS(position, nextRotation * Quaternion.Inverse(rotation), Vector3.one) * Matrix4x4.Translate(-pivot);
                if (objectScale)
                {
                    // Positive scale avoids singular geometry and inverted clothing.
                    scale = new Vector3(Mathf.Max(.001f,scale.x),Mathf.Max(.001f,scale.y),Mathf.Max(.001f,scale.z));
                    var ratio = new Vector3(scale.x/objectScaleHandle.x,scale.y/objectScaleHandle.y,scale.z/objectScaleHandle.z);
                    delta = Matrix4x4.TRS(pivot,axes,Vector3.one) * Matrix4x4.Scale(ratio) * Matrix4x4.TRS(pivot,axes,Vector3.one).inverse;
                    objectScaleHandle = scale;
                }
                // Bake into editable geometry: preview rebuilds, binding and FBX export
                // all use this same mesh. Source transforms remain export provenance.
                StudioSkinningDocument.TransformGeometry(part.mesh, delta);
                part.objectRotation = nextRotation;
                Rebuild(); Changed(); RecordSkinTutorialAction("transform");
            }
            if (e.rawType == EventType.MouseUp || e.type == EventType.Ignore)
            { FinishObjectGesture(); poseGlobalHandleRotation = Quaternion.identity; }
        }
    }
}

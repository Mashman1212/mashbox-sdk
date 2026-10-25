using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private bool poseTranslate = true;
        private bool poseLocal = true;
        private Quaternion poseGlobalHandleRotation = Quaternion.identity;
        private static Rect PoseToolbarRect(SceneView view)
        {
            var mode = ModeRect(view);
            return new Rect(mode.x,mode.yMax+5,92,24);
        }
        private void DrawPoseToolbar(SceneView view)
        {
            var rect = PoseToolbarRect(view);
            var spaceRect = new Rect(rect.x,rect.y,92,24);
            if (EditorGUI.DropdownButton(spaceRect,new GUIContent(poseLocal ? "Local" : "Global", "Joint gizmo coordinate space"),FocusType.Passive,StudioTheme.Styles.menuDropdown))
            {
                var menu = new StudioMenu();
                menu.AddItem(new GUIContent("Local"),poseLocal,()=>SetPoseSpace(true));
                menu.AddItem(new GUIContent("Global"),!poseLocal,()=>SetPoseSpace(false));
                menu.DropDown(spaceRect);
            }
        }
        private void SetPoseSpace(bool local)
        {
            FinishPoseGesture(); poseLocal=local; poseGlobalHandleRotation=Quaternion.identity; repaint();
        }
        private int poseUndoGroup = -1;
        private void FinishPoseGesture()
        {
            poseGlobalHandleRotation = Quaternion.identity;
            if (poseUndoGroup < 0) return;
            Undo.CollapseUndoOperations(poseUndoGroup); poseUndoGroup=-1; Undo.IncrementCurrentGroup();
        }
        private void DrawPoseModeIcon(Rect rect)
        {
            if (DrawSkinModeButton(rect, Tool == Brush.Pose, "Pose — select and move or rotate joints"))
            { CancelBrushInteraction(); ChooseBrush(Brush.Pose); }
            if (Event.current.type != EventType.Repaint) return;
            var color = Handles.color; var matrix = Handles.matrix;
            Handles.matrix = Matrix4x4.identity;
            Vector3 P(float x,float y) => new Vector3(rect.center.x+x,rect.center.y+y,0);
            try
            {
                Handles.color = Tool == Brush.Pose ? new Color(.5f,1,.87f) : new Color(.75f,.81f,.81f);
                Handles.DrawWireDisc(P(0,-8),Vector3.forward,3);
                Handles.DrawAAPolyLine(1.8f,P(0,-4),P(0,4),P(-6,11));
                Handles.DrawAAPolyLine(1.8f,P(0,4),P(6,11));
                Handles.DrawAAPolyLine(1.8f,P(-9,-2),P(0,0),P(8,-5));
                Handles.DrawSolidDisc(P(0,4),Vector3.forward,2);
            }
            finally { Handles.color=color; Handles.matrix=matrix; }
        }
        private void DrawManualPose(SceneView view)
        {
            var e = Event.current; var mode = ModeRect(view);
            var toolbar = PoseToolbarRect(view);
            bool overUI = mode.Contains(e.mousePosition) || toolbar.Contains(e.mousePosition) ||
                (view is MashBoxStudioViewport viewport && viewport.IsOverSkinningPanels(e.mousePosition));
            var color = Handles.color;
            try
            {
                // Joint buttons share Unity's handle picking, so a transform gizmo
                // wins naturally over a joint behind it; Alt navigation is untouched.
                for (int i=0;i<bones.Length;i++)
                {
                    if (!deformBones[i]) continue;
                    Handles.color = i == boneIndex ? Color.yellow : new Color(.4f,.95f,.8f,.9f);
                    float size = HandleUtility.GetHandleSize(bones[i].position)*.045f;
                    if (!e.alt && !overUI)
                    {
                        if (Handles.Button(bones[i].position,view.camera.transform.rotation,size,size,Handles.CircleHandleCap))
                        { GUIUtility.keyboardControl=0; EditorGUIUtility.editingTextField=false; boneIndex=i; UpdateDisplay(); }
                    }
                    else if (e.type == EventType.Repaint) Handles.DrawWireDisc(bones[i].position,view.camera.transform.forward,size);
                }
                var joint = bones[boneIndex];
                Handles.Label(joint.position, StudioSkinningDocument.BoneName(joint.name));
                EditorGUI.BeginChangeCheck();
                Vector3 p = joint.position; Quaternion q = joint.rotation;
                if (poseTranslate) p = Handles.PositionHandle(p,poseLocal ? q : Quaternion.identity);
                else if (poseLocal) q = Handles.RotationHandle(q,p);
                else
                {
                    var next = Handles.RotationHandle(poseGlobalHandleRotation,p);
                    q = next * Quaternion.Inverse(poseGlobalHandleRotation) * q;
                    poseGlobalHandleRotation = next;
                }
                if (EditorGUI.EndChangeCheck())
                {
                    if (poseUndoGroup < 0) { Undo.IncrementCurrentGroup(); poseUndoGroup=Undo.GetCurrentGroup(); }
                    Undo.RecordObject(joint,"Pose skinning joint");
                    joint.SetPositionAndRotation(p,q); cachedPart=-1; repaint();
                }
                if (e.rawType == EventType.MouseUp || e.type == EventType.Ignore)
                {
                    FinishPoseGesture();
                    if (cachedPart < 0) Run(RefreshSurface);
                }
            }
            finally { Handles.color=color; }
        }
    }
}

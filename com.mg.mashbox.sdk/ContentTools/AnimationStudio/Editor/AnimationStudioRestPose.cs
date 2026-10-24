using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private void DrawRestPoseButton()
        {
            using (new EditorGUI.DisabledScope(!OutfitActor || OutfitRig == null))
            {
                var content = new GUIContent(EditorGUIUtility.IconContent("Refresh").image,
                    "Reset pose — restore the selected actor's imported rest pose (T-pose for the shared Skeleton). With Auto Key off, press S to key the reset.");
                if (!GUILayout.Button(content, StudioTheme.Styles.overlayButton, GUILayout.Width(26), GUILayout.Height(24))) return;
                Guard(() =>
                {
                    FinishSceneEdit(); FinishActorEdits();
                    System.Action reset = () =>
                    {
                        // Rest includes matching IK targets/poles; disable IK so it cannot pull the reset out of pose.
                        pose = rig.Rest.Copy(frame);
                        for (int i = 0; i < pose.effectors.Length; i++) pose.effectors[i].weight = 0;
                        Commit("Reset to rest pose");
                    };
                    if (activeActor >= 0 && activeActor < actorPreviews.Count) InActor(actorPreviews[activeActor], reset);
                    else reset();
                    viewport?.Repaint(); Repaint();
                });
            }
        }
    }
}

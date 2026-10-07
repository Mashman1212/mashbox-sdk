#if UNITY_EDITOR
using System.Collections.Generic;
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    public partial class MashBoxMapToolsWindow
    {
        private readonly List<MBChairlift> cachedChairlifts = new();

        private void DrawChairliftsSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Chairlifts", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Create a lift, then position Loading Bay (Bottom), Loading Bay (Top), and the towers in the Scene view. " +
                    "Bay connections follow automatically. Select Chairlift Cable System to set chair count, spacing and sag; " +
                    "select the lift root to set speed and direction.",
                    MessageType.None);
                var scene = SceneManager.GetActiveScene();
                using (new EditorGUI.DisabledScope(Application.isPlaying || !scene.IsValid() || !scene.isLoaded))
                {
                    if (DrawAddButton("Create Chairlift", "Create a complete chairlift with bottom and top loading bays, towers, and connected cable paths.", 32f))
                    {
                        MBChairliftAuthoring.Create(null, GetScenePlacementPosition());
                        MBGameplayGizmoVisibility.Visible = true;
                        MBGameplayGizmoVisibility.ChairliftsEnabled = true;
                        MarkSceneToolCacheDirty();
                        SceneView.RepaintAll();
                        GUIUtility.ExitGUI();
                    }
                }
                using (new EditorGUI.DisabledScope(!MBChairliftAuthoring.CanConvert()))
                {
                    if (GUILayout.Button(new GUIContent("Convert Selected Legacy Lift", "Select an existing physical chairlift or one of its children. Conversion supports Undo."), GUILayout.Height(26f)))
                    {
                        MBChairliftAuthoring.ConvertSelected();
                        MarkSceneToolCacheDirty();
                        SceneView.RepaintAll();
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUILayout.HelpBox(
                    "The SDK shows gizmos. ProjectX spawns its physical chairs, towers and loading-bay prefabs when the map loads. " +
                    "Each loading bay includes its slow cable path and transfer connections.",
                    MessageType.None);
                foreach (var lift in cachedChairlifts)
                {
                    if (lift == null) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.ObjectField(lift, typeof(MBChairlift), true);
                        if (GUILayout.Button("Select", GUILayout.Width(65)))
                        {
                            Selection.activeGameObject = lift.gameObject;
                            EditorGUIUtility.PingObject(lift);
                        }
                        if (GUILayout.Button("Frame", GUILayout.Width(65)))
                        {
                            Selection.activeGameObject = lift.gameObject;
                            SceneView.lastActiveSceneView?.FrameSelected();
                        }
                    }
                }
            }
        }
    }
}
#endif

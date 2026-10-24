using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private GUIContent RigIcon(string name, string tooltip) => new GUIContent(EditorGUIUtility.IconContent(name).image, tooltip);

        private void DrawRigOrientationDropdown()
        {
            var content = new GUIContent(localHandleSpace ? "Local" : "Global", "Transform handle orientation");
            var style = StudioTheme.Styles.overlayDropdown;
            var rect = GUILayoutUtility.GetRect(content, style, GUILayout.Width(82), GUILayout.Height(24));
            if (!EditorGUI.DropdownButton(rect, content, FocusType.Passive, style)) return;
            var menu = new StudioMenu();
            menu.AddItem(new GUIContent("Local"), localHandleSpace, () => SetHandleSpace(true));
            menu.AddItem(new GUIContent("Global"), !localHandleSpace, () => SetHandleSpace(false));
            menu.DropDown(rect);
        }
        private void DrawCompactRigToolbar()
        {
            using (new GUILayout.VerticalScope(GUILayout.Width(386)))
            {
                // Match the settings controls, which draw at y = 0 with 24px height.
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Space(4);
                    if (GUILayout.Button(RigIcon("SettingsIcon", "Studio appearance and preferences"), StudioTheme.Styles.overlayButton, GUILayout.Width(24), GUILayout.Height(24))) StudioSettingsWindow.Open();
                    GUILayout.Space(8);
                    DrawRigOrientationDropdown();
                    GUILayout.Space(8);
                    EditorGUI.BeginChangeCheck();
                    showIKControls = GUILayout.Toggle(showIKControls, new GUIContent("IK", "Show inverse-kinematics controls"), StudioTheme.Styles.overlayButton, GUILayout.Width(32), GUILayout.Height(24));
                    GUILayout.Space(6);
                    showFKControls = GUILayout.Toggle(showFKControls, new GUIContent("FK", "Show forward-kinematics controls"), StudioTheme.Styles.overlayButton, GUILayout.Width(32), GUILayout.Height(24));
                    if (EditorGUI.EndChangeCheck()) viewport?.Repaint();
                    GUILayout.Space(10);
                    DrawRigOptionButtons();
                    GUILayout.Space(8);
                    DrawRestPoseButton();
                    GUILayout.Space(8);
                    DrawSingleFramePoseButton();
                    GUILayout.Space(4);
                }
                GUILayout.Space(3);
            }
        }

    }
}

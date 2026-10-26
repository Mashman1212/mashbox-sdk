using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private void DrawRigOptionButtons()
        {
            var selectedRig = OutfitRig;
            using (new EditorGUI.DisabledScope(selectedRig == null || !selectedRig.SupportsHumanLimits))
            {
                bool enabled = selectedRig != null && selectedRig.HumanLimitsEnabled;
                bool next = GUILayout.Toggle(enabled, new GUIContent("Human Limits",
                    "Constrain this humanoid to its avatar joint ranges, including knees and elbows. Applies while posing and previewing. Key poses to save constrained rotations. Session only."),
                    StudioTheme.Styles.overlayButton, GUILayout.Width(100), GUILayout.Height(24));
                if (next != enabled && selectedRig != null)
                {
                    Guard(() =>
                    {
                        FinishSceneEdit(); FinishActorEdits();
                        selectedRig.HumanLimitsEnabled = next;
                        System.Action apply = () => { rig.Apply(pose); };
                        if (activeActor >= 0 && activeActor < actorPreviews.Count) InActor(actorPreviews[activeActor], apply);
                        else apply();
                        EvaluateConstraints(); viewport?.Repaint(); Repaint();
                    });
                }
            }
            GUILayout.Space(6);
            bool fade = RigDisplayIcon(fadeRigControls, false, "Fade FK controls by blend. IK controls always fade with their influence.");
            if (fade != fadeRigControls)
            {
                fadeRigControls = fade;
                if (!fade) hideInactiveControls = false;
                viewport?.Repaint();
            }
            GUILayout.Space(6);
            bool hide = RigDisplayIcon(hideInactiveControls, true, "Hide inactive IK/FK controls");
            if (hide != hideInactiveControls)
            {
                hideInactiveControls = hide;
                if (hide) fadeRigControls = true;
                viewport?.Repaint();
            }
        }

        private bool RigDisplayIcon(bool active, bool hideIcon, string tooltip)
        {
            var rect = GUILayoutUtility.GetRect(26, 24, GUILayout.Width(26), GUILayout.Height(24));
            bool result = GUI.Toggle(rect, active, new GUIContent("", tooltip), StudioTheme.Styles.overlayButton);
            if (Event.current.type != EventType.Repaint) return result;
            var color = StudioTheme.Current.Text;
            if (!hideIcon)
            {
                // Graduated bars communicate opacity without relying on font glyphs or editor icon names.
                for (int i = 0; i < 4; i++)
                {
                    var shade = color; shade.a *= .25f + i * .25f;
                    EditorGUI.DrawRect(new Rect(rect.x + 5 + i * 4, rect.y + 6, 3, 12), shade);
                }
            }
            else
            {
                var old = Handles.color;
                try
                {
                    Handles.color = color;
                    var c = rect.center;
                    Handles.DrawAAPolyLine(1.5f, new Vector3(c.x-9,c.y), new Vector3(c.x-4,c.y-5),
                        new Vector3(c.x+4,c.y-5), new Vector3(c.x+9,c.y), new Vector3(c.x+4,c.y+5),
                        new Vector3(c.x-4,c.y+5), new Vector3(c.x-9,c.y));
                    Handles.DrawSolidDisc(c, Vector3.forward, 2);
                    Handles.DrawAAPolyLine(2, new Vector3(c.x-8,c.y+8), new Vector3(c.x+8,c.y-8));
                }
                finally { Handles.color = old; }
            }
            return result;
        }
    }
}

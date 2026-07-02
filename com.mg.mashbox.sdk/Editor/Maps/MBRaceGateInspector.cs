using MashBoxSDK.EditorResources;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.Maps
{
    [CustomEditor(typeof(MBRaceGate))]
    public class MBRaceGateInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            var gate = (MBRaceGate)target;
            if (gate == null)
                return;

            MashBoxInspectorHeaderUtility.DrawScriptHeader();
            EditorGUILayout.HelpBox(
                "Race Gate uses a base pivot, draws the gate volume upward from the transform, and reports distance stats for laying out race flow.",
                MessageType.None);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Gate Stats", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Race", gate.Race != null ? gate.Race.RaceName : "None");
                EditorGUILayout.LabelField("Gate Number", gate.GateNumber > 0 ? gate.GateNumber.ToString("00") : "--");
                EditorGUILayout.LabelField("Distance From Start", $"{gate.DistanceFromStart:0.0} m");
                EditorGUILayout.LabelField("Distance To Next", gate.GetNextGate() != null ? $"{gate.DistanceToNextGate:0.0} m" : "Finish gate");
                EditorGUILayout.LabelField("Total Race Distance", $"{gate.TotalRaceDistance:0.0} m");
                EditorGUILayout.LabelField("Armed", gate.IsArmed ? "Yes" : "No");
                EditorGUILayout.LabelField("Passed", gate.HasPassed ? "Yes" : "No");
                EditorGUILayout.LabelField("Trigger Size", $"{gate.GetTriggerZoneSize().x:0.0} x {gate.GetTriggerZoneSize().y:0.0} x {gate.GetTriggerZoneSize().z:0.0} m");
                EditorGUILayout.LabelField("Pass Filter", "Mixamo rig name");

                if (gate.TryGetTopClearance(out var topClearance))
                {
                    var clearanceStyle = new GUIStyle(EditorStyles.boldLabel);
                    clearanceStyle.normal.textColor = topClearance < 3f
                        ? new Color(1f, 0.35f, 0.35f, 1f)
                        : new Color(0.65f, 1f, 0.55f, 1f);
                    EditorGUILayout.LabelField("Top Clearance", $"{topClearance:0.0} m", clearanceStyle);
                }
                else
                {
                    EditorGUILayout.LabelField("Top Clearance", "No ground hit found");
                }
            }

            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Arm Gate"))
                    gate.Arm();

                if (GUILayout.Button("Reset Gate"))
                    gate.ResetGate();

                using (new EditorGUI.DisabledScope(!gate.IsArmed))
                {
                    if (GUILayout.Button("Pass Gate"))
                        gate.Pass();
                }
            }

            if (GUI.changed)
                EditorUtility.SetDirty(gate);

            DrawDefaultInspector();
        }
    }
}

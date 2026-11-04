using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private readonly Dictionary<int, StudioVehiclePartValidation.Result> vehicleValidation = new Dictionary<int, StudioVehiclePartValidation.Result>();
        private UnityEngine.Object vehicleValidationRules;
        private int vehicleRulesRevision, vehicleDocumentRevision, vehicleProjectRevision;
        private static int vehicleValidationProjectRevision;
        [InitializeOnLoadMethod]
        private static void InitializeVehicleValidation() => EditorApplication.projectChanged += () => vehicleValidationProjectRevision++;
        private StudioVehiclePartValidation.Result VehicleValidation(int index)
        {
            var rules = StudioVehiclePartCatalog.RulesAsset;
            int revision = rules ? EditorUtility.GetDirtyCount(rules) : -1;
            int documentRevision = EditorUtility.GetDirtyCount(Document);
            if (rules != vehicleValidationRules || revision != vehicleRulesRevision || documentRevision != vehicleDocumentRevision || vehicleProjectRevision != vehicleValidationProjectRevision)
            {
                vehicleValidation.Clear(); vehicleValidationRules = rules; vehicleRulesRevision = revision;
                vehicleDocumentRevision = documentRevision; vehicleProjectRevision = vehicleValidationProjectRevision;
            }
            if (!vehicleValidation.TryGetValue(index, out var result)) vehicleValidation[index] = result = StudioVehiclePartValidation.Validate(Document, index, rules);
            return result;
        }
        private void DrawVehicleValidationBadge(ref Rect row, int index)
        {
            if (Document.nodes[index].vehiclePart == null) return;
            var result = VehicleValidation(index);
            if (result.issues.Count == 0) return;
            var badge = new Rect(row.xMax - 18, row.y, 18, row.height);
            if (GUI.Button(badge, new GUIContent(EditorGUIUtility.IconContent("console.warnicon.sml").image, string.Join("\n", result.issues)), GUIStyle.none)) SelectObject(index);
            row.xMax = Mathf.Max(row.xMin, badge.xMin);
        }
        private void DrawVehicleValidationChannels()
        {
            var result = VehicleValidation(Active);
            GUILayout.Label("Naming & Hierarchy", StudioTheme.Styles.boldLabel);
            if (result.issues.Count == 0)
                EditorGUILayout.HelpBox(result.matchedRules > 0 ? "Naming and hierarchy checks passed." : "Naming checks passed. No hierarchy rule is defined for this part and brand.", MessageType.Info);
            else foreach (string issue in result.issues) EditorGUILayout.HelpBox(issue, MessageType.Warning);
            var node = Document.nodes[Active]; var tags = node.name.Split('_');
            if (tags.Length == 4 && (tags[0] != node.vehiclePart.vehicle || tags[1] != node.vehiclePart.part))
                if (GUILayout.Button("Use Selected Vehicle and Part in Name"))
                {
                    tags[0] = node.vehiclePart.vehicle; tags[1] = node.vehiclePart.part;
                    int index = Active; Run(() => RenameObject(index, string.Join("_", tags)));
                }
        }
    }
}

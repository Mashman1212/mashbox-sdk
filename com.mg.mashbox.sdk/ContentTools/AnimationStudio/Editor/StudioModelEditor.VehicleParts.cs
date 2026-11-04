using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private void AddVehiclePartMenu(StudioMenu menu, SceneView view, Action<StudioModelDocument> loaded)
        {
            var templates = StudioVehiclePartCatalog.Load();
            if (templates.Count == 0) menu.AddDisabledItem(new GUIContent("Vehicle Part (SDK rules unavailable)"));
            foreach (string vehicle in templates.Where(t => t.sourceModel).Select(t => t.vehicle).Distinct())
            {
                menu.AddItem(new GUIContent("Vehicle Part/" + vehicle + "/Full " + vehicle), false,
                    () => Run(() => CreateFullVehicle(vehicle, view, loaded)), shortcutEligible: false);
                menu.AddSeparator("Vehicle Part/" + vehicle + "/");
            }
            foreach (var template in templates)
            {
                var entry = template;
                menu.AddItem(new GUIContent(entry.MenuPath), false, () => Run(() => CreateVehiclePart(entry, view, loaded)), shortcutEligible: false);
            }
        }
        private void CreateFullVehicle(string vehicle, SceneView view, Action<StudioModelDocument> loaded)
        {
            using var camera = new CreationCameraScope(view);
            CancelMarkingMenu(); EnsureWorkspace(loaded);
            int index = Document.AddFullVehicle(vehicle);
            CollapseAddedHierarchy(index);
            mode = ComponentMode.Object; pivot = false; search = "";
            Rebuild(); SelectObject(index); Changed(false);
            if (view) view.ShowNotification(new GUIContent("Full " + vehicle + " created. All parts except the " + (vehicle == "Scooter" ? "deck" : "frame") + " are Live."));
        }
        internal void CreateVehiclePart(StudioVehiclePartCatalog.Template template, SceneView view, Action<StudioModelDocument> loaded)
        {
            using var camera = new CreationCameraScope(view);
            CancelMarkingMenu(); EnsureWorkspace(loaded);
            int index = Document.AddVehiclePart(template);
            CollapseAddedHierarchy(index);
            mode = ComponentMode.Object; pivot = false; search = "";
            Rebuild(); SelectObject(index); Changed(false);
            if (view)
            {
                view.ShowNotification(new GUIContent(template.sourceModel ?
                    "Part created from the SDK model. Geometry and anchors are ready to edit." : template.ruleCount == 0 ?
                    "No hierarchy rule for this part; created a mesh root." :
                    "Part template created. Shape the placeholders and position the anchors."));
            }
        }
    }
}

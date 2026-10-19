using System;
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
            foreach (var template in templates)
            {
                var entry = template;
                menu.AddItem(new GUIContent(entry.MenuPath), false, () => Run(() => CreateVehiclePart(entry, view, loaded)));
            }
        }
        internal void CreateVehiclePart(StudioVehiclePartCatalog.Template template, SceneView view, Action<StudioModelDocument> loaded)
        {
            using var camera = new CreationCameraScope(view);
            CancelMarkingMenu(); EnsureWorkspace(loaded);
            int index = Document.AddVehiclePart(template);
            mode = ComponentMode.Object; pivot = false; search = "";
            Rebuild(); SelectObject(index); Changed(false);
            if (view)
            {
                view.ShowNotification(new GUIContent(template.ruleCount == 0 ?
                    "No hierarchy rule for this part; created a mesh root." :
                    "Part template created. Shape the placeholders and position the anchors."));
            }
        }
    }
}

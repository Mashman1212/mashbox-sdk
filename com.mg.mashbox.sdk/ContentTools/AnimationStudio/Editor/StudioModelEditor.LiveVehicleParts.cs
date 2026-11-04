using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private Dictionary<int, string> liveVehicleStatus = new Dictionary<int, string>();
        private static Texture2D liveVehicleIcon;
        private static Texture VehiclePartIcon(bool live)
        {
            var wheel = EditorGUIUtility.IconContent("WheelCollider Icon").image;
            if (!live) return wheel;
            if (liveVehicleIcon) return liveVehicleIcon;
            liveVehicleIcon = new Texture2D(16, 16, TextureFormat.RGBA32, false) { name = "Live vehicle part", hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[256];
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            {
                float dx = x - 7.5f, dy = y - 7.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1.5f - Mathf.Abs(r - 5.5f));
                if (r < 5.5f && (Mathf.Abs(dx) < .7f || Mathf.Abs(dy) < .7f || Mathf.Abs(dx - dy) < .7f || Mathf.Abs(dx + dy) < .7f)) a = 1;
                pixels[y * 16 + x] = new Color(.25f, 1f, .4f, a);
            }
            liveVehicleIcon.SetPixels(pixels); liveVehicleIcon.Apply(); return liveVehicleIcon;
        }
        private void UpdateLiveVehicleParts()
        {
            vehicleValidation.Clear();
            if (Document && Transforms != null) liveVehicleStatus = Document.SnapLiveVehicleParts(Transforms);
        }
        internal void SetVehiclePartData(int index, StudioModelDocument.VehiclePartData data)
        {
            if (!Document || index < 0 || index >= Document.nodes.Count) return;
            if (!string.IsNullOrEmpty(Document.nodes[index].anchorType)) throw new InvalidOperationException("An anchor cannot also be a vehicle part root.");
            Undo.RegisterCompleteObjectUndo(Document, data == null ? "Unmark vehicle part" : "Change vehicle part");
            Document.nodes[index].vehiclePart = data?.Copy();
            Changed(false);
        }
        private void AddVehiclePartContext(StudioMenu menu, int index)
        {
            var node = Document.nodes[index];
            if (!string.IsNullOrEmpty(node.anchorType)) return;
            if (node.vehiclePart == null)
                menu.AddItem(new GUIContent("Mark as Vehicle Part"), false, () => Run(() => {
                    var tags = Document.nodes[index].name.Split('_');
                    var template = tags.Length >= 2 ? StudioVehiclePartCatalog.Load().FirstOrDefault(t => string.Equals(t.vehicle, tags[0], StringComparison.OrdinalIgnoreCase) && string.Equals(t.part, tags[1].Trim(), StringComparison.OrdinalIgnoreCase)) : null;
                    SetVehiclePartData(index, template != null ? StudioModelDocument.VehiclePartData.From(template) : new StudioModelDocument.VehiclePartData());
                }));
            else
            {
                menu.AddItem(new GUIContent("Live Vehicle Part"), node.vehiclePart.live, () => Run(() => {
                    var data = Document.nodes[index].vehiclePart.Copy(); data.live = !data.live; SetVehiclePartData(index, data);
                }));
                menu.AddItem(new GUIContent("Unmark Vehicle Part"), false, () => Run(() => SetVehiclePartData(index, null)));
            }
            menu.AddSeparator("");
        }
        private void DrawVehiclePartChannels()
        {
            var current = Document.nodes[Active].vehiclePart;
            if (current == null) return;
            GUILayout.Space(6);
            GUILayout.Label(new GUIContent("Vehicle Part", VehiclePartIcon(current.live)), StudioTheme.Styles.boldLabel);
            var data = current.Copy();
            var templates = StudioVehiclePartCatalog.Load();
            string[] vehicles = templates.Select(t => t.vehicle).Append(data.vehicle).Distinct().OrderBy(v => v).ToArray();
            EditorGUI.BeginChangeCheck();
            int vehicle = EditorGUILayout.Popup("Vehicle", Array.IndexOf(vehicles, data.vehicle), vehicles);
            if (vehicle >= 0 && vehicles[vehicle] != data.vehicle) { data.vehicle = vehicles[vehicle]; data.part = templates.First(t => t.vehicle == data.vehicle).part; data.targetAnchor = StudioModelDocument.VehiclePartData.DefaultAnchor(data.part); }
            string[] parts = templates.Where(t => t.vehicle == data.vehicle).Select(t => t.part).Append(data.part).Distinct().OrderBy(p => p).ToArray();
            int part = EditorGUILayout.Popup("Part", Array.IndexOf(parts, data.part), parts);
            if (part >= 0 && parts[part] != data.part) { data.part = parts[part]; data.targetAnchor = StudioModelDocument.VehiclePartData.DefaultAnchor(data.part); }
            string[] anchors = new[] { "" }.Concat(StudioAnchorCatalog.Names).Concat(Document.nodes.Select(n => n.anchorType).Where(a => !string.IsNullOrEmpty(a))).Append(data.targetAnchor ?? "").Distinct().ToArray();
            string[] labels = anchors.Select(a => string.IsNullOrEmpty(a) ? "None" : a).ToArray();
            int anchor = EditorGUILayout.Popup("Target Anchor", Array.IndexOf(anchors, data.targetAnchor ?? ""), labels);
            if (anchor >= 0) data.targetAnchor = anchors[anchor];
            data.live = EditorGUILayout.Toggle(new GUIContent("Live", "Follow a matching anchor's position and rotation."), data.live);
            if (EditorGUI.EndChangeCheck()) { int index = Active; Run(() => SetVehiclePartData(index, data)); }
            if (data.live) EditorGUILayout.HelpBox(liveVehicleStatus.TryGetValue(Active, out var status) ? status : "Waiting for a matching anchor.", MessageType.None);
            DrawVehicleValidationChannels();
            GUILayout.Space(6);
        }
    }
}

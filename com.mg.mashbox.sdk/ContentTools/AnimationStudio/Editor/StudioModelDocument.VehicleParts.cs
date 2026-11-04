using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        internal int AddFullVehicle(string vehicle)
        {
            var templates = StudioVehiclePartCatalog.Load().Where(t => t.vehicle == vehicle && t.sourceModel)
                .Where(t => vehicle != "BMX" || t.part != "Mag")
                .Where(t => vehicle != "Scooter" || t.part != "Griptape")
                .GroupBy(t => t.part).Select(g => g.First()).ToArray();
            if (templates.Length == 0) throw new InvalidOperationException("No SDK models are available for " + vehicle + ".");
            int root = nodes.Count;
            var created = new List<Node> { new Node { name = ObjectNames.GetUniqueName(
                nodes.Where(n => n.parent < 0).Select(n => n.name).ToArray(), "Full " + vehicle) } };
            try
            {
                foreach (var template in templates)
                foreach (var direction in template.part == "Grip" || template.part == "Bar End" || (vehicle == "BMX" && (template.part == "CrankArm" || template.part == "Pedal"))
                    ? new[] { VehicleTargetDirection.Left, VehicleTargetDirection.Right }
                    : (vehicle == "BMX" && (template.part == "Rim" || template.part == "Spokes" || template.part == "Nipples" || template.part == "Tire")) ||
                      (vehicle == "Scooter" && (template.part == "Wheel" || template.part == "Urethane"))
                        ? new[] { VehicleTargetDirection.Front, VehicleTargetDirection.Rear } : new[] { VehicleTargetDirection.None })
                for (int instance = 1; instance <= (vehicle == "BMX" && template.part == "Stem Bolt" ? 6 : 1); instance++)
                {
                    var captured = CaptureNodes(template.sourceModel);
                    int offset = root + created.Count;
                    created.AddRange(captured);
                    captured[0].vehiclePart = VehiclePartData.From(template);
                    if (vehicle == "BMX" && template.part == "Frame")
                    {
                        captured[0].position = new Vector3(0, .6245f, .3144f);
                        captured[0].rotation = Quaternion.Euler(344.5617f, 0, 0);
                    }
                    if (vehicle == "Scooter" && template.part == "Deck")
                    {
                        captured[0].position = new Vector3(0, .1445f, -.0102f);
                        captured[0].rotation = Quaternion.identity;
                    }
                    captured[0].vehiclePart.targetDirection = direction;
                    if (vehicle == "BMX" && template.part == "Stem Bolt")
                    {
                        captured[0].vehiclePart.targetAnchor = "StemBolt_Anchor";
                        captured[0].vehiclePart.targetAnchorId = instance;
                    }
                    if (vehicle == "Scooter" && (template.part == "Wheel" || template.part == "Urethane"))
                        captured[0].vehiclePart.targetAnchor = "Wheel_Anchor";
                    if (vehicle == "BMX")
                    {
                        if (template.part == "Rim" || template.part == "Spokes" || template.part == "Nipples" || template.part == "Tire") captured[0].vehiclePart.targetAnchor = "Wheel_Anchor";
                        else if (template.part == "Front Hub") captured[0].vehiclePart.targetAnchor = "FrontWheel_Anchor";
                        else if (template.part == "Rear Hub") captured[0].vehiclePart.targetAnchor = "RearWheel_Anchor";
                    }
                    captured[0].vehiclePart.live = !string.Equals(template.part, vehicle == "Scooter" ? "Deck" : "Frame", StringComparison.OrdinalIgnoreCase);
                    foreach (var node in captured)
                    {
                        node.parent = node.parent < 0 ? root : node.parent + offset;
                        for (int i = 0; i < node.bones.Length; i++) if (node.bones[i] >= 0) node.bones[i] += offset;
                        if (node.rootBone >= 0) node.rootBone += offset;
                    }
                }
            }
            catch
            {
                foreach (var node in created) if (node.mesh) DestroyImmediate(node.mesh);
                throw;
            }
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(this, "Create full " + vehicle);
            nodes.AddRange(created);
            EditorUtility.SetDirty(this);
            return root;
        }

        internal int AddVehiclePart(StudioVehiclePartCatalog.Template template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            int root = nodes.Count;
            if (template.sourceModel)
            {
                var captured = CaptureNodes(template.sourceModel);
                try
                {
                    // Preserve every authored name, including the root. Selecting a
                    // vehicle/part chooses the source; it does not rename that source.
                    captured[0].vehiclePart = VehiclePartData.From(template);
                    foreach (var node in captured)
                    {
                        if (node.parent >= 0) node.parent += root;
                        for (int i = 0; i < node.bones.Length; i++) if (node.bones[i] >= 0) node.bones[i] += root;
                        if (node.rootBone >= 0) node.rootBone += root;
                    }
                    Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Create " + template.vehicle + " " + template.part);
                    Undo.RegisterCompleteObjectUndo(this, "Create vehicle part");
                    nodes.AddRange(captured); EditorUtility.SetDirty(this);
                    Undo.CollapseUndoOperations(group);
                    return root;
                }
                catch
                {
                    foreach (var node in captured) if (node.mesh) DestroyImmediate(node.mesh);
                    throw;
                }
            }
            string brand = string.IsNullOrEmpty(template.brand) ? "Custom" : template.brand;
            string RootName(int number) => template.vehicle + "_" + template.part + "_" + brand + "_" + (number == 1 ? "Template" : "Template " + number);
            int copy = 1; while (nodes.Any(n => n.parent < 0 && n.name == RootName(copy))) copy++;
            var created = new List<Node> { new Node { name = RootName(copy), vehiclePart = VehiclePartData.From(template) } };
            var paths = new Dictionary<string, int>(StringComparer.Ordinal) { [""] = 0 };
            int EnsurePath(string path)
            {
                if (paths.TryGetValue(path, out int found)) return found;
                int slash = path.LastIndexOf('/');
                int parent = EnsurePath(slash < 0 ? "" : path.Substring(0, slash));
                string name = slash < 0 ? path : path.Substring(slash + 1);
                int index = created.Count;
                var node = new Node { name = name, parent = root + parent };
                if (StudioAnchorCatalog.Contains(name)) node.anchorType = name;
                created.Add(node); paths.Add(path, index); return index;
            }
            foreach (string path in template.paths) EnsurePath(path);
            foreach (var orientation in template.orientations)
                if (paths.TryGetValue(orientation.Key, out int index)) created[index].rotation = orientation.Value;

            // The rules specify names and renderer budgets, not dimensions or finished
            // geometry. Put editable blockout meshes only on recognized mesh roles.
            var meshSlots = new List<int>();
            foreach (var path in paths)
                if (new[] { "Mesh", "body", "Standard", "DriveSide" }.Contains(created[path.Value].name)) meshSlots.Add(path.Value);
            if (meshSlots.Count == 0) meshSlots.Add(0); // Leaf parts render on their root; no extra forbidden child.
            foreach (var path in paths)
                if (new[] { "Griptape", "Axle", "HubDriver" }.Contains(created[path.Value].name)) meshSlots.Add(path.Value);
            if (template.maxRenderers >= 0) meshSlots = meshSlots.Take(template.maxRenderers).ToList();
            var owned = new List<Mesh>();
            Material material = null;
            try
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath("ef2f63690a486964faa40c2e3d756d1c"))
                    ?? Shader.Find("MGShaders/HDRP/Lit/MG_Vehicle");
                if (!shader) throw new InvalidOperationException("The vehicle shader is unavailable.");
                material = new Material(shader) { name = "Vehicle Default" };
                foreach (int index in meshSlots.Distinct())
                {
                    var node = created[index];
                    var mesh = BuildPrimitive(node.name == "Griptape" ? PrimitiveType.Plane : PrimitiveType.Cube, out var polygons);
                    owned.Add(mesh); mesh.name = node.name + " Placeholder";
                    mesh.vertices = mesh.vertices.Select(v => v * .1f).ToArray(); mesh.RecalculateBounds();
                    node.mesh = mesh; node.materials = new[] { material };
                    node.cutPolygons = polygons; node.cutTriangles = mesh.triangles;node.geometryVertices=StudioSubdivisionData.Primitive(mesh);
                }
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Create " + template.vehicle + " " + template.part);
                Undo.RegisterCompleteObjectUndo(this, "Create vehicle part");
                nodes.AddRange(created); EditorUtility.SetDirty(this);
                Undo.CollapseUndoOperations(group);
                // Meshes remain in memory until the user explicitly saves the workspace.
                return root;
            }
            catch { foreach (var mesh in owned) if (mesh) DestroyImmediate(mesh); if (material) DestroyImmediate(material); throw; }
        }
    }
}

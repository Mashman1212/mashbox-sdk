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
        internal int AddVehiclePart(StudioVehiclePartCatalog.Template template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            string brand = string.IsNullOrEmpty(template.brand) ? "Custom" : template.brand;
            string RootName(int number) => template.vehicle + "_" + template.part + "_" + brand + "_" + (number == 1 ? "Template" : "Template " + number) + "_" + template.color;
            int copy = 1; while (nodes.Any(n => n.parent < 0 && n.name == RootName(copy))) copy++;
            int root = nodes.Count;
            var created = new List<Node> { new Node { name = RootName(copy) } };
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
            try
            {
                var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                var material = pipeline ? pipeline.defaultMaterial : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
                foreach (int index in meshSlots.Distinct())
                {
                    var node = created[index];
                    var mesh = BuildPrimitive(node.name == "Griptape" ? PrimitiveType.Plane : PrimitiveType.Cube, out var polygons);
                    owned.Add(mesh); mesh.name = node.name + " Placeholder";
                    mesh.vertices = mesh.vertices.Select(v => v * .1f).ToArray(); mesh.RecalculateBounds();
                    node.mesh = mesh; node.materials = new[] { material };
                    node.cutPolygons = polygons; node.cutTriangles = mesh.triangles;
                }
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Create " + template.vehicle + " " + template.part);
                Undo.RegisterCompleteObjectUndo(this, "Create vehicle part");
                nodes.AddRange(created); EditorUtility.SetDirty(this);
                Undo.CollapseUndoOperations(group);
                // Meshes remain in memory until the user explicitly saves the workspace.
                return root;
            }
            catch { foreach (var mesh in owned) if (mesh) DestroyImmediate(mesh); throw; }
        }
    }
}

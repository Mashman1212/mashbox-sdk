using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Toolbars;

namespace MashBoxSDK.AnimationStudio
{
    // Prefer native artwork; use the existing cached Studio masters for everything.
    internal static class StudioCommandIcons
    {
        private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        internal static Texture2D Get(string key)
        {
            string identity = (EditorGUIUtility.isProSkin ? "dark:" : "light:") + key;
            if (cache.TryGetValue(identity, out var icon) && icon) return icon;
            if (key == "Create/Polygon Primitives/Plane" || key == "Create/Polygon Primitives/Quad" || key == "Create/Polygon Primitives/Cylinder")
            {
                string primitive = key.Substring(key.LastIndexOf('/') + 1);
                icon = StudioToolbarIcons.Get("Studio primitive " + primitive, () => Primitive(primitive));
                cache[identity] = icon; return icon;
            }
            if (key.StartsWith("Control/", StringComparison.Ordinal))
            {
                var control = StudioCustomToolbars.CreateControl(key, null);
                if (control is EditorToolbarToggle toggle) icon = toggle.icon;
                else if (control is EditorToolbarDropdown dropdown) icon = dropdown.icon;
                else if (control is EditorToolbarButton button) icon = button.icon;
            }
            switch (key)
            {
                case "Select/Object Mode": icon = new StudioToolbarObjectMode().icon; break;
                case "Select/Vertex": icon = new StudioToolbarVertexMode().icon; break;
                case "Select/Edge": icon = new StudioToolbarEdgeMode().icon; break;
                case "Select/Face": icon = new StudioToolbarFaceMode().icon; break;
                case "Modify/Select": icon = new StudioToolbarSelect().icon; break;
            }
            if (icon) { cache[identity] = icon; return icon; }
            foreach (string candidate in Candidates(key))
                if ((icon = StudioToolbarIcons.TryGet(candidate)) != null) break;
            if (!icon) icon = StudioToolbarIcons.TryGet("SettingsIcon");
            cache[identity] = icon; return icon;
        }
        private static Texture2D Primitive(string shape)
        {
            const int scale = 4, size = 24 * scale;
            var pixels = new Color[size * size];
            void Polygon(Vector2[] points, Color color)
            {
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + .5f) / scale, (y + .5f) / scale); bool inside = false;
                    for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                        if ((points[i].y > p.y) != (points[j].y > p.y) && p.x < (points[j].x - points[i].x) * (p.y - points[i].y) / (points[j].y - points[i].y) + points[i].x) inside = !inside;
                    if (inside) pixels[y * size + x] = color;
                }
            }
            void Line(Vector2 a, Vector2 b, Color color, float width = 1.2f)
            {
                var delta = b - a; float length = delta.sqrMagnitude;
                int left = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - width) * scale));
                int right = Mathf.Min(size - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + width) * scale));
                int bottom = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - width) * scale));
                int top = Mathf.Min(size - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + width) * scale));
                for (int y = bottom; y <= top; y++) for (int x = left; x <= right; x++)
                {
                    var p = new Vector2((x + .5f) / scale, (y + .5f) / scale);
                    float t = length > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, delta) / length) : 0;
                    if ((p - a - delta * t).sqrMagnitude <= width * width * .25f) pixels[y * size + x] = color;
                }
            }
            var edge = new Color(.88f, .88f, .88f, 1); var fill = new Color(.46f, .46f, .46f, 1);
            if (shape == "Cylinder")
            {
                Polygon(new[] { new Vector2(5, 6), new Vector2(19, 6), new Vector2(19, 17), new Vector2(5, 17) }, fill);
                var ellipse = new Vector2[48];
                for (int i = 0; i < ellipse.Length; i++) { float a = i * Mathf.PI * 2 / ellipse.Length; ellipse[i] = new Vector2(12 + 7 * Mathf.Cos(a), 17 + 3 * Mathf.Sin(a)); }
                Polygon(ellipse, new Color(.68f, .68f, .68f, 1));
                for (int i = 0; i < ellipse.Length; i++) Line(ellipse[i], ellipse[(i + 1) % ellipse.Length], edge);
                Line(new Vector2(5, 6), new Vector2(5, 17), edge); Line(new Vector2(19, 6), new Vector2(19, 17), edge);
                for (int i = 0; i < 24; i++)
                {
                    float a = Mathf.PI + i * Mathf.PI / 24, b = Mathf.PI + (i + 1) * Mathf.PI / 24;
                    var p = new Vector2(12 + 7 * Mathf.Cos(a), 6 + 3 * Mathf.Sin(a));
                    var q = new Vector2(12 + 7 * Mathf.Cos(b), 6 + 3 * Mathf.Sin(b));
                    Polygon(new[] { new Vector2(p.x, 6), p, q, new Vector2(q.x, 6) }, fill); Line(p, q, edge);
                }
            }
            else
            {
                var points = shape == "Plane" ? new[] { new Vector2(3, 10), new Vector2(10, 17), new Vector2(21, 13), new Vector2(14, 6) }
                    : new[] { new Vector2(5, 5), new Vector2(19, 5), new Vector2(19, 19), new Vector2(5, 19) };
                Polygon(points, fill);
                for (int i = 0; i < 4; i++) Line(points[i], points[(i + 1) % 4], edge);
                if (shape == "Plane")
                    for (int i = 1; i <= 2; i++)
                    {
                        float t = i / 3f;
                        Line(Vector2.Lerp(points[0], points[1], t), Vector2.Lerp(points[3], points[2], t), edge, .8f);
                        Line(Vector2.Lerp(points[0], points[3], t), Vector2.Lerp(points[1], points[2], t), edge, .8f);
                    }
            }
            var reduced = new Color[24 * 24];
            for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++)
            {
                var rgb = Vector3.zero; float alpha = 0;
                for (int dy = 0; dy < scale; dy++) for (int dx = 0; dx < scale; dx++)
                { var c = pixels[(y * scale + dy) * size + x * scale + dx]; rgb += new Vector3(c.r, c.g, c.b) * c.a; alpha += c.a; }
                reduced[y * 24 + x] = alpha > 0 ? new Color(rgb.x / alpha, rgb.y / alpha, rgb.z / alpha, alpha / (scale * scale)) : Color.clear;
            }
            var texture = new Texture2D(24, 24, TextureFormat.RGBA32, false); texture.SetPixels(reduced); texture.Apply(false, true); return texture;
        }
        private static IEnumerable<string> Candidates(string key)
        {
            if (key == "Customize/Add") return new[] { "Toolbar Plus", "CreateAddNew", "SettingsIcon" };
            if (key == "Unavailable") return new[] { "console.warnicon", "console.infoicon" };
            if (key.Contains("/Camera")) return new[] { "Camera Icon" };
            if (key.Contains("/Lighting")) return new[] { "Lighting", "Light Icon" };
            if (key.StartsWith("File/"))
            {
                if (key.Contains("New")) return new[] { "SceneAsset Icon" };
                if (key.Contains("Open")) return new[] { "FolderOpened Icon", "Folder Icon" };
                if (key.Contains("Save As")) return new[] { "SaveAs", "SaveActive" };
                if (key.Contains("Save")) return new[] { "SaveActive", "SaveAs" };
                if (key.Contains("Import")) return new[] { "Import", "CollabPull", "FolderOpened Icon" };
                return new[] { "Export", "CollabPush", "Folder Icon" };
            }
            if (key == "Edit/Undo") return new[] { "Undo", "back", "tab_prev" };
            if (key == "Edit/Redo") return new[] { "Redo", "forward", "tab_next" };
            if (key == "Edit/Duplicate") return new[] { "TreeEditor.Duplicate", "Duplicate", "Clipboard" };
            if (key == "Edit/Delete") return new[] { "TreeEditor.Trash", "TreeEditor.Trash On", "Toolbar Minus" };
            if (key.Contains("Preferences") || key.Contains("Customize")) return new[] { "SettingsIcon" };
            if (key.Contains("Themes")) return new[] { "ColorPicker.CycleColor", "PreMatSphere", "Material Icon" };
            if (key.StartsWith("Create/Polygon Primitives/"))
            {
                string shape = key.Substring(key.LastIndexOf('/') + 1);
                return new[] { "GameObject/" + shape, shape + " Icon", shape == "Sphere" ? "PreMatSphere" : shape == "Plane" || shape == "Quad" ? "PreMatQuad" : "PreMatCube", "Mesh Icon" };
            }
            if (key.Contains("Foliage")) return new[] { "TerrainInspector.TerrainToolTrees", "Terrain Icon" };
            if (key.Contains("Empty")) return new[] { "GameObject Icon", "Transform Icon" };
            if (key.Contains("Anchor")) return new[] { "AvatarPivot", "ToolHandlePivot", "Transform Icon" };
            if (key.StartsWith("Create/")) return new[] { "WheelCollider Icon", "Mesh Icon" };
            if (key == "Modify/Move") return new[] { "MoveTool" };
            if (key == "Modify/Rotate") return new[] { "RotateTool" };
            if (key == "Modify/Scale") return new[] { "ScaleTool" };
            if (key == "Modify/Select") return new[] { "ViewToolArrow", "Grid.SelectTool", "RectTool" };
            if (key.Contains("Pivot")) return new[] { "ToolHandlePivot", "Transform Icon" };
            if (key.Contains("Freeze")) return new[] { "LockIcon-On", "Transform Icon" };
            if (key.Contains("Frame Selection")) return new[] { "SceneViewCamera", "ViewToolZoom", "Camera Icon" };
            if (key.StartsWith("Select/"))
            {
                if (key.Contains("Through")) return new[] { "scenevis_visible", "ViewToolOrbit" };
                if (key.Contains("Deselect")) return new[] { "Grid.EraserTool", "Toolbar Minus" };
                if (key.Contains("Invert")) return new[] { "PreTextureRGB", "Refresh" };
                if (key.EndsWith("Vertex")) return new[] { "EditCollider", "Mesh Icon" };
                if (key.EndsWith("Edge")) return new[] { "EdgeCollider2D Icon", "Mesh Icon" };
                if (key.EndsWith("Face")) return new[] { "PreMatQuad", "Mesh Icon" };
                return new[] { "Grid.SelectTool", "GameObject Icon" };
            }
            if (key.StartsWith("Mesh Tools/"))
            {
                if (key.Contains("Bevel")) return new[] { "ProBuilder.Bevel", "EditCollider", "Mesh Icon" };
                if (key.Contains("Extrude")) return new[] { "ProBuilder.Extrude", "AreaEffector2D Icon", "Mesh Icon" };
                if (key.Contains("Weld") || key.Contains("Merge")) return new[] { "JointAngularLimits", "FixedJoint Icon", "Mesh Icon" };
                if (key.Contains("Edge Loop")) return new[] { "Grid.BoxTool", "BoxCollider Icon", "Mesh Icon" };
                return new[] { "Grid.PaintTool", "EditCollider", "Mesh Icon" };
            }
            if (key.StartsWith("Deform/")) return new[] { "BlendTree Icon", "SkinnedMeshRenderer Icon" };
            if (key.Contains("Shaded Wireframe")) return new[] { "Toolbars/ShadedWireframe", "Mesh Icon" };
            if (key.Contains("Wireframe")) return new[] { "Toolbars/wireframe", "Mesh Icon" };
            if (key.Contains("X-ray")) return new[] { "SceneViewFx", "scenevis_visible" };
            if (key.Contains("Shaded")) return new[] { "PreMatSphere", "Material Icon" };
            if (key.Contains("Recalculate Normals")) return new[] { "Refresh" };
            if (key.Contains("Soften")) return new[] { "PreMatSphere", "Mesh Icon" };
            if (key.Contains("Harden")) return new[] { "PreMatCube", "Mesh Icon" };
            if (key.StartsWith("UV/")) return new[] { "UVEditor", "Texture2D Icon" };
            if (key.StartsWith("Lighting/")) return new[] { "LightmapEditor.WindowTitle", "Light Icon" };
            if (key.Contains("Statistics") || key.Contains("FPS")) return new[] { "Profiler.Graph", "ProfilerWindow", "UnityEditor.ProfilerWindow" };
            if (key.Contains("Notifications")) return new[] { "console.infoicon" };
            if (key.Contains("Orientation")) return new[] { "ToolHandleGlobal", "Transform Icon" };
            if (key.Contains("Toolbars")) return new[] { "CustomTool", "SettingsIcon" };
            return new[] { "SettingsIcon" };
        }
    }
}

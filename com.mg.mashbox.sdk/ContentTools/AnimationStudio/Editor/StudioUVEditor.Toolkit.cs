using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioUVEditor
    {
        private Vector2 toolkitScroll;
        private readonly HashSet<int> pinned = new HashSet<int>();
        private bool shellSelection, showGrid = true, showTexture = true, keepBorder = true;
        private bool selectSection = true, transformSection = true, layoutSection = true, projectSection = true, sewSection = true, setsSection = true, displaySection;
        private int projectionAxis = 1, relaxIterations = 10;
        private float padding = .02f, sewTolerance = .0001f, unwrapAngle = 60;
        private string feedback;
        private static readonly string[] Tools = { "Select", "Move", "Rotate", "Scale" };
        private void ActionButton(string label, Action action, string tooltip = null)
        {
            if (!GUILayout.Button(new GUIContent(label, tooltip ?? label), GUILayout.Height(22))) return;
            try { CancelInteraction(); action(); feedback = label; }
            catch (Exception e) { feedback = e.Message;  }
            Repaint();
        }
        private void Row(string a, Action first, string b, Action second)
        {
            using (new EditorGUILayout.HorizontalScope()) { ActionButton(a, first); ActionButton(b, second); }
        }
        private void DrawToolkit(List<Vector2> uv)
        {
            using (new EditorGUILayout.VerticalScope(StudioTheme.Styles.helpBox, GUILayout.Width(278), GUILayout.ExpandHeight(true)))
            {
                GUILayout.Label("UV Toolkit", StudioTheme.Styles.boldLabel);
                tool = GUILayout.Toolbar(tool, Tools);
                toolkitScroll = EditorGUILayout.BeginScrollView(toolkitScroll);
                bool exists = mesh && uv.Count == mesh.vertexCount && uv.Count > 0;
                bool selected = exists && selection.Count > 0;
                GUILayout.Label(selection.Count + " UVs selected", StudioTheme.Styles.miniLabel);
                selectSection = EditorGUILayout.Foldout(selectSection, "Selection", true);
                if (selectSection)
                {
                    shellSelection = GUILayout.Toolbar(shellSelection ? 1 : 0, new[] { "UV", "Shell" }) == 1;
                    using (new EditorGUI.DisabledScope(!exists))
                    {
                        Row("All", () => selection.UnionWith(Enumerable.Range(0, uv.Count)), "Clear", () => selection.Clear());
                        Row("Invert", () => { var inverse = Enumerable.Range(0, uv.Count).Where(i => !selection.Contains(i)).ToArray(); selection.Clear(); selection.UnionWith(inverse); }, "Expand to Shell", SelectShell);
                        Row("Grow", () => GrowSelection(uv, false), "Shrink", () => GrowSelection(uv, true));
                        Row("Border UVs", () => { selection.Clear(); selection.UnionWith(StudioUVTools.Border(mesh, uv)); }, "Frame Selection", () => FrameUVs(uv, true));
                    }
                }
                transformSection = EditorGUILayout.Foldout(transformSection, "Transform & Align", true);
                if (transformSection)
                using (new EditorGUI.DisabledScope(!selected))
                {
                    offset = EditorGUILayout.Vector2Field("Offset", offset);
                    ActionButton("Move UVs", () => TransformSelection(uv, v => v + offset, "Move UVs"));
                    scale = EditorGUILayout.Vector2Field("Scale", scale);
                    angle = EditorGUILayout.FloatField("Rotation", angle);
                    ActionButton("Apply Scale / Rotation", () => AroundCenter(uv, v => Rotate(Vector2.Scale(v, scale), angle * Mathf.Deg2Rad), "Transform UVs"));
                    Row("Flip U", () => AroundCenter(uv, v => new Vector2(-v.x, v.y), "Flip U"), "Flip V", () => AroundCenter(uv, v => new Vector2(v.x, -v.y), "Flip V"));
                    Row("Rotate −90°", () => AroundCenter(uv, v => Rotate(v, -Mathf.PI / 2), "Rotate UVs"), "Rotate +90°", () => AroundCenter(uv, v => Rotate(v, Mathf.PI / 2), "Rotate UVs"));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        ActionButton("Left", () => Align(uv, 0, 0)); ActionButton("Center U", () => Align(uv, 0, .5f)); ActionButton("Right", () => Align(uv, 0, 1));
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        ActionButton("Bottom", () => Align(uv, 1, 0)); ActionButton("Center V", () => Align(uv, 1, .5f)); ActionButton("Top", () => Align(uv, 1, 1));
                    }
                    Row("Distribute U", () => Distribute(uv, 0), "Distribute V", () => Distribute(uv, 1));
                }
                layoutSection = EditorGUILayout.Foldout(layoutSection, "Layout", true);
                if (layoutSection)
                {
                    padding = EditorGUILayout.Slider(new GUIContent("Padding", "Normalize/unwrap: fraction of the tile. Grid packing: fraction of each shell cell."), padding, 0, .2f);
                    using (new EditorGUI.DisabledScope(!selected))
                    {
                        Row("Normalize", () => Normalize(uv), "Center in Tile", () => CenterInTile(uv));
                        ActionButton("Pack Selected Shells", () => Pack(uv, false), "Expands selection to full shells and packs their bounding boxes into a grid. Keeps relative scale and aspect ratio; unselected shells stay unchanged.");
                    }
                    using (new EditorGUI.DisabledScope(!exists))
                        ActionButton("Pack All Shells", () => Pack(uv, true), "Packs all shell bounding boxes into a 0–1 grid, preserving relative scale and aspect ratio.");
                }
                projectSection = EditorGUILayout.Foldout(projectSection, "Create / Project", true);
                if (projectSection)
                using (new EditorGUI.DisabledScope(!mesh))
                {
                    projectionAxis = EditorGUILayout.Popup("Projection axis", projectionAxis, new[] { "X (YZ)", "Y (XZ)", "Z (XY)" });
                    ActionButton("Planar Projection", () => Project(0), "Projects selected UVs, or the entire mesh when nothing is selected.");
                    Row("Cylindrical", () => Project(1), "Spherical", () => Project(2));
                    unwrapAngle = EditorGUILayout.Slider("Seam angle", unwrapAngle, 1, 180);
                    ActionButton("Automatic Unwrap", AutoUnwrap, "Rebuilds this entire UV set using Unity's unwrap solver. Creates mesh vertex splits at UV seams and preserves other mesh attributes.");
                    GUILayout.Label("Projections: selection, or all if empty.\nAutomatic unwrap: entire mesh.", StudioTheme.Styles.wordWrappedMiniLabel);
                }
                sewSection = EditorGUILayout.Foldout(sewSection, "Sew & Relax", true);
                if (sewSection)
                {
                    sewTolerance = Mathf.Max(0, EditorGUILayout.FloatField(new GUIContent("Sew distance", "Maximum distance in mesh object space between selected vertices to sew."), sewTolerance));
                    using (new EditorGUI.DisabledScope(!selected))
                    {
                        ActionButton("Sew Selected UVs", () => { StudioUVTools.Sew(mesh, uv, selection, sewTolerance); Write(uv, "Sew UVs"); }, "Averages UVs of selected vertices that share a position within the sew distance. Does not weld geometry.");
                        Row("Pin", () => pinned.UnionWith(selection), "Unpin", () => pinned.ExceptWith(selection));
                    }
                    using (new EditorGUI.DisabledScope(!exists))
                        Row("Select Pinned", () => { selection.Clear(); selection.UnionWith(pinned); }, "Clear Pins", () => pinned.Clear());
                    keepBorder = EditorGUILayout.Toggle("Preserve borders", keepBorder);
                    relaxIterations = EditorGUILayout.IntSlider("Iterations", relaxIterations, 1, 100);
                    using (new EditorGUI.DisabledScope(!selected))
                        ActionButton("Relax Selected UVs", () => { StudioUVTools.Relax(mesh, uv, selection, pinned, relaxIterations, keepBorder); Write(uv, "Relax UVs"); }, "Smooths UV positions along mesh connectivity. Pins and optional shell borders remain fixed.");
                    GUILayout.Label("Pins constrain Relax in this editor session.", StudioTheme.Styles.wordWrappedMiniLabel);
                }
                setsSection = EditorGUILayout.Foldout(setsSection, "UV Sets", true);
                if (setsSection)
                using (new EditorGUI.DisabledScope(!mesh))
                {
                    using (new EditorGUI.DisabledScope(exists))
                        ActionButton("Create Empty Set", () => Write(new Vector2[mesh.vertexCount], "Create UV set"));
                    destination = EditorGUILayout.Popup("Destination", destination, Channels);
                    using (new EditorGUI.DisabledScope(!exists || destination == channel))
                        ActionButton("Duplicate to " + Channels[destination], () => DuplicateSet(uv));
                    using (new EditorGUI.DisabledScope(!exists))
                        ActionButton("Delete Current Set", () => { Undo.RegisterCompleteObjectUndo(mesh, "Delete UV set"); mesh.SetUVs(channel, new List<Vector2>()); selection.Clear(); pinned.Clear(); EditorUtility.SetDirty(mesh); owner?.Changed(); });
                }
                displaySection = EditorGUILayout.Foldout(displaySection, "Display", true);
                if (displaySection)
                {
                    showGrid = EditorGUILayout.Toggle("Grid", showGrid);
                    showTexture = EditorGUILayout.Toggle("Texture", showTexture);
                    texture = (Texture2D)EditorGUILayout.ObjectField("Preview", texture, typeof(Texture2D), false);
                    GUILayout.Label("Q/W/E/R: tools · F: frame selection\nA: frame all · MMB: pan · Wheel: zoom\nShift: add · Ctrl/Cmd: subtract", StudioTheme.Styles.wordWrappedMiniLabel);
                }
                if (!string.IsNullOrEmpty(feedback)) EditorGUILayout.HelpBox(feedback, MessageType.None);
                EditorGUILayout.EndScrollView();
            }
        }
        private void AroundCenter(List<Vector2> uv, Func<Vector2, Vector2> transform, string label)
        {
            Vector2 pivot = selection.Aggregate(Vector2.zero, (sum, i) => sum + uv[i]) / selection.Count;
            TransformSelection(uv, v => pivot + transform(v - pivot), label);
        }
        private void Align(List<Vector2> uv, int axis, float factor)
        {
            float target = Mathf.Lerp(selection.Min(i => uv[i][axis]), selection.Max(i => uv[i][axis]), factor);
            TransformSelection(uv, v => { v[axis] = target; return v; }, "Align UVs");
        }
        private void Distribute(List<Vector2> uv, int axis)
        {
            var groups = selection.GroupBy(i => uv[i][axis]).OrderBy(g => g.Key).ToArray();
            if (groups.Length < 3) return;
            for (int n = 0; n < groups.Length; n++)
                foreach (int i in groups[n]) { var v = uv[i]; v[axis] = Mathf.Lerp(groups[0].Key, groups[groups.Length - 1].Key, n / (float)(groups.Length - 1)); uv[i] = v; }
            Write(uv, "Distribute UVs");
        }
        private void Normalize(List<Vector2> uv)
        {
            var bounds = StudioUVTools.Bounds(uv, selection);
            float factor = (1 - 2 * padding) / Mathf.Max(.00001f, Mathf.Max(bounds.width, bounds.height));
            TransformSelection(uv, v => (v - bounds.center) * factor + Vector2.one * .5f, "Normalize UVs");
        }
        private void CenterInTile(List<Vector2> uv)
        {
            var delta = Vector2.one * .5f - StudioUVTools.Bounds(uv, selection).center;
            TransformSelection(uv, v => v + delta, "Center UVs");
        }
        private void Pack(List<Vector2> uv, bool all)
        {
            var shells = StudioUVTools.Shells(mesh, uv).Where(s => all || s.Any(selection.Contains)).ToList();
            StudioUVTools.Pack(uv, shells, padding); Write(uv, "Pack UV shells");
            selection.Clear(); foreach (var shell in shells) selection.UnionWith(shell);
        }
        private void GrowSelection(List<Vector2> uv, bool shrink)
        {
            var adjacent = StudioUVTools.Adjacency(mesh, uv);
            var originalSelection = new HashSet<int>(selection);
            foreach (int i in originalSelection)
                if (shrink) { if (adjacent[i].Any(n => !originalSelection.Contains(n))) selection.Remove(i); }
                else selection.UnionWith(adjacent[i]);
        }
        private void FrameUVs(List<Vector2> uv, bool selected)
        {
            var indices = selected ? selection.ToArray() : Enumerable.Range(0, uv.Count).ToArray();
            if (indices.Length == 0) { zoom = 1; pan = Vector2.zero; return; }
            var bounds = StudioUVTools.Bounds(uv, indices);
            float available = Mathf.Max(10, Mathf.Min(canvas.width, canvas.height) - 70);
            zoom = Mathf.Clamp(Mathf.Min((canvas.width - 40) / Mathf.Max(.01f, bounds.width), (canvas.height - 40) / Mathf.Max(.01f, bounds.height)) / available, .01f, 30);
            pan = new Vector2(.5f - bounds.center.x, bounds.center.y - .5f) * Unit;
        }
        private void DuplicateSet(List<Vector2> uv)
        {
            var target = new List<Vector2>(); mesh.GetUVs(destination, target);
            if (target.Count > 0 && !EditorUtility.DisplayDialog("Replace UV set?", "Replace " + Channels[destination] + " with a copy of " + Channels[channel] + "?", "Replace", "Cancel")) return;
            StudioModelDocument.SetUVs(mesh, destination, uv, "Duplicate UV set"); owner?.Changed(); channel = destination; selection.Clear(); pinned.Clear();
        }
        private void Project(int kind)
        {
            var uv = Read();
            if (uv.Count != mesh.vertexCount) uv = Enumerable.Repeat(Vector2.zero, mesh.vertexCount).ToList();
            var indices = selection.Count > 0 ? selection.ToArray() : Enumerable.Range(0, mesh.vertexCount).ToArray();
            StudioUVTools.Project(mesh, uv, indices, kind, projectionAxis);
            if (kind == 0) Write(uv, "Project UVs");
            else
            {
                var selected = new HashSet<int>(indices); var tr = mesh.triangles;
                var corners = tr.Select(i => uv[i]).ToArray();
                // Split the wrap seam instead of stretching triangles across the full texture.
                for (int i = 0; i < tr.Length; i += 3)
                {
                    if (!selected.Contains(tr[i]) || !selected.Contains(tr[i + 1]) || !selected.Contains(tr[i + 2])) continue;
                    float min = Mathf.Min(corners[i].x, Mathf.Min(corners[i + 1].x, corners[i + 2].x));
                    float max = Mathf.Max(corners[i].x, Mathf.Max(corners[i + 1].x, corners[i + 2].x));
                    if (max - min > .5f) for (int j = 0; j < 3; j++) if (corners[i + j].x < .5f) corners[i + j].x += 1;
                }
                StudioUVTools.ApplyCornerUVs(mesh, channel, corners);
                selection.Clear(); pinned.Clear(); owner?.MeshTopologyChanged();
            }
        }
        private void AutoUnwrap()
        {
            UnwrapParam.SetDefaults(out var parameters); parameters.hardAngle = unwrapAngle; parameters.packMargin = padding;
            var corners = Unwrapping.GeneratePerTriangleUV(mesh, parameters);
            StudioUVTools.ApplyCornerUVs(mesh, channel, corners);
            selection.Clear(); pinned.Clear(); owner?.MeshTopologyChanged();
        }
    }
}

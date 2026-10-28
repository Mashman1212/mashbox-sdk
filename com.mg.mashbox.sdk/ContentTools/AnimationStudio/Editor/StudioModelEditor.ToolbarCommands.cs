using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal sealed class ToolbarCommand
        {
            internal string Key, Path, Label;
            internal bool Enabled, Checked;
            internal StudioShortcut? Shortcut;
            internal string Tooltip => ToolbarTooltip(Key,Label,Shortcut);
            internal Action Execute;
        }
        internal static string ToolbarTooltip(string key,string label,StudioShortcut? shortcut=null)
        {
            string title=key=="Mesh Display/Soften…"?"Soften Options":label?.TrimEnd('…');
            if(string.IsNullOrEmpty(title))title=key?.Substring(key.LastIndexOf('/')+1)??"Unavailable";
            string binding=shortcut.HasValue?StudioShortcuts.Label(shortcut.Value):"";
            return title+(string.IsNullOrEmpty(binding)?"":" ("+binding+")")+"\n"+ToolbarDescription(key);
        }
        private static string ToolbarDescription(string key)
        {
            switch(key)
            {
                case "Mesh Display/Soften": return "Blend vertex normals across selected edges up to the soften angle for smoother shading.";
                case "Mesh Display/Harden": return "Split shading normals along selected edges to create sharp shading breaks.";
                case "Mesh Display/Soften…": return "Set the angle used to soften edges and preview the shading result.";
                case "Mesh Display/X-ray Mode": return "Make surfaces transparent and show edges through the mesh.";
                case "Mesh Display/Wireframe": return "Show polygon edges without drawing shaded surfaces.";
                case "Mesh Display/Shaded Wireframe": return "Toggle polygon edge outlines over shaded surfaces.";
                case "Mesh Tools/Extrude": return "Extend selected faces or open boundary edges to create new geometry.";
                case "Mesh Tools/Bevel…": return "Replace selected edges with a chamfer or rounded profile.";
                case "Mesh Tools/Merge Vertices": return "Merge selected vertices at their shared center.";
                case "Mesh Tools/Target Weld": return "Drag a source vertex onto a target vertex and weld them together.";
                case "Mesh Tools/Insert Edge Loop": return "Insert one or more connected edge loops through a polygon strip.";
                case "Mesh Tools/Multi-Cut": return "Place connected cuts across polygon faces and edges.";
                case "Deform/Blend Shape": return "Create a blend shape from the selected meshes.";
                case "Deform/Bake Current Pose": return "Apply the current blend shape pose to the mesh.";
                case "Edit Mesh/Edit Edge Flow…": return "Fit selected edges to the curvature of neighboring surfaces.";
                case "View/Viewport Lighting": return "Toggle viewport lighting to inspect mesh shading.";
                case "View/Material Textures": return "Toggle assigned material textures or neutral Studio shading.";
                case "View/Checker Map": return "Show the checker material to inspect UV scale and distortion.";
                case "View/Backface Culling": return "Hide polygon back faces or show both sides.";
                case "Viewport/Camera": return "Adjust camera position, orbit distance and navigation sensitivity.";
                case "Viewport/Lighting": return "Adjust Studio viewport lights and environment settings.";
                case "Edit/Undo": return "Undo the previous modeling action.";
                case "Edit/Redo": return "Restore the last undone modeling action.";
                case "Edit/Duplicate": return "Create copies of the selected objects.";
                case "Edit/Delete": return "Delete the selected objects or mesh components.";
                case "Select/All": return "Select all eligible objects or mesh components.";
                case "Select/Deselect": return "Clear the current selection.";
                case "Select/Invert Selection": return "Select eligible items that are not currently selected.";
                case "Select/Object Mode": return "Select and edit whole objects.";
                case "Select/Vertex": return "Select and edit mesh vertices.";
                case "Select/Edge": return "Select and edit polygon edges.";
                case "Select/Face": return "Select and edit polygon faces.";
                case "Select/Frame Selection": return "Move the camera to frame the current selection.";
                case "Select/Through Selection": return "Allow selection of components behind visible surfaces.";
                case "Select/Isolate Selected": return "Show only the selected objects or components; toggle again to show all.";
                case "Modify/Select": return "Select objects or components without a transform handle.";
                case "Modify/Move": return "Translate the selection using the move handle.";
                case "Modify/Rotate": return "Rotate the selection around its pivot.";
                case "Modify/Scale": return "Resize the selection around its pivot.";
                case "Modify/Edit Pivot": return "Edit the pivot without moving the mesh, or finish pivot editing.";
                case "Modify/Center Pivot": return "Move the pivot to the selection center.";
                case "Modify/Freeze Transforms": return "Bake transforms into the mesh and reset transform channels.";
                case "UV/UV Editor": return "Open the editor for viewing and editing mesh UV coordinates.";
                case "Lighting/Bake Vertex AO (Red)…": return "Bake ambient occlusion into the red vertex color channel.";
                case "Create/Empty Transform": return "Create an empty object in the workspace.";
                case "Create/Empty Child": return "Create an empty child under the active object.";
                case "Create/Anchor Point": return "Create a named attachment anchor at the origin.";
                case "File/New Workspace": return "Create a new modeling workspace.";
                case "File/Open Workspace…": return "Open a saved modeling workspace.";
                case "File/Import FBX…": return "Import an FBX model into the current workspace.";
                case "File/Save Workspace": return "Save the current modeling workspace.";
                case "File/Save Workspace As…": return "Save the workspace to a new file.";
                case "File/Export All… (FBX)": return "Export all workspace objects to an FBX file.";
                case "File/Export Selection… (FBX)": return "Export selected objects to an FBX file.";
            }
            if(key!=null&&key.StartsWith("Create/Polygon Primitives/",StringComparison.Ordinal))return "Create a polygon "+key.Substring(key.LastIndexOf('/')+1).ToLowerInvariant()+" in the workspace.";
            if(key!=null&&key.StartsWith("UV/UV Sets/",StringComparison.Ordinal))return "Choose this UV set for viewing and editing.";
            if(key!=null&&key.StartsWith("Create/Vehicle",StringComparison.Ordinal))return "Create a vehicle part from this template.";
            if(key!=null&&key.StartsWith("File/Open Recents/",StringComparison.Ordinal))return "Open this recently used workspace.";
            if(key!=null&&key.StartsWith("Display/Toolbars/",StringComparison.Ordinal))return "Show or hide this toolbar.";
            return "Run this action using the current selection.";
        }
        // Resolve from the menu declarations, including commands excluded from hotkeys.
        // These snapshots belong to one window/context; never retain them globally.
        internal List<ToolbarCommand> ToolbarCommands(MashBoxStudioViewport view)
        {
            var result = new List<ToolbarCommand>();
            foreach (string title in ModelMenuTitles)
            {
                var menu = new StudioMenu(title, true);
                PopulateModelMenu(title, menu, view, commandLoaded);
                CollectToolbarCommands(menu.Items, title, result);
            }
            result.Add(new ToolbarCommand { Key = "Viewport/Camera", Path = "Viewport/Camera Settings", Label = "Camera", Enabled = true, Execute = () => { ShowCameraOptions(view, true); FocusToolOptions?.Invoke(); } });
            result.Add(new ToolbarCommand { Key = "Viewport/Lighting", Path = "Viewport/Lighting Settings", Label = "Lighting", Enabled = true, Execute = () => { ShowLightingOptions(true); FocusToolOptions?.Invoke(); } });
            result.AddRange(StudioCustomToolbars.NativeControls);
            return result;
        }
        private static void CollectToolbarCommands(List<StudioMenu.Item> items, string path, List<ToolbarCommand> result)
        {
            foreach (var item in items)
            {
                if (item.Separator) continue;
                string full = path + "/" + item.Text;
                if (item.Children.Count > 0) { CollectToolbarCommands(item.Children, full, result); continue; }
                if (item.Text == "No recent files" || item.Text.Contains("SDK rules unavailable")) continue;
                result.Add(new ToolbarCommand { Key = item.ToolbarKey ?? full, Path = full, Label = item.Text, Enabled = item.Enabled && item.Action != null, Checked = item.Checked, Shortcut = item.Command, Execute = item.Action });
            }
        }
    }
}

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
            internal Action Execute;
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
                result.Add(new ToolbarCommand { Key = item.ToolbarKey ?? full, Path = full, Label = item.Text, Enabled = item.Enabled && item.Action != null, Checked = item.Checked, Execute = item.Action });
            }
        }
    }
}

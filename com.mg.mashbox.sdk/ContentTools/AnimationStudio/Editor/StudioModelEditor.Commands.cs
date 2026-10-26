using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private SceneView commandView;
        private Action<StudioModelDocument> commandLoaded;
        private bool refreshingCommands;
        internal void BindModelCommands(SceneView view, Action<StudioModelDocument> loaded)
        {
            commandView = view; commandLoaded = loaded; StudioModelCommands.SetActive(this);
            RefreshModelCommandCatalog();
        }
        internal void RefreshModelCommandCatalog()
        {
            if (!commandView || refreshingCommands) return;
            refreshingCommands = true;
            try
            {
                foreach (string title in ModelMenuTitles)
                    PopulateModelMenu(title, new StudioMenu(title, true), commandView, commandLoaded);
            }
            finally { refreshingCommands = false; }
        }
        private static StudioMenu.Item FindCommand(List<StudioMenu.Item> items, StudioShortcut id)
        {
            foreach (var item in items)
            {
                if (item.Command == id) return item;
                var nested = FindCommand(item.Children, id); if (nested != null) return nested;
            }
            return null;
        }
        internal bool TryExecuteModelCommand(Event evt, SceneView view)
        {
            if (!StudioShortcuts.MatchModelCommand(evt, out var id)) return false;
            string category = StudioModelCommands.Category(id);
            if (category == null || commandLoaded == null) return false;
            // Re-evaluate the actual menu in the current context: selection, document,
            // disabled states and action callbacks stay identical to a menu click.
            var menu = new StudioMenu(category);
            PopulateModelMenu(category, menu, view, commandLoaded);
            var item = FindCommand(menu.Items, id);
            evt.Use();
            if (item != null && item.Enabled && item.Action != null) item.Action();
            return true;
        }
    }
}

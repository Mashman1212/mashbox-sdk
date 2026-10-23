using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal Action<StudioModelDocument> WorkspaceSavedAs;
        internal Action WorkspaceSaveStateChanged;
        internal bool CanExportSelection => Document && objects.Any(i => i >= 0 && i < Document.nodes.Count);

        private void OpenWorkspace(Action<StudioModelDocument> loaded)
        {
            System.IO.Directory.CreateDirectory(StudioModelDocument.WorkspaceFolder);
            string path = EditorUtility.OpenFilePanelWithFilters("Open MashBox Studio workspace", StudioModelDocument.WorkspaceFolder,
                new[] { "MashBox Studio workspace", StudioModelDocument.WorkspaceExtension, "Legacy workspace (convert)", "asset" });
            if (string.IsNullOrEmpty(path)) return;
            OpenWorkspacePath(path, loaded);
        }

        private const int RecentFileLimit = 10;
        private static string TemporaryWorkspacePath => Path.GetFullPath(Path.Combine(StudioModelDocument.WorkspaceFolder, ".Temporary", "Untitled Workspace.workspace"));
        private bool IsTemporaryWorkspace => Document && Document.HasStorage &&
            string.Equals(Path.GetFullPath(Document.WorkspacePath), TemporaryWorkspacePath, StringComparison.OrdinalIgnoreCase);
        internal string WorkspaceSaveDestination => Document && Document.HasStorage && !IsTemporaryWorkspace ? Document.WorkspacePath : null;
        internal static void ClearTemporaryWorkspaceFile()
        {
            if (!File.Exists(TemporaryWorkspacePath)) return;
            var empty = CreateTemporaryWorkspace();
            UnityEngine.Object.DestroyImmediate(empty);
        }
        private static string RecentFilesKey => "MashBox.Studio.RecentFiles." + Application.dataPath;
        [Serializable] private sealed class RecentFiles { public List<string> paths = new List<string>(); }
#if MASHBOX_STUDIO_TESTS
        internal Func<int> WorkspaceReplacementChoice;
        internal Func<string> WorkspaceSavePathChoice;
#endif
        private static List<string> ReadRecentFiles()
        {
            try
            {
                var saved = JsonUtility.FromJson<RecentFiles>(StudioPreferenceStore.GetString(RecentFilesKey, ""));
                return (saved?.paths ?? new List<string>()).Where(p => !string.IsNullOrEmpty(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(RecentFileLimit).ToList();
            }
            catch (ArgumentException) { return new List<string>(); }
        }
        private static void RememberRecentFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            path = Path.GetFullPath(path);
            if (string.Equals(path, TemporaryWorkspacePath, StringComparison.OrdinalIgnoreCase)) return;
            string extension = Path.GetExtension(path);
            if (!extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals("." + StudioModelDocument.WorkspaceExtension, StringComparison.OrdinalIgnoreCase)) return;
            var paths = ReadRecentFiles();
            paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            paths.Insert(0, path);
            if (paths.Count > RecentFileLimit) paths.RemoveRange(RecentFileLimit, paths.Count - RecentFileLimit);
            StudioPreferenceStore.SetString(RecentFilesKey, JsonUtility.ToJson(new RecentFiles { paths = paths }));
        }
        private void AddRecentFilesMenu(StudioMenu menu, Action<StudioModelDocument> loaded)
        {
            if (menu.CatalogOnly)
            {
                // Recent-file navigation is deliberately excluded from keyboard shortcuts.
                return;
            }
            var paths = ReadRecentFiles();
            if (paths.Count == 0) { menu.AddDisabledItem(new GUIContent("Open Recents…/No recent files")); return; }
            int recentSlot = 0;
            foreach (string path in paths)
            {
                string commandKey = "Open Recents/Recent " + (++recentSlot);
                string label = Path.GetFileName(path);
                if (paths.Count(p => string.Equals(Path.GetFileName(p), label, StringComparison.OrdinalIgnoreCase)) > 1)
                    label += " (" + Path.GetDirectoryName(path).Replace('\\', '›').Replace('/', '›') + ")";
                if (!File.Exists(path)) { menu.AddDisabledItem(new GUIContent("Open Recents…/" + label + " (missing)"), commandKey, shortcutEligible: false); continue; }
                string recent = path;
                menu.AddItem(new GUIContent("Open Recents…/" + label), false, () => Run(() => {
                    if (string.Equals(Path.GetExtension(recent), ".fbx", StringComparison.OrdinalIgnoreCase)) ImportPath(recent, loaded);
                    else OpenWorkspacePath(recent, loaded);
                }), commandKey, shortcutEligible: false);
            }
        }
        internal bool HasUnsavedWorkspaceChanges => Document &&
            (EditorUtility.IsDirty(Document) || ((!Document.HasStorage || IsTemporaryWorkspace) && Document.nodes.Count > 0) ||
                Document.nodes.Any(n => n.mesh && !EditorUtility.IsPersistent(n.mesh) && EditorUtility.IsDirty(n.mesh)));
        private bool ConfirmWorkspaceReplacement(out StudioModelDocument discarded)
        {
            FinishChannelEdit();
            discarded = null;
            if (!HasUnsavedWorkspaceChanges) return true;
            int choice;
#if MASHBOX_STUDIO_TESTS
            if (WorkspaceReplacementChoice != null) choice = WorkspaceReplacementChoice();
            else
#endif
            choice = EditorUtility.DisplayDialogComplex("Unsaved workspace changes",
                "Save changes to \"" + Document.name + "\" before continuing?", "Save", "Cancel", "Don't Save");
            if (choice == 1) return false;
            if (choice == 2) { discarded = Document; return true; }
            if (choice != 0) return false;
            if (Document.HasStorage && !IsTemporaryWorkspace) Save(); else SaveAs();
            return Document && Document.HasStorage && !IsTemporaryWorkspace && !HasUnsavedWorkspaceChanges;
        }
        private void NewWorkspace(Action<StudioModelDocument> loaded)
        {
            if (!ConfirmWorkspaceReplacement(out var discarded)) return;
            var document = CreateTemporaryWorkspace();
            ReplaceWorkspace(document, loaded, discarded);
        }
        private static StudioModelDocument CreateTemporaryWorkspace()
        {
            var document = ScriptableObject.CreateInstance<StudioModelDocument>();
            // Reuse one backing file instead of accumulating numbered workspaces.
            // Only New (after confirmation) or the initial empty session resets it.
            document.SetWorkspacePath(TemporaryWorkspacePath);
            document.Save();
            return document;
        }
        private void OpenWorkspacePath(string path, Action<StudioModelDocument> loaded)
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path)) throw new FileNotFoundException("The workspace no longer exists.", path);
            if (Document && Document.HasStorage && string.Equals(Path.GetFullPath(Document.WorkspacePath), path, StringComparison.OrdinalIgnoreCase))
            { RememberRecentFile(path); return; }
            if (!ConfirmWorkspaceReplacement(out var discarded)) return;
            var document = StudioModelDocument.OpenFile(path);
            if (!document) throw new InvalidOperationException("Choose a MashBox Studio workspace in this project.");
            ReplaceWorkspace(document, loaded, discarded);
            NotifyFile(document.WorkspacePath, "loaded");
        }
        private void ReplaceWorkspace(StudioModelDocument document, Action<StudioModelDocument> loaded, StudioModelDocument discarded)
        {
            if (!discarded && Document && (!Document.HasStorage || IsTemporaryWorkspace)) discarded = Document;
            loaded(document);
            RememberRecentFile(document.WorkspacePath);
            ReleaseDiscardedWorkspace(discarded, document);
        }
        internal void DiscardWorkspaceForClose()
        {
            var discarded = Document;
            Load(null);
            ReleaseDiscardedWorkspace(discarded, null);
        }
        private static void ReleaseDiscardedWorkspace(StudioModelDocument discarded, StudioModelDocument retainedDocument)
        {
            // OpenFile reuses live documents. Reopening discarded work must read
            // the saved file rather than reusing abandoned edits in memory.
            if (!discarded || discarded == retainedDocument || EditorUtility.IsPersistent(discarded)) return;
            var retained = retainedDocument ? new HashSet<Mesh>(retainedDocument.nodes.Select(n => n.mesh).Where(m => m)) : new HashSet<Mesh>();
            foreach (var mesh in discarded.nodes.Select(n => n.mesh).Where(m => m && !EditorUtility.IsPersistent(m)).Distinct())
            {
                if (retained.Contains(mesh)) continue;
                Undo.ClearUndo(mesh); UnityEngine.Object.DestroyImmediate(mesh);
            }
            Undo.ClearUndo(discarded); UnityEngine.Object.DestroyImmediate(discarded);
        }

        private void SaveAs()
        {
            FinishLiveTool();
            if (!Document) return;
            string original = Document.WorkspacePath;
            Directory.CreateDirectory(StudioModelDocument.WorkspaceFolder);
            string path;
#if MASHBOX_STUDIO_TESTS
            if (WorkspaceSavePathChoice != null) path = WorkspaceSavePathChoice();
            else
#endif
            path = EditorUtility.SaveFilePanel("Save Workspace As", StudioModelDocument.WorkspaceFolder,
                Document.HasStorage && !IsTemporaryWorkspace ? Document.name + " Copy" : Document.name, StudioModelDocument.WorkspaceExtension);
            if (string.IsNullOrEmpty(path)) return;
            path = StudioModelDocument.ValidateWorkspacePath(path);
            if (string.Equals(path, TemporaryWorkspacePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a named workspace outside the reserved temporary location.");
            if (string.Equals(path,original,StringComparison.OrdinalIgnoreCase)) { Save(); return; }
            var copy = Document.SaveAs(path);
            ReplaceWorkspace(copy, WorkspaceSavedAs ?? Load, null);
            NotifyFile(copy.WorkspacePath, "saved");
        }

        // Keep selected subtrees once, plus skeleton dependencies needed by skinned meshes.
        internal void FilterExportSelection(Transform[] nodes)
        {
            var geometry = Document.WithDescendants(objects);
            var included = new HashSet<int>(geometry);
            void IncludeBone(int index)
            {
                for (int i=index;i>=0;i=Document.nodes[i].parent) included.Add(i);
            }
            foreach (int i in geometry)
            {
                var node = Document.nodes[i];
                if (!node.skinned) continue;
                foreach (int bone in node.bones) IncludeBone(bone);
                IncludeBone(node.rootBone);
            }
            // Detach first so destroying an unselected parent cannot destroy a selected child.
            foreach (int i in included)
                if (!included.Contains(Document.nodes[i].parent)) nodes[i].SetParent(null,true);
            for (int i=0;i<nodes.Length;i++)
            {
                if (!included.Contains(i)) { if (nodes[i]) UnityEngine.Object.DestroyImmediate(nodes[i].gameObject); continue; }
                if (geometry.Contains(i)) continue;
                var renderer = nodes[i].GetComponent<Renderer>();
                if (renderer) UnityEngine.Object.DestroyImmediate(renderer);
                var filter = nodes[i].GetComponent<MeshFilter>();
                if (filter) UnityEngine.Object.DestroyImmediate(filter);
            }
        }
    }
}

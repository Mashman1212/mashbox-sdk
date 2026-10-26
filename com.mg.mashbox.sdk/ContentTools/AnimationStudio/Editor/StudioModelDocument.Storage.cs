using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        internal const string WorkspaceExtension = "workspace";
        internal const string WorkspaceDirectory = "MashBoxStudio/Workspaces";
        [SerializeField, HideInInspector] private string workspaceFile;
        internal static string WorkspaceFolder => Path.Combine(Path.GetDirectoryName(Application.dataPath), WorkspaceDirectory);
        internal string WorkspacePath => string.IsNullOrEmpty(workspaceFile) ? AssetDatabase.GetAssetPath(this) : Path.Combine(Path.GetDirectoryName(Application.dataPath), workspaceFile);
        internal bool HasStorage => !string.IsNullOrEmpty(workspaceFile) || AssetDatabase.Contains(this);

        internal static string NewWorkspacePath(string name = "Untitled Workspace")
        {
            Directory.CreateDirectory(WorkspaceFolder);
            string path = Path.Combine(WorkspaceFolder, name + "." + WorkspaceExtension);
            for (int i = 1; File.Exists(path); i++) path = Path.Combine(WorkspaceFolder, name + " " + i + "." + WorkspaceExtension);
            return path;
        }
        internal static string ValidateWorkspacePath(string path)
        {
            string full = Path.GetFullPath(path);
            string folder = Path.GetFullPath(WorkspaceFolder) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(full), "." + WorkspaceExtension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Save the workspace in this project's MashBoxStudio/Workspaces folder with the .workspace extension.");
            return full;
        }
        internal void SetWorkspacePath(string path)
        {
            string full = ValidateWorkspacePath(path);
            workspaceFile = full.Substring(Path.GetDirectoryName(Application.dataPath).Length + 1).Replace('\\', '/');
            name = Path.GetFileNameWithoutExtension(full);
        }
        internal static StudioModelDocument OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            // Read old assets for conversion; new workspaces are never created in Assets.
            if (string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase))
            {
                var legacy = AssetDatabase.LoadAssetAtPath<StudioModelDocument>(path.Replace('\\','/').StartsWith("Assets/", StringComparison.Ordinal) ? path : FileUtil.GetProjectRelativePath(path));
                return legacy ? legacy.SaveAs(NewWorkspacePath(legacy.name)) : null;
            }
            path = ValidateWorkspacePath(path);
            if (!File.Exists(path)) return null;
            foreach (var open in Resources.FindObjectsOfTypeAll<StudioModelDocument>())
                if (!string.IsNullOrEmpty(open.workspaceFile) && string.Equals(open.WorkspacePath, path, StringComparison.OrdinalIgnoreCase)) return open;
            if (StudioWorkspaceText.IsTextWorkspace(path)) return StudioWorkspaceText.Read(path);
            var objects = InternalEditorUtility.LoadSerializedFileAndForget(path);
            var document = objects.OfType<StudioModelDocument>().SingleOrDefault();
            if (!document)
            {
                foreach (var value in objects) if (value && !EditorUtility.IsPersistent(value)) DestroyImmediate(value);
                throw new InvalidDataException("This file is not a MashBox Studio workspace.");
            }
            document.SetWorkspacePath(path);
            return document;
        }
        private void SaveWorkspaceFile()
        {
            string path = ValidateWorkspacePath(WorkspacePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                StudioWorkspaceText.Write(this, temporary);
                // Preserve the original native workspace when it is first upgraded.
                string backup = File.Exists(path) && !StudioWorkspaceText.IsTextWorkspace(path) && !File.Exists(path + ".legacy.bak") ? path + ".legacy.bak" : null;
                if (File.Exists(path)) File.Replace(temporary, path, backup);
                else File.Move(temporary, path);
                EditorUtility.ClearDirty(this);
                foreach (var mesh in nodes.Select(n => n.mesh).Where(m => m).Distinct()) EditorUtility.ClearDirty(mesh);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private void AttachOwnedMesh(Mesh mesh)
        {
            if (string.IsNullOrEmpty(workspaceFile) && AssetDatabase.Contains(this)) AssetDatabase.AddObjectToAsset(mesh, this);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioMeshWorkspaceStorage
    {
        internal static string Folder { get { Directory.CreateDirectory(StudioModelDocument.WorkspaceFolder); return StudioModelDocument.WorkspaceFolder; } }
        internal static string Absolute(string relative) => Path.Combine(Path.GetDirectoryName(Application.dataPath), relative);
        internal static string Choose(string title, string name) => StudioFileDialogs.SaveWorkspace(title, name);
        internal static string ChooseOpen(string title) => StudioFileDialogs.OpenWorkspace(title, new[] { "Studio workspace", "workspace", "Legacy Unity asset", "asset" });
        internal static string Save(ScriptableObject document, IEnumerable<Object> resources, string path)
        {
            path = StudioModelDocument.ValidateWorkspacePath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var objects = new[] { (Object)document }.Concat(resources.Where(o => o && !EditorUtility.IsPersistent(o))).Distinct().ToArray();
            var flags = objects.Select(o => o.hideFlags).ToArray();
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                foreach (var value in objects) value.hideFlags = HideFlags.None;
                InternalEditorUtility.SaveToSerializedFileAndForget(objects, temporary, true);
                // Verify the new file before replacing a previously saved workspace.
                var loaded = InternalEditorUtility.LoadSerializedFileAndForget(temporary);
                try
                {
                    if (!loaded.Any(o => o && o.GetType() == document.GetType()) || loaded.Length != objects.Length)
                        throw new InvalidDataException("Workspace validation failed; the previous file was preserved.");
                }
                finally { foreach (var value in loaded) if(value && !EditorUtility.IsPersistent(value)) Object.DestroyImmediate(value); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally
            {
                for (int i = 0; i < objects.Length; i++) if(objects[i]) objects[i].hideFlags = flags[i];
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            return path.Substring(Path.GetDirectoryName(Application.dataPath).Length + 1).Replace('\\', '/');
        }
        internal static T Open<T>(string path) where T : ScriptableObject
        {
            if (string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase))
                return AssetDatabase.LoadAssetAtPath<T>(FileUtil.GetProjectRelativePath(path));
            path = StudioModelDocument.ValidateWorkspacePath(path);
            var objects = InternalEditorUtility.LoadSerializedFileAndForget(path);
            var document = objects.OfType<T>().SingleOrDefault();
            if (!document)
            {
                foreach (var value in objects) if(value && !EditorUtility.IsPersistent(value)) Object.DestroyImmediate(value);
                throw new InvalidDataException("This workspace belongs to a different Studio. Open it in the Studio that saved it.");
            }
            foreach (var value in objects) if(value && !EditorUtility.IsPersistent(value)) value.hideFlags = HideFlags.HideAndDontSave;
            return document;
        }
    }
}

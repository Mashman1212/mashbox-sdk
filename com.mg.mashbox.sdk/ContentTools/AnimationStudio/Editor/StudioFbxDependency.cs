using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioFbxDependency
    {
        internal const string PackageName = "com.unity.formats.fbx";
        internal const string Message = "Saving or exporting FBX files requires Unity's FBX Exporter package (com.unity.formats.fbx).\n\nOpen Package Manager and install FBX Exporter from the Unity Registry, then try again. Your Studio edits are unchanged. Workspace, animation clip, prefab and OBJ saves do not require it.";

        // Check the loaded API, not a cached package flag: installation may still be compiling.
        internal static bool Available
        {
            get
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter"))
                    .FirstOrDefault(t => t != null);
                var options = type?.Assembly.GetType("UnityEditor.Formats.Fbx.Exporter.ExportModelOptions");
                return type?.GetMethod("ExportObject", new[] { typeof(string), typeof(UnityEngine.Object) }) != null &&
                    options != null && type.GetMethod("ExportObject", new[] { typeof(string), typeof(UnityEngine.Object), options }) != null &&
                    type.GetMethod("ExportObjects", new[] { typeof(string), typeof(UnityEngine.Object[]), options }) != null;
            }
        }

        internal static bool Require()
        {
            if (Available) return true;
            if (Application.isBatchMode) { EnsureAvailable(); return false; }
            if (EditorUtility.DisplayDialog("FBX Exporter required", Message, "Open Package Manager", "Cancel"))
                UnityEditor.PackageManager.UI.Window.Open(PackageName);
            return false;
        }

        internal static void EnsureAvailable()
        {
            if (!Available) throw new InvalidOperationException(Message);
        }
    }
}

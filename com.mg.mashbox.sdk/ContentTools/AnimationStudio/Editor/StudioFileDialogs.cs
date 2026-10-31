using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioFileDialogs
    {
        private const string WorkspaceKey="Studio.Files.WorkspaceFolder", AssetKey="Studio.Files.AssetFolder";
        private static string StartFolder(bool workspace)
        {
            string fallback=workspace?StudioModelDocument.WorkspaceFolder:Application.dataPath;
            Directory.CreateDirectory(fallback);
            string saved=StudioPreferenceStore.GetString(workspace?WorkspaceKey:AssetKey,"");
            return !string.IsNullOrEmpty(saved)&&Directory.Exists(saved)?saved:Path.GetFullPath(fallback);
        }
        private static string Choose(bool workspace,Func<string,string> show)
        {
            string path=show(StartFolder(workspace));
            if(!string.IsNullOrEmpty(path))
            {
                string absolute=Path.IsPathRooted(path)?path:Path.Combine(Path.GetDirectoryName(Application.dataPath),path);
                string folder=Path.GetDirectoryName(Path.GetFullPath(absolute));
                if(Directory.Exists(folder))StudioPreferenceStore.SetString(workspace?WorkspaceKey:AssetKey,folder);
            }
            return path;
        }
        internal static string OpenWorkspace(string title,string[] filters)=>Choose(true,folder=>EditorUtility.OpenFilePanelWithFilters(title,folder,filters));
        internal static string SaveWorkspace(string title,string name,string extension="workspace")=>Choose(true,folder=>EditorUtility.SaveFilePanel(title,folder,name,extension));
        internal static string OpenAsset(string title,string extensions)=>Choose(false,folder=>EditorUtility.OpenFilePanel(title,folder,extensions));
        internal static string SaveAsset(string title,string name,string extension)=>Choose(false,folder=>EditorUtility.SaveFilePanel(title,folder,name,extension));
        internal static string SaveAssetInProject(string title,string name,string extension,string message)=>Choose(false,folder=>{
            string relative=FileUtil.GetProjectRelativePath(folder);
            if(string.IsNullOrEmpty(relative)||!(relative=="Assets"||relative.StartsWith("Assets/",StringComparison.Ordinal)))relative="Assets";
            return EditorUtility.SaveFilePanelInProject(title,name,extension,message,relative);
        });
    }
}

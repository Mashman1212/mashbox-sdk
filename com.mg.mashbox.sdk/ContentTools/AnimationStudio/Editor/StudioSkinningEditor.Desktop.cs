using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private static bool IsDesktopModel(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".fbx" || extension == ".obj" || extension == ".blend" || extension == ".dae" || extension == ".3ds";
        }
        private void HandleModelDrop(Event evt)
        {
            if (DragAndDrop.objectReferences.Any(o => o is Material)) return;
            var models = DragAndDrop.objectReferences.OfType<GameObject>().ToArray();
            var paths = DragAndDrop.paths.Where(IsDesktopModel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (models.Length == 0 && paths.Length == 0) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                int firstImported = Document.parts.Count;
                foreach (var model in models) Run(() => Import(model));
                foreach (var path in paths)
                {
                    if (models.Any(m => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m)) && string.Equals(Path.GetFullPath(AssetDatabase.GetAssetPath(m)),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))) continue;
                    Run(() => ImportDesktopModel(path));
                }
                int importedSelection = Document.parts.FindIndex(firstImported, part => !part.referenceOnly);
                if (importedSelection >= 0)
                {
                    CancelBrushInteraction();
                    ChooseBrush(Brush.Object);
                    Document.selected = importedSelection;
                    cachedPart = -1;
                    objectScale = false;
                    poseTranslate = true;
                    poseGlobalHandleRotation = Quaternion.identity;
                    RefreshSurface();
                    repaint();
                }
            }
            evt.Use();
        }
        private void ImportDesktopModel(string source)
        {
            string full = Path.GetFullPath(source);
            if (!File.Exists(full)) throw new FileNotFoundException("Dropped model was not found.", full);
            string projectPath = FileUtil.GetProjectRelativePath(full).Replace('\\','/');
            var existing = string.IsNullOrEmpty(projectPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(projectPath);
            if (existing) { Import(existing); return; }
            const string folder = "Assets/MashBox Studio Imports";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets","MashBox Studio Imports");
            string destination = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileName(full));
            File.Copy(full,Path.GetFullPath(destination),false);
            AssetDatabase.ImportAsset(destination,ImportAssetOptions.ForceSynchronousImport);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(destination);
            if (!model)
                throw new InvalidOperationException("Unity could not import " + Path.GetFileName(full) +
                    (Path.GetExtension(full).Equals(".blend",StringComparison.OrdinalIgnoreCase)
                    ? ". Blender must be installed and discoverable by Unity; alternatively export FBX from Blender."
                    : ". Check the Unity import errors. The original file has not been changed."));
            Import(model); error = null;
        }
    }
}

using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [UnityEditor.Callbacks.OnOpenAsset(1)]
        static bool OpenModelAsset(int instanceId, int line)
        {
            var document = EditorUtility.InstanceIDToObject(instanceId) as StudioModelDocument;
            if (!document) return false;
            OpenModelDocument(document);
            return true;
        }
        /// <summary>Exports all editable workspace meshes through Studio's FBX exporter.</summary>
        public static void ExportModelDocument(StudioModelDocument document, string path)
        {
            if (!document) throw new System.ArgumentNullException(nameof(document));
            using (var editor = new StudioModelEditor(() => { }))
            {
                editor.Load(document);
                editor.ExportFile(System.IO.Path.GetFullPath(path), false);
            }
        }
        public static void OpenModelDocument(StudioModelDocument document)
        {
            if(!document) return;
            var window=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().FirstOrDefault();
            if(!window) window=CreateInstance<AnimationStudioWindow>();
            window.modelingMode=true;
            window.OpenViewport();
            window.LoadStudioModel(document);
            window.BindStudioMode();
            window.viewport.Show();
            window.viewport.Focus();
        }
    }
}

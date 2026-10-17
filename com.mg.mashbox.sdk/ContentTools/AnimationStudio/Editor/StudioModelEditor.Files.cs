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
        internal bool CanExportSelection => Document && objects.Any(i => i >= 0 && i < Document.nodes.Count);

        private void OpenWorkspace(Action<StudioModelDocument> loaded)
        {
            string path = EditorUtility.OpenFilePanel("Open MashBox Studio workspace", Application.dataPath, "asset");
            if (string.IsNullOrEmpty(path)) return;
            var document = AssetDatabase.LoadAssetAtPath<StudioModelDocument>(FileUtil.GetProjectRelativePath(path));
            if (!document) throw new InvalidOperationException("Choose a MashBox Studio workspace .asset in this project.");
            loaded(document);
        }

        private void SaveAs()
        {
            if (!Document) return;
            string original = AssetDatabase.GetAssetPath(Document);
            string directory = string.IsNullOrEmpty(original) ? "Assets" : Path.GetDirectoryName(original).Replace('\\','/');
            string path = EditorUtility.SaveFilePanelInProject("Save Workspace As", Document.name + " Copy", "asset", "Save an independent editable MashBox Studio workspace.", directory);
            if (string.IsNullOrEmpty(path)) return;
            if (string.Equals(path,original,StringComparison.OrdinalIgnoreCase)) { Save(); return; }
            var copy = Document.SaveAs(path);
            if (WorkspaceSavedAs != null) WorkspaceSavedAs(copy); else Load(copy);
            EditorGUIUtility.PingObject(copy);
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

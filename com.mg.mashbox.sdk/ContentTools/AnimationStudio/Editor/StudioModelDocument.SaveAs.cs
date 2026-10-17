using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        public StudioModelDocument SaveAs(string path)
        {
            path = path.Replace('\\','/');
            if (!path.StartsWith("Assets/",StringComparison.Ordinal) || Path.GetExtension(path) != ".asset" ||
                !AssetDatabase.IsValidFolder(Path.GetDirectoryName(path).Replace('\\','/')))
                throw new ArgumentException("Save the workspace as an .asset inside this project's Assets folder.");
            if (File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path))
                throw new InvalidOperationException("A file already exists at that path. Choose a new workspace name.");
            var copy = Instantiate(this);
            copy.name = Path.GetFileNameWithoutExtension(path);
            var meshes = new Dictionary<Mesh,Mesh>();
            try
            {
                // Copy current in-memory edits, including meshes that have never been saved.
                foreach (var node in copy.nodes)
                {
                    if (!node.mesh) continue;
                    if (!meshes.TryGetValue(node.mesh,out var mesh))
                    {
                        mesh = Instantiate(node.mesh); mesh.name = node.mesh.name;
                        meshes.Add(node.mesh,mesh);
                    }
                    node.mesh = mesh;
                }
                AssetDatabase.CreateAsset(copy,path);
                copy.Save();
                return copy;
            }
            catch
            {
                if (AssetDatabase.Contains(copy)) AssetDatabase.DeleteAsset(path);
                foreach (var mesh in meshes.Values) if (mesh && !AssetDatabase.Contains(mesh)) DestroyImmediate(mesh);
                if (copy && !AssetDatabase.Contains(copy)) DestroyImmediate(copy);
                throw;
            }
        }
    }
}

using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Display only: renderer names still identify roles and original skin bindings.
    internal static class StudioImportedMeshLabel
    {
        internal static string Get(string rendererName, Mesh sourceMesh)
        {
            if (!IsGeneric(rendererName) || !sourceMesh) return rendererName;
            var path = AssetDatabase.GetAssetPath(sourceMesh);
            if (string.IsNullOrEmpty(path)) return rendererName;
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!root) return rendererName;
            // Include the part for multi-mesh assets so their rows remain distinguishable.
            int count = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var skin = renderer as SkinnedMeshRenderer;
                var filter = renderer.GetComponent<MeshFilter>();
                if (skin ? skin.sharedMesh : filter ? filter.sharedMesh : null) count++;
            }
            return count > 1 ? root.name + " / " + rendererName : root.name;
        }

        private static bool IsGeneric(string name) =>
            string.Equals(name, "Clothing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Body", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Headgear", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Bust", StringComparison.OrdinalIgnoreCase);
    }
}

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
    // Only exceptional in-memory materials/textures need Unity resource serialization.
    // Editable geometry always uses the compact numeric JSON tables.
    internal static partial class StudioWorkspaceText
    {
        [Serializable] private sealed class MaterialReference
        {
            public string guid, path, global, name;
            public long local;
            public int embedded = -1;
        }
        private static MaterialReference[] CaptureMaterials(List<Material> materials, string path, out string resources)
        {
            var inline = new List<Material>();
            var result = materials.Select(material => {
                if (!EditorUtility.IsPersistent(material))
                { int index = inline.Count; inline.Add(material); return new MaterialReference { embedded = index }; }
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long local);
                return new MaterialReference { guid = guid, local = local, path = AssetDatabase.GetAssetPath(material),
                    global = GlobalObjectId.GetGlobalObjectIdSlow(material).ToString(), name = material.name };
            }).ToArray();
            resources = null;
            if (inline.Count == 0) return result;
            var holder = ScriptableObject.CreateInstance<StudioWorkspaceResources>(); holder.materials = inline.ToArray();
            string temporary = Path.Combine(Path.GetTempPath(), "mbx-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                // CollectDependencies does not reliably include textures assigned to
                // transient materials, so explicitly make those serialization roots.
                var textures = inline.SelectMany(m => m.GetTexturePropertyNames().Select(p => m.GetTexture(p))).Where(t => t).Cast<Object>();
                var roots = new Object[] { holder }.Concat(inline.Cast<Object>()).Concat(textures).Distinct().ToArray();
                var objects = EditorUtility.CollectDependencies(roots).Concat(roots).Where(o => o && !EditorUtility.IsPersistent(o)).Distinct().ToArray();
                InternalEditorUtility.SaveToSerializedFileAndForget(objects, temporary, true);
                resources = File.ReadAllText(temporary);
            }
            finally { Object.DestroyImmediate(holder); if (File.Exists(temporary)) File.Delete(temporary); }
            return result;
        }
        private static Material[] RestoreMaterials(MaterialReference[] references, string resources, string path, List<Object> owned)
        {
            Material[] inline = Array.Empty<Material>();
            if (!string.IsNullOrEmpty(resources))
            {
                string temporary = Path.Combine(Path.GetTempPath(), "mbx-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    File.WriteAllText(temporary, resources);
                    var objects = InternalEditorUtility.LoadSerializedFileAndForget(temporary);
                    owned.AddRange(objects.Where(o => o && !EditorUtility.IsPersistent(o)));
                    var holder = objects.OfType<StudioWorkspaceResources>().SingleOrDefault();
                    if (!holder || holder.materials == null) throw new InvalidDataException("Invalid embedded workspace resources.");
                    inline = holder.materials; owned.Remove(holder); Object.DestroyImmediate(holder);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return references.Select(reference => {
                if (reference == null) throw new InvalidDataException("Invalid workspace material reference.");
                if (reference.embedded >= 0)
                {
                    if (reference.embedded >= inline.Length || !inline[reference.embedded]) throw new InvalidDataException("Missing embedded material.");
                    return inline[reference.embedded];
                }
                Material material = null;
                if (!string.IsNullOrEmpty(reference.global) && GlobalObjectId.TryParse(reference.global, out var global)) material = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(global) as Material;
                string assetPath = string.IsNullOrEmpty(reference.guid) ? reference.path : AssetDatabase.GUIDToAssetPath(reference.guid);
                if (!material && !string.IsNullOrEmpty(assetPath))
                    material = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Material>().FirstOrDefault(m =>
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out string _, out long id) && id == reference.local);
                if (!material && reference.path == "Resources/unity_builtin_extra") material = AssetDatabase.GetBuiltinExtraResource<Material>(reference.name + ".mat");
                if (!material) throw new InvalidDataException("Cannot resolve workspace material: " + reference.name + " (" + reference.path + ").");
                return material;
            }).ToArray();
        }
    }
}

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    // Import callbacks can run while Unity is still replacing asset objects. Coalesce
    // them and release render resources on the next editor update instead.
    sealed class MGTerrainPrototypeRefresh : AssetPostprocessor
    {
        static readonly HashSet<string> ChangedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool s_HasDeletedAssets;
        static readonly string[] SourceFields = { "m_Prefab", "m_Mesh", "m_Material", "m_TreeLod1Prefab", "m_TreeLod2Prefab" };

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            ChangedPaths.UnionWith(imported);
            ChangedPaths.UnionWith(deleted);
            ChangedPaths.UnionWith(moved);
            ChangedPaths.UnionWith(movedFrom);
            s_HasDeletedAssets |= deleted.Length > 0;
            EditorApplication.delayCall -= RefreshChanged;
            EditorApplication.delayCall += RefreshChanged;
        }

        static void RefreshChanged()
        {
            if (ChangedPaths.Count == 0) return;
            var changed = new HashSet<string>(ChangedPaths, StringComparer.OrdinalIgnoreCase);
            bool deleted = s_HasDeletedAssets;
            ChangedPaths.Clear();
            s_HasDeletedAssets = false;
            var affectedSources = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            bool refreshed = false;
            foreach (var terrain in Resources.FindObjectsOfTypeAll<MGTerrain>())
            {
                if (EditorUtility.IsPersistent(terrain) || !terrain.gameObject.scene.IsValid()) continue;
                // Deleted references can already compare equal to null and no longer
                // appear in GetDependencies. Conservatively refresh for deletions.
                if (terrain.Prototypes.Count == 0 || (!deleted && !UsesChangedSource(terrain, changed, affectedSources))) continue;
                terrain.RefreshPrototypeAssets();
                refreshed = true;
            }
            if (refreshed) Repaint();
        }

        static bool UsesChangedSource(MGTerrain terrain, HashSet<string> changed, Dictionary<string, bool> affectedSources)
        {
            using (var serialized = new SerializedObject(terrain))
            {
                var prototypes = serialized.FindProperty("m_Prototypes");
                for (int i = 0; i < prototypes.arraySize; i++)
                    foreach (string field in SourceFields)
                    {
                        var source = prototypes.GetArrayElementAtIndex(i).FindPropertyRelative(field).objectReferenceValue;
                        string path = AssetDatabase.GetAssetPath(source);
                        if (string.IsNullOrEmpty(path)) continue;
                        if (!affectedSources.TryGetValue(path, out bool affected))
                        {
                            // Recursive dependencies include nested prefabs, variants,
                            // imported models, materials and their source assets.
                            affected = changed.Contains(path);
                            if (!affected)
                                foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                                    if (changed.Contains(dependency)) { affected = true; break; }
                            affectedSources.Add(path, affected);
                        }
                        if (affected) return true;
                    }
            }
            return false;
        }

        [MenuItem("Tools/MashBox/MG Terrain/Refresh Foliage Prefabs")]
        internal static void RefreshAllLoaded()
        {
            foreach (var terrain in Resources.FindObjectsOfTypeAll<MGTerrain>())
                if (!EditorUtility.IsPersistent(terrain) && terrain.gameObject.scene.IsValid())
                    terrain.RefreshPrototypeAssets();
            Repaint();
        }

        static void Repaint()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }
}
#endif

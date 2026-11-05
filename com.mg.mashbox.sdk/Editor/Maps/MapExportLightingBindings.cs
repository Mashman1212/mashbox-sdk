#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    // Editor-only: keep Unity's native lighting data (including probes and mixed lights).
    // A texture/index component would lose parts of that data and require new player code.
    internal sealed class MapExportLightingBindings
    {
        private static readonly string[] BindingArrays =
        {
            "m_LightmappedRendererDataIDs", "m_EnlightenSceneMappingRendererIDs",
            "m_Lights", "m_BakedReflectionProbes"
        };

        private sealed class Binding
        {
            public string Path;
            public Object Target;
        }

        private sealed class BakeryReference
        {
            public Component Storage;
            public string Path;
            public Object Target;
        }

        private readonly LightingDataAsset data;
        private readonly List<Binding> bindings = new List<Binding>();
        private readonly List<BakeryReference> bakeryReferences = new List<BakeryReference>();

        private MapExportLightingBindings(LightingDataAsset lightingData) { data = lightingData; }

        internal static LightingDataAsset GetLightingData(Scene scene)
        {
#if UNITY_6000_0_OR_NEWER
            return Lightmapping.GetLightingDataAssetForScene(scene);
#else
            if (SceneManager.GetActiveScene() != scene)
                throw new InvalidOperationException("The map scene must be active while preserving baked lighting.");
            return Lightmapping.lightingDataAsset;
#endif
        }

        private static SerializedObject OpenData(LightingDataAsset asset)
        {
            var serialized = new SerializedObject(asset);
            // These native binding tables are hidden in normal inspector mode in Unity 2022.
            var mode = typeof(SerializedObject).GetProperty("inspectorMode", BindingFlags.Instance | BindingFlags.NonPublic);
            if (mode == null)
            {
                serialized.Dispose();
                throw new InvalidOperationException("This Unity version cannot inspect baked lighting bindings; map export was stopped.");
            }
            mode.SetValue(serialized, InspectorMode.Debug);
            return serialized;
        }

        private static string Key(ulong target, ulong prefab) => target + ":" + prefab;

        private static IEnumerable<Component> Components(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Component>(true)).Where(component => component != null);

        private static Dictionary<string, Object> Targets(Scene scene)
        {
            var targets = new Dictionary<string, Object>();
            foreach (var component in Components(scene))
            {
                var id = GlobalObjectId.GetGlobalObjectIdSlow(component);
                if (id.targetObjectId != 0)
                    targets.Add(Key(id.targetObjectId, id.targetPrefabId), component);
            }
            return targets;
        }

        internal static MapExportLightingBindings Capture(Scene scene, bool recoverUnpacked = false)
        {
            var state = new MapExportLightingBindings(GetLightingData(scene));
            if (state.data != null)
            {
                var targets = Targets(scene);
                using (var serialized = OpenData(state.data))
                {
                    foreach (var name in BindingArrays)
                    {
                        var array = serialized.FindProperty(name);
                        if (array == null || !array.isArray)
                            throw new InvalidOperationException("Unsupported baked lighting table: " + name);
                        for (var i = 0; i < array.arraySize; i++)
                        {
                            var entry = array.GetArrayElementAtIndex(i);
                            var targetProperty = entry.FindPropertyRelative("targetObject");
                            var prefabProperty = entry.FindPropertyRelative("targetPrefab");
                            if (targetProperty == null || prefabProperty == null)
                                throw new InvalidOperationException("Unsupported baked lighting identifier: " + entry.propertyPath);
                            var target = unchecked((ulong)targetProperty.longValue);
                            var prefab = unchecked((ulong)prefabProperty.longValue);
                            if (target == 0 && prefab == 0) continue;
                            if (!targets.TryGetValue(Key(target, prefab), out var component) && recoverUnpacked && prefab != 0)
                            {
                                // Unity's unpacked scene file ID is the source file ID XOR the
                                // prefab instance ID, with the sign bit cleared. Accept only an
                                // existing component in this exact scene, never a name match.
                                targets.TryGetValue(Key((target ^ prefab) & 0x7fffffffffffffffUL, 0), out component);
                            }
                            if (component == null || !MatchesTable(name, component))
                                throw new InvalidOperationException($"Baked lighting binding '{name}[{i}]' cannot be resolved in '{scene.path}'. Re-export from the original baked scene.");
                            state.bindings.Add(new Binding { Path = entry.propertyPath, Target = component });
                        }
                    }
                }
            }

            // Bakery owns its additional directional, vertex and probe data. Preserve its
            // native storage and object references instead of replacing it with Unity-only data.
            foreach (var component in Components(scene).Where(component =>
                component.GetType().Name == "ftLightmapsStorage" || component.GetType().Name == "BakeryLightmappedPrefab" ||
                component.GetType().Name == "BakeryPrefabLightmapData"))
            {
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var target = property.objectReferenceValue;
                        if (target is Renderer || target is Terrain || target is Texture || target is Mesh)
                            state.bakeryReferences.Add(new BakeryReference { Storage = component, Path = property.propertyPath, Target = target });
                    }
                }
            }
            return state;
        }

        private static bool MatchesTable(string table, Object target)
        {
            if (table == "m_Lights") return target is Light;
            if (table == "m_BakedReflectionProbes") return target is ReflectionProbe;
            return target is Renderer || target is Terrain;
        }

        internal int Rebind(Scene scene)
        {
            var changed = 0;
            if (data != null)
            {
                if (GetLightingData(scene) != data)
                    throw new InvalidOperationException("The map's Lighting Data Asset changed during export.");
                using (var serialized = OpenData(data))
                {
                    foreach (var binding in bindings)
                    {
                        if (!(binding.Target is Component component) || component == null || component.gameObject.scene != scene)
                            throw new InvalidOperationException("A baked scene object was removed during export: " + binding.Path);
                        var id = GlobalObjectId.GetGlobalObjectIdSlow(component);
                        if (id.targetObjectId == 0)
                            throw new InvalidOperationException("Save the temporary scene before rebinding baked lighting.");
                        var entry = serialized.FindProperty(binding.Path);
                        var target = entry.FindPropertyRelative("targetObject");
                        var prefab = entry.FindPropertyRelative("targetPrefab");
                        if (unchecked((ulong)target.longValue) == id.targetObjectId && unchecked((ulong)prefab.longValue) == id.targetPrefabId) continue;
                        target.longValue = unchecked((long)id.targetObjectId);
                        prefab.longValue = unchecked((long)id.targetPrefabId);
                        changed++;
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                if (changed > 0)
                {
                    EditorUtility.SetDirty(data);
                    AssetDatabase.SaveAssetIfDirty(data);
                }
            }
            foreach (var reference in bakeryReferences)
            {
                if (reference.Storage == null || reference.Target == null)
                    throw new InvalidOperationException("A Bakery lighting reference was removed during export.");
                using (var serialized = new SerializedObject(reference.Storage))
                {
                    var property = serialized.FindProperty(reference.Path);
                    if (property == null || property.objectReferenceValue != reference.Target)
                        throw new InvalidOperationException("A Bakery lighting reference changed during export: " + reference.Path);
                }
            }
            return changed;
        }

        internal static int RepairImportedScene(Scene scene)
        {
            // Only old cooker exports with a private copied LightingData asset are eligible.
            // Never rewrite a creator's shared/source bake as an import side effect.
            const string root = "Assets/__MashBoxMapExportTemp/";
            if (!scene.path.StartsWith(root, StringComparison.Ordinal)) return 0;
            var asset = GetLightingData(scene);
            if (asset == null) return 0;
            var directory = scene.path.Substring(0, scene.path.LastIndexOf('/') + 1);
            if (!AssetDatabase.GetAssetPath(asset).StartsWith(directory, StringComparison.Ordinal))
                throw new InvalidOperationException("The exported map does not own its Lighting Data Asset; re-export it before cooking.");
            var state = Capture(scene, recoverUnpacked: true);
            var count = state.Rebind(scene);
            if (count > 0) Debug.Log($"[Cooker] Repaired {count} baked lighting object bindings in '{scene.path}'.");
            return count;
        }
    }
}
#endif

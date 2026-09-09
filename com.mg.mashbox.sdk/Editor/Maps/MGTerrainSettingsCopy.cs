#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        MGTerrain m_SettingsCopySource;
        bool m_ShowSettingsCopy = true;
        string m_SettingsCopyResult;
        MessageType m_SettingsCopyResultType;
        void DrawSettingsCopy(MGTerrain terrain)
        {
            m_ShowSettingsCopy = EditorGUILayout.Foldout(m_ShowSettingsCopy, "Copy Settings From Another Terrain", true);
            if (!m_ShowSettingsCopy) return;
            m_SettingsCopySource = (MGTerrain)EditorGUILayout.ObjectField("Source MG Terrain", m_SettingsCopySource, typeof(MGTerrain), true);
            EditorGUILayout.HelpBox("Copies MG Terrain rendering/distance settings, prototype setup, detail-layer settings and foliage palettes. Matching layers keep this terrain's painted density/size maps. Missing layers are added unpainted; destination-only layers and placed instances stay. Surface mesh, transforms, colliders, control maps, materials and baked height/appearance maps are preserved. Shared detail prefab/material/palette assets are referenced, not duplicated.", MessageType.Info);
            EditorGUILayout.LabelField("Destination Setup", DescribeSetup(terrain));
            if (m_SettingsCopySource != null)
                EditorGUILayout.LabelField("Source Setup", DescribeSetup(m_SettingsCopySource));
            if (!string.IsNullOrEmpty(m_SettingsCopyResult))
                EditorGUILayout.HelpBox(m_SettingsCopyResult, m_SettingsCopyResultType);
            using (new EditorGUI.DisabledScope(Application.isPlaying || m_SettingsCopySource == null || m_SettingsCopySource == terrain))
                if (GUILayout.Button("Copy Settings and Detail Setup"))
                {
                    FinishDetailStroke();
                    serializedObject.ApplyModifiedProperties();
                    try
                    {
                        string result = MGTerrainSettingsCopy.Copy(m_SettingsCopySource, terrain);
                        m_SettingsCopyResult = result;
                        m_SettingsCopyResultType = MessageType.Info;
                        Debug.Log(result, terrain);
                    }
                    catch (Exception exception)
                    {
                        m_SettingsCopyResult = exception.Message;
                        m_SettingsCopyResultType = MessageType.Error;
                        Debug.LogException(exception, terrain);
                    }
                    serializedObject.Update();
                    Repaint();
                    GUIUtility.ExitGUI();
                }
        }

        static string DescribeSetup(MGTerrain terrain)
        {
            using var data = new SerializedObject(terrain);
            return $"{data.FindProperty("m_Prototypes").arraySize} prototypes, {data.FindProperty("m_DensityDetailLayers").arraySize} detail layers, {data.FindProperty("m_DetailFoliagePalettes").arraySize} palettes";
        }
    }

    internal static class MGTerrainSettingsCopy
    {
        // These scalar values describe the destination's geometry or serialization state, not setup.
        static readonly HashSet<string> LocalScalars = new HashSet<string>
        { "m_SurfaceGridWidth", "m_SurfaceGridHeight", "m_ColliderSourceVertexCount", "m_HoleVertexCount", "m_DetailSettingsVersion" };
        static readonly HashSet<string> LayerData = new HashSet<string>
        { "m_PrototypeIndex", "m_DensityMap", "m_SizeMap", "m_PaletteSourceMap", "m_RepresentedInstanceCount" };

        internal static string Copy(MGTerrain source, MGTerrain destination)
        {
            if (source == null || destination == null || source == destination) throw new ArgumentException("Choose a different source MG Terrain.");
            if (Application.isPlaying) throw new InvalidOperationException("Copy terrain settings outside Play Mode.");
            using var from = new SerializedObject(source);
            using var to = new SerializedObject(destination);
            from.Update(); to.Update();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Copy MG Terrain Settings");
            Undo.RegisterCompleteObjectUndo(destination, "Copy MG Terrain Settings");
            int addedPrototypes = 0, addedLayers = 0, updatedLayers = 0;
            try
            {
                // Reflect only explicitly serialized fields on MGTerrain: never copy component ownership,
                // object references, arrays, mesh dimensions, or hidden geometry/collider bookkeeping.
                foreach (var field in typeof(MGTerrain).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.GetCustomAttribute<SerializeField>() == null || LocalScalars.Contains(field.Name)) continue;
                    Type type = field.FieldType;
                    if (!type.IsPrimitive && !type.IsEnum) continue;
                    var a = from.FindProperty(field.Name); var b = to.FindProperty(field.Name);
                    if (a != null && b != null) CopyValue(a, b);
                }

                var sourcePrototypes = from.FindProperty("m_Prototypes");
                var destinationPrototypes = to.FindProperty("m_Prototypes");
                var prototypeMap = new Dictionary<int, int>();
                var usedPrototypes = new HashSet<int>();
                for (int i = 0; i < sourcePrototypes.arraySize; i++)
                {
                    var a = sourcePrototypes.GetArrayElementAtIndex(i);
                    int match = Find(destinationPrototypes, usedPrototypes, b => SamePrototype(a, b));
                    if (match < 0) { match = destinationPrototypes.arraySize++; addedPrototypes++; }
                    CopyValue(a, destinationPrototypes.GetArrayElementAtIndex(match));
                    usedPrototypes.Add(match); prototypeMap.Add(i, match);
                }

                var palettes = to.FindProperty("m_DetailFoliagePalettes");
                var sourcePalettes = from.FindProperty("m_DetailFoliagePalettes");
                var usedPalettes = new HashSet<int>();
                for (int i = 0; i < sourcePalettes.arraySize; i++)
                {
                    var a = sourcePalettes.GetArrayElementAtIndex(i);
                    int match = Find(palettes, usedPalettes, b => Object(a, "m_Palette") == Object(b, "m_Palette"));
                    bool added = match < 0;
                    if (added) match = palettes.arraySize++;
                    var b = palettes.GetArrayElementAtIndex(match);
                    var localMap = added ? null : Object(b, "m_SourceDensityMap");
                    CopyValue(a, b);
                    b.FindPropertyRelative("m_SourceDensityMap").objectReferenceValue = localMap;
                    b.FindPropertyRelative("m_NeedsBake").boolValue = true;
                    usedPalettes.Add(match);
                }

                var sourceLayers = from.FindProperty("m_DensityDetailLayers");
                var layers = to.FindProperty("m_DensityDetailLayers");
                var usedLayers = new HashSet<int>();
                for (int i = 0; i < sourceLayers.arraySize; i++)
                {
                    var a = sourceLayers.GetArrayElementAtIndex(i);
                    if (!prototypeMap.TryGetValue(a.FindPropertyRelative("m_PrototypeIndex").intValue, out int prototype))
                        throw new InvalidOperationException("A source detail layer has an invalid prototype. Fix its prototype before copying.");
                    int match = Find(layers, usedLayers, b =>
                        b.FindPropertyRelative("m_PrototypeIndex").intValue == prototype
                        && Object(a, "m_GeneratedByPalette") == Object(b, "m_GeneratedByPalette")
                        && a.FindPropertyRelative("m_PaletteEntryIndex").intValue == b.FindPropertyRelative("m_PaletteEntryIndex").intValue
                        && a.FindPropertyRelative("m_PaletteSourceOnly").boolValue == b.FindPropertyRelative("m_PaletteSourceOnly").boolValue);
                    bool added = match < 0;
                    if (added) { match = layers.arraySize++; addedLayers++; } else updatedLayers++;
                    var b = layers.GetArrayElementAtIndex(match);
                    if (added)
                    {
                        foreach (string name in new[] { "m_DensityMap", "m_SizeMap", "m_PaletteSourceMap" }) b.FindPropertyRelative(name).objectReferenceValue = null;
                        b.FindPropertyRelative("m_RepresentedInstanceCount").longValue = 0;
                    }
                    CopyChildren(a, b, LayerData);
                    b.FindPropertyRelative("m_PrototypeIndex").intValue = prototype;
                    if (added && Object(b, "m_GeneratedByPalette") != null)
                    {
                        int binding = Find(palettes, new HashSet<int>(), p => Object(p, "m_Palette") == Object(b, "m_GeneratedByPalette"));
                        if (binding >= 0) b.FindPropertyRelative("m_PaletteSourceMap").objectReferenceValue = Object(palettes.GetArrayElementAtIndex(binding), "m_SourceDensityMap");
                    }
                    usedLayers.Add(match);
                }

                to.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(destination);
                EditorUtility.SetDirty(destination);
                if (destination.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(destination.gameObject.scene);
                destination.InvalidateRenderCache();
                destination.NotifySurfaceMeshChanged();
                SceneView.RepaintAll();
                Undo.CollapseUndoOperations(undoGroup);
                return $"Copied MG Terrain settings from '{source.name}' to '{destination.name}': {updatedLayers} detail layers updated, {addedLayers} new unpainted layers, {addedPrototypes} new prototypes. Destination geometry, maps and placed instances preserved.";
            }
            catch
            {
                to.Update();
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        static int Find(SerializedProperty list, HashSet<int> used, Func<SerializedProperty, bool> matches)
        {
            for (int i = 0; i < list.arraySize; i++) if (!used.Contains(i) && matches(list.GetArrayElementAtIndex(i))) return i;
            return -1;
        }
        static UnityEngine.Object Object(SerializedProperty p, string name) => p.FindPropertyRelative(name).objectReferenceValue;
        static bool SamePrototype(SerializedProperty a, SerializedProperty b)
        {
            if (a.FindPropertyRelative("m_Kind").enumValueIndex != b.FindPropertyRelative("m_Kind").enumValueIndex) return false;
            var prefab = Object(a, "m_Prefab");
            if (prefab != null) return prefab == Object(b, "m_Prefab");
            return Object(b, "m_Prefab") == null && Object(a, "m_Mesh") == Object(b, "m_Mesh") && Object(a, "m_Material") == Object(b, "m_Material");
        }
        static void CopyChildren(SerializedProperty source, SerializedProperty destination, HashSet<string> skip = null)
        {
            var child = source.Copy();
            if (!child.Next(true)) return;
            while (child.depth == source.depth + 1)
            {
                if (skip == null || !skip.Contains(child.name)) CopyValue(child, destination.FindPropertyRelative(child.name));
                if (!child.Next(false)) break;
            }
        }
        static void CopyValue(SerializedProperty a, SerializedProperty b)
        {
            if (b == null) throw new InvalidOperationException("Missing destination setting: " + a.propertyPath);
            switch (a.propertyType)
            {
                case SerializedPropertyType.Generic:
                    if (a.isArray)
                    {
                        b.arraySize = a.arraySize;
                        for (int i = 0; i < a.arraySize; i++) CopyValue(a.GetArrayElementAtIndex(i), b.GetArrayElementAtIndex(i));
                    }
                    else CopyChildren(a, b);
                    break;
                case SerializedPropertyType.Integer: b.longValue = a.longValue; break;
                case SerializedPropertyType.Boolean: b.boolValue = a.boolValue; break;
                case SerializedPropertyType.Float: b.doubleValue = a.doubleValue; break;
                case SerializedPropertyType.Enum: b.intValue = a.intValue; break;
                case SerializedPropertyType.ObjectReference: b.objectReferenceValue = a.objectReferenceValue; break;
                case SerializedPropertyType.Color: b.colorValue = a.colorValue; break;
                case SerializedPropertyType.Vector2: b.vector2Value = a.vector2Value; break;
                case SerializedPropertyType.Vector3: b.vector3Value = a.vector3Value; break;
                case SerializedPropertyType.Vector4: b.vector4Value = a.vector4Value; break;
                case SerializedPropertyType.String: b.stringValue = a.stringValue; break;
                default: throw new InvalidOperationException("Unsupported setting type: " + a.propertyPath);
            }
        }
    }
}
#endif

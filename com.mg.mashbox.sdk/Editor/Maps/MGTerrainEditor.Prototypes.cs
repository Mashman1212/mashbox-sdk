#if UNITY_EDITOR
using System;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        const string AdvancedFoldoutPreference = "MashBoxSDK.MGTerrainEditor.ShowAdvanced";
        // This is a user preference, not scene data. Editor instances are recreated
        // on selection and Play mode changes, so an instance field loses the choice.
        bool m_ShowAdvanced
        {
            get => EditorPrefs.GetBool(AdvancedFoldoutPreference, false);
            set
            {
                if (value != m_ShowAdvanced)
                    EditorPrefs.SetBool(AdvancedFoldoutPreference, value);
            }
        }
        bool m_ShowPrototypeAdvanced;
        int m_SelectedPrototype;

        static void DrawPrototypeFields(SerializedProperty parent, params string[] hidden)
        {
            var field = parent.Copy();
            var end = parent.GetEndProperty();
            bool enter = true;
            while (field.NextVisible(enter) && !SerializedProperty.EqualContents(field, end))
            {
                enter = false;
                if (Array.IndexOf(hidden, field.name) < 0) EditorGUILayout.PropertyField(field, true);
            }
        }

        void SelectPrototype(int index)
        {
            FinishDetailStroke();
            m_SelectedPrototype = index;
            for (int i = 0; i < m_DensityDetailLayers.arraySize; i++)
                if (m_DensityDetailLayers.GetArrayElementAtIndex(i).FindPropertyRelative("m_PrototypeIndex").intValue == index)
                { m_PaintDetailIndex = i; return; }
            SetDetailPainting(false);
        }

        void AddEmptyDensityLayer(int prototype)
        {
            int index = m_DensityDetailLayers.arraySize++;
            var layer = m_DensityDetailLayers.GetArrayElementAtIndex(index);
            ResetDetailPainting(layer);
            layer.FindPropertyRelative("m_PrototypeIndex").intValue = prototype;
            foreach (string field in new[] { "m_MinWidth", "m_MaxWidth", "m_MinHeight", "m_MaxHeight", "m_SizeMultiplier", "m_WidthMultiplier", "m_HeightMultiplier", "m_WindMultiplier" })
                layer.FindPropertyRelative(field).floatValue = 1f;
            layer.FindPropertyRelative("m_ShaderTint").colorValue = Color.white;
            layer.FindPropertyRelative("m_FarBakeColor").colorValue = new Color(.32f, .4f, .12f, 1f);
            layer.FindPropertyRelative("m_TextureSlice").intValue = 0;
            layer.FindPropertyRelative("m_UseGrassArray").boolValue = false;
            layer.FindPropertyRelative("m_YOffset").floatValue = 0f;
            layer.FindPropertyRelative("m_Seed").intValue = UnityEngine.Random.Range(1, int.MaxValue);
            m_PaintDetailIndex = index;
        }

        void AddPrototypeAsset(MGTerrain terrain, UnityEngine.Object asset)
        {
            FinishDetailStroke();
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(terrain, "Add Terrain Prototype");
            int index = asset is GameObject prefab
                ? terrain.FindOrAddPrototype(prefab, MGTerrain.InstanceKind.Detail)
                : terrain.FindOrAddPrototype((Mesh)asset, null, MGTerrain.InstanceKind.Detail);
            serializedObject.Update();
            bool hasLayer = false;
            for (int i = 0; i < m_DensityDetailLayers.arraySize; i++)
                hasLayer |= m_DensityDetailLayers.GetArrayElementAtIndex(i).FindPropertyRelative("m_PrototypeIndex").intValue == index;
            if (!hasLayer) AddEmptyDensityLayer(index);
            serializedObject.ApplyModifiedProperties();
            terrain.InvalidateRenderCache();
            EditorUtility.SetDirty(terrain);
            SelectPrototype(index);
            SceneView.RepaintAll();
        }

        void DuplicateSelectedDetail(MGTerrain terrain)
        {
            FinishDetailStroke();
            SetDetailPainting(false);
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            int sourcePrototype = m_SelectedPrototype;
            if (sourcePrototype < 0 || sourcePrototype >= m_Prototypes.arraySize) return;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            const string action = "Duplicate Terrain Detail";
            Undo.SetCurrentGroupName(action);
            Undo.RegisterCompleteObjectUndo(terrain, action);
            var createdPaths = new System.Collections.Generic.List<string>();
            try
            {
                int duplicatePrototype = m_Prototypes.arraySize;
                m_Prototypes.arraySize++;
                MGTerrainSettingsCopy.CopyValue(m_Prototypes.GetArrayElementAtIndex(sourcePrototype),
                    m_Prototypes.GetArrayElementAtIndex(duplicatePrototype));
                int originalLayerCount = m_DensityDetailLayers.arraySize;
                bool copiedLayer = false;
                for (int index = 0; index < originalLayerCount; index++)
                {
                    if (m_DensityDetailLayers.GetArrayElementAtIndex(index).FindPropertyRelative("m_PrototypeIndex").intValue != sourcePrototype) continue;
                    int duplicateLayer = m_DensityDetailLayers.arraySize;
                    m_DensityDetailLayers.arraySize++;
                    var source = m_DensityDetailLayers.GetArrayElementAtIndex(index);
                    var copy = m_DensityDetailLayers.GetArrayElementAtIndex(duplicateLayer);
                    MGTerrainSettingsCopy.CopyValue(source, copy);
                    copy.FindPropertyRelative("m_PrototypeIndex").intValue = duplicatePrototype;
                    foreach (string field in new[] { "m_DensityMap", "m_SizeMap" })
                    {
                        var texture = source.FindPropertyRelative(field).objectReferenceValue as Texture2D;
                        if (texture == null) continue;
                        string sourcePath = AssetDatabase.GetAssetPath(texture);
                        string folder = sourcePath.StartsWith("Assets/", StringComparison.Ordinal)
                            ? System.IO.Path.GetDirectoryName(sourcePath).Replace('\\', '/') : "Assets";
                        string suffix = field == "m_DensityMap" ? "Density" : "Size";
                        string filename = MGTerrainAppearanceCaptureAssets.TerrainPrefix(terrain.name)
                            + $"DetailPrototype_{duplicatePrototype}_Layer_{duplicateLayer}_{suffix}.asset";
                        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + filename);
                        var textureCopy = Instantiate(texture);
                        textureCopy.name = System.IO.Path.GetFileNameWithoutExtension(path);
                        textureCopy.hideFlags = HideFlags.None;
                        try { AssetDatabase.CreateAsset(textureCopy, path); }
                        catch { DestroyImmediate(textureCopy); throw; }
                        createdPaths.Add(path);
                        copy.FindPropertyRelative(field).objectReferenceValue = textureCopy;
                    }
                    // A duplicate is an editable snapshot, not an output owned by a palette bake.
                    copy.FindPropertyRelative("m_GeneratedByPalette").objectReferenceValue = null;
                    copy.FindPropertyRelative("m_PaletteSourceMap").objectReferenceValue = null;
                    copy.FindPropertyRelative("m_PaletteEntryIndex").intValue = -1;
                    copy.FindPropertyRelative("m_PaletteSourceOnly").boolValue = false;
                    copiedLayer = true;
                }
                if (!copiedLayer) AddEmptyDensityLayer(duplicatePrototype);
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(terrain);
                EditorUtility.SetDirty(terrain);
                if (terrain.gameObject.scene.IsValid())
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
                foreach (string path in createdPaths) AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                terrain.InvalidateRenderCache();
                SelectPrototype(duplicatePrototype);
                // Keep saved maps when undoing the component edit, so redo retains their references.
                Undo.CollapseUndoOperations(undoGroup);
                SceneView.RepaintAll();
                Repaint();
            }
            catch (Exception exception)
            {
                serializedObject.Update();
                Undo.RevertAllDownToGroup(undoGroup);
                foreach (string path in createdPaths) AssetDatabase.DeleteAsset(path);
                serializedObject.Update();
                terrain.InvalidateRenderCache();
                Debug.LogException(exception, terrain);
                EditorUtility.DisplayDialog("Duplicate Terrain Detail", exception.Message, "OK");
            }
        }

        void RemoveSelectedPrototype(MGTerrain terrain)
        {
            FinishDetailStroke();
            SetDetailPainting(false);
            foreach (var items in new[] { m_DensityDetailLayers, serializedObject.FindProperty("m_Instances") })
                for (int i = items.arraySize - 1; i >= 0; i--)
                {
                    var reference = items.GetArrayElementAtIndex(i).FindPropertyRelative("m_PrototypeIndex");
                    if (reference.intValue == m_SelectedPrototype) items.DeleteArrayElementAtIndex(i);
                    else if (reference.intValue > m_SelectedPrototype) reference.intValue--;
                }
            m_Prototypes.DeleteArrayElementAtIndex(m_SelectedPrototype);
            serializedObject.ApplyModifiedProperties();
            terrain.InvalidateRenderCache();
            EditorUtility.SetDirty(terrain);
            SelectPrototype(Mathf.Clamp(m_SelectedPrototype, 0, Mathf.Max(0, m_Prototypes.arraySize - 1)));
            SceneView.RepaintAll();
        }

        const string PrototypeDragKey = "MGTerrain.PrototypeReorder";
        Vector2 m_PrototypeDragStart;
        sealed class PrototypeDrag
        {
            public MGTerrain Terrain;
            public int From;
            public string Snapshot;
        }

        void MovePrototype(MGTerrain terrain, int from, int to)
        {
            int count = m_Prototypes.arraySize;
            if (from == to || from < 0 || to < 0 || from >= count || to >= count) return;
            FinishDetailStroke();
            Undo.SetCurrentGroupName("Reorder Terrain Prototypes");
            m_Prototypes.MoveArrayElement(from, to);
            foreach (var items in new[] { m_DensityDetailLayers, serializedObject.FindProperty("m_Instances") })
                for (int i = 0; i < items.arraySize; i++)
                {
                    var reference = items.GetArrayElementAtIndex(i).FindPropertyRelative("m_PrototypeIndex");
                    int old = reference.intValue;
                    if (old == from) reference.intValue = to;
                    else if (from < to && old > from && old <= to) reference.intValue = old - 1;
                    else if (from > to && old >= to && old < from) reference.intValue = old + 1;
                }
            serializedObject.ApplyModifiedProperties();
            terrain.InvalidateRenderCache();
            EditorUtility.SetDirty(terrain);
            SelectPrototype(to);
            SceneView.RepaintAll();
            Repaint();
        }

        void DrawPrototypeCell(MGTerrain terrain, Rect cell, int index)
        {
            int control = GUIUtility.GetControlID(FocusType.Passive, cell);
            Event evt = Event.current;
            if (evt.type == EventType.Repaint)
                GUI.skin.button.Draw(cell, GUIContent.none, cell.Contains(evt.mousePosition),
                    GUIUtility.hotControl == control, m_SelectedPrototype == index, false);
            if (serializedObject.isEditingMultipleObjects) return;
            switch (evt.GetTypeForControl(control))
            {
                case EventType.MouseDown:
                    if (evt.button != 0 || !cell.Contains(evt.mousePosition)) break;
                    GUIUtility.hotControl = control;
                    m_PrototypeDragStart = evt.mousePosition;
                    SelectPrototype(index);
                    evt.Use();
                    Repaint();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != control) break;
                    if (!Application.isPlaying && (evt.mousePosition - m_PrototypeDragStart).sqrMagnitude >= 25f)
                    {
                        serializedObject.ApplyModifiedProperties();
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                        DragAndDrop.SetGenericData(PrototypeDragKey, new PrototypeDrag
                        {
                            Terrain = terrain, From = index, Snapshot = PrototypeOrderSnapshot()
                        });
                        GUIUtility.hotControl = 0;
                        DragAndDrop.StartDrag("Move Foliage Prototype");
                    }
                    evt.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != control || evt.button != 0) break;
                    GUIUtility.hotControl = 0;
                    evt.Use();
                    break;
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (Application.isPlaying || !cell.Contains(evt.mousePosition)) break;
                    var drag = DragAndDrop.GetGenericData(PrototypeDragKey) as PrototypeDrag;
                    if (drag == null) break;
                    bool valid = drag.Terrain == terrain && drag.From >= 0 && drag.From < m_Prototypes.arraySize;
                    DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Rejected;
                    if (evt.type == EventType.DragPerform)
                    {
                        valid &= drag.Snapshot == PrototypeOrderSnapshot();
                        DragAndDrop.AcceptDrag();
                        DragAndDrop.SetGenericData(PrototypeDragKey, null);
                        if (valid) MovePrototype(terrain, drag.From, index);
                        evt.Use();
                        GUIUtility.ExitGUI();
                    }
                    evt.Use();
                    Repaint();
                    break;
            }
        }

        string PrototypeOrderSnapshot()
        {
            var ids = new System.Text.StringBuilder();
            for (int i = 0; i < m_Prototypes.arraySize; i++)
            {
                var item = m_Prototypes.GetArrayElementAtIndex(i);
                foreach (string field in new[] { "m_Prefab", "m_Mesh", "m_Material" })
                    ids.Append(item.FindPropertyRelative(field).objectReferenceInstanceIDValue).Append(',');
                ids.Append(';');
            }
            return ids.ToString();
        }

        bool m_ShowGrassLayerSettings;
        int m_GrassPaintSubId;
        int m_GrassPaintPopulation;
        Texture2D m_GrassStrokeIds;

        void DrawGrassSubIdGrid(MGTerrain terrain)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Grass Sub-IDs", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Choose a population, then paint its grass ID. IDs share one density map.", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            int population = GUILayout.Toolbar(m_GrassPaintPopulation, new[] { "Population A", "Population B" });
            if (EditorGUI.EndChangeCheck()) { FinishDetailStroke(); SetDetailPainting(false); m_GrassPaintPopulation = population; }
            if (MGGrassDetailLayers.SharedIndex(terrain, m_SelectedPrototype, 0) < 0)
                EditorGUILayout.HelpBox("First selection consolidates legacy grass into one density population. The densest ID wins in overlaps. Population B is only created when selected. Original maps and an editor backup are kept.", MessageType.Info);
            if (MGGrassDetailLayers.SharedIndex(terrain, m_SelectedPrototype, 0) < 0)
            {
                using (new EditorGUI.DisabledScope(Application.isPlaying || serializedObject.isEditingMultipleObjects))
                    if (GUILayout.Button("Consolidate Grass Layers into Shared Populations"))
                    {
                        FinishDetailStroke();
                        SetDetailPainting(false);
                        serializedObject.ApplyModifiedProperties();
                        m_PaintDetailIndex = MGGrassDetailLayers.EnsureShared(terrain, m_SelectedPrototype, 0);
                        m_GrassPaintPopulation = 0;
                        serializedObject.Update();
                        GUIUtility.ExitGUI();
                    }
            }
            int columns = Mathf.Clamp(Mathf.FloorToInt((EditorGUIUtility.currentViewWidth - 42f) / 88f), 1, 8);
            for (int row = 0; row < Mathf.CeilToInt(8f / columns); row++)
            {
                Rect strip = EditorGUILayout.GetControlRect(false, 98f);
                float width = strip.width / columns;
                for (int column = 0; column < columns; column++)
                {
                    int id = row * columns + column;
                    if (id >= 8) break;
                    string label = "Empty";
                    Texture2D thumbnail = null;
                    foreach (var material in m_DetailSourceMaterials)
                        if (material != null && material.HasProperty("_GrassUseArrays")
                            && MGGrassDetailLayers.SlotInfo(material, id, out string slotName, out Texture2D source))
                        { label = slotName; thumbnail = source; break; }
                    int existing = -1;
                    for (int i = 0; i < terrain.DensityDetailLayerCount; i++)
                    {
                        var layer = terrain.DensityDetailLayers[i];
                        if (layer != null && layer.PrototypeIndex == m_SelectedPrototype && layer.UsesGrassArray && layer.GrassIdMap != null && layer.GrassPopulation == m_GrassPaintPopulation)
                        { existing = i; break; }
                    }
                    bool available = thumbnail != null;
                    bool selected = existing >= 0 && existing == m_PaintDetailIndex && id == m_GrassPaintSubId;
                    Rect cell = new Rect(strip.x + column * width, strip.y, width - 4f, 94f);
                    Color previous = GUI.backgroundColor;
                    if (selected) GUI.backgroundColor = new Color(.4f, .7f, 1f);
                    bool clicked;
                    using (new EditorGUI.DisabledScope(!available || Application.isPlaying || serializedObject.isEditingMultipleObjects))
                        clicked = GUI.Button(cell, new GUIContent("", available ? "Paint Sub-ID " + id + ": " + label : "Assign this slot in the grass material first."));
                    GUI.backgroundColor = previous;
                    if (Event.current.type == EventType.Repaint && thumbnail != null)
                        GUI.DrawTexture(new Rect(cell.x + 5, cell.y + 20, cell.width - 10, 50), thumbnail, ScaleMode.ScaleToFit, true);
                    GUI.Label(new Rect(cell.x + 5, cell.y + 3, cell.width - 10, 18), "Sub-ID " + id, EditorStyles.miniBoldLabel);
                    GUI.Label(new Rect(cell.x + 3, cell.y + 72, cell.width - 6, 18), new GUIContent(label, label), EditorStyles.centeredGreyMiniLabel);
                    if (!clicked) continue;
                    FinishDetailStroke();
                    serializedObject.ApplyModifiedProperties();
                    m_PaintDetailIndex = MGGrassDetailLayers.EnsureShared(terrain, m_SelectedPrototype, m_GrassPaintPopulation);
                    m_GrassPaintSubId = id;
                    serializedObject.Update();
                    m_PaintChannel = 0;
                    SetDetailPainting(true);
                    Repaint();
                    SceneView.RepaintAll();
                    GUIUtility.ExitGUI();
                }
            }
        }
        void DrawPrototypeGrid(MGTerrain terrain)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Foliage Prototypes", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(Application.isPlaying ? "Select a thumbnail to tune its settings." : "Drag thumbnails to reorder IDs.", EditorStyles.miniLabel);
            if (Event.current.type == EventType.DragExited)
            {
                DragAndDrop.SetGenericData(PrototypeDragKey, null);
                Repaint();
            }
            int count = m_Prototypes.arraySize;
            m_SelectedPrototype = Mathf.Clamp(m_SelectedPrototype, 0, Mathf.Max(0, count - 1));
            int columns = Mathf.Max(1, Mathf.FloorToInt((EditorGUIUtility.currentViewWidth - 42f) / 88f));
            for (int row = 0; row < Mathf.CeilToInt(count / (float)columns); row++)
            {
                Rect strip = EditorGUILayout.GetControlRect(false, 98f);
                float width = strip.width / columns;
                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column;
                    if (index >= count) break;
                    var prototype = m_Prototypes.GetArrayElementAtIndex(index);
                    var asset = prototype.FindPropertyRelative("m_Prefab").objectReferenceValue
                        ?? prototype.FindPropertyRelative("m_Mesh").objectReferenceValue;
                    string label = asset != null ? asset.name : "Empty Prototype";
                    Rect cell = new Rect(strip.x + column * width, strip.y, width - 4f, 94f);
                    DrawPrototypeCell(terrain, cell, index);
                    // Previews are visual-only. Request them only when drawing, and let normal
                    // Inspector events show completed previews instead of recursively repainting
                    // the entire terrain Inspector while Unity's preview queue is busy.
                    if (asset != null && Event.current.type == EventType.Repaint)
                    {
                        Texture preview = AssetPreview.GetAssetPreview(asset) ?? AssetPreview.GetMiniThumbnail(asset);
                        if (preview != null) GUI.DrawTexture(new Rect(cell.x + 5, cell.y + 4, cell.width - 10, 65), preview, ScaleMode.ScaleToFit);
                    }
                    GUI.Label(new Rect(cell.x + 5, cell.y + 3, 48, 18), "ID " + index, EditorStyles.helpBox);
                    var dragging = DragAndDrop.GetGenericData(PrototypeDragKey) as PrototypeDrag;
                    if (dragging != null && dragging.Terrain == terrain && cell.Contains(Event.current.mousePosition))
                        EditorGUI.DrawRect(new Rect(cell.x, cell.yMax - 3, cell.width, 3), new Color(.3f, .65f, 1f));
                    GUI.Label(new Rect(cell.x + 3, cell.y + 72, cell.width - 6, 18), new GUIContent(label, label), EditorStyles.centeredGreyMiniLabel);
                }
            }
            Rect drop = GUILayoutUtility.GetRect(0, 38, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "Drop prefabs or meshes here to add foliage", EditorStyles.helpBox);
            Event evt = Event.current;
            if (!Application.isPlaying && drop.Contains(evt.mousePosition) && (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform))
            {
                bool valid = Array.Exists(DragAndDrop.objectReferences, item => EditorUtility.IsPersistent(item) && (item is GameObject || item is Mesh));
                DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                if (valid && evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var item in DragAndDrop.objectReferences)
                        if (EditorUtility.IsPersistent(item) && (item is GameObject || item is Mesh)) AddPrototypeAsset(terrain, item);
                    evt.Use();
                    GUIUtility.ExitGUI();
                }
                evt.Use();
            }
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                var add = EditorGUILayout.ObjectField("Add Prefab", null, typeof(GameObject), false);
                if (add != null) { AddPrototypeAsset(terrain, add); GUIUtility.ExitGUI(); }
            }
            if (count == 0) return;
            var selected = m_Prototypes.GetArrayElementAtIndex(m_SelectedPrototype);
            EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_Prefab"));
            if (selected.FindPropertyRelative("m_Prefab").objectReferenceValue == null)
            {
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_Mesh"));
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_Material"));
            }
            m_ShowPrototypeAdvanced = EditorGUILayout.Foldout(m_ShowPrototypeAdvanced, "Advanced Prototype Settings", true);
            if (m_ShowPrototypeAdvanced) DrawPrototypeFields(selected, "m_Prefab", "m_Mesh", "m_Material");
            terrain.GetDetailSourceMaterials(m_SelectedPrototype, m_DetailSourceMaterials);
            bool grassArrayMaterial = m_DetailSourceMaterials.Exists(material => material != null && material.HasProperty("_GrassUseArrays"));
            if (grassArrayMaterial) DrawGrassSubIdGrid(terrain);
            bool found = false;
            for (int i = 0; i < m_DensityDetailLayers.arraySize; i++)
                found |= m_DensityDetailLayers.GetArrayElementAtIndex(i).FindPropertyRelative("m_PrototypeIndex").intValue == m_SelectedPrototype;
            if (grassArrayMaterial)
                m_ShowGrassLayerSettings = EditorGUILayout.Foldout(m_ShowGrassLayerSettings, "Grass Paint Layer Settings", true);
            if (found && (!grassArrayMaterial || m_ShowGrassLayerSettings)) DrawDensityLayersWithFlood(terrain);
            else if (!found && !grassArrayMaterial && selected.FindPropertyRelative("m_Kind").enumValueIndex == (int)MGTerrain.InstanceKind.Detail)
            {
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                    if (GUILayout.Button("Enable Density Painting")) AddEmptyDensityLayer(m_SelectedPrototype);
            }
            using (new EditorGUI.DisabledScope(Application.isPlaying || serializedObject.isEditingMultipleObjects
                || selected.FindPropertyRelative("m_Kind").enumValueIndex != (int)MGTerrain.InstanceKind.Detail))
                if (GUILayout.Button(new GUIContent("Duplicate Detail and Maps",
                    "Copies this prototype, its density layers and painted density/size maps. Prefab, mesh and material assets stay shared. Palette-generated layers become standalone copies.")))
                {
                    DuplicateSelectedDetail(terrain);
                    GUIUtility.ExitGUI();
                }
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                if (GUILayout.Button("Remove Selected Prototype...") && EditorUtility.DisplayDialog(
                    "Remove Terrain Prototype?", "Remove this prototype and its placed instances and density layers? Texture and prefab assets are kept. This can be undone.", "Remove", "Cancel"))
                {
                    RemoveSelectedPrototype(terrain);
                    GUIUtility.ExitGUI();
                }
        }
    }
}
#endif

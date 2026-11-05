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
        int m_SelectedPrototype;
        readonly System.Collections.Generic.List<Mesh> m_TreeLodSourceMeshes = new System.Collections.Generic.List<Mesh>();

        void DrawDetectedTreeLod(MGTerrain terrain, int lod, string label, SerializedProperty overridePrefab = null)
        {
            bool automatic = overridePrefab == null || overridePrefab.objectReferenceValue == null;
            terrain.GetTreeLodSourceMeshes(m_SelectedPrototype, lod, m_TreeLodSourceMeshes);
            if (m_TreeLodSourceMeshes.Count == 0)
            {
                EditorGUILayout.HelpBox(label + ": no usable mesh found in the prefab.", MessageType.Warning);
                return;
            }
            using (new EditorGUI.DisabledScope(true))
                for (int index = 0; index < m_TreeLodSourceMeshes.Count; index++)
                    EditorGUILayout.ObjectField(index == 0 ? label + (automatic ? " (Automatic)" : " (Override)") : "",
                        m_TreeLodSourceMeshes[index], typeof(Mesh), false);
        }

        static void DrawPrototypeFields(SerializedProperty parent, params string[] hidden)
        {
            var field = parent.Copy();
            var end = parent.GetEndProperty();
            bool enter = true;
            while (field.NextVisible(enter) && !SerializedProperty.EqualContents(field, end))
            {
                enter = false;
                if (Array.IndexOf(hidden, field.name) >= 0 || field.name == "m_Seed") continue;
                if (field.name == "m_MinWidth" && parent.FindPropertyRelative("m_MaxWidth") != null)
                    DrawDensitySizeRange(parent, "Width", "m_MinWidth", "m_MaxWidth");
                else if (field.name == "m_MinHeight" && parent.FindPropertyRelative("m_MaxHeight") != null)
                    DrawDensitySizeRange(parent, "Height", "m_MinHeight", "m_MaxHeight");
                else if (field.name != "m_MaxWidth" && field.name != "m_MaxHeight")
                    EditorGUILayout.PropertyField(field, new GUIContent(field.displayName.Replace("Grass", "Detail"), field.tooltip.Replace("grass", "detail").Replace("Grass", "Detail")), true);
            }
        }

        void SelectPrototype(int index, bool additive = false) => SelectDetailPrototype(index, additive, false);

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
                    foreach (string field in new[] { "m_DensityMap", "m_SizeMap", "m_GrassIdMap" })
                    {
                        var texture = source.FindPropertyRelative(field).objectReferenceValue as Texture2D;
                        if (texture == null) continue;
                        string suffix = field == "m_DensityMap" ? "Density" : field == "m_SizeMap" ? "Size" : "GrassIDs";
                        string path = MGTerrainSceneAssets.UniquePath(terrain,
                            $"Detail_{duplicatePrototype}_Layer_{duplicateLayer}_{suffix}");
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
            m_DetailSubIdSelections.Clear();
            m_SubIdSelectionPrototype = -1;
            SelectPrototype(to);
            SceneView.RepaintAll();
            Repaint();
        }

        void DrawPrototypeCell(MGTerrain terrain, Rect cell, int index, float footerHeight = 0)
        {
            int control = GUIUtility.GetControlID(FocusType.Passive, cell);
            Event evt = Event.current;
            Rect interaction = cell;
            interaction.height -= footerHeight;
            if (evt.type == EventType.Repaint)
            {
                Color previous = GUI.backgroundColor;
                if (IsPaintPrototypeSelected(terrain, index)) GUI.backgroundColor = new Color(.4f, .7f, 1f);
                GUI.skin.button.Draw(cell, GUIContent.none, interaction.Contains(evt.mousePosition),
                    GUIUtility.hotControl == control, false, false);
                GUI.backgroundColor = previous;
            }
            if (serializedObject.isEditingMultipleObjects) return;
            switch (evt.GetTypeForControl(control))
            {
                case EventType.MouseDown:
                    if (evt.button != 0 || !interaction.Contains(evt.mousePosition) || DetailCardCheckboxRect(cell).Contains(evt.mousePosition) || DetailCardSettingsRect(cell).Contains(evt.mousePosition)) break;
                    GUIUtility.hotControl = control;
                    m_PrototypeDragStart = evt.mousePosition;
                    SelectDetailPrototype(index, evt.control || evt.command, evt.shift);
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
                    if (Application.isPlaying || !interaction.Contains(evt.mousePosition)) break;
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


        bool m_ShowPrototypeAdvanced = true;
        int m_GrassPaintSubId;
        int m_GrassPaintPopulation;
        Texture2D m_GrassStrokeIds;
        bool m_RandomGrassSubIds;
        int m_GrassSubIdMask = 255;
        int m_GrassMixSeed = 1;
        int m_GrassStrokeSubIdMask;

        int AvailableGrassSubIds(MGTerrain terrain, int prototype)
        {
            terrain.GetDetailSourceMaterials(prototype, m_DetailSourceMaterials);
            int mask = 0;
            for (int id = 0; id < 8; id++)
                foreach (var material in m_DetailSourceMaterials)
                    if (material != null && material.HasProperty("_GrassUseArrays")
                        && MGGrassDetailLayers.SlotInfo(material, id, out _, out Texture2D source) && source != null)
                    { mask |= 1 << id; break; }
            return mask;
        }

        readonly System.Collections.Generic.Dictionary<(int prototype, int population), int> m_DetailSubIdSelections =
            new System.Collections.Generic.Dictionary<(int, int), int>();
        int m_SubIdSelectionPrototype = -1, m_SubIdSelectionPopulation = -1;

        void SyncDetailSubIdSelection(MGTerrain terrain)
        {
            var key = (m_SelectedPrototype, m_GrassPaintPopulation);
            if (m_SubIdSelectionPrototype != key.Item1 || m_SubIdSelectionPopulation != key.Item2)
            {
                FinishDetailStroke();
                m_SubIdSelectionPrototype = key.Item1;
                m_SubIdSelectionPopulation = key.Item2;
            }
            // Row rendering and world painting temporarily reuse the current mask.
            // Always reload this prototype/population instead of saving another row's transient mask.
            m_GrassSubIdMask = DetailSubIdMask(terrain, key.Item1, key.Item2);
            m_RandomGrassSubIds = true;
        }

        readonly System.Collections.Generic.Dictionary<(int prototype, int population), int> m_SubIdSelectionAnchors =
            new System.Collections.Generic.Dictionary<(int, int), int>();

        void SelectDetailSubIds(MGTerrain terrain, int id, bool toggle, bool range)
        {
            int available = AvailableGrassSubIds(terrain, m_SelectedPrototype);
            if ((available & (1 << id)) == 0) return;
            var key = (m_SelectedPrototype, m_GrassPaintPopulation);
            int previous = DetailSubIdMask(terrain, key.Item1, key.Item2);
            int next;
            if (range)
            {
                int anchor = m_SubIdSelectionAnchors.TryGetValue(key, out int stored) ? stored : id;
                int span = 0;
                for (int i = Mathf.Min(anchor, id); i <= Mathf.Max(anchor, id); i++) span |= 1 << i;
                next = ((toggle ? previous : 0) | span) & available;
            }
            else
            {
                next = (toggle ? previous ^ (1 << id) : 1 << id) & available;
                m_SubIdSelectionAnchors[key] = id;
            }
            if (next == previous) return;
            FinishDetailStroke();
            m_GrassSubIdMask = next;
            m_DetailSubIdSelections[key] = next;
            if (next != 0)
            {
                // Existing populations only change editor selection, not terrain data.
                // Avoid serializing the entire terrain on each checkbox/Ctrl-click.
                if (MGGrassDetailLayers.SharedIndex(terrain, m_SelectedPrototype, m_GrassPaintPopulation) < 0)
                {
                    serializedObject.ApplyModifiedProperties();
                    MGGrassDetailLayers.EnsureShared(terrain, m_SelectedPrototype, m_GrassPaintPopulation);
                    serializedObject.Update();
                }
                m_GrassPaintSubId = id;
            }
            Repaint();
            SceneView.RepaintAll();
        }

        static Rect DetailCardCheckboxRect(Rect cell) => new Rect(cell.xMax - 21, cell.y + 3, 17, 16);

        bool DrawDetailSubIdCard(Rect cell, bool selected, string tooltip, out bool checkboxChanged)
        {
            Rect checkbox = DetailCardCheckboxRect(cell);
            int control = GUIUtility.GetControlID(FocusType.Passive, cell);
            Event evt = Event.current;
            if (evt.type == EventType.Repaint)
                GUI.skin.button.Draw(cell, GUIContent.none, cell.Contains(evt.mousePosition), GUIUtility.hotControl == control, false, false);
            bool clicked = false;
            if (GUI.enabled)
                switch (evt.GetTypeForControl(control))
                {
                    case EventType.MouseDown:
                        if (evt.button == 0 && cell.Contains(evt.mousePosition) && !checkbox.Contains(evt.mousePosition) && !DetailCardSettingsRect(cell).Contains(evt.mousePosition))
                        { GUIUtility.hotControl = control; evt.Use(); }
                        break;
                    case EventType.MouseDrag:
                        if (GUIUtility.hotControl == control) evt.Use();
                        break;
                    case EventType.MouseUp:
                        if (GUIUtility.hotControl != control || evt.button != 0) break;
                        GUIUtility.hotControl = 0;
                        clicked = cell.Contains(evt.mousePosition) && !checkbox.Contains(evt.mousePosition) && !DetailCardSettingsRect(cell).Contains(evt.mousePosition);
                        evt.Use();
                        break;
                }
            bool next = GUI.Toggle(checkbox, selected, new GUIContent("", tooltip), EditorStyles.toggle);
            checkboxChanged = next != selected;
            GUI.Label(new Rect(cell.x, cell.y + 21, cell.width, cell.height - 21), new GUIContent("", tooltip));
            return clicked;
        }


        internal static byte RandomGrassSubId(int x, int z, int seed, int mask, int fallback)
        {
            mask &= 255;
            int count = 0;
            for (int id = 0; id < 8; id++) if ((mask & (1 << id)) != 0) count++;
            if (count == 0) return (byte)(fallback & 7);
            unchecked
            {
                uint hash = (uint)x * 0x9E3779B9u ^ (uint)z * 0x85EBCA6Bu ^ (uint)seed;
                hash ^= hash >> 16; hash *= 0x7FEB352Du;
                hash ^= hash >> 15; hash *= 0x846CA68Bu; hash ^= hash >> 16;
                int choice = (int)(hash % (uint)count);
                for (int id = 0; id < 8; id++)
                    if ((mask & (1 << id)) != 0 && choice-- == 0) return (byte)id;
            }
            return (byte)(fallback & 7);
        }

        void DrawGrassSubIdGrid(MGTerrain terrain, bool synchronize = true)
        {
            EditorGUILayout.Space();
            if (MGGrassDetailLayers.SharedIndex(terrain, m_SelectedPrototype, 0) < 0)
                EditorGUILayout.HelpBox("First selection consolidates legacy details into one density population. The densest ID wins in overlaps. Population B is only created when selected. Original maps and an editor backup are kept.", MessageType.Info);
            if (MGGrassDetailLayers.SharedIndex(terrain, m_SelectedPrototype, 0) < 0)
            {
                using (new EditorGUI.DisabledScope(Application.isPlaying || serializedObject.isEditingMultipleObjects))
                    if (GUILayout.Button("Consolidate Detail Layers into Shared Populations"))
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
            if (synchronize) SyncDetailSubIdSelection(terrain);
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
                    bool available = thumbnail != null;
                    bool selected = available && (m_GrassSubIdMask & (1 << id)) != 0;
                    Rect cell = new Rect(strip.x + column * width, strip.y, width - 4f, 94f);
                    Color previous = GUI.backgroundColor;
                    if (selected) GUI.backgroundColor = new Color(.4f, .7f, 1f);
                    bool clicked, checkboxChanged;
                    using (new EditorGUI.DisabledScope(!available || Application.isPlaying || serializedObject.isEditingMultipleObjects))
                        clicked = DrawDetailSubIdCard(cell, selected, available ? "Sub-ID " + id + ": " + label + ". Click to select only this type; Ctrl-click toggles; Shift-click selects a range. The checkbox toggles independently." : "Assign this slot in the detail material first.", out checkboxChanged);
                    GUI.backgroundColor = previous;
                    if (Event.current.type == EventType.Repaint && thumbnail != null)
                        GUI.DrawTexture(new Rect(cell.x + 5, cell.y + 20, cell.width - 10, 50), thumbnail, ScaleMode.ScaleToFit, true);
                    using (new EditorGUI.DisabledScope(!available)) DrawDetailCardSettings(cell, m_SelectedPrototype, m_GrassPaintPopulation, id);
                    GUI.Label(new Rect(cell.x + 23, cell.y + 3, cell.width - 46, 18), "Sub-ID " + id, EditorStyles.miniBoldLabel);
                    GUI.Label(new Rect(cell.x + 3, cell.y + 72, cell.width - 6, 18), new GUIContent(label, label), EditorStyles.centeredGreyMiniLabel);
                    if (!clicked && !checkboxChanged) continue;
                    SelectDetailSubIds(terrain, id, checkboxChanged || Event.current.control || Event.current.command, !checkboxChanged && Event.current.shift);
                    GUIUtility.ExitGUI();
                }
            }
        }
        void DrawPrototypePopulationButtons(MGTerrain terrain, Rect cell, int prototype)
        {
            float width = (cell.width - 8) * .5f;
            using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
                for (int population = 0; population < 2; population++)
                {
                    bool selected = m_SelectedDetailPrototypes.Contains(prototype) && DetailPopulation(prototype) == population;
                    Rect button = new Rect(cell.x + 4 + population * width, cell.y + 95, width, 19);
                    string label = population == 0 ? "A" : "B";
                    if (!GUI.Toggle(button, selected, new GUIContent(label, "Select Population " + label),
                        population == 0 ? EditorStyles.miniButtonLeft : EditorStyles.miniButtonRight) || selected) continue;
                    SetDetailPrototypePopulation(terrain, prototype, population);
                    GUIUtility.ExitGUI();
                }
        }

        void DrawPrototypeCards(MGTerrain terrain)
        {
            DrawDetailCardsHeader();
            if (Event.current.type == EventType.DragExited)
            {
                DragAndDrop.SetGenericData(PrototypeDragKey, null);
                Repaint();
            }
            int count = m_Prototypes.arraySize;
            m_SelectedPrototype = Mathf.Clamp(m_SelectedPrototype, 0, Mathf.Max(0, count - 1));
            EnsureDetailPrototypeSelection(terrain);
            int columns = Mathf.Max(1, Mathf.FloorToInt((EditorGUIUtility.currentViewWidth - 100f) / 88f));
            var hasPopulations = new bool[count];
            for (int i = 0; i < count; i++)
            {
                terrain.GetDetailSourceMaterials(i, m_DetailSourceMaterials);
                hasPopulations[i] = m_DetailSourceMaterials.Exists(material => material != null && material.HasProperty("_GrassUseArrays"));
            }
            Rect drop = default;
            Rect refresh = default;
            Rect visibility = default;
            // The final slot is always the add target, including when the prototype list is empty.
            for (int row = 0; row < Mathf.CeilToInt((count + 1) / (float)columns); row++)
            {
                bool populationRow = false;
                for (int i = row * columns; i < Mathf.Min(count, (row + 1) * columns); i++) populationRow |= hasPopulations[i];
                Rect strip = EditorGUILayout.GetControlRect(false, populationRow ? 122f : 98f);
                if (row == 0)
                {
                    refresh = new Rect(strip.xMax - 38, strip.y, 38, 38);
                    visibility = new Rect(refresh.x - 44, strip.y, 38, 38);
                }
                strip.width = Mathf.Max(1, strip.width - 90);
                float width = strip.width / columns;
                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column;
                    if (index > count) break;
                    Rect cell = new Rect(strip.x + column * width, strip.y, width - 4f, 94f);
                    if (index == count)
                    {
                        drop = cell;
                        using (new EditorGUI.DisabledScope(Application.isPlaying))
                        {
                            GUI.Box(cell, new GUIContent("", "Drop prefab or mesh assets here to add foliage."), EditorStyles.helpBox);
                            Color icon = Application.isPlaying ? Color.gray : EditorGUIUtility.isProSkin
                                ? new Color(.7f, .78f, .85f) : new Color(.3f, .38f, .45f);
                            Vector2 centre = new Vector2(cell.center.x, cell.y + 36);
                            EditorGUI.DrawRect(new Rect(centre.x - 12, centre.y - 1.5f, 24, 3), icon);
                            EditorGUI.DrawRect(new Rect(centre.x - 1.5f, centre.y - 12, 3, 24), icon);
                            GUI.Label(new Rect(cell.x + 3, cell.y + 72, cell.width - 6, 18), "Drop Prefab", EditorStyles.centeredGreyMiniLabel);
                        }
                        continue;
                    }
                    var prototype = m_Prototypes.GetArrayElementAtIndex(index);
                    var asset = prototype.FindPropertyRelative("m_Prefab").objectReferenceValue
                        ?? prototype.FindPropertyRelative("m_Mesh").objectReferenceValue;
                    string label = asset != null ? asset.name : "Empty Prototype";
                    bool populations = hasPopulations[index];
                    if (populations) cell.height += 24;
                    DrawPrototypeCell(terrain, cell, index, populations ? 24 : 0);
                    if (populations) DrawPrototypePopulationButtons(terrain, cell, index);
                    // Previews are visual-only. Request them only when drawing, and let normal
                    // Inspector events show completed previews instead of recursively repainting
                    // the entire terrain Inspector while Unity's preview queue is busy.
                    if (asset != null && Event.current.type == EventType.Repaint)
                    {
                        Texture preview = GetDetailPrototypeThumbnail(terrain, index, asset);
                        if (preview != null) GUI.DrawTexture(new Rect(cell.x + 5, cell.y + 20, cell.width - 10, 50), preview, ScaleMode.ScaleToFit);
                    }
                    DrawDetailCardSettings(cell, index, DetailPopulation(index));
                    GUI.Label(new Rect(cell.x + 23, cell.y + 3, cell.width - 46, 18), "ID " + index, EditorStyles.miniBoldLabel);
                    bool included = IsPaintPrototypeSelected(terrain, index);
                    using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
                        if (GUI.Toggle(DetailCardCheckboxRect(cell), included, new GUIContent("", "Include or exclude detail ID " + index + ". Click the card to select only it; Ctrl-click toggles; Shift-click selects a range."), EditorStyles.toggle) != included)
                        {
                            SelectDetailPrototype(index, true, false);
                            Repaint();
                            SceneView.RepaintAll();
                            GUIUtility.ExitGUI();
                        }
                    var dragging = DragAndDrop.GetGenericData(PrototypeDragKey) as PrototypeDrag;
                    if (dragging != null && dragging.Terrain == terrain && cell.Contains(Event.current.mousePosition))
                        EditorGUI.DrawRect(new Rect(cell.x, cell.yMax - 3, cell.width, 3), new Color(.3f, .65f, 1f));
                    GUI.Label(new Rect(cell.x + 3, cell.y + 72, cell.width - 6, 18), new GUIContent(label, label), EditorStyles.centeredGreyMiniLabel);
                }
            }
            DrawLayerVisibilityButton(visibility);
            if (MGTerrainToolbarIcons.Button(refresh, MGTerrainToolbarIcons.Icon.Refresh,
                "Refresh detail prefabs: reload meshes, LODs and materials for loaded terrain tiles. Painted maps and placements are preserved.", EditorStyles.miniButton))
            {
                serializedObject.ApplyModifiedProperties();
                ClearDetailPrototypeThumbnails();
                MGGrassDetailLayers.ClearSlotInfoCache();
                MGTerrainPrototypeRefresh.RefreshAllLoaded();
                GUIUtility.ExitGUI();
            }
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
        }

        void DrawPrototypeGrid(MGTerrain terrain)
        {
            DrawPrototypeCards(terrain);
            DrawSelectedPrototypeSubIds(terrain);
        }

        void DrawSelectedPrototypeSubIds(MGTerrain terrain) => DrawSelectedDetailSubIdRows(terrain);

        void DrawSelectedPrototypeSettings(MGTerrain terrain, bool popup = false)
        {
            if (!popup && m_DetailTargetsInitialized && m_SelectedDetailPrototypes.Count == 0) return;
            if (m_Prototypes.arraySize == 0) return;
            if (!popup)
            {
                const string preference = "MashBox.MGTerrain.DetailSettings";
                bool expanded = SessionState.GetBool(preference, true);
                bool next = EditorGUILayout.Foldout(expanded, "Detail Settings", true);
                if (next != expanded) SessionState.SetBool(preference, next);
                if (!next) return;
                DrawDetailSettingsTarget(terrain);
            }
            var selected = m_Prototypes.GetArrayElementAtIndex(m_SelectedPrototype);
            terrain.GetDetailSourceMaterials(m_SelectedPrototype, m_DetailSourceMaterials);
            bool grassArrayMaterial = m_DetailSourceMaterials.Exists(material => material != null && material.HasProperty("_GrassUseArrays"));
            bool found = false;
            for (int i = 0; i < m_DensityDetailLayers.arraySize; i++)
                found |= m_DensityDetailLayers.GetArrayElementAtIndex(i).FindPropertyRelative("m_PrototypeIndex").intValue == m_SelectedPrototype;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent("Prefab Settings", "Prefab, kind, draw distance, shadows and tree LODs apply to every population of this detail prototype."), EditorStyles.boldLabel);
                    GUILayout.Label("ID " + m_SelectedPrototype + " · All populations", EditorStyles.miniLabel);
                }
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_Prefab"));
                if (selected.FindPropertyRelative("m_Prefab").objectReferenceValue == null)
                {
                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_Mesh"));
                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_Material"));
                }

                bool isTree = selected.FindPropertyRelative("m_Kind").enumValueIndex == (int)MGTerrain.InstanceKind.Tree;
                DrawPrototypeFields(selected, "m_Prefab", "m_Mesh", "m_Material", "m_TreeLodCount", "m_TreeLod1Distance", "m_TreeLod2Distance", "m_TreeLodHysteresis", "m_TreeLod1Prefab", "m_TreeLod2Prefab", "m_TreeMidDensity", "m_TreeFarDensity", "m_TreeDensityInitialized", "m_TreeDensityTransitionWidth");
                if (isTree)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Tree LODs", EditorStyles.boldLabel);
                    var lodCount = selected.FindPropertyRelative("m_TreeLodCount");
                    EditorGUILayout.PropertyField(lodCount, new GUIContent("LOD Count"));
                    DrawDetectedTreeLod(terrain, 0, "Close Mesh");
                    if (lodCount.intValue > 1)
                    {
                        EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLod1Distance"), new GUIContent("Medium Distance"));
                        EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLod1Prefab"), new GUIContent("Medium Prefab Override", "Optional. Leave empty to discover the mesh inside the source prefab automatically."));
                        DrawDetectedTreeLod(terrain, 1, "Medium Mesh", selected.FindPropertyRelative("m_TreeLod1Prefab"));
                    }
                    if (lodCount.intValue > 2)
                    {
                        EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLod2Distance"), new GUIContent("Far Distance"));
                        EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLod2Prefab"), new GUIContent("Far Prefab Override", "Optional. Leave empty to discover the mesh inside the source prefab automatically."));
                        DrawDetectedTreeLod(terrain, 2, "Far Mesh", selected.FindPropertyRelative("m_TreeLod2Prefab"));
                    }
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Tree Density by Distance", EditorStyles.boldLabel);
                    if (lodCount.intValue <= 1)
                        EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLod1Distance"), new GUIContent("Medium Distance"));
                    if (lodCount.intValue <= 2)
                        EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLod2Distance"), new GUIContent("Far Density Distance"));
                    EditorGUILayout.Slider(selected.FindPropertyRelative("m_TreeMidDensity"), 0f, 1f, new GUIContent("Mid Density", "Fraction of painted trees retained after Medium Distance. Near density remains 100%."));
                    EditorGUILayout.Slider(selected.FindPropertyRelative("m_TreeFarDensity"), 0f, 1f, new GUIContent("Far Density", "Fraction of painted trees retained after Far Distance, even with only one or two mesh LODs."));
                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeDensityTransitionWidth"), new GUIContent("Density Transition Width", "GodGrass density dither width in metres, using this tree type's Medium/Far distances and densities. Zero disables the fade. All mesh parts must use GodGrass."));
                    EditorGUILayout.HelpBox("Density changes the drawn population, not the painted maps. 1 = all trees; 0 = none. GodGrass trees use the same stable dither fade as details when Density Transition Width is positive. Mesh LOD selection stays independent. Unsupported shaders keep hard density changes.", MessageType.Info);
                    if (lodCount.intValue > 1)
                    {
                        EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLodHysteresis"), new GUIContent("Transition Hysteresis"));
                        EditorGUILayout.HelpBox("LOD meshes are discovered automatically inside the source prefab, including child LODGroups. Empty override slots use the meshes shown above. With only two mesh levels, Medium and Far reuse the same last level. Overrides must share the source pivot and scale.", MessageType.Info);
                    }
                }
            }
            EditorGUILayout.Space(4);
            if (found) DrawDensityLayersWithFlood(terrain, grassArrayMaterial);
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

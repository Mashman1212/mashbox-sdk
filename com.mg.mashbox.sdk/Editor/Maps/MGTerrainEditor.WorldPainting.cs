#if UNITY_EDITOR
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        readonly Dictionary<(MGTerrain tile, int layer, int channel), MGTerrainEditor> m_WorldPaintEditors =
            new Dictionary<(MGTerrain, int, int), MGTerrainEditor>();
        readonly HashSet<(MGTerrain, int, int)> m_WorldPaintSkipped = new HashSet<(MGTerrain, int, int)>();
        readonly HashSet<MGTerrain.DensityDetailLayer> m_SelectedPaintLayers = new HashSet<MGTerrain.DensityDetailLayer>();
        int m_WorldStrokeUndo = -1;
        bool m_ShowPaintTogether;
        bool m_WorldPaintDelegate;

        void SelectPaintDetail(int index, bool additive = false)
        {
            FinishDetailStroke();
            var terrain = (MGTerrain)target;
            if (additive && m_SelectedPaintLayers.Count == 0 && (uint)m_PaintDetailIndex < terrain.DensityDetailLayerCount)
                m_SelectedPaintLayers.Add(terrain.DensityDetailLayers[m_PaintDetailIndex]);
            if (!additive) m_SelectedPaintLayers.Clear();
            m_PaintDetailIndex = index;
            if ((uint)index >= terrain.DensityDetailLayerCount) return;
            var layer = terrain.DensityDetailLayers[index];
            if (!additive || !m_SelectedPaintLayers.Remove(layer) || m_SelectedPaintLayers.Count == 0)
                m_SelectedPaintLayers.Add(layer);
            else
                for (int i = 0; i < terrain.DensityDetailLayerCount; i++)
                    if (m_SelectedPaintLayers.Contains(terrain.DensityDetailLayers[i])) { m_PaintDetailIndex = i; break; }
            m_SelectedPrototype = terrain.DensityDetailLayers[m_PaintDetailIndex].PrototypeIndex;
        }

        bool IsPaintLayerSelected(MGTerrain terrain, int index) =>
            index == m_PaintDetailIndex || m_SelectedPaintLayers.Contains(terrain.DensityDetailLayers[index]);

        bool IsPaintPrototypeSelected(MGTerrain terrain, int prototype)
        {
            for (int i = 0; i < terrain.DensityDetailLayerCount; i++)
                if (terrain.DensityDetailLayers[i].PrototypeIndex == prototype && IsPaintLayerSelected(terrain, i)) return true;
            return m_SelectedPrototype == prototype;
        }

        void DrawPaintTogether(MGTerrain terrain, string[] labels)
        {
            m_ShowPaintTogether = EditorGUILayout.Foldout(m_ShowPaintTogether, "Paint Together", true);
            if (!m_ShowPaintTogether) return;
            using (new EditorGUI.IndentLevelScope())
                for (int i = 0; i < labels.Length; i++)
                {
                    if (terrain.DensityDetailLayers[i].PaletteSourceOnly) continue;
                    bool selected = IsPaintLayerSelected(terrain, i);
                    if (EditorGUILayout.ToggleLeft(labels[i], selected) != selected) SelectPaintDetail(i, true);
                }
            EditorGUILayout.HelpBox("Selected details share the brush and target density. Each layer receives the full target. You can also Ctrl-click detail thumbnails to select several types. Per-detail settings below apply to the active detail.", MessageType.None);
        }

        bool RaycastDetailWorld(MGTerrain terrain, Ray ray, out RaycastHit hit)
        {
            if (terrain.World == null) return terrain.RaycastEditingSurface(ray, out hit, float.MaxValue);
            hit = default;
            bool found = false;
            float distance = float.MaxValue;
            foreach (var tile in terrain.World.Chunks)
                if (tile != null && tile.isActiveAndEnabled && tile.RaycastEditingSurface(ray, out var next, distance))
                { hit = next; distance = next.distance; found = true; }
            return found;
        }

        internal static int FindWorldPaintLayer(MGTerrain source, int sourceIndex, MGTerrain destination)
        {
            if ((uint)sourceIndex >= source.DensityDetailLayerCount) return -1;
            var a = source.DensityDetailLayers[sourceIndex];
            if (!string.IsNullOrEmpty(a.WorldDetailId))
            {
                for (int n = 0; n < destination.DensityDetailLayerCount; n++)
                    if (destination.DensityDetailLayers[n].WorldDetailId == a.WorldDetailId) return n;
            }
            if ((uint)a.PrototypeIndex >= source.Prototypes.Count) return -1;
            var p = source.Prototypes[a.PrototypeIndex];
            int match = -1;
            for (int index = 0; index < destination.DensityDetailLayerCount; index++)
            {
                var b = destination.DensityDetailLayers[index];
                if (b == null || b.PaletteSourceOnly || (uint)b.PrototypeIndex >= destination.Prototypes.Count) continue;
                var q = destination.Prototypes[b.PrototypeIndex];
                if (q == null || p == null || p.Kind != q.Kind || p.Prefab != q.Prefab || p.Mesh != q.Mesh || p.Material != q.Material
                    || a.GeneratedByPalette != b.GeneratedByPalette || a.PaletteEntryIndex != b.PaletteEntryIndex
                    || a.UsesGrassArray != b.UsesGrassArray || (a.GrassIdMap != null) != (b.GrassIdMap != null)
                      || (a.GrassIdMap != null ? a.GrassPopulation != b.GrassPopulation : a.TextureSlice != b.TextureSlice)) continue;
                // Duplicate definitions are ambiguous; never guess a destination painted layer.
                if (match >= 0) return -1;
                match = index;
            }
            return match;
        }

        internal static void SetWorldDetailVisibility(MGTerrain source, int sourceIndex, bool disabled)
        {
            var world = source.GetComponentInParent<MGTerrainWorld>();
            if (world == null) { source.InvalidateRenderCache(); return; }
            // Include inactive tiles; they must inherit visibility when enabled later.
            foreach (var tile in world.GetComponentsInChildren<MGTerrain>(true))
            {
                if (tile.GetComponentInParent<MGTerrainWorld>() != world) continue;
                int index = tile == source ? sourceIndex : FindWorldPaintLayer(source, sourceIndex, tile);
                if (index < 0)
                {
                    Debug.LogWarning($"Detail visibility not applied to '{tile.name}': no unique matching detail layer.", tile);
                    continue;
                }
                using var data = new SerializedObject(tile);
                var layers = data.FindProperty("m_DensityDetailLayers");
                layers.GetArrayElementAtIndex(index).FindPropertyRelative("m_RenderDisabled").boolValue = disabled;
                data.ApplyModifiedProperties();
                tile.InvalidateRenderCache();
                // Play-mode visibility is a live test, not an edit to the saved scene.
                if (!Application.isPlaying)
                {
                    EditorUtility.SetDirty(tile);
                    if (tile.gameObject.scene.IsValid())
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(tile.gameObject.scene);
                }
            }
            SceneView.RepaintAll();
        }

        void BeginMultiDetailStroke()
        {
            FinishDetailStroke();
            Undo.IncrementCurrentGroup();
            m_WorldStrokeUndo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paint MG Terrain Details");
        }

        void PaintSelectedDetailDab(MGTerrain terrain, Vector3 point, bool erase, bool eraseAll)
        {
            PaintTile(terrain);
            if (terrain.World != null)
                foreach (var chunk in terrain.World.Chunks)
                    if (chunk != terrain) PaintTile(chunk);

            void PaintTile(MGTerrain chunk)
            {
                if (chunk == null || !chunk.isActiveAndEnabled || chunk.MeshFilter == null
                    || chunk.MeshFilter.sharedMesh == null) return;
                var bounds = chunk.MeshFilter.sharedMesh.bounds;
                Vector3 local = chunk.transform.InverseTransformPoint(point);
                // Brush falloff operates on the local XZ plane, matching the existing detail brush.
                Vector3 nearest = new Vector3(Mathf.Clamp(local.x, bounds.min.x, bounds.max.x), local.y,
                    Mathf.Clamp(local.z, bounds.min.z, bounds.max.z));
                if (chunk.transform.TransformVector(local - nearest).sqrMagnitude >= MBEditorToolState.BrushRadius * MBEditorToolState.BrushRadius) return;
                int channel = eraseAll ? 0 : m_PaintChannel;
                int count = eraseAll ? chunk.DensityDetailLayerCount : terrain.DensityDetailLayerCount;
                for (int sourceIndex = 0; sourceIndex < count; sourceIndex++)
                {
                    if (!eraseAll && !IsPaintLayerSelected(terrain, sourceIndex)) continue;
                    int index = eraseAll || chunk == terrain ? sourceIndex : FindWorldPaintLayer(terrain, sourceIndex, chunk);
                    if (index < 0)
                    {
                        if (m_WorldPaintSkipped.Add((chunk, sourceIndex, -1))) Debug.LogWarning($"Skipped cross-chunk paint of layer {sourceIndex} on '{chunk.name}': no unique matching detail definition. Match the chunk's detail setup before painting across this border.", chunk);
                        continue;
                    }
                    var detail = chunk.DensityDetailLayers[index];
                    if (detail.PaletteSourceOnly || (uint)detail.PrototypeIndex >= chunk.Prototypes.Count) continue;
                    // Erasing an unpainted layer must not allocate a blank texture.
                    if ((eraseAll || (erase && channel == 0)) && detail.DensityMap == null) continue;
                    var key = (chunk, index, channel);
                    if (m_WorldPaintSkipped.Contains(key)) continue;
                    if (!m_WorldPaintEditors.TryGetValue(key, out var editor))
                    {
                        editor = (MGTerrainEditor)CreateEditor(chunk, typeof(MGTerrainEditor));
                        editor.m_WorldPaintDelegate = true;
                        editor.m_PaintDetailIndex = index;
                        editor.m_GrassPaintSubId = m_GrassPaintSubId;
                        editor.m_GrassIdOnly = m_GrassIdOnly;
                        editor.m_GrassPaintPopulation = m_GrassPaintPopulation;
                        editor.m_PaintChannel = channel; editor.m_PaintDensity = m_PaintDensity;
                        editor.m_PaintSize = m_PaintSize; editor.m_RandomPaintSize = m_RandomPaintSize;
                        editor.m_PaintSizeMin = m_PaintSizeMin; editor.m_PaintSizeMax = m_PaintSizeMax;
                        editor.m_PaintSizeSeed = m_PaintSizeSeed;
                        m_WorldPaintEditors.Add(key, editor);
                        if (!editor.BeginDetailStroke(chunk)) { m_WorldPaintSkipped.Add(key); continue; }
                    }
                    editor.PaintDetailDab(chunk, point, erase || eraseAll);
                }
            }
        }

        void FinishWorldPaintStroke()
        {
            if (m_WorldPaintDelegate) return;
            Tool tool = Tools.current;
            bool editing = MBEditorToolState.ActiveEditing;
            foreach (var editor in m_WorldPaintEditors.Values)
                if (editor != null) { editor.FinishDetailStroke(); DestroyImmediate(editor); }
            Tools.current = tool; MBEditorToolState.ActiveEditing = editing;
            m_WorldPaintEditors.Clear(); m_WorldPaintSkipped.Clear();
            if (m_WorldStrokeUndo >= 0) Undo.CollapseUndoOperations(m_WorldStrokeUndo);
            m_WorldStrokeUndo = -1;
        }
    }
}
#endif


#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        readonly HashSet<int> m_SelectedDetailPrototypes = new HashSet<int>();
        readonly Dictionary<int, int> m_DetailPopulations = new Dictionary<int, int>();
        bool m_DetailTargetsInitialized;
        int m_DetailSelectionAnchor;

        void EnsureDetailPrototypeSelection(MGTerrain terrain)
        {
            if (m_DetailTargetsInitialized) return;
            m_DetailTargetsInitialized = true;
            if ((uint)m_SelectedPrototype < terrain.Prototypes.Count)
            {
                m_SelectedDetailPrototypes.Add(m_SelectedPrototype);
                m_DetailPopulations[m_SelectedPrototype] = m_GrassPaintPopulation;
                m_DetailSelectionAnchor = m_SelectedPrototype;
            }
        }

        int DetailPopulation(int prototype) => m_DetailPopulations.TryGetValue(prototype, out int population) ? population : 0;

        void SelectDetailPrototype(int prototype, bool additive, bool range)
        {
            var terrain = (MGTerrain)target;
            EnsureDetailPrototypeSelection(terrain);
            if ((uint)prototype >= terrain.Prototypes.Count) return;
            FinishDetailStroke();
            if (range)
            {
                if (!additive) m_SelectedDetailPrototypes.Clear();
                for (int i = Mathf.Min(m_DetailSelectionAnchor, prototype); i <= Mathf.Max(m_DetailSelectionAnchor, prototype); i++)
                    m_SelectedDetailPrototypes.Add(i);
            }
            else
            {
                if (!additive) m_SelectedDetailPrototypes.Clear();
                if (!additive || !m_SelectedDetailPrototypes.Remove(prototype)) m_SelectedDetailPrototypes.Add(prototype);
                m_DetailSelectionAnchor = prototype;
            }
            m_SelectedPrototype = m_SelectedDetailPrototypes.Contains(prototype) ? prototype
                : m_SelectedDetailPrototypes.Count > 0 ? m_SelectedDetailPrototypes.Min() : prototype;
            m_GrassPaintPopulation = DetailPopulation(m_SelectedPrototype);
            RefreshDetailPaintTargets(terrain);
            if (m_SelectedDetailPrototypes.Count == 0) SetDetailPainting(false);
        }

        void SetDetailPrototypePopulation(MGTerrain terrain, int prototype, int population)
        {
            EnsureDetailPrototypeSelection(terrain);
            FinishDetailStroke();
            if (!m_SelectedDetailPrototypes.Contains(prototype) && !(Event.current.control || Event.current.command || Event.current.shift))
                m_SelectedDetailPrototypes.Clear();
            m_SelectedDetailPrototypes.Add(prototype);
            m_DetailPopulations[prototype] = population;
            m_SelectedPrototype = prototype;
            m_GrassPaintPopulation = population;
            m_DetailSelectionAnchor = prototype;
            RefreshDetailPaintTargets(terrain);
            Repaint();
            SceneView.RepaintAll();
        }

        bool UsesDetailSubIds(MGTerrain terrain, int prototype)
        {
            terrain.GetDetailSourceMaterials(prototype, m_DetailSourceMaterials);
            return m_DetailSourceMaterials.Exists(material => material != null && material.HasProperty("_GrassUseArrays"));
        }

        int DetailSubIdMask(MGTerrain terrain, int prototype, int population)
        {
            int available = AvailableGrassSubIds(terrain, prototype);
            var key = (prototype, population);
            int mask = m_DetailSubIdSelections.TryGetValue(key, out int stored) ? stored & available : available & -available;
            m_DetailSubIdSelections[key] = mask;
            return mask;
        }

        int DetailTargetLayer(MGTerrain terrain, int prototype, int population, bool subIds)
        {
            if (subIds)
            {
                int shared = MGGrassDetailLayers.SharedIndex(terrain, prototype, population);
                if (shared >= 0) return shared;
            }
            for (int i = 0; i < terrain.DensityDetailLayerCount; i++)
            {
                var layer = terrain.DensityDetailLayers[i];
                if (layer == null || layer.PaletteSourceOnly || layer.PrototypeIndex != prototype) continue;
                if (!subIds || layer.GrassPopulation == population) return i;
            }
            return -1;
        }

        bool RefreshDetailPaintTargets(MGTerrain terrain)
        {
            EnsureDetailPrototypeSelection(terrain);
            m_SelectedDetailPrototypes.RemoveWhere(index => (uint)index >= terrain.Prototypes.Count);
            m_SelectedPaintLayers.Clear();
            bool paintable = false;
            m_PaintDetailIndex = -1;
            foreach (int prototype in m_SelectedDetailPrototypes.OrderBy(index => index))
            {
                int population = DetailPopulation(prototype);
                bool subIds = UsesDetailSubIds(terrain, prototype);
                bool renderable = m_DetailSourceMaterials.Count > 0;
                int layer = DetailTargetLayer(terrain, prototype, population, subIds);
                if (layer < 0) continue;
                m_SelectedPaintLayers.Add(terrain.DensityDetailLayers[layer]);
                if (prototype == m_SelectedPrototype) m_PaintDetailIndex = layer;
                paintable |= renderable && (!subIds || DetailSubIdMask(terrain, prototype, population) != 0);
            }
            if (m_PaintDetailIndex < 0 && m_SelectedPaintLayers.Count > 0)
                for (int i = 0; i < terrain.DensityDetailLayerCount; i++)
                    if (m_SelectedPaintLayers.Contains(terrain.DensityDetailLayers[i])) { m_PaintDetailIndex = i; break; }
            return paintable;
        }

        void DrawDetailPaintSelection(MGTerrain terrain)
        {
            foreach (int prototype in m_SelectedDetailPrototypes.OrderBy(index => index))
            {
                if ((uint)prototype >= terrain.Prototypes.Count) continue;
                var definition = terrain.Prototypes[prototype];
                string name = definition.Prefab != null ? definition.Prefab.name : definition.Mesh != null ? definition.Mesh.name : "Detail";
                int population = DetailPopulation(prototype);
                bool subIds = UsesDetailSubIds(terrain, prototype);
                string title = "ID " + prototype + " · " + name + (subIds ? " · Population " + (population == 0 ? "A" : "B") : "");
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(new GUIContent(title, title), EditorStyles.wordWrappedMiniLabel);
                    if (!subIds) continue;
                    int mask = DetailSubIdMask(terrain, prototype, population);
                    var types = new List<string>();
                    for (int id = 0; id < 8; id++)
                    {
                        if ((mask & (1 << id)) == 0) continue;
                        string typeName = null;
                        foreach (var material in m_DetailSourceMaterials)
                            if (material != null && material.HasProperty("_GrassUseArrays")
                                && MGGrassDetailLayers.SlotInfo(material, id, out string slotName, out Texture2D source) && source != null)
                            { typeName = slotName; break; }
                        types.Add("Sub-ID " + id + (string.IsNullOrEmpty(typeName) ? "" : " · " + typeName));
                    }
                    string text = types.Count > 0 ? string.Join("     •     ", types) : "No Sub-IDs checked";
                    EditorGUILayout.LabelField(new GUIContent(text, text), EditorStyles.wordWrappedLabel);
                }
            }
            GUILayout.Space(4);
        }


        int CheckedDetailTypeCount(MGTerrain terrain)
        {
            int count = 0;
            foreach (int prototype in m_SelectedDetailPrototypes)
            {
                if (!UsesDetailSubIds(terrain, prototype)) continue;
                int mask = DetailSubIdMask(terrain, prototype, DetailPopulation(prototype));
                for (int id = 0; id < 8; id++) if ((mask & (1 << id)) != 0) count++;
            }
            return count;
        }

        void SetDetailSettingsTarget(MGTerrain terrain, int prototype)
        {
            if (!m_SelectedDetailPrototypes.Contains(prototype)) return;
            FinishDetailStroke();
            m_SelectedPrototype = prototype;
            m_GrassPaintPopulation = DetailPopulation(prototype);
            if (UsesDetailSubIds(terrain, prototype)) SyncDetailSubIdSelection(terrain);
            RefreshDetailPaintTargets(terrain);
            if (m_WorldAutoDetailBrush) UpdateWorldDetailBrush(terrain);
            Repaint();
            SceneView.RepaintAll();
        }

        void DrawDetailSettingsTarget(MGTerrain terrain)
        {
            if (m_SelectedDetailPrototypes.Count < 2) return;
            var prototypes = m_SelectedDetailPrototypes.OrderBy(index => index).ToArray();
            var labels = new string[prototypes.Length];
            for (int i = 0; i < prototypes.Length; i++)
            {
                var definition = terrain.Prototypes[prototypes[i]];
                string name = definition.Prefab != null ? definition.Prefab.name : definition.Mesh != null ? definition.Mesh.name : "Detail";
                labels[i] = "ID " + prototypes[i] + " · " + name + (UsesDetailSubIds(terrain, prototypes[i]) ? " · Population " + (DetailPopulation(prototypes[i]) == 0 ? "A" : "B") : "");
            }
            int active = System.Array.IndexOf(prototypes, m_SelectedPrototype);
            int next = EditorGUILayout.Popup(new GUIContent("Editing", "Choose which selected detail's settings to edit. This does not change the layers selected for painting or copy values between them."), Mathf.Max(0, active), labels);
            if (next == active) return;
            SetDetailSettingsTarget(terrain, prototypes[next]);
            GUIUtility.ExitGUI();
        }

        void DrawSelectedDetailSubIdRows(MGTerrain terrain)
        {
            EnsureDetailPrototypeSelection(terrain);
            int active = m_SelectedPrototype, population = m_GrassPaintPopulation;
            try
            {
                foreach (int prototype in m_SelectedDetailPrototypes.OrderBy(index => index).ToArray())
                {
                    if ((uint)prototype >= terrain.Prototypes.Count || !UsesDetailSubIds(terrain, prototype)) continue;
                    m_SelectedPrototype = prototype;
                    m_GrassPaintPopulation = DetailPopulation(prototype);
                    m_GrassSubIdMask = DetailSubIdMask(terrain, prototype, m_GrassPaintPopulation);
                    m_RandomGrassSubIds = true;
                    if (m_SelectedDetailPrototypes.Count > 1)
                    {
                        var definition = terrain.Prototypes[prototype];
                        string name = definition.Prefab != null ? definition.Prefab.name : definition.Mesh != null ? definition.Mesh.name : "Detail";
                        EditorGUILayout.LabelField($"ID {prototype} · {name} · Population {(m_GrassPaintPopulation == 0 ? "A" : "B")}", EditorStyles.boldLabel);
                    }
                    DrawGrassSubIdGrid(terrain, false);
                }
            }
            finally
            {
                m_SelectedPrototype = active;
                m_GrassPaintPopulation = population;
                if ((uint)active < terrain.Prototypes.Count && UsesDetailSubIds(terrain, active)) SyncDetailSubIdSelection(terrain);
                RefreshDetailPaintTargets(terrain);
                if (m_WorldAutoDetailBrush) UpdateWorldDetailBrush(terrain);
            }
        }
    }
}
#endif

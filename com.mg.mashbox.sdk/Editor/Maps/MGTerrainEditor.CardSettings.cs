#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        static Rect DetailCardSettingsRect(Rect cell) => new Rect(cell.x + 3, cell.y + 3, 17, 16);

        void DrawDetailCardSettings(Rect cell, int prototype, int population, int subId = -1)
        {
            Rect rect = DetailCardSettingsRect(cell);
            bool clicked;
            using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
                clicked = GUI.Button(rect, new GUIContent("", subId < 0
                    ? "Settings for detail ID " + prototype + ", Population " + (population == 0 ? "A" : "B")
                    : "Material settings for Sub-ID " + subId), GUIStyle.none);
            using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
                MGTerrainToolbarIcons.DrawSettingsIcon(rect);
            if (!clicked) return;
            FinishDetailStroke();
            serializedObject.ApplyModifiedProperties();
            PopupWindow.Show(rect, new DetailCardSettingsPopup(this, prototype, population, subId));
            GUIUtility.ExitGUI();
        }

        // Exact prefix matching prevents editing a neighbouring Sub-ID or shared fallback value.
        static MaterialProperty[] DetailSubIdMaterialProperties(Material material, int subId)
        {
            if (material == null || (uint)subId >= 8) return Array.Empty<MaterialProperty>();
            string prefix = "_GrassLayer" + subId.ToString("00") + "_";
            return MaterialEditor.GetMaterialProperties(new UnityEngine.Object[] { material })
                .Where(property => property.name.StartsWith(prefix, StringComparison.Ordinal)
                    && property.name.IndexOf("Seed", StringComparison.OrdinalIgnoreCase) < 0).ToArray();
        }

        // Godgrass intentionally hides per-index properties from the default inspector.
        // They are editable here because the exact Sub-ID prefix defines their scope.
        static void SetDetailSubIdOverrides(Material material, bool enabled)
        {
            const string toggle = "_GrassUseLayerParameters", version = "_GrassLayerParametersVersion";
            if (!material.HasProperty(toggle)) return;
            if (material.HasProperty(version) && material.GetFloat(version) < 1)
            {
                var shader = material.shader;
                for (int p = 0; p < shader.GetPropertyCount(); p++)
                {
                    string source = shader.GetPropertyName(p);
                    if (!material.HasProperty("_GrassLayer00_" + source.TrimStart('_'))) continue;
                    for (int id = 0; id < 8; id++)
                    {
                        string destination = "_GrassLayer" + id.ToString("00") + "_" + source.TrimStart('_');
                        if (!material.HasProperty(destination)) continue;
                        if (shader.GetPropertyType(p) == UnityEngine.Rendering.ShaderPropertyType.Color)
                            material.SetColor(destination, material.GetColor(source));
                        else if (shader.GetPropertyType(p) == UnityEngine.Rendering.ShaderPropertyType.Vector)
                            material.SetVector(destination, material.GetVector(source));
                        else if (shader.GetPropertyType(p) == UnityEngine.Rendering.ShaderPropertyType.Float
                            || shader.GetPropertyType(p) == UnityEngine.Rendering.ShaderPropertyType.Range)
                            material.SetFloat(destination, material.GetFloat(source));
                    }
                }
                if (material.HasProperty("_GrassSharedShadowMode") && material.HasProperty("_UseShadowThreshold"))
                    material.SetFloat("_GrassSharedShadowMode", material.GetFloat("_UseShadowThreshold"));
                material.SetFloat(version, 1);
            }
            if (material.HasProperty("_GrassSharedShadowMode") && material.HasProperty("_UseShadowThreshold"))
            {
                if (enabled && material.GetFloat(toggle) < .5f)
                    material.SetFloat("_GrassSharedShadowMode", material.GetFloat("_UseShadowThreshold"));
                material.SetFloat("_UseShadowThreshold", enabled ? 1 : material.GetFloat("_GrassSharedShadowMode"));
            }
            material.SetFloat(toggle, enabled ? 1 : 0);
            EditorUtility.SetDirty(material);
        }

        static string DetailMaterialPropertyGroup(MaterialProperty property)
        {
            string name = property.name;
            if (name.IndexOf("Wind", StringComparison.OrdinalIgnoreCase) >= 0 || name.Contains("Multiply_by_Object_Scale")) return "Wind";
            if (name.IndexOf("Health", StringComparison.OrdinalIgnoreCase) >= 0 || name.Contains("Dry")) return "Health";
            if (name.Contains("Width") || name.Contains("Height") || name.Contains("Size")) return "Size";
            return "Appearance";
        }

        sealed class DetailCardSettingsPopup : PopupWindowContent
        {
            readonly MGTerrainEditor m_Owner;
            readonly int m_Prototype, m_Population, m_SubId;
            readonly Dictionary<Material, MaterialEditor> m_Editors = new Dictionary<Material, MaterialEditor>();
            Vector2 m_Scroll;
            internal DetailCardSettingsPopup(MGTerrainEditor owner, int prototype, int population, int subId)
            { m_Owner = owner; m_Prototype = prototype; m_Population = population; m_SubId = subId; }

            public override Vector2 GetWindowSize() => new Vector2(560, 560);
            public override void OnClose()
            {
                foreach (var editor in m_Editors.Values) if (editor != null) DestroyImmediate(editor);
                m_Editors.Clear();
            }

            public override void OnGUI(Rect rect)
            {
                if (m_Owner == null || !(m_Owner.target is MGTerrain terrain)
                    || (uint)m_Prototype >= terrain.Prototypes.Count) { editorWindow.Close(); return; }
                float labelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 195;
                m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
                try
                {
                    GUILayout.Space(6);
                    var definition = terrain.Prototypes[m_Prototype];
                    string name = definition.Prefab != null ? definition.Prefab.name : definition.Mesh != null ? definition.Mesh.name : "Detail";
                    EditorGUILayout.LabelField(m_SubId < 0 ? "Detail Settings" : "Sub-ID " + m_SubId + " Settings", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("ID " + m_Prototype + " · " + name + (m_SubId < 0 ? " · Population " + (m_Population == 0 ? "A" : "B") : ""), EditorStyles.wordWrappedMiniLabel);
                    GUILayout.Space(6);
                    if (m_SubId < 0) DrawPrototypeSettings(terrain);
                    else DrawSubIdSettings(terrain);
                }
                finally { EditorGUILayout.EndScrollView(); EditorGUIUtility.labelWidth = labelWidth; }
            }

            void DrawPrototypeSettings(MGTerrain terrain)
            {
                int prototype = m_Owner.m_SelectedPrototype, population = m_Owner.m_GrassPaintPopulation, layer = m_Owner.m_PaintDetailIndex;
                bool previous = m_Owner.m_WorldDetailControls;
                bool shared = terrain.World != null;
                if (shared) m_Owner.PrepareSharedWorldDetails(terrain);
                m_Owner.serializedObject.Update();
                m_Owner.m_SelectedPrototype = m_Prototype;
                m_Owner.m_GrassPaintPopulation = m_Population;
                m_Owner.m_WorldDetailControls = shared;
                try { m_Owner.DrawSelectedPrototypeSettings(terrain, true); }
                finally
                {
                    m_Owner.serializedObject.ApplyModifiedProperties();
                    try { if (shared) m_Owner.CommitSharedWorldDetails(terrain); }
                    finally
                    {
                        m_Owner.m_SelectedPrototype = prototype;
                        m_Owner.m_GrassPaintPopulation = population;
                        m_Owner.m_PaintDetailIndex = layer;
                        m_Owner.m_WorldDetailControls = previous;
                        m_Owner.RefreshDetailPaintTargets(terrain);
                        m_Owner.Repaint();
                    }
                }
            }

            void DrawSubIdSettings(MGTerrain terrain)
            {
                var materials = new List<Material>();
                terrain.GetDetailSourceMaterials(m_Prototype, materials);
                bool found = false;
                foreach (var material in materials)
                {
                    var properties = DetailSubIdMaterialProperties(material, m_SubId);
                    if (properties.Length == 0) continue;
                    found = true;
                    if (!m_Editors.TryGetValue(material, out var editor) || editor == null)
                        m_Editors[material] = editor = (MaterialEditor)UnityEditor.Editor.CreateEditor(material, typeof(MaterialEditor));
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("Material", material, typeof(Material), false);
                        EditorGUILayout.LabelField(new GUIContent("Shared material · all populations using this material", "These shader overrides affect this Sub-ID wherever this material is used. Population ranges and painted size multiply these values."), EditorStyles.wordWrappedMiniLabel);
                        var all = MaterialEditor.GetMaterialProperties(new UnityEngine.Object[] { material });
                        var enabled = all.FirstOrDefault(property => property.name == "_GrassUseLayerParameters");
                        EditorGUI.BeginChangeCheck();
                        bool overrides = enabled == null || enabled.floatValue > .5f;
                        if (enabled != null)
                        {
                            bool next = EditorGUILayout.Toggle(new GUIContent("Per-Sub-ID Overrides", "Enable independent shader parameters for each Sub-ID. First activation copies the shared values, matching the Godgrass material inspector."), overrides);
                            if (next != overrides)
                            {
                                editor.RegisterPropertyChangeUndo("Toggle Detail Sub-ID Overrides");
                                SetDetailSubIdOverrides(material, next);
                                overrides = next;
                            }
                        }
                        using (new EditorGUI.DisabledScope(!overrides))
                            foreach (string group in new[] { "Size", "Wind", "Appearance", "Health" })
                            {
                                var entries = properties.Where(property => DetailMaterialPropertyGroup(property) == group).ToArray();
                                if (entries.Length == 0) continue;
                                GUILayout.Space(5);
                                EditorGUILayout.LabelField(group, EditorStyles.boldLabel);
                                foreach (var property in entries)
                                {
                                    string label = property.displayName;
                                    int suffix = label.LastIndexOf(" / Layer ", StringComparison.Ordinal);
                                    if (suffix >= 0) label = label.Substring(0, suffix);
                                    editor.ShaderProperty(property, label.Replace("Grass ", "Detail "));
                                }
                            }
                        if (EditorGUI.EndChangeCheck())
                        {
                            editor.PropertiesChanged();
                            EditorUtility.SetDirty(material);
                            m_Owner.ClearDetailPrototypeThumbnails();
                            if (terrain.World != null) { foreach (var tile in SharedTiles(terrain)) if (tile != null) tile.InvalidateRenderCache(); }
                            else terrain.InvalidateRenderCache();
                            m_Owner.Repaint();
                            SceneView.RepaintAll();
                        }
                    }
                }
                if (!found) EditorGUILayout.HelpBox("This detail's material does not expose per-Sub-ID settings.", MessageType.None);
            }
        }
    }
}
#endif

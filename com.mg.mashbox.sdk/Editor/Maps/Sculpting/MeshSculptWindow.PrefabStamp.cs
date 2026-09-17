using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MeshSculptWindow
    {
        enum StampSourceKind { Mesh, Prefab }
        [SerializeField] StampSourceKind m_StampSourceKind;
        [SerializeField] GameObject m_StampPrefab;
        [SerializeField] bool m_PrefabMaterialPreview = true;
        [SerializeField] bool m_StampColour, m_StampNormals;
        PrefabStampSource m_PrefabStamp;
        [SerializeField] Texture m_LastStampedNormal;
        [SerializeField] string m_LastAppearanceResult;
        readonly List<PrefabStampAppearance.Dab> m_AppearanceDabs = new List<PrefabStampAppearance.Dab>();
        Mesh StampMesh => m_StampSourceKind == StampSourceKind.Prefab ? m_PrefabStamp?.Mesh : m_StampMesh;

        void DrawStampSource()
        {
            EditorGUI.BeginChangeCheck();
            m_StampSourceKind = (StampSourceKind)EditorGUILayout.EnumPopup("Stamp Source", m_StampSourceKind);
            if (m_StampSourceKind == StampSourceKind.Mesh)
                m_StampMesh = (Mesh)EditorGUILayout.ObjectField("Stamp Mesh", m_StampMesh, typeof(Mesh), false);
            else
                m_StampPrefab = (GameObject)EditorGUILayout.ObjectField("Stamp Prefab", m_StampPrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                ReleasePrefabStamp(); m_MeshStampBrush = null; m_FailedStampMesh = null; m_StampReadError = null;
            }
            if (m_StampSourceKind != StampSourceKind.Prefab) return;
            if (m_StampPrefab != null && m_PrefabStamp == null && string.IsNullOrEmpty(m_StampReadError))
            {
                try { m_PrefabStamp = new PrefabStampSource(m_StampPrefab); }
                catch (Exception error) { m_StampReadError = error.Message; }
            }
            m_PrefabMaterialPreview = EditorGUILayout.Toggle(new GUIContent("Materials", "Show the prefab materials on the live stamp preview."), m_PrefabMaterialPreview);
            m_StampColour = EditorGUILayout.Toggle(new GUIContent("Stamp RGB", "Transfer prefab base colour into each affected tile's far-range appearance map when the stroke ends."), m_StampColour);
            m_StampNormals = EditorGUILayout.Toggle(new GUIContent("Stamp Normals", "Project the prefab material normal into world space and replace the far-range normal map inside the stamp. Falloff blends the edges."), m_StampNormals);
            if (m_StampColour || m_StampNormals)
            {
                var setupTiles = new List<MGTerrain>();
                var setupWorld = SelectedTerrainWorld;
                if (setupWorld != null)
                {
                    foreach (var tile in setupWorld.Chunks)
                        if (tile != null && tile.isActiveAndEnabled && MGTerrainStampMapSetup.NeedsMaps(tile)) setupTiles.Add(tile);
                }
                else
                {
                    var selectedTile = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<MGTerrain>() : null;
                    if (selectedTile != null && MGTerrainStampMapSetup.NeedsMaps(selectedTile)) setupTiles.Add(selectedTile);
                }
                if (setupTiles.Count > 0)
                {
                    EditorGUILayout.HelpBox(setupTiles.Count + " tile(s) need appearance maps. Generate them once before stamping.", MessageType.Info);
                    bool createBlank = GUILayout.Button(new GUIContent("Create Blank Maps", "Create neutral-grey RGB and flat world-space normals without capturing terrain or details. Only missing maps are created. Unstamped areas use the blank colour."));
                    bool bakeExisting = GUILayout.Button(new GUIContent("Bake Terrain Appearance", "Capture the terrain's current appearance into missing maps. Existing maps are preserved."));
                    if (createBlank || bakeExisting)
                    {
                        int completed = 0;
                        try
                        {
                            foreach (var tile in setupTiles) { MGTerrainStampMapSetup.Generate(tile, createBlank); completed++; }
                            m_StampReadError = null;
                            m_LastAppearanceResult = "Appearance maps ready on " + completed + " tile(s). You can stamp now.";
                        }
                        catch (Exception error) { m_StampReadError = error.Message; Debug.LogException(error); }
                        Repaint(); SceneView.RepaintAll();
                    }
                }
                if (!string.IsNullOrEmpty(m_LastAppearanceResult)) EditorGUILayout.HelpBox(m_LastAppearanceResult, MessageType.None);
                if (m_LastStampedNormal != null)
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.ObjectField(new GUIContent("Last Normal Map", "The new map assigned by your last normal stamp. Click to inspect it. Original source textures remain unchanged."), m_LastStampedNormal, typeof(Texture), false);
                EditorGUILayout.HelpBox("Maps apply on release into assigned terrain maps, preserving their resolution. RGB transfers unlit base colour; normals follow the stamp surface. Undo restores the previous maps.", MessageType.None);
            }
        }

        void ReleasePrefabStamp() { m_PrefabStamp?.Dispose(); m_PrefabStamp = null; }

        bool ValidateStampAppearance(MGTerrain tile)
        {
            if (m_StampSourceKind != StampSourceKind.Prefab || (!m_StampColour && !m_StampNormals)) return true;
            try { PrefabStampAppearance.ValidateTarget(tile, m_StampColour, m_StampNormals); return true; }
            catch (Exception error) { m_StampReadError = error.Message; Repaint(); return false; }
        }
        void QueueStampAppearance(MGTerrain terrain, Vector3 center, bool invert)
        {
            if (terrain == null || m_StampSourceKind != StampSourceKind.Prefab || m_PrefabStamp == null || (!m_StampColour && !m_StampNormals)) return;
            m_AppearanceDabs.Add(new PrefabStampAppearance.Dab { tile = terrain, center = center, radius = m_Radius,
                height = m_StampHeight, rotation = m_StampRotation, falloff = m_Falloff, invert = invert });
        }

        void CommitStampAppearance()
        {
            if (m_AppearanceDabs.Count == 0) return;
            try
            {
                PrefabStampAppearance.Apply(m_PrefabStamp, m_MeshStampBrush, m_AppearanceDabs, m_StampColour, m_StampNormals);
                var tiles = new HashSet<MGTerrain>();
                foreach (var dab in m_AppearanceDabs) if (dab.tile != null) tiles.Add(dab.tile);
                m_LastStampedNormal = null;
                if (m_StampNormals)
                    foreach (var tile in tiles)
                    {
                        m_LastStampedNormal = tile.MeshRenderer.sharedMaterial.GetTexture(MGTerrainAppearanceCaptureAssets.NormalProperty);
                        break;
                    }
                m_LastAppearanceResult = (m_StampNormals ? "Normal map" + (m_StampColour ? " and RGB" : "") : "RGB map")
                    + " updated on " + tiles.Count + " tile(s). New maps assigned; original textures unchanged.";
                Repaint(); SceneView.RepaintAll();
            }
            catch (Exception error)
            {
                m_StampReadError = "Geometry stamped, but appearance maps were not assigned: " + error.Message;
                Debug.LogException(error); Repaint();
            }
            finally { m_AppearanceDabs.Clear(); }
        }

        void DrawPrefabStampPreview(Vector3 center, bool invert, List<MGTerrain> tiles)
        {
            if (m_StampSourceKind != StampSourceKind.Prefab || !m_PrefabMaterialPreview || m_PrefabStamp == null) return;
            float? Surface(Vector3 point)
            {
                foreach (var tile in tiles)
                {
                    var b = MGTerrainTileAuthoring.BoundsOf(tile);
                    if (point.x < b.min.x || point.x > b.max.x || point.z < b.min.z || point.z > b.max.z) continue;
                    if (tile.RaycastSurface(new Ray(new Vector3(point.x, b.max.y + 1, point.z), Vector3.down), out var hit, b.size.y + 2)) return hit.point.y + .02f;
                }
                return null;
            }
            m_PrefabStamp.Draw(center, m_Radius, m_StampHeight, m_StampRotation, m_Falloff, invert, Surface);
        }
    }
}

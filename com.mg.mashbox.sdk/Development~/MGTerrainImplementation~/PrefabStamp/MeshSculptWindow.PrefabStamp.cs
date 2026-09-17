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
        [SerializeField] int m_StampMapResolution = 1024;
        [SerializeField] float m_StampExposure = 10;
        PrefabStampSource m_PrefabStamp;
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
            m_StampColour = EditorGUILayout.Toggle(new GUIContent("Stamp RGB", "Composite rendered prefab colour into each affected tile's far-range appearance map when the stroke ends."), m_StampColour);
            m_StampNormals = EditorGUILayout.Toggle(new GUIContent("Stamp Normals", "Capture material and geometry normals in world space and blend into the far-range normal map."), m_StampNormals);
            if (m_StampColour || m_StampNormals)
            {
                m_StampMapResolution = EditorGUILayout.IntPopup("Capture", m_StampMapResolution, new[] { "512", "1K", "2K" }, new[] { 512, 1024, 2048 });
                m_StampExposure = EditorGUILayout.FloatField(new GUIContent("Exposure", "Fixed EV100 for rendered colour. Includes material shading and directional lighting."), m_StampExposure);
                if (float.IsNaN(m_StampExposure) || float.IsInfinity(m_StampExposure)) m_StampExposure = 10;
                EditorGUILayout.HelpBox("Maps apply on release into existing baked terrain maps, preserving their resolution. RGB includes lighting. Undo restores the previous maps.", MessageType.None);
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
            try { PrefabStampAppearance.Apply(m_PrefabStamp, m_MeshStampBrush, m_AppearanceDabs, m_StampColour, m_StampNormals, m_StampMapResolution, m_StampExposure); }
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

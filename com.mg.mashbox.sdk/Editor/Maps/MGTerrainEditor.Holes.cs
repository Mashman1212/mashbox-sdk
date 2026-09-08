#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        bool m_HolePainting, m_RestoreHoles;
        Tool m_HolePreviousTool;
        bool m_HolePreviousEditing;
        int m_HoleStroke = -1;
        Vector3 m_LastHoleDab;

        void DrawHolePainter(MGTerrain terrain)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Terrain Holes", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(Application.isPlaying || terrain.MeshFilter == null || terrain.MeshFilter.sharedMesh == null))
            {
                bool active = GUILayout.Toggle(m_HolePainting, "Hole Cutting Brush", "Button");
                if (active != m_HolePainting) SetHolePainting(active);
            }
            if (!m_HolePainting) return;
            m_RestoreHoles = GUILayout.Toolbar(m_RestoreHoles ? 1 : 0, new[] { "Cut Faces", "Restore Faces" }) == 1;
            MBEditorToolState.BrushRadius = EditorGUILayout.Slider("Brush Radius", MBEditorToolState.BrushRadius, .1f, MBEditorToolState.MaxBrushRadius);
            EditorGUILayout.HelpBox("Drag to cut or restore faces and their collision. Shift reverses the mode. Escape exits. Faces are selected by their centers; small holes need sufficiently dense geometry.", MessageType.Info);
        }

        void SetHolePainting(bool active)
        {
            if (active == m_HolePainting) return;
            FinishHoleStroke();
            if (active)
            {
                SetDetailPainting(false);
                m_HolePreviousTool = Tools.current;
                m_HolePreviousEditing = MBEditorToolState.ActiveEditing;
                MBEditorToolState.ActiveEditing = false;
                Tools.current = Tool.None;
            }
            else
            {
                Tools.current = m_HolePreviousTool;
                MBEditorToolState.ActiveEditing = m_HolePreviousEditing;
            }
            m_HolePainting = active;
            SceneView.RepaintAll();
            Repaint();
        }

        void FinishHoleStroke()
        {
            if (m_HoleStroke < 0) return;
            Undo.CollapseUndoOperations(m_HoleStroke);
            m_HoleStroke = -1;
            GUIUtility.hotControl = 0;
            // Meshes remain dirty and are persisted by the normal editor save workflow.
        }

        void BeginHoleStroke(MGTerrain terrain)
        {
            Undo.IncrementCurrentGroup();
            m_HoleStroke = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paint Terrain Holes");
            Undo.RegisterCompleteObjectUndo(terrain, "Paint Terrain Holes");
            // Older/sculpted terrains may no longer have recoverable chunk vertex maps.
            // Repair those chunks within this stroke's Undo group, then paint normally.
            if (!terrain.TryPrepareHoleColliderMappings())
            {
                BuildSurfaceColliders(terrain, m_ColliderCellSize, saveAssets: false);
                serializedObject.Update();
            }
            bool initialize = !terrain.HasHoleData;
            terrain.InitializeSurfaceHoles();
            if (initialize)
            {
                // Make persistent private copies, leaving imported/shared mesh assets intact.
                const string folder = "Assets/MGTerrainHoles";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "MGTerrainHoles");
                string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/TerrainHoles.asset");
                Mesh source = Instantiate(terrain.MeshFilter.sharedMesh);
                source.name = terrain.MeshFilter.sharedMesh.name + " Holes";
                AssetDatabase.CreateAsset(source, path);
                Undo.RecordObject(terrain.MeshFilter, "Paint Terrain Holes");
                terrain.MeshFilter.sharedMesh = source;
                foreach (var collider in terrain.SurfaceColliderChunks)
                {
                    if (collider == null || collider.sharedMesh == null) continue;
                    Undo.RecordObject(collider, "Paint Terrain Holes");
                    Mesh copy = Instantiate(collider.sharedMesh);
                    copy.name = collider.sharedMesh.name;
                    AssetDatabase.AddObjectToAsset(copy, path);
                    collider.sharedMesh = copy;
                }
            }
            var objects = new List<UnityEngine.Object> { terrain.MeshFilter.sharedMesh };
            if (terrain.MeshCollider != null) objects.Add(terrain.MeshCollider);
            foreach (var collider in terrain.SurfaceColliderChunks)
            {
                if (collider == null) continue;
                objects.Add(collider);
                if (collider.sharedMesh != null) objects.Add(collider.sharedMesh);
            }
            foreach (Mesh mesh in terrain.GetHoleColliderMeshes())
                if (!objects.Contains(mesh)) objects.Add(mesh);
            Undo.RegisterCompleteObjectUndo(objects.ToArray(), "Paint Terrain Holes");
        }

        void DrawHoleSceneGUI()
        {
            if (!m_HolePainting) return;
            if (Application.isPlaying || MBEditorToolState.ActiveEditing || Tools.current != Tool.None)
            { SetHolePainting(false); return; }
            Event e = Event.current;
            int control = GUIUtility.GetControlID("MGTerrainHoles".GetHashCode(), FocusType.Passive);
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            { SetHolePainting(false); e.Use(); return; }
            if (e.rawType == EventType.MouseUp || e.type == EventType.Ignore)
            {
                bool wasPainting = m_HoleStroke >= 0;
                FinishHoleStroke();
                if (wasPainting && e.type != EventType.Ignore) e.Use();
                // Releasing the mouse needs no further mesh picking or allocations.
                return;
            }
            if (e.alt || Tools.viewToolActive) return;
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(control);
                return;
            }
            var terrain = (MGTerrain)target;
            if (!terrain.RaycastSurfaceIncludingHoles(HandleUtility.GUIPointToWorldRay(e.mousePosition), out Vector3 point, out Vector3 normal)) return;
            bool restore = m_RestoreHoles ^ e.shift;
            float radius = MBEditorToolState.BrushRadius;
            Handles.color = restore ? Color.green : Color.red;
            Handles.DrawWireDisc(point, normal, radius);
            MBEditorToolVisuals.DrawBrushAction(restore ? "Restore Terrain Faces" : "Cut Terrain Hole");
            if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            if (e.button != 0 || (e.type != EventType.MouseDown && e.type != EventType.MouseDrag)) return;
            try
            {
                if (e.type == EventType.MouseDown)
                {
                    BeginHoleStroke(terrain);
                    GUIUtility.hotControl = control;
                    m_LastHoleDab = point;
                    terrain.PaintSurfaceHoles(point, radius, restore);
                }
                else if (m_HoleStroke >= 0)
                {
                    int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(m_LastHoleDab, point) / Mathf.Max(.05f, radius * .2f)), 1, 128);
                    bool changed = false;
                    for (int i = 1; i <= steps; i++)
                        changed |= terrain.PaintSurfaceHoles(Vector3.Lerp(m_LastHoleDab, point, i / (float)steps), radius, restore, false);
                    if (changed) terrain.ApplySurfaceHoles();
                    m_LastHoleDab = point;
                }
                EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
                SceneView.RepaintAll();
            }
            catch (Exception exception)
            {
                FinishHoleStroke();
                SetHolePainting(false);
                Debug.LogException(exception, terrain);
            }
            e.Use();
        }
    }
}
#endif

using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    [CustomEditor(typeof(MGGrassInteractionMap))]
    internal sealed class MGGrassInteractionMapEditor : Editor
    {
        double nextPreviewRepaint;
        void OnEnable() { EditorApplication.update += RefreshPreview; }
        void OnDisable() { EditorApplication.update -= RefreshPreview; }
        void RefreshPreview()
        {
            if (target == null || EditorApplication.timeSinceStartup < nextPreviewRepaint) return;
            nextPreviewRepaint = EditorApplication.timeSinceStartup + 0.2;
            var map = (MGGrassInteractionMap)target;
            if (Application.isPlaying || map.previewInEditMode) Repaint();
        }
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var map = (MGGrassInteractionMap)target;
            int resolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(map.resolution, 64, 2048));
            EditorGUILayout.HelpBox($"{resolution} x {resolution}: {Mathf.Max(4, map.worldSize) / resolution:0.###} m/texel, {resolution * resolution * 16L / 1048576f:0.##} MiB for both GPU buffers. One moving window across all tiles; tracks outside it are discarded. The window always follows the Main Camera. Interaction pauses when no Main Camera is available.", MessageType.Info);
            if (MGGrassInteractionMap.Active != null && MGGrassInteractionMap.Active != map)
                EditorGUILayout.HelpBox("Another enabled interaction map owns the shader globals. Only one map can be active at a time.", MessageType.Warning);
            EditorGUILayout.LabelField("Last update", $"{map.LastStampCount} stamps / {map.LastUpdatedTexels:N0} texels");
            if (GUILayout.Button("Clear Interaction Map")) map.ClearMap();
            if (map.InteractionTexture != null)
            {
                Rect rect = GUILayoutUtility.GetAspectRect(1);
                // Alpha is pressure; signed direction / premultiplied height are not display colors.
                EditorGUI.DrawTextureAlpha(rect, map.InteractionTexture);
            }
        }

        [MenuItem("Tools/MashBox/MG Terrain/Interaction/Add Map to Selected World")]
        static void AddMap()
        {
            var world = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<MGTerrainWorld>() : null;
            if (world == null)
            {
                Debug.LogWarning("Select an MG Terrain World or one of its children first.");
                return;
            }
            var map = world.GetComponent<MGGrassInteractionMap>();
            if (map == null) map = Undo.AddComponent<MGGrassInteractionMap>(world.gameObject);
            Selection.activeObject = map;
            EditorGUIUtility.PingObject(map);
        }
        [MenuItem("Tools/MashBox/MG Terrain/Interaction/Add Brush to Selected Objects")]
        static void AddBrushes()
        {
            foreach (var go in Selection.gameObjects)
            {
                if (!go.scene.IsValid()) continue;
                var brush = go.GetComponent<MGGrassInteractor>();
                if (brush == null) brush = Undo.AddComponent<MGGrassInteractor>(go);
                Undo.RecordObject(brush, "Set Grass Brush Collider");
                if (brush.sourceCollider == null) brush.sourceCollider = go.GetComponent<Collider>();
                EditorUtility.SetDirty(brush);
            }
        }
    }
}

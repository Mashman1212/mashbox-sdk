using UnityEditor;
using UnityEngine;
using MashBoxSDK.Maps.TerrainSystem;
namespace MashBoxSDK.MapTools
{
    public partial class MGTerrainEditor
    {
        void DrawDetailCapsuleSettings(MGTerrain terrain, SerializedProperty layer)
        {
            var settings = layer.FindPropertyRelative("m_CapsuleCollision");
            if (settings == null) return;
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("enabled"), new GUIContent("Tree Capsule Collision", "Enable trunk collision for this painted detail, including trees marked as Detail. Applies to existing and new painting."));
            if (!settings.FindPropertyRelative("enabled").boolValue) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(settings.FindPropertyRelative("capsule"), new GUIContent("Trunk Capsule"), true);
                EditorGUILayout.PropertyField(settings.FindPropertyRelative("subIdOverrides"), new GUIContent("Tree Sub-ID Overrides"), true);
                EditorGUILayout.PropertyField(settings.FindPropertyRelative("material"), new GUIContent("Physics Material"));
                var collisionLayer = settings.FindPropertyRelative("layer");
                collisionLayer.intValue = EditorGUILayout.LayerField("Collision Layer", collisionLayer.intValue);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_DetailCollisionTarget"), new GUIContent("Player / Collision Target"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_DetailCollisionRadius"), new GUIContent("Collision Radius (m)"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_PreviewDetailColliders"), new GUIContent("Preview Trunk Capsules"));
                EditorGUILayout.LabelField("Nearby capsules", terrain.ActiveDetailColliderCount.ToString("N0"));
                EditorGUILayout.HelpBox("Capsules follow painted position, rotation, random size, Size painting and Y Offset. Dimensions are in prototype-local units. Override individual tree IDs when their trunks differ. Shader-only wind, health scaling and custom deformation do not move collision: fit these as stable trunk proxies. Collision-enabled layers keep their full painted population instead of density thinning. Physics streams around the target (gameplay camera if empty), independent of visual cell culling.", MessageType.None);
            }
        }
    }
    [InitializeOnLoad] static class MGDetailColliderPreview
    {
        static double next;
        static MGDetailColliderPreview() { EditorApplication.update += Update; SceneView.duringSceneGui += Draw; }
        static void Update()
        {
            if (Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + .2;
            var view = SceneView.lastActiveSceneView;
            foreach (var terrain in MGTerrain.EditorActiveTerrains)
            {
                if (!terrain) continue;
                if (terrain.PreviewDetailColliders && view && view.camera) terrain.UpdateDetailColliders(view.camera.transform.position);
                else if (terrain.HasDetailColliderCache) terrain.ReleaseDetailColliders();
            }
        }
        static void Draw(SceneView view)
        {
            if (Event.current.type != EventType.Repaint || Application.isPlaying) return;
            foreach (var terrain in MGTerrain.EditorActiveTerrains) if (terrain) terrain.DrawDetailColliderPreview();
        }
    }
}

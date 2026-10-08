using System;
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools.WorldBorders
{
    public static class MBWorldBorderAuthoring
    {
        public static Material DefaultMaterial => AssetDatabase.LoadAssetAtPath<Material>(MGWorldBorderBuilder.DefaultMaterialPath);
        public static Transform Group()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == "World Border Proxies" && root.transform.localScale == Vector3.one) return root.transform;
            var group = new GameObject("World Border Proxies");
            Undo.RegisterCreatedObjectUndo(group, "Create border proxy group");
            return group.transform;
        }

        public static MBWorldBorderWall CreateWall(Transform parent, Vector3 centre, Quaternion rotation, Vector3 size)
        {
            var go = new GameObject("Border Wall");
            Undo.RegisterCreatedObjectUndo(go, "Create border wall proxy");
            go.transform.SetPositionAndRotation(centre, rotation);
            go.transform.localScale = size;
            go.transform.SetParent(parent, true);
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            var wall = Undo.AddComponent<MBWorldBorderWall>(go);
            wall.materialTemplate = DefaultMaterial;
            EditorUtility.SetDirty(wall);
            return wall;
        }

        public static GameObject CreateRectangle(Vector3 baseCentre)
        {
            Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create four border wall proxies");
            var group = new GameObject("World Border Proxies");
            Undo.RegisterCreatedObjectUndo(group, "Create border proxy group");
            group.transform.position = baseCentre;
            for (int i = 0; i < 4; i++)
            {
                var rotation = Quaternion.Euler(0, i * 90, 0);
                CreateWall(group.transform, baseCentre + rotation * new Vector3(0, 64, 512), rotation, new Vector3(1024, 128, .5f));
            }
            Undo.CollapseUndoOperations(undo);
            Selection.activeGameObject = group;
            EditorSceneManager.MarkSceneDirty(group.scene);
            return group;
        }

        public static bool CanConvert(GameObject root) => root && root.transform.Find("Visual Tiles") &&
            root.transform.Find("Collision Walls") && root.transform.Find("Collision Walls").GetComponentsInChildren<BoxCollider>(true).Length > 0;

        // Only replaces groups owned by the previous SDK builder; root and unrelated children survive.
        public static void ConvertBakedBorder(GameObject root)
        {
            if (!CanConvert(root)) throw new InvalidOperationException("Select the root of a border built by the previous SDK tool.");
            var visual = root.transform.Find("Visual Tiles");
            var collision = root.transform.Find("Collision Walls");
            var renderer = visual.GetComponentInChildren<MeshRenderer>(true);
            var template = renderer ? renderer.sharedMaterial : DefaultMaterial;
            var colliders = collision.GetComponentsInChildren<BoxCollider>(true);
            MGWorldBorderProfile profile = null;
            foreach (var guid in AssetDatabase.FindAssets("t:MGWorldBorderProfile"))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<MGWorldBorderProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (MGWorldBorderBuilder.ResolveRoot(candidate) == root) { profile = candidate; break; }
            }
            Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Convert baked border to runtime proxies");
            try
            {
                foreach (var collider in colliders)
                {
                    Vector3 scale = collider.transform.lossyScale;
                    var size = Vector3.Scale(collider.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                    var wall = CreateWall(root.transform, collider.transform.TransformPoint(collider.center), collider.transform.rotation, size);
                    wall.gameObject.name = collider.name + " Proxy";
                    wall.gameObject.layer = collider.gameObject.layer;
                    wall.gameObject.tag = collider.gameObject.tag;
                    wall.gameObject.SetActive(collider.gameObject.activeInHierarchy);
                    wall.enabled = collider.enabled;
                    wall.physicsMaterial = collider.sharedMaterial;
                    wall.materialTemplate = template;
                    if (template)
                    {
                        wall.tint = template.GetColor("_COLOUR_OVERLAY");
                        wall.opacity = template.GetFloat("_BORDER_OPACITY");
                        wall.visibilityDistance = template.GetFloat("_VISIBILITY_DISTANCE");
                        wall.fadeWidth = template.GetFloat("_FADE_WIDTH");
                    }
                    var filter = visual.GetComponentInChildren<MeshFilter>(true);
                    if (filter && filter.sharedMesh && filter.sharedMesh.uv.Length >= 2)
                    {
                        var uv = filter.sharedMesh.uv; var vertices = filter.sharedMesh.vertices;
                        float repeats = Mathf.Abs(uv[1].x - uv[0].x);
                        if (repeats > .0001f) wall.patternRepeat = Vector3.Distance(vertices[0], vertices[1]) / repeats;
                    }
                    if (profile)
                    {
                        wall.tileSize = profile.tileSize;
                        wall.maxVerticalFov = profile.maxVerticalFov;
                        wall.minLodBias = profile.minLodBias;
                        wall.cullPadding = profile.cullPadding;
                    }
                    var problem = wall.Problem();
                    if (problem != null) throw new InvalidOperationException(problem);
                    EditorUtility.SetDirty(wall);
                }
                Undo.DestroyObjectImmediate(visual.gameObject);
                Undo.DestroyObjectImmediate(collision.gameObject);
                EditorSceneManager.MarkSceneDirty(root.scene);
                Undo.CollapseUndoOperations(undo);
            }
            catch { Undo.RevertAllDownToGroup(undo); throw; }
        }
    }

    [CustomEditor(typeof(MBWorldBorderWall)), CanEditMultipleObjects]
    public sealed class MBWorldBorderWallEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Move, rotate and scale this wall using Unity's transform tools. Scale X = width, Y = height, Z = thickness. The pivot is its centre. Geometry and collision are generated when the map loads.", MessageType.Info);
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
            foreach (var obj in targets)
            {
                var wall = (MBWorldBorderWall)obj;
                string issue = wall.Problem();
                if (issue != null) EditorGUILayout.HelpBox(wall.name + ": " + issue, MessageType.Error);
                else EditorGUILayout.LabelField(wall.name + ": " + wall.TileCount + " runtime tiles · " + (wall.solidCollision ? "1 solid wall" : "visual only"));
            }
            if (Application.isPlaying && GUILayout.Button("Apply changes to runtime walls"))
                foreach (var obj in targets) ((MBWorldBorderWall)obj).RebuildRuntime();
        }
        public bool HasFrameBounds() => true;
        public Bounds OnGetFrameBounds()
        {
            var wall = (MBWorldBorderWall)target;
            var bounds = new Bounds(wall.transform.TransformPoint(Vector3.one * -.5f), Vector3.zero);
            for (int i = 0; i < 8; i++) bounds.Encapsulate(wall.transform.TransformPoint(new Vector3((i&1)==0?-.5f:.5f, (i&2)==0?-.5f:.5f, (i&4)==0?-.5f:.5f)));
            return bounds;
        }
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
        static void DrawProxy(MBWorldBorderWall wall, GizmoType type)
        {
            if (Application.isPlaying || !MBGameplayGizmoVisibility.Visible) return;
            var matrix = Gizmos.matrix; var color = Gizmos.color;
            Gizmos.matrix = wall.transform.localToWorldMatrix;
            Gizmos.color = new Color(.15f, .8f, 1, (type & GizmoType.Selected) != 0 ? .18f : .06f);
            Gizmos.DrawCube(Vector3.zero, Vector3.one);
            Gizmos.color = new Color(.15f, .8f, 1, .9f);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = matrix; Gizmos.color = color;
        }
    }
}

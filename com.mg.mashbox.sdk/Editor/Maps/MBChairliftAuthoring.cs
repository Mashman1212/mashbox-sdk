#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public static class MBChairliftAuthoring
    {
        public const string DefaultPrefabPath = "Packages/com.mg.mashbox.sdk/Runtime/Maps/Prefabs/Chairlift.prefab";
        [MenuItem("GameObject/MashBox/Chairlift", false, 30)]
        public static void Create()
        {
            var parent = Selection.activeTransform;
            var existingLift = parent != null ? parent.GetComponentInParent<MBChairlift>() : null;
            if (existingLift != null) parent = existingLift.transform.parent;
            Create(parent, parent != null ? parent.position : Vector3.zero);
        }

        public static MBChairlift Create(Transform parent, Vector3 position)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Chairlift");
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath);
            if (template == null)
                throw new InvalidOperationException("The SDK chairlift template is missing: " + DefaultPrefabPath);
            string uniqueName = GameObjectUtility.GetUniqueNameForSibling(parent, "Chair Lift");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(template, parent);
            Undo.RegisterCreatedObjectUndo(root, "Create Chairlift");
            root.name = uniqueName;
            root.transform.position = position;
            var lift = root.GetComponent<MBChairlift>();
            lift.chairliftName = root.name;
            PrefabUtility.RecordPrefabInstancePropertyModifications(root);
            PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(lift);
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
            return lift;
        }

        private static MonoBehaviour SelectedLift() => Selection.activeGameObject == null ? null :
            Selection.activeGameObject.GetComponentsInParent<MonoBehaviour>(true).FirstOrDefault(c => c != null && c.GetType().FullName == "ChairliftBehaviour");

        [MenuItem("MashBox/Maps/Chairlifts/Convert Selected Legacy Lift to SDK Proxy", true)]
        public static bool CanConvert() => !Application.isPlaying && SelectedLift() != null && !EditorUtility.IsPersistent(SelectedLift());

        // Uses serialized data so the SDK never takes a dependency on Assembly-CSharp.
        // A single Undo restores the original lift, including all authored physical objects.
        [MenuItem("MashBox/Maps/Chairlifts/Convert Selected Legacy Lift to SDK Proxy")]
        public static void ConvertSelected()
        {
            var source = SelectedLift();
            if (source == null) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Convert Chairlift to SDK Proxy");
            try
            {
                var components = source.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null).ToArray();
                if (components.Count(c => c.GetType().FullName == "ChairliftBehaviour") != 1)
                    throw new InvalidOperationException("Convert each lift separately; nested legacy lifts are unsupported.");
                var root = new GameObject(source.name);
                Undo.RegisterCreatedObjectUndo(root, "Create Chairlift Proxy");
                root.transform.SetParent(source.transform.parent, false);
                if (source.transform.parent == null)
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, source.gameObject.scene);
                root.transform.localPosition = source.transform.localPosition;
                root.transform.localRotation = source.transform.localRotation;
                root.transform.localScale = source.transform.localScale;
                root.transform.SetSiblingIndex(source.transform.GetSiblingIndex());
                var proxy = root.AddComponent<MBChairlift>();
                CopyFields(source, proxy);
                proxy.reverse = new SerializedObject(source).FindProperty("direction").intValue < 0;
                var transforms = new Dictionary<Transform, Transform> { [source.transform] = root.transform };
                var paths = new Dictionary<UnityEngine.Object, MBChairliftPath>();

                foreach (var oldPath in components.Where(c => c.GetType().FullName == "CableSplinePath"))
                {
                    // Refresh authored tower/bay references before copying the explicit route.
                    string method = (bool)oldPath.GetType().GetProperty("UsesTowers").GetValue(oldPath)
                        ? "RefreshControlPointsFromTowerRoot" : "RefreshControlPointsFromChildren";
                    oldPath.GetType().GetMethod(method).Invoke(oldPath, null);
                    var path = Node(oldPath.transform, source.transform, transforms).gameObject.AddComponent<MBChairliftPath>();
                    CopyFields(oldPath, path);
                    var data = new SerializedObject(oldPath);
                    path.loadingBay = data.FindProperty("pathStyle").enumValueIndex == 1;
                    path.catmullRom = data.FindProperty("interpolationMode").enumValueIndex == 1;
                    var points = data.FindProperty("controlPoints");
                    for (int i = 0; i < points.arraySize; i++)
                    {
                        var point = points.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                        if (point == null) throw new InvalidOperationException("A cable has a missing control point.");
                        path.points.Add(Node(point, source.transform, transforms));
                    }
                    var spawner = oldPath.GetComponents<MonoBehaviour>().FirstOrDefault(c => c != null && c.GetType().FullName == "CableSplineSpawner");
                    path.spawnChairs = spawner != null;
                    if (spawner != null)
                    {
                        CopyFields(spawner, path);
                        var settings = new SerializedObject(spawner);
                        path.useSpacing = settings.FindProperty("spawnMode").enumValueIndex == 1;
                        path.chairCount = settings.FindProperty("carrierCount").intValue;
                        path.loopChairs = settings.FindProperty("loop").boolValue;
                    }
                    paths.Add(oldPath, path);
                }
                foreach (var oldBay in components.Where(c => c.GetType().FullName == "ChairliftLoadingBay"))
                {
                    var station = Node(oldBay.transform, source.transform, transforms).gameObject.AddComponent<MBChairliftStation>();
                    station.kind = oldBay.name.IndexOf("Top", StringComparison.OrdinalIgnoreCase) >= 0
                        ? MBChairliftStationKind.Top : MBChairliftStationKind.Bottom;
                    var oldBayPath = new SerializedObject(oldBay).FindProperty("loadingBayPath").objectReferenceValue;
                    if (oldBayPath == null || !paths.TryGetValue(oldBayPath, out station.loadingBayPath))
                        throw new InvalidOperationException("A loading bay has no cable path.");
                    CaptureStationPreview(station, oldBay.transform);
                }
                foreach (var oldTower in components.Where(c => c.GetType().FullName == "ChairliftTower"))
                    Node(oldTower.transform, source.transform, transforms).gameObject.AddComponent<MBChairliftTower>();
                foreach (var oldZone in components.Where(c => c.GetType().FullName == "ChairliftCableTransferZone"))
                {
                    var zone = Node(oldZone.transform, source.transform, transforms).gameObject.AddComponent<MBChairliftTransfer>();
                    CopyFields(oldZone, zone);
                    var target = new SerializedObject(oldZone).FindProperty("targetPath").objectReferenceValue;
                    if (target == null || !paths.TryGetValue(target, out zone.targetPath))
                        throw new InvalidOperationException("A transfer targets a missing/external path.");
                    var collider = oldZone.GetComponent<Collider>();
                    if (collider is BoxCollider box) { zone.center = box.center; zone.size = box.size; }
                    else if (collider is SphereCollider sphere) { zone.sphere = true; zone.center = sphere.center; zone.radius = sphere.radius; }
                    else throw new InvalidOperationException("Only box/sphere transfer volumes can be converted.");
                }
                foreach (var oldPoint in components.Where(c => c.GetType().FullName == "CableSplineControlPoint"))
                {
                    if (!transforms.ContainsKey(oldPoint.transform)) continue;
                    var linked = new SerializedObject(oldPoint).FindProperty("linkedPoint").objectReferenceValue as Transform;
                    if (linked == null) continue;
                    var link = transforms[oldPoint.transform].gameObject.AddComponent<MBChairliftPointLink>();
                    CopyFields(oldPoint, link);
                    link.linkedPoint = Node(linked, source.transform, transforms);
                }
                if (!proxy.Validate(out string error)) throw new InvalidOperationException(error);
                root.SetActive(source.gameObject.activeSelf);
                proxy.enabled = source.enabled;
                // The route-only proxy has no private scripts, meshes, colliders or prefab dependencies.
                Undo.DestroyObjectImmediate(source.gameObject);
                Selection.activeGameObject = root;
                EditorSceneManager.MarkSceneDirty(root.scene);
                Undo.CollapseUndoOperations(group);
                Debug.Log("Converted chairlift to SDK proxy. ProjectX resolves its definitionId to game-owned prefabs. Undo restores the original lift.", root);
            }
            catch (Exception ex)
            {
                Undo.RevertAllDownToGroup(group);
                Debug.LogError("Chairlift conversion cancelled: " + ex.Message);
            }
        }


        public static bool IsStationVisualPart(string name) =>
            name == "Hut" || name.StartsWith("Transfer Bay", StringComparison.Ordinal);

        // Store only lightweight bounds in the SDK; no game mesh/prefab dependencies.
        public static void CaptureStationPreview(MBChairliftStation station, Transform visualRoot)
        {
            var parts = new List<Bounds>();
            foreach (Transform child in visualRoot)
            {
                if (!IsStationVisualPart(child.name)) continue;
                bool initialized = false;
                var bounds = new Bounds();
                foreach (var renderer in child.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var local = renderer.localBounds;
                    var matrix = visualRoot.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var sign = new Vector3((corner & 1) == 0 ? -1 : 1,
                            (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                        var point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, sign));
                        if (!initialized) { bounds = new Bounds(point, Vector3.zero); initialized = true; }
                        else bounds.Encapsulate(point);
                    }
                }
                if (initialized)
                {
                    parts.Add(bounds);
                    if (child.name == "Hut") station.placementOffset = new Vector3(0, bounds.min.y, 0);
                }
            }
            station.previewParts = parts.ToArray();
            var combined = parts.Count > 0 ? parts[0] : new Bounds(Vector3.zero, Vector3.zero);
            for (int i = 1; i < parts.Count; i++) combined.Encapsulate(parts[i]);
            station.previewCenter = combined.center;
            station.previewSize = combined.size;
        }

        private static Transform Node(Transform source, Transform lift, Dictionary<Transform, Transform> map)
        {
            if (map.TryGetValue(source, out var existing)) return existing;
            if (!source.IsChildOf(lift)) throw new InvalidOperationException("Referenced transforms must be inside the selected lift.");
            var parent = Node(source.parent, lift, map);
            var node = new GameObject(source.name).transform;
            node.SetParent(parent, false);
            node.localPosition = source.localPosition;
            node.localRotation = source.localRotation;
            node.localScale = source.localScale;
            node.gameObject.layer = source.gameObject.layer;
            map.Add(source, node);
            return node;
        }

        private static void CopyFields(UnityEngine.Object source, UnityEngine.Object target)
        {
            var from = new SerializedObject(source);
            var to = new SerializedObject(target);
            var p = to.GetIterator();
            if (!p.NextVisible(true)) return;
            do
            {
                if (p.name.StartsWith("m_", StringComparison.Ordinal)) continue;
                var value = from.FindProperty(p.propertyPath);
                if (value == null) continue;
                // Copy data only, never source GameObjects, components or prefab references.
                switch (p.propertyType)
                {
                    case SerializedPropertyType.String: p.stringValue = value.stringValue; break;
                    case SerializedPropertyType.Boolean: p.boolValue = value.boolValue; break;
                    case SerializedPropertyType.Float: p.floatValue = value.floatValue; break;
                    case SerializedPropertyType.Integer: p.intValue = value.intValue; break;
                    case SerializedPropertyType.Vector3: p.vector3Value = value.vector3Value; break;
                }
            } while (p.NextVisible(false));
            to.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    [CustomEditor(typeof(MBChairlift))]
    public sealed class MBChairliftEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            var lift = (MBChairlift)target;
            if (EditorGUI.EndChangeCheck()) lift.ApplySettings();
            if (!lift.Validate(out string error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.HelpBox("The game supplies lift vehicles, towers and stations automatically.", MessageType.Info);
        }
    }
}
#endif

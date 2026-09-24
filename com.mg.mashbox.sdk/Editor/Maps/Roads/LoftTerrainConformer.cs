using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.Sculpting;
using MashBoxSDK.Maps.TerrainSystem;
using MashBoxSDK.Maps.Spline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.Maps.TerrainSystem.Editor
{
    // Shared SDK terrain-conforming service and loft inspector integration.
    [InitializeOnLoad]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "MappyX.EditorTools", "Assembly-CSharp-Editor", null)]
    public sealed class LoftTerrainConformer : EditorWindow
    {
        [SerializeField] GameObject source;
        [SerializeField] bool includeChildren = true;
        [SerializeField] float offset = -0.05f;
        [SerializeField] float blendDistance = 3f;
        [SerializeField] float strength = 1f;
        [SerializeField] AnimationCurve falloffCurve = null;
        [SerializeField] HeightMode mode;
        [SerializeField] bool mgTerrain = true, unityTerrain = true;
        string result;
        public enum HeightMode { RaiseAndLower, LowerOnly, RaiseOnly }

        static LoftTerrainConformer inspectorControls;
        static string inspectorKey;

        static LoftTerrainConformer()
        {
            MultiSplineLoftEditor.TerrainConformGUI += DrawLoftInspector;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseInspectorControls;
        }

        static void ReleaseInspectorControls()
        {
            if (inspectorControls != null) DestroyImmediate(inspectorControls);
            inspectorKey = null;
        }

        static void DrawLoftInspector(MultiSplineLoft loft)
        {
            string key = "MappyX.LoftTerrainConform." + loft.GetEntityId();
            if (inspectorControls == null || inspectorKey != key)
            {
                ReleaseInspectorControls();
                inspectorControls = CreateInstance<LoftTerrainConformer>();
                inspectorControls.hideFlags = HideFlags.HideAndDontSave;
                inspectorKey = key;
                string saved = SessionState.GetString(key, "");
                if (!string.IsNullOrEmpty(saved)) JsonUtility.FromJsonOverwrite(saved, inspectorControls);
            }
            inspectorControls.source = loft.gameObject;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Conform Terrain to This Loft", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                inspectorControls.DrawControls();
                if (EditorGUI.EndChangeCheck())
                    SessionState.SetString(key, JsonUtility.ToJson(inspectorControls));
            }
        }

        [MenuItem("MashBox/Terrain/Conform Terrain to Selected Loft…")]
        public static void OpenSelection() { Open(Selection.activeGameObject); }

        static void Open(GameObject go)
        {
            var window = CreateWindow<LoftTerrainConformer>("Loft → Terrain");
            window.source = go;
            window.minSize = new Vector2(360, 360);
            window.Show();
        }

        void OnGUI()
        {
            source = (GameObject)EditorGUILayout.ObjectField("Loft / Loft Parent", source, typeof(GameObject), true);
            if (GUILayout.Button("Use Selected Loft")) { source = Selection.activeGameObject; result = null; }
            DrawControls();
        }

        void DrawControls()
        {
            includeChildren = EditorGUILayout.Toggle(new GUIContent("Include Child Meshes", "Includes child lofts and shoulders. Select the loft itself to exclude unrelated meshes."), includeChildren);
            offset = EditorGUILayout.FloatField(new GUIContent("Height Offset (m)", "Negative values leave the terrain below the loft."), offset);
            blendDistance = Mathf.Max(0, EditorGUILayout.FloatField("Edge Blend Distance (m)", blendDistance));
            strength = EditorGUILayout.Slider("Strength", strength, 0, 1);
            mode = (HeightMode)EditorGUILayout.EnumPopup("Height Mode", mode);
            mgTerrain = EditorGUILayout.Toggle("MG Terrain Tiles", mgTerrain);
            unityTerrain = EditorGUILayout.Toggle("Unity Terrain Tiles", unityTerrain);
            EditorGUILayout.HelpBox("Finds active terrain tiles in loaded scenes beneath the mesh footprint, including the edge blend. Overlapping loft surfaces use the highest surface. Terrain resolution limits the resulting detail. Changes support Undo (Ctrl+Z).", MessageType.Info);
            using (new EditorGUI.DisabledScope(source == null || Application.isPlaying || (!mgTerrain && !unityTerrain) || strength <= 0))
            {
                if (GUILayout.Button("Find Affected Tiles")) Run(false);
                if (GUILayout.Button("Conform Terrain to Loft", GUILayout.Height(32))) Run(true);
            }
            if (!string.IsNullOrEmpty(result)) EditorGUILayout.HelpBox(result, MessageType.Info);
        }

        void Run(bool apply)
        {
            try
            {
                if (float.IsNaN(offset) || float.IsInfinity(offset) || float.IsNaN(blendDistance) || float.IsInfinity(blendDistance))
                    throw new InvalidOperationException("Enter finite height offset and blend distance values.");
                if (!source.scene.IsValid() || EditorUtility.IsPersistent(source) || PrefabStageUtility.GetPrefabStage(source) != null)
                    throw new InvalidOperationException("Select a loft in a loaded scene, outside Prefab Mode.");
                var filters = includeChildren ? source.GetComponentsInChildren<MeshFilter>() : source.GetComponents<MeshFilter>();
                var surface = new LoftSurface(filters);
                var edits = BuildEdits(surface);
                if (apply) Apply(edits);
                int count = 0;
                foreach (var edit in edits) count += edit.count;
                result = $"{(apply ? "Conformed" : "Found")} {edits.Count} terrain tile(s), {count:N0} height samples.";
                if (edits.Count == 0) result += " No height changes needed. Check the source mesh, terrain resolution and height mode.";
                SceneView.RepaintAll();
            }
            catch (OperationCanceledException) { result = "Cancelled. Terrain was not changed."; }
            catch (Exception error) { result = error.Message; Debug.LogException(error); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        internal sealed class Edit
        {
            internal MGTerrain mg;
            internal Terrain unity;
            internal List<MeshSculptModifier.SeamVertex> deltas;
            internal float[,] heights;
            internal int x, z, count;
        }

        static bool SceneObject(Component value) => value.gameObject.activeInHierarchy
            && value.gameObject.scene.IsValid() && value.gameObject.scene.isLoaded
            && !EditorUtility.IsPersistent(value) && PrefabStageUtility.GetPrefabStage(value.gameObject) == null;

        internal List<Edit> BuildEdits(LoftSurface surface, IEnumerable<MGTerrain> meshTiles = null, IEnumerable<Terrain> heightTiles = null, bool matchTerrainCells = false, float roadbedPaddingCells = 0)
        {
            try
            {
                var edits = new List<Edit>();
                if (mgTerrain)
                    foreach (var tile in meshTiles ?? Object.FindObjectsByType<MGTerrain>())
                    {
                        if (!SceneObject(tile) || tile.MeshFilter == null || tile.MeshFilter.sharedMesh == null) continue;
                        var filter = tile.MeshFilter;
                        var mesh = filter.sharedMesh;
                        if (!matchTerrainCells && !surface.Overlaps(WorldBounds(filter), blendDistance)) continue;
                        if (!mesh.isReadable) throw new InvalidOperationException($"Terrain {tile.name} needs a readable mesh.");
                        var vertices = mesh.vertices;
                        var supports = matchTerrainCells ? TerrainCellSupports(mesh, filter.transform, vertices) : null;
                        // One cell supports triangles crossing the edge; the extra cell forms the full-height apron.
                        if (supports != null) for (int i = 0; i < supports.Length; i++) supports[i] *= 1 + Mathf.Max(0, roadbedPaddingCells);
                        float maximumSupport = 0;
                        if (supports != null) foreach (float radius in supports) maximumSupport = Mathf.Max(maximumSupport, radius);
                        if (!surface.Overlaps(WorldBounds(filter), blendDistance + maximumSupport)) continue;
                        var edit = new Edit { mg = tile, deltas = new List<MeshSculptModifier.SeamVertex>() };
                        for (int i = 0; i < vertices.Length; i++)
                        {
                            if (i % 4096 == 0) Progress(tile.name, (float)i / vertices.Length);
                            Vector3 p = filter.transform.TransformPoint(vertices[i]);
                            if (!Target(surface, p, out float height, supports == null ? 0 : supports[i])) continue;
                            p.y = height;
                            edit.deltas.Add(new MeshSculptModifier.SeamVertex { index = i, delta = filter.transform.InverseTransformPoint(p) - vertices[i] });
                        }
                        edit.count = edit.deltas.Count;
                        if (edit.count > 0) edits.Add(edit);
                    }
                if (unityTerrain)
                    foreach (var tile in heightTiles ?? Object.FindObjectsByType<Terrain>())
                    {
                        if (!SceneObject(tile) || tile.terrainData == null) continue;
                        var data = tile.terrainData;
                        var origin = tile.transform.position;
                        var bounds = new Bounds(origin + data.size * .5f, data.size);
                        if (!surface.Overlaps(bounds, blendDistance)) continue;
                        if (Quaternion.Angle(tile.transform.rotation, Quaternion.identity) > .001f || tile.transform.lossyScale != Vector3.one)
                            throw new InvalidOperationException($"Unity Terrain {tile.name} must have identity rotation and unit scale.");
                        // Editing shared TerrainData would also change a tile outside the footprint.
                        foreach (var other in Resources.FindObjectsOfTypeAll<Terrain>())
                            if (other != tile && other.gameObject.scene.IsValid() && other.terrainData == data)
                                throw new InvalidOperationException($"{tile.name} shares TerrainData with {other.name}. Give each tile its own TerrainData before conforming.");
                        int resolution = data.heightmapResolution;
                        Vector3 min = surface.Bounds.min - Vector3.one * blendDistance;
                        Vector3 max = surface.Bounds.max + Vector3.one * blendDistance;
                        int x0 = Mathf.Clamp(Mathf.FloorToInt((min.x - origin.x) / data.size.x * (resolution - 1)), 0, resolution - 1);
                        int z0 = Mathf.Clamp(Mathf.FloorToInt((min.z - origin.z) / data.size.z * (resolution - 1)), 0, resolution - 1);
                        int x1 = Mathf.Clamp(Mathf.CeilToInt((max.x - origin.x) / data.size.x * (resolution - 1)), 0, resolution - 1);
                        int z1 = Mathf.Clamp(Mathf.CeilToInt((max.z - origin.z) / data.size.z * (resolution - 1)), 0, resolution - 1);
                        var edit = new Edit { unity = tile, x = x0, z = z0, heights = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1) };
                        for (int z = 0; z <= z1 - z0; z++)
                        {
                            Progress(tile.name, (float)z / (z1 - z0 + 1));
                            for (int x = 0; x <= x1 - x0; x++)
                            {
                                var p = origin + new Vector3((x + x0) * data.size.x / (resolution - 1), edit.heights[z, x] * data.size.y, (z + z0) * data.size.z / (resolution - 1));
                                if (!Target(surface, p, out float height)) continue;
                                float normalized = (height - origin.y) / data.size.y;
                                if (normalized < 0 || normalized > 1)
                                    throw new InvalidOperationException($"Target height exceeds {tile.name}'s height range. Adjust its height range or the offset first. No terrain was changed.");
                                edit.heights[z, x] = normalized;
                                edit.count++;
                            }
                        }
                        if (edit.count > 0) edits.Add(edit);
                    }
                return edits;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        internal static float[] TerrainCellSupports(Mesh mesh, Transform transform, Vector3[] vertices)
        {
            var radii = new float[vertices.Length];
            var toWorld = transform.localToWorldMatrix;
            var indices = mesh.triangles;
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                Vector3 ab = toWorld.MultiplyVector(vertices[a] - vertices[b]); ab.y = 0;
                Vector3 bc = toWorld.MultiplyVector(vertices[b] - vertices[c]); bc.y = 0;
                Vector3 ca = toWorld.MultiplyVector(vertices[c] - vertices[a]); ca.y = 0;
                float radius = Mathf.Sqrt(Mathf.Max(ab.sqrMagnitude, Mathf.Max(bc.sqrMagnitude, ca.sqrMagnitude)));
                radii[a] = Mathf.Max(radii[a], radius);
                radii[b] = Mathf.Max(radii[b], radius);
                radii[c] = Mathf.Max(radii[c], radius);
            }
            return radii;
        }

        internal bool Target(LoftSurface surface, Vector3 p, out float height, float support = 0)
        {
            height = p.y;
            if (!surface.Sample(p, blendDistance + support, out float loftHeight, out float distance)) return false;
            distance = Mathf.Max(0, distance - support);
            float weight = distance <= 0 ? 1 : falloffCurve != null
                ? Mathf.Clamp01(falloffCurve.Evaluate(distance / blendDistance))
                : 1 - Mathf.SmoothStep(0, 1, distance / blendDistance);
            float target = loftHeight + offset;
            if (mode == HeightMode.LowerOnly) target = Mathf.Min(p.y, target);
            if (mode == HeightMode.RaiseOnly) target = Mathf.Max(p.y, target);
            height = Mathf.Lerp(p.y, target, weight * strength);
            return Mathf.Abs(height - p.y) > .00001f;
        }

        static void Progress(string name, float progress)
        {
            if (EditorUtility.DisplayCancelableProgressBar("Conform Terrain to Loft", "Sampling " + name, progress))
                throw new OperationCanceledException();
        }

        internal static void Apply(List<Edit> edits)
        {
            if (edits.Count == 0) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Conform Terrain to Loft");
            try
            {
                foreach (var edit in edits)
                {
                    if (edit.mg != null)
                    {
                        var filter = edit.mg.MeshFilter;
                        // Also capture the mesh bindings: undoing the first stroke removes
                        // its newly added modifier, so that modifier cannot replay Undo.
                        var previousMesh = filter.sharedMesh;
                        Undo.RegisterCompleteObjectUndo(filter, "Conform Terrain to Loft");
                        Undo.RegisterCompleteObjectUndo(previousMesh, "Conform Terrain to Loft");
                        foreach (var collider in edit.mg.GetComponentsInChildren<MeshCollider>(true))
                            Undo.RegisterCompleteObjectUndo(collider, "Conform Terrain to Loft");
                        var modifier = filter.GetComponent<MeshSculptModifier>();
                        if (modifier == null) modifier = Undo.AddComponent<MeshSculptModifier>(filter.gameObject);
                        Undo.RegisterCompleteObjectUndo(edit.mg, "Conform Terrain Instances");
                        Undo.RecordObject(modifier, "Prepare Terrain Sculpting");
                        modifier.SetTarget(filter);
                        modifier.UpdateMeshCollider = true;
                        modifier.Rebuild();
                        if (filter.sharedMesh != previousMesh)
                            Undo.RegisterCreatedObjectUndo(filter.sharedMesh, "Create Conformed Terrain Mesh");
                        Undo.RegisterCompleteObjectUndo(modifier, "Conform Terrain to Loft");
                        Undo.RegisterCompleteObjectUndo(filter.sharedMesh, "Conform Terrain to Loft");
                        modifier.AddStroke(new MeshSculptModifier.Stroke { mode = MeshSculptModifier.SculptMode.SeamFit,
                            space = MeshSculptModifier.StrokeSpace.TargetLocal, seamVertexCount = filter.sharedMesh.vertexCount,
                            seamVertices = edit.deltas.ToArray() });
                        modifier.ApplyLatestStrokePreview();
                        modifier.FinalizeStrokePreview();
                        EditorUtility.SetDirty(modifier);
                        EditorUtility.SetDirty(edit.mg);
                        EditorSceneManager.MarkSceneDirty(edit.mg.gameObject.scene);
                    }
                    else
                    {
                        Undo.RegisterCompleteObjectUndo(edit.unity.terrainData, "Conform Terrain to Loft");
                        edit.unity.terrainData.SetHeights(edit.x, edit.z, edit.heights);
                        edit.unity.Flush();
                        EditorUtility.SetDirty(edit.unity.terrainData);
                    }
                }
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }

        static Bounds WorldBounds(MeshFilter filter)
        {
            var b = filter.sharedMesh.bounds;
            var result = new Bounds(filter.transform.TransformPoint(b.min), Vector3.zero);
            for (int i = 0; i < 8; i++)
                result.Encapsulate(filter.transform.TransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z)));
            return result;
        }
    }

    // XZ triangle tree: exact footprint sampling, independent of colliders and winding.
    internal sealed class LoftSurface
    {
        struct Triangle { internal Vector3 a, b, c; internal Bounds bounds; internal float centerX, centerZ; }
        sealed class Node { internal Bounds bounds; internal Node left, right; internal int start, count; internal float minX, maxX, minZ, maxZ; }
        readonly Triangle[] triangles;
        readonly Node root;
        internal Bounds Bounds => root.bounds;

        internal LoftSurface(IEnumerable<MeshFilter> filters, bool includeTerrain = false)
        {
            var list = new List<Triangle>();
            foreach (var filter in filters)
            {
                if (filter == null || (!includeTerrain && filter.GetComponentInParent<MGTerrain>() != null) || filter.sharedMesh == null) continue;
                var mesh = filter.sharedMesh;
                if (!mesh.isReadable) throw new InvalidOperationException($"Loft mesh {filter.name} must be readable.");
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = filter.transform.TransformPoint(vertices[i]);
                var indices = mesh.triangles;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var t = new Triangle { a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]] };
                    if (Mathf.Abs(Cross(t.b - t.a, t.c - t.a)) < 1e-8f) continue;
                    t.bounds = new Bounds(t.a, Vector3.zero); t.bounds.Encapsulate(t.b); t.bounds.Encapsulate(t.c);
                    t.centerX = t.bounds.center.x; t.centerZ = t.bounds.center.z;
                    list.Add(t);
                }
            }
            if (list.Count == 0) throw new InvalidOperationException("The selected loft has no readable surface triangles. Generate its mesh first.");
            triangles = list.ToArray();
            root = Build(0, triangles.Length);
        }

        Node Build(int start, int count)
        {
            var node = new Node { start = start, count = count, bounds = triangles[start].bounds };
            for (int i = start + 1; i < start + count; i++) node.bounds.Encapsulate(triangles[i].bounds);
            var min = node.bounds.min; var max = node.bounds.max;
            node.minX = min.x; node.maxX = max.x; node.minZ = min.z; node.maxZ = max.z;
            if (count <= 12) return node;
            bool x = node.bounds.size.x > node.bounds.size.z;
            Array.Sort(triangles, start, count, Comparer<Triangle>.Create((a, b) =>
                (x ? a.centerX : a.centerZ).CompareTo(x ? b.centerX : b.centerZ)));
            int half = count / 2;
            node.left = Build(start, half); node.right = Build(start + half, count - half);
            return node;
        }

        internal bool Overlaps(Bounds b, float margin) => b.max.x >= Bounds.min.x - margin && b.min.x <= Bounds.max.x + margin
            && b.max.z >= Bounds.min.z - margin && b.min.z <= Bounds.max.z + margin;

        internal bool Sample(Vector3 p, float radius, out float height, out float distance)
        {
            float best = radius * radius;
            height = float.NegativeInfinity;
            Search(root, p, ref best, ref height);
            distance = Mathf.Sqrt(best);
            return !float.IsNegativeInfinity(height);
        }

        void Search(Node node, Vector3 p, ref float best, ref float height)
        {
            if (NodeDistance(node, p) > best + 1e-8f) return;
            if (node.left != null)
            {
                // Find a close triangle first so the remaining tree can be pruned early.
                // Still visit ties: overlapping road surfaces select the highest height.
                var first = node.left; var second = node.right;
                if (NodeDistance(second, p) < NodeDistance(first, p)) { first = node.right; second = node.left; }
                Search(first, p, ref best, ref height); Search(second, p, ref best, ref height); return;
            }
            for (int i = node.start; i < node.start + node.count; i++)
            {
                var t = triangles[i];
                float den = Cross(t.b - t.a, t.c - t.a);
                float u = Cross(p - t.a, t.c - t.a) / den;
                float v = Cross(t.b - t.a, p - t.a) / den;
                float distance; float y;
                if (u >= -1e-6f && v >= -1e-6f && u + v <= 1 + 1e-6f)
                { distance = 0; y = t.a.y + u * (t.b.y - t.a.y) + v * (t.c.y - t.a.y); }
                else
                {
                    Vector3 q = ClosestEdge(p, t.a, t.b);
                    Vector3 r = ClosestEdge(p, t.b, t.c);
                    Vector3 s = ClosestEdge(p, t.c, t.a);
                    if (Distance(p, r) < Distance(p, q)) q = r;
                    if (Distance(p, s) < Distance(p, q)) q = s;
                    distance = Distance(p, q); y = q.y;
                }
                if (distance > best + 1e-8f) continue;
                if (distance < best - 1e-8f) height = y;
                else height = Mathf.Max(height, y);
                best = Mathf.Min(best, distance);
            }
        }

        static float NodeDistance(Node node, Vector3 p)
        {
            float dx = p.x < node.minX ? node.minX - p.x : p.x > node.maxX ? p.x - node.maxX : 0;
            float dz = p.z < node.minZ ? node.minZ - p.z : p.z > node.maxZ ? p.z - node.maxZ : 0;
            return dx * dx + dz * dz;
        }
        static float Cross(Vector3 a, Vector3 b) => a.x * b.z - a.z * b.x;
        static float Distance(Vector3 a, Vector3 b) => (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z);
        static Vector3 ClosestEdge(Vector3 p, Vector3 a, Vector3 b)
        {
            var d = b - a;
            float length = d.x * d.x + d.z * d.z;
            float t = length < 1e-12f ? 0 : Mathf.Clamp01(((p.x - a.x) * d.x + (p.z - a.z) * d.z) / length);
            return Vector3.Lerp(a, b, t);
        }
    }
}

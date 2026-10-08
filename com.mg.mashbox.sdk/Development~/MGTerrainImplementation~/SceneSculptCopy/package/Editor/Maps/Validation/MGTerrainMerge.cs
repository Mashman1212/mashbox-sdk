#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    [InitializeOnLoad]
    internal static class MGTerrainMergeUndo
    {
        static readonly HashSet<MGTerrain> terrains = new HashSet<MGTerrain>();
        static MGTerrainMergeUndo() { Undo.undoRedoPerformed += Refresh; }
        internal static void Track(MGTerrain terrain) => terrains.Add(terrain);
        static void Refresh()
        {
            // Undo must still refresh collision after the merge window has been closed.
            terrains.RemoveWhere(t => t == null);
            foreach (var terrain in terrains)
            {
                if (terrain.MeshFilter == null || terrain.MeshFilter.sharedMesh == null) continue;
                terrain.NotifySurfaceMeshChanged(); terrain.RefreshSurfaceCollidersFromMesh();
                EditorUtility.SetDirty(terrain.MeshFilter.sharedMesh);
            }
            SceneView.RepaintAll();
        }
    }

    // Immutable, world-space triangle samples. No colliders or objects from the source
    // scene survive capture, and neither sampling nor preview edits a source asset.
    internal sealed class MGTerrainMergeSurface
    {
        internal readonly Vector3[] vertices;
        readonly int[] triangles;
        readonly List<int>[] cells;
        readonly Bounds bounds;
        readonly int resolution;

        internal MGTerrainMergeSurface(Mesh mesh, Matrix4x4 matrix)
        {
            if (mesh == null || !mesh.isReadable) throw new InvalidOperationException("The source needs a readable mesh.");
            vertices = mesh.vertices;
            if (vertices.Length == 0) throw new InvalidOperationException("The source mesh is empty.");
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                if (!Finite(vertices[i].x) || !Finite(vertices[i].y) || !Finite(vertices[i].z))
                    throw new InvalidOperationException("The source contains invalid vertex positions.");
            }
            bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (var p in vertices) bounds.Encapsulate(p);
            if (bounds.size.x < .00001f || bounds.size.z < .00001f)
                throw new InvalidOperationException("The source has no terrain footprint.");
            triangles = mesh.triangles;
            if (triangles.Length == 0) throw new InvalidOperationException("The source has no triangles.");
            resolution = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(triangles.Length / 6f)), 1, 256);
            cells = new List<int>[resolution * resolution];
            for (int t = 0; t < triangles.Length; t += 3)
            {
                var a = vertices[triangles[t]]; var b = vertices[triangles[t + 1]]; var c = vertices[triangles[t + 2]];
                int x0 = X(Mathf.Min(a.x, b.x, c.x)), x1 = X(Mathf.Max(a.x, b.x, c.x));
                int z0 = Z(Mathf.Min(a.z, b.z, c.z)), z1 = Z(Mathf.Max(a.z, b.z, c.z));
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                {
                    int cell = z * resolution + x;
                    if (cells[cell] == null) cells[cell] = new List<int>();
                    cells[cell].Add(t);
                }
            }
        }
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        int X(float x) => Mathf.Clamp((int)((x - bounds.min.x) / bounds.size.x * resolution), 0, resolution - 1);
        int Z(float z) => Mathf.Clamp((int)((z - bounds.min.z) / bounds.size.z * resolution), 0, resolution - 1);
        internal bool TryHeight(Vector3 p, out float height)
        {
            height = 0;
            if (p.x < bounds.min.x - .0001f || p.x > bounds.max.x + .0001f
                || p.z < bounds.min.z - .0001f || p.z > bounds.max.z + .0001f) return false;
            var candidates = cells[Z(p.z) * resolution + X(p.x)];
            if (candidates == null) return false;
            bool found = false;
            foreach (int t in candidates)
            {
                var a = vertices[triangles[t]]; var b = vertices[triangles[t + 1]]; var c = vertices[triangles[t + 2]];
                float bx = b.x - a.x, bz = b.z - a.z, cx = c.x - a.x, cz = c.z - a.z;
                float determinant = bx * cz - bz * cx;
                if (Mathf.Abs(determinant) < 1e-12f) continue;
                float px = p.x - a.x, pz = p.z - a.z;
                float u = (px * cz - pz * cx) / determinant;
                float v = (bx * pz - bz * px) / determinant;
                if (u < -.00001f || v < -.00001f || u + v > 1.00001f) continue;
                float y = a.y + u * (b.y - a.y) + v * (c.y - a.y);
                if (!found || y > height) height = y;
                found = true;
            }
            return found;
        }
    }

    internal sealed class MGTerrainMergeSession : IDisposable
    {
        internal sealed class Tile
        {
            internal MGTerrain terrain;
            internal Mesh original, editable, ghost;
            internal Matrix4x4 matrix, inverse;
            internal Vector3[] positions;
            internal float[] sourceHeights;
            internal bool[] covered;
            internal int coveredCount;
        }
        internal readonly List<Tile> targets = new List<Tile>();
        readonly List<MGTerrain> world;
        readonly Dictionary<MGTerrain, Tile> states = new Dictionary<MGTerrain, Tile>();
        readonly HashSet<MGTerrain> changed = new HashSet<MGTerrain>();
        internal int StrokeGroup { get; private set; } = -1;
        internal int CoveredCount => targets.Sum(t => t.coveredCount);
        internal int VertexCount => targets.Sum(t => t.positions.Length);

        internal static List<MGTerrain> Tiles(GameObject root)
        {
            if (root == null) throw new InvalidOperationException("Choose a terrain tile or world.");
            var tile = root.GetComponent<MGTerrain>();
            var result = tile != null ? new List<MGTerrain> { tile }
                : root.GetComponentsInChildren<MGTerrain>(true).ToList();
            if (result.Count == 0) throw new InvalidOperationException("That object contains no MG Terrain tiles.");
            return result;
        }

        internal MGTerrainMergeSession(List<MGTerrain> destinations, IReadOnlyList<MGTerrainMergeSurface> sources)
        {
            if (destinations.Count == 0 || sources.Count == 0) throw new InvalidOperationException("Choose both source and destination terrain.");
            // Include inactive neighbours and use hierarchy ownership, not transient render registration.
            world = destinations.SelectMany(t => {
                var owner = t.GetComponentInParent<MGTerrainWorld>(true);
                return owner != null ? owner.GetComponentsInChildren<MGTerrain>(true) : new[] { t };
            }).Distinct().ToList();
            try
            {
                foreach (var terrain in world)
                {
                    MGTerrainTileAuthoring.Validate(terrain);
                    if (EditorUtility.IsPersistent(terrain) || !terrain.gameObject.scene.IsValid()
                        || EditorSceneManager.IsPreviewScene(terrain.gameObject.scene))
                        throw new InvalidOperationException("The destination must be in an open editable scene.");
                    var tile = new Tile { terrain = terrain, original = terrain.MeshFilter.sharedMesh,
                        matrix = terrain.MeshFilter.transform.localToWorldMatrix,
                        inverse = terrain.MeshFilter.transform.worldToLocalMatrix,
                        positions = terrain.MeshFilter.sharedMesh.vertices };
                    states.Add(terrain, tile);
                }
                foreach (var terrain in destinations)
                {
                    var tile = states[terrain]; targets.Add(tile);
                    tile.sourceHeights = new float[tile.positions.Length];
                    tile.covered = new bool[tile.positions.Length];
                    for (int i = 0; i < tile.positions.Length; i++)
                    {
                        var point = tile.matrix.MultiplyPoint3x4(tile.positions[i]);
                        if (!MGTerrainMergeSurface.Finite(point.y)) throw new InvalidOperationException("The destination contains invalid heights.");
                        bool found = false; float height = 0;
                        foreach (var source in sources)
                        {
                            if (!source.TryHeight(point, out float next)) continue;
                            if (found && Mathf.Abs(next - height) > .01f)
                                throw new InvalidOperationException("Source tiles overlap with different heights. Choose a single source world or tile.");
                            height = next; found = true;
                        }
                        tile.sourceHeights[i] = height; tile.covered[i] = found;
                        if (found) tile.coveredCount++;
                    }
                }
                if (CoveredCount == 0) throw new InvalidOperationException("The source and destination do not overlap. Scene/world sources use world coordinates; mesh assets use the destination tile transform.");
                RefreshGhosts();
            }
            catch { Dispose(); throw; }
        }

        internal void ValidateCurrent()
        {
            foreach (var tile in states.Values)
            {
                if (tile.terrain == null || tile.terrain.MeshFilter == null)
                    throw new InvalidOperationException("A destination tile was removed. Rebuild the preview.");
                var filter = tile.terrain.MeshFilter;
                if (filter.transform.localToWorldMatrix != tile.matrix || filter.sharedMesh == null
                    || filter.sharedMesh.vertexCount != tile.positions.Length
                    || (filter.sharedMesh != tile.original && filter.sharedMesh != tile.editable))
                    throw new InvalidOperationException("The destination transform or mesh changed. Rebuild the preview.");
            }
        }
        internal void BeginStroke()
        {
            ValidateCurrent();
            if (StrokeGroup >= 0) return;
            Undo.IncrementCurrentGroup(); StrokeGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Merge Terrain Sculpt"); changed.Clear();
            // Undo / external sculpting can change vertex contents without changing mesh identity.
            foreach (var tile in states.Values) tile.positions = tile.terrain.MeshFilter.sharedMesh.vertices;
        }
        void Prepare(MGTerrain terrain)
        {
            MGTerrainMergeUndo.Track(terrain);
            var tile = states[terrain]; var filter = terrain.MeshFilter;
            if (tile.editable == null || filter.sharedMesh != tile.editable)
            {
                // Always detach, including neighbours. References in closed scenes cannot be discovered.
                if (!terrain.SceneSculptCopy && !AssetDatabase.Contains(filter.sharedMesh)) MGTerrainSceneAssets.Create(filter.sharedMesh, terrain, "BeforeMerge");
                var mesh = Object.Instantiate(filter.sharedMesh); mesh.hideFlags = HideFlags.None;
                try
                {
                    if (terrain.SceneSculptCopy) Undo.RegisterCreatedObjectUndo(mesh, "Merge Terrain Sculpt");
                    else MGTerrainSceneAssets.Create(mesh, terrain, "MergedSculpt");
                }
                catch { Object.DestroyImmediate(mesh); throw; }
                DetachColliders(terrain);
                Undo.RecordObject(filter, "Merge Terrain Sculpt");
                Undo.RecordObject(terrain, "Merge Terrain Sculpt");
                if (terrain.MeshCollider != null) Undo.RecordObject(terrain.MeshCollider, "Merge Terrain Sculpt");
                filter.sharedMesh = mesh; tile.editable = mesh;
                using (var data = new SerializedObject(terrain))
                {
                    data.FindProperty("m_EditableSculptMesh").objectReferenceValue = mesh;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                PrefabUtility.RecordPrefabInstancePropertyModifications(terrain);
                EditorUtility.SetDirty(filter);
                Undo.FlushUndoRecordObjects();
            }
            if (changed.Add(terrain)) Undo.RegisterCompleteObjectUndo(filter.sharedMesh, "Merge Terrain Sculpt");
        }
        internal static void DetachColliders(MGTerrain terrain, string label = "Merge Terrain Sculpt", string role = "MergedColliders")
        {
            // Converted worlds can share persistent collision meshes too. Preserve holes
            // and vertex maps while detaching all mutable collision geometry as one asset.
            var copies = new Dictionary<Mesh, Mesh>();
            Mesh root = null;
            Mesh Copy(Mesh original)
            {
                if (original == null) return null;
                if (copies.TryGetValue(original, out var existing)) return existing;
                var copy = Object.Instantiate(original); copy.hideFlags = HideFlags.None;
                if (terrain.SceneSculptCopy) Undo.RegisterCreatedObjectUndo(copy, label);
                else if (root == null) { MGTerrainSceneAssets.Create(copy, terrain, role); root = copy; }
                else AssetDatabase.AddObjectToAsset(copy, root);
                copies.Add(original, copy); return copy;
            }
            using var data = new SerializedObject(terrain);
            var maps = data.FindProperty("m_SurfaceColliderVertexMaps");
            if (maps.arraySize == 0 && terrain.SurfaceColliderChunks.Count > 0)
            {
                var lookup = new Dictionary<Vector3, int>();
                var filter = terrain.MeshFilter; var vertices = filter.sharedMesh.vertices;
                for (int i = 0; i < vertices.Length; i++) lookup[filter.transform.TransformPoint(vertices[i])] = i;
                var recovered = new List<MGTerrain.SurfaceColliderVertexMap>();
                foreach (var collider in terrain.SurfaceColliderChunks)
                {
                    if (collider == null || collider.sharedMesh == null || !collider.sharedMesh.isReadable)
                        throw new InvalidOperationException("Rebuild destination colliders before merging this legacy terrain.");
                    var positions = collider.sharedMesh.vertices; var indices = new int[positions.Length];
                    for (int i = 0; i < positions.Length; i++)
                        if (!lookup.TryGetValue(collider.transform.TransformPoint(positions[i]), out indices[i]))
                            throw new InvalidOperationException("Destination collider mapping is stale. Rebuild colliders before merging.");
                    recovered.Add(new MGTerrain.SurfaceColliderVertexMap(collider, indices));
                }
                terrain.SetSurfaceColliderVertexMaps(recovered.ToArray(), vertices.Length); data.Update();
                maps = data.FindProperty("m_SurfaceColliderVertexMaps");
            }
            foreach (var collider in terrain.SurfaceColliderChunks)
            {
                if (collider == null) continue;
                var copied = Copy(collider.sharedMesh);
                Undo.RecordObject(collider, label);
                collider.sharedMesh = copied;
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            }
            for (int i = 0; i < maps.arraySize; i++)
            {
                var holeMesh = maps.GetArrayElementAtIndex(i).FindPropertyRelative("holeMesh");
                holeMesh.objectReferenceValue = Copy(holeMesh.objectReferenceValue as Mesh);
            }
            Undo.RecordObject(terrain, label);
            data.ApplyModifiedPropertiesWithoutUndo();
            Undo.FlushUndoRecordObjects();
        }
        internal static float Weight(float distance, float radius, float hardness, float strength)
        {
            if (radius <= 0 || distance >= radius) return 0;
            float inner = radius * Mathf.Clamp01(hardness);
            float fade = distance <= inner ? 1 : 1 - Mathf.SmoothStep(0, 1, (distance - inner) / (radius - inner));
            return fade * Mathf.Clamp01(strength);
        }
        internal int Paint(Vector3 center, float radius, float hardness, float strength, bool wholeTile = false)
        {
            if (StrokeGroup < 0) throw new InvalidOperationException("Begin a merge stroke before painting.");
            ValidateCurrent();
            if (!MGTerrainMergeSurface.Finite(radius) || radius <= 0 || !MGTerrainMergeSurface.Finite(strength)
                || !MGTerrainMergeSurface.Finite(hardness)) throw new ArgumentException("Brush settings must be finite and radius must be positive.");
            int count = 0;
            var touched = new HashSet<MGTerrain>();
            foreach (var tile in targets)
            {
                bool dirty = false;
                for (int i = 0; i < tile.positions.Length; i++)
                {
                    if (!tile.covered[i]) continue;
                    var p = tile.matrix.MultiplyPoint3x4(tile.positions[i]);
                    float weight = wholeTile ? 1 : Weight(new Vector2(p.x - center.x, p.z - center.z).magnitude, radius, hardness, strength);
                    float y = Mathf.Lerp(p.y, tile.sourceHeights[i], weight);
                    if (Mathf.Abs(y - p.y) < .000001f) continue;
                    if (!dirty) { Prepare(tile.terrain); dirty = true; }
                    p.y = y;
                    tile.positions[i].y = tile.inverse.MultiplyPoint3x4(p).y;
                    count++;
                }
                if (!dirty) continue;
                var mesh = tile.terrain.MeshFilter.sharedMesh;
                mesh.vertices = tile.positions; mesh.RecalculateBounds(); mesh.RecalculateNormals();
                if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) mesh.RecalculateTangents();
                touched.Add(tile.terrain);
            }
            if (touched.Count == 0) return 0;
            // The seam resolver may extend to the endpoints of a coarse neighbour segment.
            var neighbours = MGTerrainSeams.Neighbours(world, touched.ToList());
            MGTerrainSeams.Join(neighbours, center, wholeTile ? float.MaxValue : radius, false,
                tile => changed.Add(tile), touched, world, Prepare);
            foreach (var terrain in changed)
            {
                states[terrain].positions = terrain.MeshFilter.sharedMesh.vertices;
                terrain.NotifySurfaceMeshChanged(); EditorUtility.SetDirty(terrain.MeshFilter.sharedMesh);
                EditorUtility.SetDirty(terrain); EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            }
            return count;
        }
        internal void EndStroke(bool cancel = false)
        {
            if (StrokeGroup < 0) return;
            int group = StrokeGroup; StrokeGroup = -1;
            if (cancel) Undo.RevertAllDownToGroup(group);
            foreach (var terrain in changed)
            {
                if (terrain == null) continue;
                terrain.NotifySurfaceMeshChanged(); terrain.RefreshSurfaceCollidersFromMesh();
                EditorUtility.SetDirty(terrain.MeshFilter.sharedMesh);
            }
            if (!cancel) { Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); }
            changed.Clear(); RefreshGhosts();
            EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll();
        }
        internal void RefreshAfterUndo()
        {
            RefreshGhosts();
        }
        internal void RefreshGhosts()
        {
            foreach (var tile in targets)
            {
                if (tile.terrain == null || tile.terrain.MeshFilter.sharedMesh == null) continue;
                var positions = tile.terrain.MeshFilter.sharedMesh.vertices;
                if (positions.Length != tile.covered.Length) continue;
                if (tile.ghost == null)
                {
                    tile.ghost = Object.Instantiate(tile.original);
                    tile.ghost.hideFlags = HideFlags.HideAndDontSave;
                }
                var colors = new Color[positions.Length];
                for (int i = 0; i < positions.Length; i++)
                {
                    if (!tile.covered[i]) continue;
                    var point = tile.matrix.MultiplyPoint3x4(positions[i]);
                    float difference = tile.sourceHeights[i] - point.y;
                    point.y = tile.sourceHeights[i]; positions[i] = tile.inverse.MultiplyPoint3x4(point);
                    colors[i] = difference >= 0 ? new Color(.1f, .85f, 1f, 1) : new Color(1f, .45f, .1f, 1);
                    colors[i].a = Mathf.Clamp01(Mathf.Abs(difference) / .1f);
                }
                tile.ghost.vertices = positions; tile.ghost.colors = colors; tile.ghost.RecalculateBounds();
                // Never bridge an uncovered region or a source hole in the overlay.
                var indices = tile.original.triangles; var visible = new List<int>();
                for (int i = 0; i < indices.Length; i += 3)
                    if (tile.covered[indices[i]] && tile.covered[indices[i + 1]] && tile.covered[indices[i + 2]])
                    { visible.Add(indices[i]); visible.Add(indices[i + 1]); visible.Add(indices[i + 2]); }
                tile.ghost.subMeshCount = 1; tile.ghost.SetTriangles(visible, 0);
            }
        }
        public void Dispose()
        {
            EndStroke();
            foreach (var tile in targets) if (tile.ghost != null) Object.DestroyImmediate(tile.ghost);
            targets.Clear(); states.Clear();
        }
    }
}
#endif

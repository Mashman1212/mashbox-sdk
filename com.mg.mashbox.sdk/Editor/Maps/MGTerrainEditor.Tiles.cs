#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.Sculpting;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        Terrain m_HeightSource;
        float m_StampBlend = 1f, m_StampOffset;
        bool m_ShowTileAuthoring = true;

        void DrawTileAuthoring(MGTerrain terrain)
        {
            m_ShowTileAuthoring = EditorGUILayout.Foldout(m_ShowTileAuthoring, "Terrain Tiles and Stamps", true);
            if (!m_ShowTileAuthoring) return;
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (terrain.World == null && GUILayout.Button("Convert to Multi-Tile"))
                    RunTileAction(() => Selection.activeGameObject = MGTerrainWorldEditor.Adopt(new[] { terrain }).gameObject);
                using (new EditorGUI.DisabledScope(terrain.World == null))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Add North (+Z)")) AddTile(terrain, Vector2Int.up);
                        if (GUILayout.Button("Add South (-Z)")) AddTile(terrain, Vector2Int.down);
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Add West (-X)")) AddTile(terrain, Vector2Int.left);
                        if (GUILayout.Button("Add East (+X)")) AddTile(terrain, Vector2Int.right);
                    }
                }
                m_HeightSource = (Terrain)EditorGUILayout.ObjectField("Unity Terrain Source", m_HeightSource, typeof(Terrain), true);
                m_StampBlend = EditorGUILayout.Slider("Blend", m_StampBlend, 0f, 1f);
                m_StampOffset = EditorGUILayout.FloatField("Height Offset (m)", m_StampOffset);
                EditorGUILayout.HelpBox("Conform samples Unity Terrain at world X/Z in the overlap. To paint shapes interactively, open the Mesh Stamp brush.", MessageType.Info);
                using (new EditorGUI.DisabledScope(m_HeightSource == null || m_HeightSource.terrainData == null))
                    if (GUILayout.Button("Conform to Unity Terrain")) RunTileAction(() => MGTerrainTileAuthoring.Conform(terrain, m_HeightSource, null, m_StampBlend, m_StampOffset));
                if (GUILayout.Button("Mesh Stamp Brush"))
                {
                    Selection.activeGameObject = terrain.MeshFilter.gameObject;
                    MGTerrainTileAuthoring.Modifier(terrain);
                    MeshSculptWindow.OpenMeshStamp();
                }
            }
        }

        static void RunTileAction(Action action)
        {
            try { action(); }
            catch (Exception error) { Debug.LogException(error); }
        }

        static void AddTile(MGTerrain terrain, Vector2Int direction)
        {
            RunTileAction(() => Selection.activeGameObject = MGTerrainTileAuthoring.AddTile(terrain, direction).gameObject);
        }
    }

    // Editor-only geometry authoring; each child retains its own MGTerrain and sculpt history.
    internal static class MGTerrainTileAuthoring
    {
        internal static void Validate(MGTerrain tile)
        {
            if (tile == null || tile.MeshFilter == null || tile.MeshFilter.sharedMesh == null || !tile.MeshFilter.sharedMesh.isReadable)
                throw new InvalidOperationException("Tile authoring requires a readable surface mesh.");
            var t = tile.MeshFilter.transform;
            if (Quaternion.Angle(t.rotation, Quaternion.identity) > .001f || t.lossyScale.x <= 0 || t.lossyScale.y <= 0 || t.lossyScale.z <= 0)
                throw new InvalidOperationException("Tile authoring requires axis-aligned terrain with positive scale.");
        }

        internal static Bounds BoundsOf(MGTerrain tile)
        {
            var b = tile.MeshFilter.sharedMesh.bounds;
            return new Bounds(tile.MeshFilter.transform.TransformPoint(b.center), Vector3.Scale(b.size, tile.MeshFilter.transform.lossyScale));
        }

        internal static MeshSculptModifier Modifier(MGTerrain tile)
        {
            var filter = tile.MeshFilter;
            var modifier = filter.GetComponent<MeshSculptModifier>();
            if (modifier == null) modifier = Undo.AddComponent<MeshSculptModifier>(filter.gameObject);
            Undo.RecordObject(modifier, "Edit Terrain Height");
            modifier.SetTarget(filter);
            modifier.UpdateMeshCollider = true;
            return modifier;
        }

        internal static void ApplyDeltas(MGTerrain tile, List<MeshSculptModifier.SeamVertex> deltas)
        {
            if (deltas.Count == 0) return;
            var modifier = Modifier(tile);
            modifier.AddStroke(new MeshSculptModifier.Stroke { mode = MeshSculptModifier.SculptMode.SeamFit,
                space = MeshSculptModifier.StrokeSpace.TargetLocal, seamVertexCount = tile.MeshFilter.sharedMesh.vertexCount,
                seamVertices = deltas.ToArray() });
            MeshSeamFitBrush.TrackUndo(modifier);
            modifier.ApplyLatestStrokePreview();
            EditorUtility.SetDirty(modifier);
        }

        internal static MGTerrain AddTile(MGTerrain source, Vector2Int direction)
        {
            Validate(source);
            if (source.World == null) throw new InvalidOperationException("Convert to multi-tile first.");
            if (Quaternion.Angle(source.World.transform.rotation, Quaternion.identity) > .001f || source.World.transform.lossyScale != Vector3.one)
                throw new InvalidOperationException("Use an unrotated, unit-scale terrain world for adding tiles.");
            var bounds = BoundsOf(source);
            var offset = new Vector3(direction.x * bounds.size.x, 0, direction.y * bounds.size.z);
            var destination = new Bounds(bounds.center + offset, bounds.size);
            foreach (var other in source.World.Chunks)
            {
                Validate(other);
                var b = BoundsOf(other);
                if (Mathf.Min(b.max.x, destination.max.x) - Mathf.Max(b.min.x, destination.min.x) > .001f
                    && Mathf.Min(b.max.z, destination.max.z) - Mathf.Max(b.min.z, destination.min.z) > .001f)
                    throw new InvalidOperationException("A terrain tile already occupies that space.");
            }
            var mesh = source.MeshFilter.sharedMesh;
            BuildTileVertices(mesh, direction, out var xs, out var zs, out var output, out var uv);
            int[] triangles = new int[(xs.Length - 1) * (zs.Length - 1) * 6];
            int index = 0;
            for (int z = 0; z < zs.Length - 1; z++)
                for (int x = 0; x < xs.Length - 1; x++)
                {
                    int i = z * xs.Length + x;
                    triangles[index++] = i; triangles[index++] = i + xs.Length; triangles[index++] = i + 1;
                    triangles[index++] = i + 1; triangles[index++] = i + xs.Length; triangles[index++] = i + xs.Length + 1;
                }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add MG Terrain Tile");
            try
            {
                var go = new GameObject("Terrain Tile " + direction);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, source.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(go, "Add Terrain Tile");
                go.transform.SetParent(source.World.transform, false);
                go.transform.position = source.MeshFilter.transform.position + offset;
                go.transform.localScale = Vector3.Scale(source.MeshFilter.transform.lossyScale,
                    new Vector3(1 / source.World.transform.lossyScale.x, 1 / source.World.transform.lossyScale.y, 1 / source.World.transform.lossyScale.z));
                go.layer = source.gameObject.layer;
                var tile = Undo.AddComponent<MGTerrain>(go);
                var result = new Mesh { name = go.name, indexFormat = output.Length > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32 : mesh.indexFormat, vertices = output, uv = uv, triangles = triangles };
                Undo.RegisterCreatedObjectUndo(result, "Create Terrain Tile Mesh");
                tile.MeshFilter.sharedMesh = result;
                // Match every already occupied border, including corners, before exposing the new tile.
                MatchNewEdges(tile, source.World.Chunks);
                result.RecalculateNormals(); result.RecalculateBounds(); result.RecalculateTangents();
                var collider = Undo.AddComponent<MeshCollider>(go); collider.sharedMesh = result;
                tile.Configure(tile.MeshFilter, tile.MeshRenderer, collider);
                tile.ConfigureSurfaceGrid(xs.Length, zs.Length);
                var materials = source.MeshRenderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null)
                    {
                        materials[i] = new Material(materials[i]) { name = go.name + " Surface" };
                        Undo.RegisterCreatedObjectUndo(materials[i], "Create Tile Material");
                    }
                tile.MeshRenderer.sharedMaterials = materials;
                Texture2D CloneMap(Texture2D map)
                {
                    if (map == null) return null;
                    var copy = Object.Instantiate(map); copy.name = go.name + " " + map.name;
                    Undo.RegisterCreatedObjectUndo(copy, "Create Tile Control Map"); return copy;
                }
                tile.SetControlMaps(CloneMap(source.ControlMap1), CloneMap(source.ControlMap2));
                tile.ApplyControlMapsToMaterial();
                MGTerrainSettingsCopy.Copy(source, tile);
                tile.HeightOnlySculpt = true;
                Modifier(tile);
                source.World.RefreshChunks();
                Undo.CollapseUndoOperations(group);
                return tile;
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }

        // Tile extension depends on the perimeter, not the source's interior
        // triangulation or whether UV/normal seams duplicate vertices.
        internal static void BuildTileVertices(Mesh mesh, Vector2Int direction, out float[] xs, out float[] zs,
            out Vector3[] output, out Vector2[] uv)
        {
            if (Mathf.Abs(direction.x) + Mathf.Abs(direction.y) != 1)
                throw new InvalidOperationException("Choose a cardinal tile direction.");
            var vertices = mesh.vertices;
            var bounds = mesh.bounds;
            const float tolerance = .0001f;
            if (bounds.size.x <= tolerance || bounds.size.z <= tolerance)
                throw new InvalidOperationException("The terrain needs a non-zero rectangular footprint to add a tile.");
            bool Near(float a, float b) => Mathf.Abs(a - b) <= tolerance;
            xs = vertices.Where(v => Near(v.z, bounds.min.z) || Near(v.z, bounds.max.z))
                .Select(v => v.x).Distinct().OrderBy(v => v).ToArray();
            zs = vertices.Where(v => Near(v.x, bounds.min.x) || Near(v.x, bounds.max.x))
                .Select(v => v.z).Distinct().OrderBy(v => v).ToArray();
            if (xs.Length < 2 || zs.Length < 2 || !Near(xs[0], bounds.min.x) || !Near(xs[xs.Length - 1], bounds.max.x)
                || !Near(zs[0], bounds.min.z) || !Near(zs[zs.Length - 1], bounds.max.z))
                throw new InvalidOperationException("Add Tile needs rectangular outer edges. This mesh's boundary is not rectangular.");
            if ((long)xs.Length * zs.Length > 4000000)
                throw new InvalidOperationException("The terrain perimeter would produce more than four million tile vertices. Simplify its edges first.");
            bool alongX = direction.y != 0;
            float edgePosition = alongX ? (direction.y > 0 ? bounds.max.z : bounds.min.z)
                : (direction.x > 0 ? bounds.max.x : bounds.min.x);
            var edge = vertices.Where(v => Near(alongX ? v.z : v.x, edgePosition))
                .GroupBy(v => alongX ? v.x : v.z).OrderBy(g => g.Key).Select(g =>
                {
                    if (g.Max(v => v.y) - g.Min(v => v.y) > tolerance)
                        throw new InvalidOperationException("The selected edge has multiple surface heights at one position. Remove skirts or overlapping edge surfaces before extending it.");
                    return new Vector2(g.Key, g.First().y);
                }).ToArray();
            float min = alongX ? bounds.min.x : bounds.min.z, max = alongX ? bounds.max.x : bounds.max.z;
            if (edge.Length < 2 || !Near(edge[0].x, min) || !Near(edge[edge.Length - 1].x, max))
                throw new InvalidOperationException("The selected edge must span the full width of the terrain.");
            var coordinates = alongX ? xs : zs;
            var heights = new float[coordinates.Length];
            int segment = 0;
            for (int i = 0; i < coordinates.Length; i++)
            {
                while (segment < edge.Length - 2 && coordinates[i] > edge[segment + 1].x) segment++;
                heights[i] = Mathf.Lerp(edge[segment].y, edge[segment + 1].y,
                    Mathf.InverseLerp(edge[segment].x, edge[segment + 1].x, coordinates[i]));
            }
            output = new Vector3[xs.Length * zs.Length];
            uv = new Vector2[output.Length];
            for (int z = 0; z < zs.Length; z++)
                for (int x = 0; x < xs.Length; x++)
                {
                    int i = z * xs.Length + x;
                    output[i] = new Vector3(xs[x], heights[alongX ? x : z], zs[z]);
                    uv[i] = new Vector2((xs[x] - bounds.min.x) / bounds.size.x, (zs[z] - bounds.min.z) / bounds.size.z);
                }
        }

        static Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.RoundToInt(p.x * 1000), Mathf.RoundToInt(p.z * 1000));
        struct EdgeSample
        {
            internal MGTerrain tile;
            internal int index;
            internal Vector3 local, world, normal;
        }

        internal static void JoinBrushEdges(List<MGTerrain> tiles, Vector3 center, float radius)
        {
            if (tiles.Count < 2) return;
            var edges = new Dictionary<Vector2Int, List<EdgeSample>>();
            foreach (var tile in tiles)
            {
                var mesh = tile.MeshFilter.sharedMesh;
                var vertices = mesh.vertices; var normals = mesh.normals; var bounds = mesh.bounds;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (!Border(vertices[i], bounds)) continue;
                    var world = tile.MeshFilter.transform.TransformPoint(vertices[i]);
                    float dx = world.x - center.x, dz = world.z - center.z;
                    if (dx * dx + dz * dz > radius * radius) continue;
                    var key = Key(world);
                    if (!edges.TryGetValue(key, out var samples)) edges.Add(key, samples = new List<EdgeSample>(4));
                    samples.Add(new EdgeSample { tile = tile, index = i, local = vertices[i], world = world,
                        normal = normals.Length == vertices.Length ? tile.MeshFilter.transform.worldToLocalMatrix.transpose.MultiplyVector(normals[i]).normalized : Vector3.up });
                }
            }
            var changes = new Dictionary<MGTerrain, List<MeshSculptModifier.SeamVertex>>();
            foreach (var samples in edges.Values)
            {
                if (samples.Count < 2 || samples.All(s => s.tile == samples[0].tile)) continue;
                float height = 0; Vector3 normal = Vector3.zero;
                foreach (var s in samples) { height += s.world.y; normal += s.normal; }
                height /= samples.Count; normal.Normalize();
                foreach (var s in samples)
                {
                    if (!changes.TryGetValue(s.tile, out var list)) changes.Add(s.tile, list = new List<MeshSculptModifier.SeamVertex>());
                    var p = s.world; p.y = height;
                    list.Add(new MeshSculptModifier.SeamVertex { index = s.index,
                        delta = s.tile.MeshFilter.transform.InverseTransformPoint(p) - s.local,
                        normal = s.tile.MeshFilter.transform.localToWorldMatrix.transpose.MultiplyVector(normal).normalized, normalWeight = 1 });
                }
            }
            foreach (var change in changes) ApplyDeltas(change.Key, change.Value);
        }

        static bool Border(Vector3 p, Bounds b) => Mathf.Abs(p.x - b.min.x) < .001f || Mathf.Abs(p.x - b.max.x) < .001f
            || Mathf.Abs(p.z - b.min.z) < .001f || Mathf.Abs(p.z - b.max.z) < .001f;

        static void MatchNewEdges(MGTerrain tile, IReadOnlyList<MGTerrain> neighbours)
        {
            tile.MeshFilter.sharedMesh.RecalculateBounds();
            var tileBounds = BoundsOf(tile);
            var tileKeys = new HashSet<Vector2Int>(tile.MeshFilter.sharedMesh.vertices.Select(v => Key(tile.MeshFilter.transform.TransformPoint(v))));
            var samples = new Dictionary<Vector2Int, float>();
            foreach (var other in neighbours)
            {
                if (other == tile) continue;
                var b = other.MeshFilter.sharedMesh.bounds;
                foreach (var v in other.MeshFilter.sharedMesh.vertices)
                    if (Border(v, b))
                    {
                        var p = other.MeshFilter.transform.TransformPoint(v); var key = Key(p);
                        if (p.x >= tileBounds.min.x - .001f && p.x <= tileBounds.max.x + .001f
                            && p.z >= tileBounds.min.z - .001f && p.z <= tileBounds.max.z + .001f && !tileKeys.Contains(key))
                            throw new InvalidOperationException("The neighboring edge has a different vertex spacing. Resample the tiles to matching grids before joining them.");
                        if (samples.TryGetValue(key, out float old) && Mathf.Abs(old - p.y) > .001f)
                            throw new InvalidOperationException("Existing tile edges disagree in height. Repair them before adding this tile.");
                        samples[key] = p.y;
                    }
            }
            var mesh = tile.MeshFilter.sharedMesh; mesh.RecalculateBounds();
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                if (Border(vertices[i], mesh.bounds))
                {
                    var p = tile.MeshFilter.transform.TransformPoint(vertices[i]);
                    if (samples.TryGetValue(Key(p), out float height))
                    { p.y = height; vertices[i] = tile.MeshFilter.transform.InverseTransformPoint(p); }
                    else foreach (var other in neighbours)
                    {
                        if (other == tile) continue;
                        var b = BoundsOf(other);
                        if (p.x >= b.min.x - .001f && p.x <= b.max.x + .001f && p.z >= b.min.z - .001f && p.z <= b.max.z + .001f)
                            throw new InvalidOperationException("The neighboring edge has a different vertex spacing. Resample the tiles to matching grids before joining them.");
                    }
                }
            mesh.vertices = vertices;
        }

        internal static void Conform(MGTerrain selected, Terrain source, MeshCollider stamp, float blend, float offset)
        {
            Validate(selected);
            if (stamp != null && (stamp.convex || !stamp.enabled || !stamp.gameObject.activeInHierarchy
                || stamp.GetComponentInParent<MGTerrain>() != null))
                throw new InvalidOperationException("Use an enabled, non-convex mesh stamp outside the destination terrain hierarchy.");
            if (source != null && (Quaternion.Angle(source.transform.rotation, Quaternion.identity) > .001f || source.transform.lossyScale != Vector3.one))
                throw new InvalidOperationException("Unity Terrain height import requires an unrotated, unit-scale source.");
            var tiles = selected.World != null ? new List<MGTerrain>(selected.World.Chunks) : new List<MGTerrain> { selected };
            foreach (var tile in tiles) Validate(tile);
            Physics.SyncTransforms();
            Bounds footprint = source != null ? new Bounds(source.transform.position + source.terrainData.size * .5f, source.terrainData.size) : stamp.bounds;
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Stamp Terrain Height");
            try
            {
                foreach (var tile in tiles)
                {
                    var b = BoundsOf(tile);
                    if (b.max.x < footprint.min.x || b.min.x > footprint.max.x || b.max.z < footprint.min.z || b.min.z > footprint.max.z) continue;
                    var vertices = tile.MeshFilter.sharedMesh.vertices;
                    var deltas = new List<MeshSculptModifier.SeamVertex>();
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 p = tile.MeshFilter.transform.TransformPoint(vertices[i]);
                        if (p.x < footprint.min.x || p.x > footprint.max.x || p.z < footprint.min.z || p.z > footprint.max.z) continue;
                        float height;
                        if (source != null) height = source.SampleHeight(p) + source.transform.position.y;
                        else
                        {
                            var ray = new Ray(new Vector3(p.x, footprint.max.y + 1, p.z), Vector3.down);
                            if (!stamp.Raycast(ray, out var hit, footprint.size.y + 2)) continue;
                            height = hit.point.y;
                        }
                        p.y = Mathf.Lerp(p.y, height + offset, blend);
                        deltas.Add(new MeshSculptModifier.SeamVertex { index = i, delta = tile.MeshFilter.transform.InverseTransformPoint(p) - vertices[i] });
                    }
                    ApplyDeltas(tile, deltas);
                    if (deltas.Count > 0) Modifier(tile).FinalizeStrokePreview();
                }
                Undo.CollapseUndoOperations(group);
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }
    }
}
#endif

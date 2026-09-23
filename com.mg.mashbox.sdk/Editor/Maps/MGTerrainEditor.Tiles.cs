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
        float? m_FlattenHeight;

        void DrawMultipleTileInspector()
        {
            var tiles = targets.Cast<MGTerrain>().ToArray();
            EditorGUILayout.LabelField("MG Terrain", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox($"{tiles.Length} terrain tiles selected. Flatten applies to all selected tiles at the same world height. Edit shared settings from the terrain world.", MessageType.Info);
            var worlds = tiles.Select(tile => tile.World).Where(world => world != null).Distinct().ToArray();
            foreach (var world in worlds)
                if (GUILayout.Button(worlds.Length == 1 ? "Edit Terrain World" : "Edit Terrain World: " + world.name))
                    Selection.activeGameObject = world.gameObject;
            DrawFlattenTile(tiles[0]);
        }

        void DrawFlattenTile(MGTerrain terrain)
        {
            if (terrain.MeshFilter == null || terrain.MeshFilter.sharedMesh == null)
            {
                EditorGUILayout.HelpBox("This tile lost its mesh reference. Restore a saved terrain mesh to make it visible again.", MessageType.Error);
                if (GUILayout.Button("Restore Saved Sculpt Mesh...")) RestoreSavedSculptMesh(terrain);
                return;
            }
            var tiles = targets.Cast<MGTerrain>().ToArray();
            if (!m_FlattenHeight.HasValue)
                m_FlattenHeight = terrain.MeshFilter != null ? terrain.MeshFilter.transform.position.y : terrain.transform.position.y;
            using (new EditorGUI.DisabledScope(Application.isPlaying || tiles.Any(tile => tile.MeshFilter == null
                || tile.MeshFilter.sharedMesh == null || !tile.MeshFilter.sharedMesh.isReadable)))
            {
                m_FlattenHeight = EditorGUILayout.FloatField(new GUIContent("Flatten Height (m)",
                    "World-space elevation for every selected tile. Defaults to the first tile origin's height."), m_FlattenHeight.Value);
                using (new EditorGUI.DisabledScope(float.IsNaN(m_FlattenHeight.Value) || float.IsInfinity(m_FlattenHeight.Value)))
                    if (GUILayout.Button(new GUIContent(tiles.Length > 1 ? $"Flatten {tiles.Length} Selected Tiles" : "Flatten Tile", "Flatten every vertex of the selected tiles to the specified height. Unselected tiles are unchanged. Undo restores the previous shapes.")))
                        RunTileAction(() =>
                        {
                            // Validate the full selection before editing any tile.
                            foreach (var tile in tiles) MGTerrainTileAuthoring.Validate(tile);
                            Undo.IncrementCurrentGroup();
                            int group = Undo.GetCurrentGroup();
                            Undo.SetCurrentGroupName("Flatten Selected Terrain Tiles");
                            try { foreach (var tile in tiles) MGTerrainTileAuthoring.Flatten(tile, m_FlattenHeight.Value); }
                            finally { Undo.CollapseUndoOperations(group); }
                        });
            }
        }

        static void RestoreSavedSculptMesh(MGTerrain terrain)
        {
            if (terrain.MeshFilter == null) return;
            string folder = MGTerrainSceneAssets.Folder(terrain);
            string path = EditorUtility.OpenFilePanel("Restore Saved Sculpt Mesh (choose BeforeSculpt for the pre-stroke shape, or SculptedMesh for the saved result)",
                System.IO.Path.GetFullPath(folder), "asset");
            if (string.IsNullOrEmpty(path)) return;
            string relative = TerrainToMeshConverter.ToProjectAssetPath(path);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(relative);
            if (mesh == null) { EditorUtility.DisplayDialog("Not a Mesh", "Choose a saved terrain mesh asset.", "OK"); return; }
            Undo.RecordObject(terrain.MeshFilter, "Restore Terrain Mesh");
            terrain.MeshFilter.sharedMesh = mesh;
            terrain.NotifySurfaceMeshChanged();
            terrain.RefreshSurfaceCollidersFromMesh();
            EditorUtility.SetDirty(terrain.MeshFilter);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            SceneView.RepaintAll();
        }

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
    internal static partial class MGTerrainTileAuthoring
    {
        internal static void Flatten(MGTerrain tile, float worldHeight)
        {
            Validate(tile);
            if (float.IsNaN(worldHeight) || float.IsInfinity(worldHeight))
                throw new ArgumentOutOfRangeException(nameof(worldHeight));
            var filter = tile.MeshFilter;
            var vertices = filter.sharedMesh.vertices;
            var deltas = new List<MeshSculptModifier.SeamVertex>();
            for (int i = 0; i < vertices.Length; i++)
            {
                float difference = worldHeight - filter.transform.TransformPoint(vertices[i]).y;
                if (Mathf.Abs(difference) < .000001f) continue;
                deltas.Add(new MeshSculptModifier.SeamVertex { index = i,
                    delta = filter.transform.InverseTransformVector(Vector3.up * difference) });
            }
            if (deltas.Count == 0) return;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Flatten Terrain Tile");
            try
            {
                ApplyDeltas(tile, deltas);
                filter.GetComponent<MeshSculptModifier>().FinalizeStrokePreview();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(tile.gameObject.scene);
            }
            finally { Undo.CollapseUndoOperations(undoGroup); }
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

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
            if (tile == null || tile.MeshFilter == null || tile.MeshFilter.sharedMesh == null)
                throw new InvalidOperationException("Terrain mesh is missing. Restore the saved sculpt mesh before editing this tile.");
            var b = tile.MeshFilter.sharedMesh.bounds;
            return new Bounds(tile.MeshFilter.transform.TransformPoint(b.center), Vector3.Scale(b.size, tile.MeshFilter.transform.lossyScale));
        }

        internal static MeshSculptModifier Modifier(MGTerrain tile)
        {
            var filter = tile.MeshFilter;
            var modifier = filter.GetComponent<MeshSculptModifier>();
            if (modifier == null) modifier = Undo.AddComponent<MeshSculptModifier>(filter.gameObject);
            if (modifier.Target != filter || !modifier.UpdateMeshCollider)
            {
                Undo.RecordObject(modifier, "Edit Terrain Height");
                modifier.SetTarget(filter);
                modifier.UpdateMeshCollider = true;
            }
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

        internal static MGTerrain AddTile(MGTerrain source, Vector2Int direction, Bounds? requestedBounds = null)
        {
            Validate(source);
            if (source.World == null) throw new InvalidOperationException("Convert to multi-tile first.");
            if (Quaternion.Angle(source.World.transform.rotation, Quaternion.identity) > .001f || source.World.transform.lossyScale != Vector3.one)
                throw new InvalidOperationException("Use an unrotated, unit-scale terrain world for adding tiles.");
            var bounds = BoundsOf(source);
            var offset = new Vector3(direction.x * bounds.size.x, 0, direction.y * bounds.size.z);
            var destination = requestedBounds ?? new Bounds(bounds.center + offset, bounds.size);
            foreach (var other in source.World.Chunks)
            {
                Validate(other);
                var b = BoundsOf(other);
                if (Mathf.Min(b.max.x, destination.max.x) - Mathf.Max(b.min.x, destination.min.x) > .001f
                    && Mathf.Min(b.max.z, destination.max.z) - Mathf.Max(b.min.z, destination.min.z) > .001f)
                    throw new InvalidOperationException("A terrain tile already occupies that space.");
            }
            var mesh = source.MeshFilter.sharedMesh;
            float[] xs, zs; Vector3[] output; Vector2[] uv;
            if (requestedBounds.HasValue && source.SurfaceGridWidth > 2 && source.SurfaceGridHeight > 2
                && (long)source.SurfaceGridWidth * source.SurfaceGridHeight <= mesh.vertexCount)
            {
                xs = new float[source.SurfaceGridWidth]; zs = new float[source.SurfaceGridHeight];
                output = null; uv = null;
            }
            else BuildTileVertices(mesh, direction, out xs, out zs, out output, out uv);
            int[] triangles = new int[(xs.Length - 1) * (zs.Length - 1) * 6];
            int index = 0;
            for (int z = 0; z < zs.Length - 1; z++)
                for (int x = 0; x < xs.Length - 1; x++)
                {
                    int i = z * xs.Length + x;
                    triangles[index++] = i; triangles[index++] = i + xs.Length; triangles[index++] = i + 1;
                    triangles[index++] = i + 1; triangles[index++] = i + xs.Length; triangles[index++] = i + xs.Length + 1;
                }
            if (requestedBounds.HasValue)
                BuildScaledGeometry(source, destination, source.World.Chunks, xs.Length, zs.Length,
                    out output, out uv, out triangles);
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add MG Terrain Tile");
            try
            {
                var coordinate = MGTerrainTileNames.TryCoordinates(source, out var sourceCoordinate) ? sourceCoordinate + direction : direction;
                var go = new GameObject($"Terrain Tile ({coordinate.x}, {coordinate.y})");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, source.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(go, "Add Terrain Tile");
                go.transform.SetParent(source.World.transform, false);
                go.transform.position = requestedBounds.HasValue ? new Vector3(destination.min.x, source.MeshFilter.transform.position.y, destination.min.z) : source.MeshFilter.transform.position + offset;
                go.transform.localScale = Vector3.Scale(source.MeshFilter.transform.lossyScale,
                    new Vector3(1 / source.World.transform.lossyScale.x, 1 / source.World.transform.lossyScale.y, 1 / source.World.transform.lossyScale.z));
                if (requestedBounds.HasValue) go.transform.localScale = Vector3.one;
                go.layer = source.gameObject.layer;
                var tile = Undo.AddComponent<MGTerrain>(go);
                var result = new Mesh { name = go.name, indexFormat = output.Length > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32 : mesh.indexFormat, vertices = output, uv = uv, triangles = triangles };
                Undo.RegisterCreatedObjectUndo(result, "Create Terrain Tile Mesh");
                tile.MeshFilter.sharedMesh = result;
                // Match every already occupied border, including corners, before exposing the new tile.
                if (!requestedBounds.HasValue) MatchNewEdges(tile, source.World.Chunks);
                result.RecalculateNormals(); result.RecalculateBounds(); result.RecalculateTangents();
                if (MGTerrainTileNames.TryCoordinates(tile, out var actualCoordinate))
                    go.name = result.name = $"Terrain Tile ({actualCoordinate.x}, {actualCoordinate.y})";
                var collider = Undo.AddComponent<MeshCollider>(go); collider.sharedMesh = result;
                tile.Configure(tile.MeshFilter, tile.MeshRenderer, collider);
                tile.ConfigureSurfaceGrid(xs.Length, zs.Length, requestedBounds.HasValue);
                var materials = source.MeshRenderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null)
                    {
                        materials[i] = new Material(materials[i]) { name = go.name + " Surface" };
                        // Per-tile captures cannot be inherited by a different footprint.
                        foreach (string property in new[]{"_FarRangeAppearanceMap", "_FarRangeAppearanceNormalMap", "_DistantSurfaceHeightMap"})
                            if (materials[i].HasProperty(property)) materials[i].SetTexture(property, null);
                        if (materials[i].HasProperty("_DistantSurfaceStrength")) materials[i].SetFloat("_DistantSurfaceStrength", 0);
                        Undo.RegisterCreatedObjectUndo(materials[i], "Create Tile Material");
                    }
                tile.MeshRenderer.sharedMaterials = materials;
                Texture2D CloneMap(Texture2D map)
                {
                    if (map == null) return null;
                    var copy = Object.Instantiate(map); copy.name = go.name + " " + map.name;
                    Undo.RegisterCreatedObjectUndo(copy, "Create Tile Control Map"); return copy;
                }
                tile.SetControlMaps(CloneMap(MGTerrainControlMapOwnership.Source(source, 1)), CloneMap(MGTerrainControlMapOwnership.Source(source, 2)));
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
            internal EdgeMeshData buffer;
            internal int index;
            internal Vector3 local, world, normal;
        }

        sealed class EdgeMeshData
        {
            public EdgeMeshData() { }
            internal Matrix4x4 worldToLocal, worldNormalToLocal;
            internal readonly List<Vector3> vertices = new List<Vector3>();
            internal readonly List<Vector3> normals = new List<Vector3>();
            internal readonly List<MeshSculptModifier.SeamVertex> changes = new List<MeshSculptModifier.SeamVertex>();
        }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MGTerrain, EdgeMeshData> EdgeBuffers =
            new System.Runtime.CompilerServices.ConditionalWeakTable<MGTerrain, EdgeMeshData>();
        static readonly Dictionary<Vector2Int, List<EdgeSample>> EdgeGroups = new Dictionary<Vector2Int, List<EdgeSample>>();
        static readonly Stack<List<EdgeSample>> EdgeGroupPool = new Stack<List<EdgeSample>>();
        static readonly Unity.Profiling.ProfilerMarker JoinEdgesMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.JoinBrushEdges");

        // The world brush has already prepared/registered Undo for every affected
        // mesh. Standalone commands retain ApplyDeltas' copy-on-write preparation.
        internal static void JoinBrushEdges(List<MGTerrain> tiles, Vector3 center, float radius, bool preparedMeshes = false)
        {
            if (tiles.Count < 2) return;
            using var profile = JoinEdgesMarker.Auto();
            try
            {
                foreach (var tile in tiles)
                {
                    var mesh = tile.MeshFilter.sharedMesh;
                    var buffer = EdgeBuffers.GetOrCreateValue(tile);
                    buffer.changes.Clear();
                    mesh.GetVertices(buffer.vertices);
                    mesh.GetNormals(buffer.normals);
                    var bounds = mesh.bounds;
                    var transform = tile.MeshFilter.transform;
                    var localToWorld = transform.localToWorldMatrix;
                    buffer.worldToLocal = transform.worldToLocalMatrix;
                    buffer.worldNormalToLocal = localToWorld.transpose;
                    var normalMatrix = buffer.worldToLocal.transpose;
                    void AddSample(int i)
                    {
                        var vertex = buffer.vertices[i];
                        if (!Border(vertex, bounds)) return;
                        var world = localToWorld.MultiplyPoint3x4(vertex);
                        float dx = world.x - center.x, dz = world.z - center.z;
                        if (dx * dx + dz * dz > radius * radius) return;
                        var key = Key(world);
                        if (!EdgeGroups.TryGetValue(key, out var samples))
                        {
                            samples = EdgeGroupPool.Count > 0 ? EdgeGroupPool.Pop() : new List<EdgeSample>(4);
                            EdgeGroups.Add(key, samples);
                        }
                        samples.Add(new EdgeSample { tile = tile, buffer = buffer, index = i, local = vertex, world = world,
                            normal = buffer.normals.Count == buffer.vertices.Count ? normalMatrix.MultiplyVector(buffer.normals[i]).normalized : Vector3.up });
                    }
                    int width = tile.SurfaceGridWidth;
                    int height = tile.SurfaceGridHeight;
                    if (width >= 2 && height >= 2 && (long)width * height == buffer.vertices.Count)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            AddSample(x);
                            AddSample((height - 1) * width + x);
                        }
                        for (int z = 1; z < height - 1; z++)
                        {
                            AddSample(z * width);
                            AddSample(z * width + width - 1);
                        }
                    }
                    else for (int i = 0; i < buffer.vertices.Count; i++) AddSample(i);
                }
                foreach (var samples in EdgeGroups.Values)
                {
                    if (samples.Count < 2) continue;
                    bool multipleTiles = false;
                    float height = 0; Vector3 normal = Vector3.zero;
                    foreach (var s in samples)
                    {
                        multipleTiles |= s.tile != samples[0].tile;
                        height += s.world.y; normal += s.normal;
                    }
                    if (!multipleTiles) continue;
                    height /= samples.Count; normal.Normalize();
                    foreach (var s in samples)
                    {
                        var buffer = s.buffer;
                        var position = s.world; position.y = height;
                        var delta = buffer.worldToLocal.MultiplyPoint3x4(position) - s.local;
                        var localNormal = buffer.worldNormalToLocal.MultiplyVector(normal).normalized;
                        // Idempotent joins need neither uploads nor new sculpt/Undo records.
                        if (delta.sqrMagnitude <= 1e-12f && buffer.normals.Count == buffer.vertices.Count
                            && (buffer.normals[s.index] - localNormal).sqrMagnitude <= 1e-10f) continue;
                        buffer.changes.Add(new MeshSculptModifier.SeamVertex { index = s.index,
                            delta = delta, normal = localNormal, normalWeight = 1 });
                    }
                }
                foreach (var tile in tiles)
                {
                    var buffer = EdgeBuffers.GetOrCreateValue(tile);
                    if (buffer.changes.Count == 0) continue;
                    if (!preparedMeshes) { ApplyDeltas(tile, buffer.changes); continue; }
                    var mesh = tile.MeshFilter.sharedMesh;
                    bool positionsChanged = false;
                    foreach (var change in buffer.changes)
                    {
                        if (change.delta.sqrMagnitude <= 1e-12f) continue;
                        buffer.vertices[change.index] += change.delta;
                        positionsChanged = true;
                    }
                    if (positionsChanged)
                    {
                        mesh.SetVertices(buffer.vertices);
                        mesh.RecalculateBounds();
                        mesh.RecalculateNormals();
                        mesh.GetNormals(buffer.normals);
                    }
                    else if (buffer.normals.Count != buffer.vertices.Count)
                    {
                        mesh.RecalculateNormals();
                        mesh.GetNormals(buffer.normals);
                    }
                    foreach (var change in buffer.changes) buffer.normals[change.index] = change.normal;
                    mesh.SetNormals(buffer.normals);
                    if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) mesh.RecalculateTangents();
                    mesh.UploadMeshData(false);
                    tile.NotifySurfaceMeshChanged();
                    EditorUtility.SetDirty(mesh);
                }
            }
            finally
            {
                foreach (var samples in EdgeGroups.Values) { samples.Clear(); EdgeGroupPool.Push(samples); }
                EdgeGroups.Clear();
            }
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

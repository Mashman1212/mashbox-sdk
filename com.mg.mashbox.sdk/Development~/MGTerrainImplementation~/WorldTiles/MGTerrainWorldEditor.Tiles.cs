#if UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainWorldEditor
    {
        bool m_AddTiles;
        string m_TileError;
        string m_DensityTextureProperty;
        static readonly Vector2Int[] Directions = { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };

        [MenuItem("GameObject/MashBox/MG Terrain World", false, 19)]
        static void CreateEmptyWorld(MenuCommand command)
        {
            var go = new GameObject("MG Terrain World");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create MG Terrain World");
            Undo.AddComponent<MGTerrainWorld>(go);
            Selection.activeGameObject = go;
        }

        static MGTerrain[] OwnedTiles(MGTerrainWorld world) => world.GetComponentsInChildren<MGTerrain>(true)
            .Where(t => t.GetComponentInParent<MGTerrainWorld>(true) == world).ToArray();

        void DrawTileCreation(MGTerrainWorld world)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Terrain Tiles", EditorStyles.boldLabel);
            bool empty = OwnedTiles(world).Length == 0;
            using (new EditorGUI.DisabledScope(Application.isPlaying || !empty))
            {
                serializedObject.Update();
                var size = serializedObject.FindProperty("TileSize");
                var resolution = serializedObject.FindProperty("TileResolution");
                float density = (resolution.intValue - 1) / Mathf.Max(1, size.floatValue);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(size, new GUIContent("Tile Size (metres)"));
                size.floatValue = Mathf.Clamp(float.IsNaN(size.floatValue) ? 512 : size.floatValue, 1, 32768);
                using (new EditorGUILayout.HorizontalScope())
                    foreach (int preset in new[] { 512, 1024, 2048, 3072 })
                        if (GUILayout.Button(preset.ToString())) size.floatValue = preset;
                bool sizeChanged = EditorGUI.EndChangeCheck();
                EditorGUI.BeginChangeCheck();
                density = EditorGUILayout.DelayedFloatField(new GUIContent("Mesh Resolution (vertices per metre)",
                    "Mesh sampling density along each axis. 1 = one vertex every metre; 2 = every 0.5 m; 0.25 = every 4 m. An extra endpoint closes each tile edge. Changing tile size preserves this density. The grid is rounded to whole intervals."), density);
                bool densityChanged = EditorGUI.EndChangeCheck();
                using (new EditorGUILayout.HorizontalScope())
                    foreach (float preset in new[] { .125f, .25f, .5f, 1f, 2f, 4f })
                        if (GUILayout.Button(preset.ToString("0.###") + " /m")) { density = preset; densityChanged = true; }
                if (sizeChanged || densityChanged)
                    resolution.intValue = ResolutionFromDensity(size.floatValue, density);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("TileMaterial"), new GUIContent("Surface Material"));
                serializedObject.ApplyModifiedProperties();
                bool tooDense = world.TileResolution > 2000;
                if (tooDense) EditorGUILayout.HelpBox("This density exceeds the 4 million vertices per tile limit. Reduce mesh resolution or tile size before generating.", MessageType.Info);
                using (new EditorGUI.DisabledScope(tooDense))
                if (GUILayout.Button("Generate Terrain Tile")) RunWorldTileAction(() =>
                {
                    var tile = GenerateTile(world);
                    SceneView.lastActiveSceneView?.Frame(MGTerrainTileAuthoring.BoundsOf(tile), false);
                });
            }
            DrawCreationDensity(world);
            using (new EditorGUI.DisabledScope(Application.isPlaying || empty))
            {
                bool next = GUILayout.Toggle(m_AddTiles, "Add Terrain Tile Mode", "Button");
                if (next != m_AddTiles) { m_AddTiles = next; SceneView.RepaintAll(); }
            }
            if (m_AddTiles) EditorGUILayout.HelpBox("Hover an empty neighbouring square in the Scene view and click to add it. Esc exits. Alt + mouse still navigates. New tiles match the adjacent tile's size and border heights.", MessageType.Info);
            if (!string.IsNullOrEmpty(m_TileError)) EditorGUILayout.HelpBox(m_TileError, MessageType.Error);
        }

        internal static int ResolutionFromDensity(float size, float density)
        {
            if (float.IsNaN(density) || float.IsInfinity(density) || density <= 0) density = .25f;
            return Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(10000000f, size * density))) + 1;
        }

        void DrawCreationDensity(MGTerrainWorld world)
        {
            float size = Mathf.Max(1, world.TileSize);
            int points = Mathf.Max(2, world.TileResolution);
            float intervals = points - 1;
            EditorGUILayout.LabelField("Creation Settings Density", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(new GUIContent("Vertex Spacing", "Distance between neighbouring mesh points along X and Z. Tile size / (points per side - 1)."),
                $"{size / intervals:0.###} m between vertices");
            EditorGUILayout.LabelField(new GUIContent("Vertex Density (per metre)", "Regular-grid sampling density: (points per side - 1) / tile size. Counts vertex intervals, excluding the extra endpoint shared with a neighbouring tile."),
                $"{intervals / size:0.###} along each axis");
            EditorGUILayout.LabelField(new GUIContent("Vertices Per Tile", "Includes all border vertices. Neighbouring tiles each store their own border vertices."),
                $"{(long)points * points:N0}");
            EditorGUILayout.LabelField("Triangles Per Tile", (2L * (points - 1) * (points - 1)).ToString("N0"));

            var material = world.TileMaterial;
            var properties = material != null ? material.GetTexturePropertyNames()
                .Where(p => material.GetTexture(p) is Texture2D).ToArray() : Array.Empty<string>();
            if (properties.Length == 0)
            {
                EditorGUILayout.LabelField("Texel Density", "No surface texture assigned");
                return;
            }
            int selected = Array.IndexOf(properties, m_DensityTextureProperty);
            if (selected < 0)
            {
                selected = Array.FindIndex(properties, p => p == "_BaseColorMap" || p == "_BaseMap" || p == "_MainTex");
                if (selected < 0) selected = 0;
            }
            selected = EditorGUILayout.Popup(new GUIContent("Density Reference Texture", "Select the surface material texture used for the UV0 density estimate. Each texture can have a different resolution and tiling."), selected,
                properties.Select(p => new GUIContent(material.GetTexture(p).name + " (" + p + ")")).ToArray());
            m_DensityTextureProperty = properties[selected];
            var texture = material.GetTexture(m_DensityTextureProperty);
            var tiling = material.GetTextureScale(m_DensityTextureProperty);
            EditorGUILayout.LabelField("Texture Resolution", $"{texture.width} x {texture.height}");
            EditorGUILayout.LabelField("Texture Tiling (U / V)", $"{tiling.x:0.###} / {tiling.y:0.###}");
            EditorGUILayout.LabelField(new GUIContent("Texels Per Metre (UV0 estimate)", "Texture resolution x absolute material tiling / tile size. Assumes the texture uses the generated tile's 0–1 UVs. Shader-specific world-space, triplanar or additional UV scaling is not included."),
                $"X: {texture.width * Mathf.Abs(tiling.x) / size:0.###}   Z: {texture.height * Mathf.Abs(tiling.y) / size:0.###}");
            EditorGUILayout.HelpBox("Texture density assumes UV0 mapping. World-space mapping or extra scaling inside the shader changes the actual density.", MessageType.None);
        }

        void RunWorldTileAction(Action action)
        {
            try { action(); m_TileError = null; }
            catch (Exception error) { m_TileError = error.Message; Debug.LogException(error, target); }
            Repaint(); SceneView.RepaintAll();
        }

        internal static MGTerrain GenerateTile(MGTerrainWorld world)
        {
            if (Application.isPlaying || EditorUtility.IsPersistent(world) || !world.gameObject.scene.IsValid())
                throw new InvalidOperationException("Create tiles in an editable scene outside Play Mode.");
            if (OwnedTiles(world).Length != 0) throw new InvalidOperationException("Use Add Terrain Tile Mode to extend this world.");
            if (Quaternion.Angle(world.transform.rotation, Quaternion.identity) > .001f || world.transform.lossyScale != Vector3.one)
                throw new InvalidOperationException("Terrain World requires zero world rotation and unit scale.");
            int n = world.TileResolution;
            float size = world.TileSize;
            if (n < 2 || n > 2000 || float.IsNaN(size) || float.IsInfinity(size) || size < 1 || size > 32768)
                throw new InvalidOperationException("Use a finite tile size from 1 to 32768 metres and a mesh density producing at most 4 million vertices per tile.");
            var shader = world.TileMaterial == null ? Shader.Find("HDRP/Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") : null;
            if (world.TileMaterial == null && shader == null) throw new InvalidOperationException("Assign a surface material before creating terrain.");
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Generate MG Terrain Tile");
            try
            {
                Undo.RecordObject(world, "Initialize Terrain World");
                var go = new GameObject("Terrain Tile (0, 0)");
                SceneManager.MoveGameObjectToScene(go, world.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(go, "Create Terrain Tile");
                go.transform.SetParent(world.transform, false); go.layer = world.gameObject.layer;
                var tile = Undo.AddComponent<MGTerrain>(go);
                var vertices = new Vector3[n * n]; var uv = new Vector2[vertices.Length];
                var triangles = new int[(n - 1) * (n - 1) * 6]; int index = 0;
                for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    uv[i] = new Vector2(x / (float)(n - 1), z / (float)(n - 1));
                    vertices[i] = new Vector3(uv[i].x * size, 0, uv[i].y * size);
                    if (x == n - 1 || z == n - 1) continue;
                    triangles[index++] = i; triangles[index++] = i + n; triangles[index++] = i + 1;
                    triangles[index++] = i + 1; triangles[index++] = i + n; triangles[index++] = i + n + 1;
                }
                var mesh = new Mesh { name = go.name, indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                    vertices = vertices, uv = uv, triangles = triangles };
                Undo.RegisterCreatedObjectUndo(mesh, "Create Terrain Mesh");
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                tile.MeshFilter.sharedMesh = mesh;
                var collider = Undo.AddComponent<MeshCollider>(go); collider.sharedMesh = mesh;
                tile.Configure(tile.MeshFilter, tile.MeshRenderer, collider); tile.ConfigureSurfaceGrid(n, n);
                var material = world.TileMaterial != null ? new Material(world.TileMaterial) : new Material(shader);
                material.name = go.name + " Surface";
                Undo.RegisterCreatedObjectUndo(material, "Create Terrain Material");
                tile.MeshRenderer.sharedMaterial = material; tile.HeightOnlySculpt = true;
                MGTerrainTileAuthoring.Modifier(tile);
                world.RefreshChunks(); world.ApplySharedQuality();
                EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
                Undo.CollapseUndoOperations(group);
                return tile;
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }

        bool DrawTilePlacement(SceneView view)
        {
            if (!m_AddTiles || Application.isPlaying) return false;
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            { m_AddTiles = false; e.Use(); Repaint(); view.Repaint(); return true; }
            var world = (MGTerrainWorld)target;
            var tiles = OwnedTiles(world).Where(t => t.MeshFilter != null && t.MeshFilter.sharedMesh != null).ToArray();
            var candidates = new List<(MGTerrain tile, Vector2Int direction, Bounds bounds)>();
            foreach (var tile in tiles)
            {
                var bounds = MGTerrainTileAuthoring.BoundsOf(tile);
                foreach (var direction in Directions)
                {
                    var candidate = new Bounds(bounds.center + new Vector3(direction.x * bounds.size.x, 0, direction.y * bounds.size.z), bounds.size);
                    if (tiles.Any(t => Overlaps(candidate, MGTerrainTileAuthoring.BoundsOf(t))) || candidates.Any(c => Overlaps(candidate, c.bounds))) continue;
                    candidates.Add((tile, direction, candidate));
                }
            }
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            int hovered = -1; float closest = float.PositiveInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                var b = candidates[i].bounds;
                if (new Plane(Vector3.up, b.center).Raycast(ray, out float distance))
                {
                    var p = ray.GetPoint(distance);
                    if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z && distance < closest)
                    { hovered = i; closest = distance; }
                }
            }
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout && !e.alt) HandleUtility.AddDefaultControl(control);
            if (e.type == EventType.Repaint)
                for (int i = 0; i < candidates.Count; i++)
                {
                    var b = candidates[i].bounds; float y = b.center.y;
                    var corners = new[] { new Vector3(b.min.x,y,b.min.z), new Vector3(b.min.x,y,b.max.z), new Vector3(b.max.x,y,b.max.z), new Vector3(b.max.x,y,b.min.z) };
                    Handles.DrawSolidRectangleWithOutline(corners, i == hovered ? new Color(.2f,1,.4f,.3f) : new Color(.2f,.7f,1,.08f), i == hovered ? Color.green : Color.cyan);
                    Handles.Label(b.center, "+ Add Terrain Tile");
                }
            if (e.type == EventType.MouseMove) view.Repaint();
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && hovered >= 0 && HandleUtility.nearestControl == control)
            {
                var chosen = candidates[hovered];
                RunWorldTileAction(() =>
                {
                    MGTerrainTileAuthoring.AddTile(chosen.tile, chosen.direction);
                    world.RefreshChunks(); world.ApplySharedQuality();
                    EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
                });
                e.Use();
            }
            return true;
        }

        static bool Overlaps(Bounds a, Bounds b) => Mathf.Min(a.max.x,b.max.x) - Mathf.Max(a.min.x,b.min.x) > .001f
            && Mathf.Min(a.max.z,b.max.z) - Mathf.Max(a.min.z,b.min.z) > .001f;
    }

    public static class MGTerrainWorldCreationValidation
    {
        [MenuItem("MashBox/Validation/Validate World Tile Creation")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var resources = new HashSet<UnityEngine.Object>();
            void Check(bool condition, string message)
            { if (!condition) throw new InvalidOperationException(message); }
            try
            {
                Check(MGTerrainWorldEditor.ResolutionFromDensity(512, .25f) == 129, "Quarter vertex per metre conversion.");
                Check(MGTerrainWorldEditor.ResolutionFromDensity(512, 1) == 513, "One vertex per metre conversion.");
                Check(MGTerrainWorldEditor.ResolutionFromDensity(512, 2) == 1025, "Two vertices per metre conversion.");
                Check(MGTerrainWorldEditor.ResolutionFromDensity(1024, .25f) == 257, "Density must stay constant when size changes.");
                foreach (int size in new[] { 512, 1024, 2048, 3072 })
                {
                    var root = new GameObject("World Creation Validation");
                    SceneManager.MoveGameObjectToScene(root, scene);
                    root.transform.position = new Vector3(-150, 23, 300);
                    var world = root.AddComponent<MGTerrainWorld>();
                    world.TileSize = size; world.TileResolution = size == 512 ? 257 : 33;
                    var tile = MGTerrainWorldEditor.GenerateTile(world);
                    Check(world.Chunks.Count == 1 && tile.World == world, "First tile must register in its world.");
                    var mesh = tile.MeshFilter.sharedMesh;
                    Check(mesh.vertexCount == world.TileResolution * world.TileResolution, "Grid resolution mismatch.");
                    Check(mesh.bounds.size.x == size && mesh.bounds.size.z == size, "Tile size mismatch.");
                    Check(tile.transform.position == root.transform.position, "World origin was not preserved.");
                    Check(tile.MeshCollider.sharedMesh == mesh, "Collider must use the terrain surface.");
                    if (size == 512) Check(mesh.indexFormat == IndexFormat.UInt32, "Large grids require 32-bit indices.");
                    bool rejected = false;
                    try { MGTerrainWorldEditor.GenerateTile(world); }
                    catch (InvalidOperationException) { rejected = true; }
                    Check(rejected && world.Chunks.Count == 1, "Generating into an occupied world must fail without mutation.");
                    foreach (var direction in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                    {
                        var neighbour = MGTerrainTileAuthoring.AddTile(tile, direction);
                        Check(neighbour.World == world, "Neighbour ownership mismatch.");
                        Check(neighbour.transform.position == root.transform.position + new Vector3(direction.x * size, 0, direction.y * size), "Neighbour placement mismatch.");
                        Check(neighbour.MeshFilter.sharedMesh != mesh, "Tiles must own separate meshes.");
                    }
                    Check(world.Chunks.Count == 5, "Cardinal additions must create five tiles.");
                    foreach (var child in world.Chunks)
                    {
                        resources.Add(child.MeshFilter.sharedMesh);
                        foreach (var material in child.MeshRenderer.sharedMaterials) resources.Add(material);
                    }
                }
                Debug.Log("MG Terrain World creation validation passed: sizes, origins, grids, colliders, ownership, occupied origin and four neighbour directions.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (var resource in resources) if (resource != null && !EditorUtility.IsPersistent(resource)) UnityEngine.Object.DestroyImmediate(resource);
            }
        }
    }
}
#endif

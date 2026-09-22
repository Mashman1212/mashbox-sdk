#if UNITY_EDITOR
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.MapTools
{
    // Editor preferences only: visual debugging never changes terrain assets or scene data.
    internal static class MGTerrainGizmos
    {
        const string Prefix = "MashBox.MGTerrain.Gizmos.";
        static readonly List<Vector3> Vertices = new List<Vector3>();
        static readonly List<Vector3> Lines = new List<Vector3>();
        static readonly Plane[] FrustumPlanes = new Plane[6];
        static Vector3[] OutlineLines = System.Array.Empty<Vector3>();
        static Vector3[] GridLines = System.Array.Empty<Vector3>();

        static void DrawBufferedLines(ref Vector3[] buffer)
        {
            if (buffer.Length != Lines.Count) buffer = new Vector3[Lines.Count];
            Lines.CopyTo(buffer);
            if (buffer.Length > 0) Handles.DrawLines(buffer);
        }
        static readonly Color OutlineColor = new Color(.2f, .85f, 1f, 1f);
        static readonly Color GridColor = new Color(.4f, 1f, .65f, .65f);
        static bool PerTileColors => EditorPrefs.GetBool(Prefix + "PerTileColors", true);
        static bool Outlines => EditorPrefs.GetBool(Prefix + "Outlines", true);
        static bool Grid => EditorPrefs.GetBool(Prefix + "Grid", false);
        static bool Labels => EditorPrefs.GetBool(Prefix + "Labels", false);
        static bool Through => EditorPrefs.GetBool(Prefix + "Through", true);

        internal static void DrawSettings()
        {
            bool expanded = SessionState.GetBool(Prefix + "Expanded", false);
            bool next = EditorGUILayout.Foldout(expanded, "Terrain Gizmos", true);
            if (next != expanded) SessionState.SetBool(Prefix + "Expanded", next);
            if (!next) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUI.BeginChangeCheck();
                bool outlines = EditorGUILayout.Toggle("Tile Outlines", Outlines);
                bool perTileColors = EditorGUILayout.Toggle("Different Color Per Tile", PerTileColors);
                bool labels = EditorGUILayout.Toggle("Tile Names", Labels);
                bool grid = EditorGUILayout.Toggle("Mesh Resolution Grid", Grid);
                bool through = EditorGUILayout.Toggle("Show Through Foliage", Through);
                int step = Mathf.Clamp(EditorPrefs.GetInt(Prefix + "Step", 1), 1, 32);
                float radius = Mathf.Clamp(EditorPrefs.GetFloat(Prefix + "Radius", 100f), 5f, 1000f);
                if (grid)
                {
                    step = EditorGUILayout.IntSlider("Every Nth Grid Line", step, 1, 32);
                    radius = EditorGUILayout.Slider("Grid Focus Radius (m)", radius, 5f, 1000f);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetBool(Prefix + "Outlines", outlines);
                    EditorPrefs.SetBool(Prefix + "PerTileColors", perTileColors);
                    EditorPrefs.SetBool(Prefix + "Labels", labels);
                    EditorPrefs.SetBool(Prefix + "Grid", grid);
                    EditorPrefs.SetBool(Prefix + "Through", through);
                    EditorPrefs.SetInt(Prefix + "Step", step);
                    EditorPrefs.SetFloat(Prefix + "Radius", radius);
                    SceneView.RepaintAll();
                }
                EditorGUILayout.HelpBox("Select a world to outline its tiles, or select individual tiles to highlight them. Requires Scene view Gizmos. Settings apply to all MG terrains in this editor.", MessageType.Info);
                if (grid) EditorGUILayout.HelpBox("The grid follows stored mesh vertices near the Scene view focus (orbit pivot), including cut holes. Grid drawing is capped at 40,000 segments; reduce the radius or increase the line interval for dense terrain. Unsupported or unreadable meshes show bounds only.", MessageType.Info);
            }
        }

        [DrawGizmo(GizmoType.Selected)]
        static void DrawWorld(MGTerrainWorld world, GizmoType type)
        {
            var view = SceneView.currentDrawingSceneView;
            if (view == null || !view.drawGizmos || (!Outlines && !Grid && !Labels)) return;
            GeometryUtility.CalculateFrustumPlanes(view.camera, FrustumPlanes);
            int budget = 40000;
            foreach (var tile in world.Chunks)
                if (tile != null) DrawTile(tile, view, ref budget);
        }

        [DrawGizmo(GizmoType.Selected)]
        static void DrawSelectedTile(MGTerrain tile, GizmoType type)
        {
            var view = SceneView.currentDrawingSceneView;
            if (view == null || !view.drawGizmos || (!Outlines && !Grid && !Labels)) return;
            // The selected world's pass already includes this tile.
            if (tile.World != null && Selection.Contains(tile.World.gameObject)) return;
            GeometryUtility.CalculateFrustumPlanes(view.camera, FrustumPlanes);
            int budget = 40000;
            DrawTile(tile, view, ref budget);
        }

        // Position in the world grid keeps colors stable across reloads, selection,
        // hierarchy reordering and moving the whole terrain world. No asset writes.
        static Color TileColor(MGTerrain tile)
        {
            var world = tile.World;
            Vector3 position = world != null
                ? world.transform.InverseTransformPoint(tile.transform.position)
                : tile.transform.position;
            float size = world != null ? Mathf.Max(.001f, world.TileSize) : 1f;
            float hue = Mathf.Repeat(.52f + Mathf.Round(position.x / size) * .618034f
                + Mathf.Round(position.z / size) * .277f, 1f);
            return Color.HSVToRGB(hue, .65f, 1f);
        }
        static void DrawTile(MGTerrain tile, SceneView view, ref int budget)
        {
            var filter = tile.MeshFilter;
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return;
            var renderer = tile.GetComponent<MeshRenderer>();
            if (renderer != null && !GeometryUtility.TestPlanesAABB(FrustumPlanes, renderer.bounds)) return;
            Color oldColor = Handles.color;
            Matrix4x4 oldMatrix = Handles.matrix;
            CompareFunction oldDepth = Handles.zTest;
            try
            {
                Handles.matrix = filter.transform.localToWorldMatrix;
                Handles.zTest = Through ? CompareFunction.Always : CompareFunction.LessEqual;
                bool perTileColors = PerTileColors;
                Color tileColor = perTileColors ? TileColor(tile) : OutlineColor;
                Handles.color = Selection.Contains(tile.gameObject)
                    ? (perTileColors ? Color.Lerp(tileColor, Color.white, .3f) : Color.yellow) : tileColor;
                if (Labels) Handles.Label(mesh.bounds.center + Vector3.up * mesh.bounds.extents.y, tile.name);
                if (!Outlines && !Grid) return;
                int width, height;
                using (var data = new SerializedObject(tile))
                {
                    width = data.FindProperty("m_SurfaceGridWidth").intValue;
                    height = data.FindProperty("m_SurfaceGridHeight").intValue;
                }
                if (!mesh.isReadable || width < 2 || height < 2 || (long)width * height != mesh.vertexCount)
                {
                    if (Outlines) Handles.DrawWireCube(mesh.bounds.center, mesh.bounds.size);
                    return;
                }
                float radius = Mathf.Clamp(EditorPrefs.GetFloat(Prefix + "Radius", 100f), 5f, 1000f);
                bool drawGrid = Grid && budget > 0 && (renderer == null || renderer.bounds.SqrDistance(view.pivot) <= radius * radius);
                if (!Outlines && !drawGrid) return;
                // Reuse buffers but read current positions on repaint so sculpting and Undo stay visible.
                mesh.GetVertices(Vertices);
                if (Outlines)
                {
                    Lines.Clear();
                    for (int x = 0; x < width - 1; x++)
                    {
                        AddEdge(x, x + 1);
                        AddEdge((height - 1) * width + x, (height - 1) * width + x + 1);
                    }
                    for (int z = 0; z < height - 1; z++)
                    {
                        AddEdge(z * width, (z + 1) * width);
                        AddEdge(z * width + width - 1, (z + 1) * width + width - 1);
                    }
                    DrawBufferedLines(ref OutlineLines);
                }
                if (!drawGrid) return;
                Lines.Clear();
                int step = Mathf.Clamp(EditorPrefs.GetInt(Prefix + "Step", 1), 1, 32);
                // Limit iteration as well as drawing to the focus region in local X/Z.
                var localFocus = filter.transform.InverseTransformPoint(view.pivot);
                var inverse = filter.transform.worldToLocalMatrix;
                float rx = radius * new Vector3(inverse.m00, inverse.m01, inverse.m02).magnitude;
                float rz = radius * new Vector3(inverse.m20, inverse.m21, inverse.m22).magnitude;
                float dx = (Vertices[width - 1].x - Vertices[0].x) / (width - 1);
                float dz = (Vertices[(height - 1) * width].z - Vertices[0].z) / (height - 1);
                if (dx <= 0 || dz <= 0) return;
                int minX = Mathf.Clamp(Mathf.FloorToInt((localFocus.x - rx - Vertices[0].x) / dx), 0, width - 1);
                int maxX = Mathf.Clamp(Mathf.CeilToInt((localFocus.x + rx - Vertices[0].x) / dx), 0, width - 1);
                int minZ = Mathf.Clamp(Mathf.FloorToInt((localFocus.z - rz - Vertices[0].z) / dz), 0, height - 1);
                int maxZ = Mathf.Clamp(Mathf.CeilToInt((localFocus.z + rz - Vertices[0].z) / dz), 0, height - 1);
                for (int z = ((minZ + step - 1) / step) * step; z <= maxZ && budget > 0; z += step)
                    for (int x = minX; x < maxX && budget > 0; x++)
                        AddGridEdge(z * width + x, z * width + x + 1, filter.transform, view.pivot, radius, ref budget);
                for (int x = ((minX + step - 1) / step) * step; x <= maxX && budget > 0; x += step)
                    for (int z = minZ; z < maxZ && budget > 0; z++)
                        AddGridEdge(z * width + x, (z + 1) * width + x, filter.transform, view.pivot, radius, ref budget);
                Handles.color = perTileColors ? new Color(tileColor.r, tileColor.g, tileColor.b, GridColor.a) : GridColor;
                DrawBufferedLines(ref GridLines);
            }
            finally
            {
                Handles.color = oldColor;
                Handles.matrix = oldMatrix;
                Handles.zTest = oldDepth;
            }
        }

        static void AddEdge(int a, int b)
        {
            // Small local-space lift avoids z-fighting when depth testing is enabled.
            Lines.Add(Vertices[a] + Vector3.up * .03f);
            Lines.Add(Vertices[b] + Vector3.up * .03f);
        }

        static void AddGridEdge(int a, int b, Transform transform, Vector3 focus, float radius, ref int budget)
        {
            Vector3 midpoint = transform.TransformPoint((Vertices[a] + Vertices[b]) * .5f);
            if ((midpoint - focus).sqrMagnitude > radius * radius) return;
            AddEdge(a, b);
            budget--;
        }
    }
}
#endif

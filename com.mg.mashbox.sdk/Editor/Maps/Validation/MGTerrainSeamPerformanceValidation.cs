#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using MashBoxSDK.Maps.Sculpting;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MeshSculptWindow
    {
        [MenuItem("Tools/MashBox/MG Terrain/Validate Brush Vertex Buffers")]
        public static void ValidateBrushVertexBuffers()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            Mesh mesh = null;
            try
            {
                const int width = 129;
                var vertices = new Vector3[width * width];
                var indices = new int[(width - 1) * (width - 1) * 6];
                for (int z = 0; z < width; z++) for (int x = 0; x < width; x++)
                    vertices[z * width + x] = new Vector3(x, 0, z);
                for (int z = 0, t = 0; z < width - 1; z++) for (int x = 0; x < width - 1; x++)
                {
                    int i = z * width + x;
                    indices[t++] = i; indices[t++] = i + width; indices[t++] = i + 1;
                    indices[t++] = i + 1; indices[t++] = i + width; indices[t++] = i + width + 1;
                }
                mesh = new Mesh { vertices = vertices, triangles = indices };
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var go = new GameObject("Brush buffer validation");
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.localScale = new Vector3(2, 3, 4);
                var terrain = go.AddComponent<MGTerrain>();
                terrain.MeshFilter.sharedMesh = mesh;
                terrain.HeightOnlySculpt = true;
                var modifier = go.AddComponent<MeshSculptModifier>();
                modifier.SetTarget(terrain.MeshFilter);
                // Exercise preview without the asset preparation hook in this isolated scene.
                typeof(MeshSculptModifier).GetField("m_TerrainStroke", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(modifier, modifier.CreateStroke(MeshSculptModifier.SculptMode.Displace,
                        MeshSculptModifier.StrokeSpace.World, Vector3.zero, Vector3.up, 20, 1, 1));
                modifier.ApplyLatestStrokePreview();
                vertices[0].y = 7;
                mesh.vertices = vertices; // External edit/Undo must supersede cached contents.
                var timer = new System.Diagnostics.Stopwatch();
                long before = GC.GetAllocatedBytesForCurrentThread();
                timer.Start();
                for (int i = 0; i < 30; i++) modifier.ApplyLatestStrokePreview();
                timer.Stop();
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                var actual = mesh.vertices;
                if (Mathf.Abs(actual[0].y - 17) > 0.0001f || actual[width * width - 1] != vertices[width * width - 1])
                    throw new InvalidOperationException("Brush buffers ignored external mesh edits, scale, or radius.");
                for (int i = 0; i < actual.Length; i++)
                    if (actual[i].x != vertices[i].x || actual[i].z != vertices[i].z)
                        throw new InvalidOperationException("Height brush changed planar coordinates.");
                if (bytes / 30 > 4096) throw new InvalidOperationException("Brush preview still allocates full vertex arrays.");
                Debug.Log($"MG TERRAIN BRUSH BUFFER VALIDATION PASSED: external edits, nonuniform scale, radius and height-only coordinates preserved. 129x129 preview: {timer.Elapsed.TotalMilliseconds / 30:F3} ms/sample, {bytes / 30:N0} managed bytes/sample (synthetic scene).");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (mesh != null) DestroyImmediate(mesh);
            }
        }
        [MenuItem("Tools/MashBox/MG Terrain/Validate Explicit Sculpt Saving")]
        public static void ValidateExplicitSculptSaving()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            string path = "Assets/__MGSaveValidation_" + Guid.NewGuid().ToString("N") + ".asset";
            Mesh mesh = null;
            GameObject go = null;
            void Cleanup()
            {
                if (mesh != null) Undo.ClearUndo(mesh);
                if (go != null)
                    foreach (var component in go.GetComponents<Component>()) Undo.ClearUndo(component);
                EditorSceneManager.ClosePreviewScene(scene);
                AssetDatabase.DeleteAsset(path);
            }
            try
            {
                mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward, Vector3.right + Vector3.forward },
                    triangles = new[] { 0,2,1,1,2,3 } };
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, path);
                byte[] before = System.IO.File.ReadAllBytes(path);
                go = new GameObject("Explicit sculpt save validation");
                SceneManager.MoveGameObjectToScene(go, scene);
                var terrain = go.AddComponent<MGTerrain>();
                terrain.MeshFilter.sharedMesh = mesh;
                terrain.ConfigureSurfaceGrid(2, 2);
                using (var data = new SerializedObject(terrain))
                {
                    data.FindProperty("m_EditableSculptMesh").objectReferenceValue = mesh;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                var modifier = go.AddComponent<MeshSculptModifier>(); modifier.SetTarget(terrain.MeshFilter);
                modifier.AddStroke(modifier.CreateStroke(MeshSculptModifier.SculptMode.Displace,
                    MeshSculptModifier.StrokeSpace.World, Vector3.zero, Vector3.up, 5, 1, 1));
                modifier.ApplyLatestStrokePreview(); modifier.FinalizeStrokePreview();
                double deadline = EditorApplication.timeSinceStartup + 2.0;
                void CheckAfterIdle()
                {
                    if (EditorApplication.timeSinceStartup < deadline) return;
                    EditorApplication.update -= CheckAfterIdle;
                    try
                    {
                        bool Equal(byte[] a, byte[] b)
                        {
                            if (a.Length != b.Length) return false;
                            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
                            return true;
                        }
                        if (!EditorUtility.IsDirty(mesh) || !Equal(before, System.IO.File.ReadAllBytes(path)))
                            throw new InvalidOperationException("Stroke completion/idle unexpectedly saved the mesh.");
                        AssetDatabase.SaveAssetIfDirty(mesh);
                        if (Equal(before, System.IO.File.ReadAllBytes(path)))
                            throw new InvalidOperationException("Explicit save did not persist the changed mesh.");
                        Debug.Log("MG TERRAIN EXPLICIT SAVE VALIDATION PASSED: completed sculpt remained dirty and disk bytes unchanged after two seconds; explicit save persisted the changes.");
                    }
                    catch (Exception error) { Debug.LogException(error); }
                    finally { Cleanup(); }
                }
                EditorApplication.update += CheckAfterIdle;
            }
            catch { Cleanup(); throw; }
        }


        [MenuItem("Tools/MashBox/MG Terrain/Validate Buffered Seam Joining")]
        public static void ValidateBufferedSeamJoining()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var meshes = new List<Mesh>();
            var tiles = new List<MGTerrain>();
            const int width = 129;
            const int cells = width - 1;
            var vertices = new Vector3[width * width];
            var triangles = new int[cells * cells * 6];
            for (int z = 0, t = 0; z < cells; z++) for (int x = 0; x < cells; x++)
            {
                int i = z * width + x;
                triangles[t++] = i; triangles[t++] = i + width; triangles[t++] = i + 1;
                triangles[t++] = i + 1; triangles[t++] = i + width; triangles[t++] = i + width + 1;
            }
            void Check(bool condition, string message)
            { if (!condition) throw new InvalidOperationException(message); }
            try
            {
                for (int n = 0; n < 4; n++)
                {
                    var go = new GameObject("Buffered seam test " + n);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    go.transform.position = new Vector3((n % 2) * cells, 0, (n / 2) * cells);
                    var tile = go.AddComponent<MGTerrain>();
                    for (int z = 0; z < width; z++) for (int x = 0; x < width; x++) vertices[z * width + x] = new Vector3(x, n, z);
                    var mesh = new Mesh { vertices = vertices, triangles = triangles };
                    mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes.Add(mesh);
                    tile.MeshFilter.sharedMesh = mesh;
                    tile.ConfigureSurfaceGrid(width, width);
                    tiles.Add(tile);
                }
                var center = new Vector3(cells, 0, cells);
                MGTerrainTileAuthoring.JoinBrushEdges(tiles, center, 20, preparedMeshes: true);
                var shared = new Dictionary<Vector2Int, (float height, Vector3 normal)>();
                for (int n = 0; n < tiles.Count; n++)
                {
                    var tile = tiles[n]; var mesh = meshes[n];
                    Check(tile.MeshFilter.sharedMesh == mesh && tile.GetComponent<MeshSculptModifier>() == null,
                        "Joining prepared meshes must not replace meshes or create sculpt workers.");
                    var positions = mesh.vertices; var normals = mesh.normals;
                    Check(positions[width * (width / 2) + width / 2].y == n, "Interior vertices must remain unchanged.");
                    Check(mesh.triangles.Length == triangles.Length, "Joining must preserve topology.");
                    for (int i = 0; i < positions.Length; i++)
                    {
                        int x = i % width, z = i / width;
                        if (x != 0 && x != cells && z != 0 && z != cells) continue;
                        var p = tile.transform.TransformPoint(positions[i]);
                        if (new Vector2(p.x - center.x, p.z - center.z).sqrMagnitude > 400) continue;
                        var key = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z));
                        if (shared.TryGetValue(key, out var previous))
                        {
                            Check(Mathf.Abs(previous.height - p.y) < 1e-5f, "Shared edge/corner heights must agree.");
                            Check((previous.normal - normals[i]).sqrMagnitude < 1e-8f, "Shared edge/corner normals must agree.");
                        }
                        else shared.Add(key, (p.y, normals[i]));
                    }
                }
                Check(Mathf.Abs(meshes[0].vertices[width * width - 1].y - 1.5f) < 1e-5f, "Four-way corner must average all four tiles.");
                // Measure changing borders, not just the idempotent no-op path.
                var timer = new System.Diagnostics.Stopwatch();
                long allocated = 0;
                const int iterations = 30;
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    for (int n = 0; n < meshes.Count; n++)
                    {
                        for (int z = 0; z < width; z++) for (int x = 0; x < width; x++) vertices[z * width + x] = new Vector3(x, n + iteration * .01f, z);
                        meshes[n].vertices = vertices;
                        meshes[n].RecalculateNormals();
                    }
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    timer.Start();
                    MGTerrainTileAuthoring.JoinBrushEdges(tiles, center, 20, preparedMeshes: true);
                    timer.Stop();
                    allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                }
                Debug.Log($"MG TERRAIN BUFFERED SEAM VALIDATION PASSED: four 129x129 tiles, shared heights/normals and four-way corner, unchanged interiors, stable mesh identity. Changing-border join: {timer.Elapsed.TotalMilliseconds / iterations:F3} ms/sample, {allocated / iterations:N0} managed bytes/sample across {iterations} warmed samples (synthetic preview scene, not full frame time).");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (var mesh in meshes) if (mesh != null) DestroyImmediate(mesh);
            }
        }


        [MenuItem("Tools/MashBox/MG Terrain/Validate Seam Stroke Performance")]
        public static void ValidateSeamStrokePerformance()
        {
            if (GUIUtility.hotControl != 0 || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run validation outside a stroke and Play Mode.");
            var scene = EditorSceneManager.NewPreviewScene();
            var resources = new List<UnityEngine.Object>();
            var owner = s_ActiveSceneToolOwner;
            bool editing = MBEditorToolState.ActiveEditing;
            Tool tool = Tools.current;
            MeshSculptWindow window = null;
            Editor editor = null;
            void Check(bool condition, string message)
            { if (!condition) throw new InvalidOperationException(message); }
            try
            {
                MBEditorToolState.ActiveEditing = false;
                Mesh MakeMesh(float height)
                {
                    var mesh = new Mesh
                    {
                        vertices = new[] { new Vector3(0,height,0), new Vector3(1,height,0), new Vector3(0,height,1), new Vector3(1,height,1) },
                        triangles = new[] { 0,2,1,1,2,3 }
                    };
                    mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    resources.Add(mesh);
                    return mesh;
                }
                var original = MakeMesh(0);
                var changed = MakeMesh(1);
                MGTerrain MakeTile(string name)
                {
                    var go = new GameObject(name);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    var tile = go.AddComponent<MGTerrain>();
                    tile.MeshFilter.sharedMesh = original;
                    var collider = go.AddComponent<MeshCollider>();
                    collider.sharedMesh = original;
                    tile.Configure(tile.MeshFilter, tile.MeshRenderer, collider);
                    tile.ConfigureSurfaceGrid(2, 2);
                    return tile;
                }
                var first = MakeTile("Seam regression A");
                var second = MakeTile("Seam regression B");
                var a = first.gameObject.AddComponent<MeshSculptModifier>(); a.SetTarget(first.MeshFilter); a.UpdateMeshCollider = true;
                var b = second.gameObject.AddComponent<MeshSculptModifier>(); b.SetTarget(second.MeshFilter); b.UpdateMeshCollider = true;
                first.MeshFilter.sharedMesh = changed;
                second.MeshFilter.sharedMesh = changed;
                window = CreateInstance<MeshSculptWindow>();
                window.m_Modifier = a;
                window.m_IsSculpting = true;
                window.m_StrokeModifiers.Add(a); window.m_StrokeModifiers.Add(b);
                for (int i = 0; i < 20; i++) window.SwitchModifierDuringStroke((i & 1) == 0 ? b : a);
                Check(first.MeshCollider.sharedMesh == original && second.MeshCollider.sharedMesh == original,
                    "Crossing a seam must not recook either collider before mouse-up.");
                Check(window.m_StrokeModifiers.Count == 2 && window.m_IsSculpting,
                    "Repeated seam crossings must preserve one stroke and both touched tiles.");
                window.StopStroke();
                Check(first.MeshCollider.sharedMesh == changed && second.MeshCollider.sharedMesh == changed,
                    "Mouse-up must finalize both terrain colliders, including tiles without child colliders.");
                Check(window.m_StrokeModifiers.Count == 0 && !window.m_IsSculpting, "Mouse-up must clear the active stroke.");

                var field = typeof(MGTerrainEditor).GetField("m_PaintCopies", BindingFlags.Instance | BindingFlags.NonPublic);
                var map = new Texture2D(2, 2); resources.Add(map);
                editor = Editor.CreateEditor(first, typeof(MGTerrainEditor));
                ((HashSet<Texture2D>)field.GetValue(editor)).Add(map);
                DestroyImmediate(editor); editor = Editor.CreateEditor(first, typeof(MGTerrainEditor));
                Check(((HashSet<Texture2D>)field.GetValue(editor)).Contains(map),
                    "Recreating a neighbor editor must retain paint map ownership.");
                DestroyImmediate(editor); editor = Editor.CreateEditor(second, typeof(MGTerrainEditor));
                Check(!((HashSet<Texture2D>)field.GetValue(editor)).Contains(map),
                    "Paint map ownership must remain isolated between tiles.");
                Debug.Log("MG TERRAIN SEAM PERFORMANCE VALIDATION PASSED: 20 seam crossings without collider finalization; both colliders finalized on mouse-up; paint ownership survives editor recreation and stays isolated per tile.");
            }
            finally
            {
                if (editor != null) DestroyImmediate(editor);
                if (window != null) DestroyImmediate(window);
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (var resource in resources) if (resource != null) DestroyImmediate(resource);
                MBEditorToolState.ActiveEditing = editing;
                Tools.current = tool;
                if (owner != null && editing) owner.ActivateSceneTool();
            }
        }
    }
}
#endif

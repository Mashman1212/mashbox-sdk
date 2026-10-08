#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    public static class MGTerrainMergeValidation
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static Mesh Grid(int size, Func<float, float, float> height)
        {
            var vertices = new Vector3[size * size]; var uv = new Vector2[vertices.Length];
            var triangles = new int[(size - 1) * (size - 1) * 6];
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                float px = 8f * x / (size - 1), pz = 8f * z / (size - 1);
                vertices[z * size + x] = new Vector3(px, height(px, pz), pz);
                uv[z * size + x] = new Vector2(px / 8, pz / 8);
            }
            for (int z = 0, t = 0; z < size - 1; z++) for (int x = 0; x < size - 1; x++)
            {
                int i = z * size + x;
                triangles[t++] = i; triangles[t++] = i + size; triangles[t++] = i + 1;
                triangles[t++] = i + 1; triangles[t++] = i + size; triangles[t++] = i + size + 1;
            }
            var mesh = new Mesh { vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        static MGTerrain Tile(Scene scene, Mesh mesh, string name, Vector3 position)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = position;
            var tile = go.AddComponent<MGTerrain>(); tile.MeshFilter.sharedMesh = mesh;
            int size = Mathf.RoundToInt(Mathf.Sqrt(mesh.vertexCount)); tile.ConfigureSurfaceGrid(size, size);
            var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
            tile.Configure(tile.MeshFilter, tile.MeshRenderer, collider);
            return tile;
        }
        static void Sampling()
        {
            var mesh = Grid(5, (x, z) => x * 2 + z * 3);
            try
            {
                var matrix = Matrix4x4.TRS(new Vector3(100, -40, 30), Quaternion.identity, new Vector3(2, 3, 4));
                var surface = new MGTerrainMergeSurface(mesh, matrix);
                Check(surface.TryHeight(new Vector3(106, 0, 50), out float height) && Mathf.Abs(height - 23) < .0001f,
                    "World-space interpolation ignored source scale or translation.");
                Check(!surface.TryHeight(Vector3.zero, out _), "Sampling outside the source footprint succeeded.");
                Check(surface.TryHeight(matrix.MultiplyPoint3x4(new Vector3(8, 0, 8)), out height) && Mathf.Abs(height - 80) < .0001f,
                    "Source maximum edge could not be sampled.");
                var changed = mesh.vertices; for (int i = 0; i < changed.Length; i++) changed[i].y = 1000; mesh.vertices = changed;
                Check(surface.TryHeight(new Vector3(106, 0, 50), out height) && Mathf.Abs(height - 23) < .0001f, "Source snapshot was not immutable.");
                Check(MGTerrainMergeSession.Weight(10, 10, .5f, 1) == 0 && MGTerrainMergeSession.Weight(0, 10, .5f, .5f) == .5f,
                    "Brush radius or strength is incorrect.");
                Check(MGTerrainMergeSession.Weight(7.5f, 10, .5f, 1) > 0 && MGTerrainMergeSession.Weight(7.5f, 10, .5f, 1) < 1,
                    "Brush falloff has no soft transition.");
                mesh.triangles = new[] { 0, 5, 1 };
                var partial = new MGTerrainMergeSurface(mesh, Matrix4x4.identity);
                Check(!partial.TryHeight(new Vector3(7, 0, 7), out _), "Uncovered geometry was treated as source terrain.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [MenuItem("Tools/MashBox/MG Terrain/Validation/Terrain Merge")]
        public static void Run()
        {
            Sampling();
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                if (!Application.isBatchMode) throw new InvalidOperationException("Save the current untitled scene before running terrain merge validation.");
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/MergeValidationBootstrap.unity");
            }
            string folder = "Assets/__TerrainMergeValidation_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            var originalActive = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            MGTerrainMergeSession session = null;
            try
            {
                string scenePath = folder + "/Destination.unity";
                Check(EditorSceneManager.SaveScene(scene, scenePath), "Could not save validation scene.");
                var original = Grid(9, (x, z) => 0); AssetDatabase.CreateAsset(original, folder + "/Original.asset");
                var source = Grid(5, (x, z) => 10); AssetDatabase.CreateAsset(source, folder + "/Source.asset");
                var tile = Tile(scene, original, "Destination", Vector3.zero);
                var chunkObject = new GameObject("Saved collision chunk"); chunkObject.transform.SetParent(tile.transform, false);
                var chunk = chunkObject.AddComponent<MeshCollider>();
                var originalCollision = Object.Instantiate(original); AssetDatabase.CreateAsset(originalCollision, folder + "/OriginalCollision.asset");
                chunk.sharedMesh = originalCollision;
                using (var data = new SerializedObject(tile))
                {
                    var chunks = data.FindProperty("m_SurfaceColliderChunks"); chunks.arraySize = 1;
                    chunks.GetArrayElementAtIndex(0).objectReferenceValue = chunk;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                tile.SetSurfaceColliderVertexMaps(new[] { new MGTerrain.SurfaceColliderVertexMap(chunk, Enumerable.Range(0, 81).ToArray()) }, 81);
                var shared = Tile(scene, original, "Shared reference", new Vector3(30, 0, 0));
                SourceScenePreview(folder, tile, source);
                var surfaces = new[] { new MGTerrainMergeSurface(source, Matrix4x4.identity) };
                session = new MGTerrainMergeSession(new List<MGTerrain> { tile }, surfaces);
                Check(session.CoveredCount == 81 && tile.MeshFilter.sharedMesh == original, "Preview mutated the destination or missed coverage.");
                session.BeginStroke(); session.Paint(new Vector3(4, 0, 4), 2, .5f, .5f); session.EndStroke();
                var first = tile.MeshFilter.sharedMesh;
                Check(first != original && AssetDatabase.Contains(first), "Merge failed to create an independent persistent mesh.");
                Check(first.vertices[40].y == 5 && first.vertices[0].y == 0 && first.vertices[38].y == 0,
                    "Brush changed heights outside its radius or applied the wrong strength.");
                Check(original.vertices.All(p => p.y == 0) && shared.MeshFilter.sharedMesh == original && source.vertices.All(p => p.y == 10),
                    "Merge modified a source/shared asset.");
                Check(chunk.sharedMesh != originalCollision && originalCollision.vertices.All(p => p.y == 0)
                    && chunk.sharedMesh.vertices[40].y == 5, "Merge mutated shared collision or failed to refresh its independent copy.");
                var originalUV = original.uv;
                Check(first.triangles.SequenceEqual(original.triangles) && first.uv.SequenceEqual(originalUV), "Merge changed topology or UVs.");
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Undo.PerformUndo(); session.RefreshAfterUndo();
                Check(tile.MeshFilter.sharedMesh == original, "Undo did not restore the original mesh reference.");
                Undo.PerformRedo(); session.RefreshAfterUndo();
                Check(tile.MeshFilter.sharedMesh == first && first.vertices[40].y == 5,
                    $"Redo did not restore the merged sculpt: reference={tile.MeshFilter.sharedMesh == first}, current={tile.MeshFilter.sharedMesh.vertices[40].y}, merged={first.vertices[40].y}, collision={chunk.sharedMesh.vertices[40].y}.");
                session.BeginStroke(); session.Paint(new Vector3(4, 0, 4), 2, .5f, 1); session.EndStroke(true);
                Check(first.vertices[40].y == 5, "Cancelling a stroke did not restore the preceding sculpt.");
                session.BeginStroke(); session.Paint(Vector3.zero, 1, 1, 1, true); session.EndStroke();
                Check(first.vertices.All(p => Mathf.Abs(p.y - 10) < .0001f), "Whole-tile transfer was incomplete.");
                session.BeginStroke(); int repeat = session.Paint(Vector3.zero, 1, 1, 1, true); session.EndStroke();
                Check(repeat == 0 && first.vertices.All(p => p.y == 10), "Repeated merge stacked source heights.");
                tile.RaycastEditingSurface(new Ray(new Vector3(4, 100, 4), Vector3.down), out var hit, 200);
                Check(Mathf.Abs(hit.point.y - 10) < .001f, "Editing collision did not follow the merged surface.");
                session.Dispose(); session = null;

                // Different-resolution neighbours share one world. Their old sculpt meshes
                // remain untouched, while a brush at the boundary closes the entire seam.
                var root = new GameObject("Merge world"); SceneManager.MoveGameObjectToScene(root, scene);
                var world = root.AddComponent<MGTerrainWorld>();
                var coarseMesh = Grid(3, (x, z) => 0); AssetDatabase.CreateAsset(coarseMesh, folder + "/Coarse.asset");
                var left = Tile(scene, original, "Left", new Vector3(0, 0, 20)); left.transform.SetParent(root.transform, true);
                var right = Tile(scene, coarseMesh, "Right", new Vector3(8, 0, 20)); right.transform.SetParent(root.transform, true);
                session = new MGTerrainMergeSession(new List<MGTerrain> { left }, new[] {
                    new MGTerrainMergeSurface(source, Matrix4x4.Translate(new Vector3(0, 0, 20))) });
                session.BeginStroke(); session.Paint(new Vector3(8, 0, 24), 3, 0, 1); session.EndStroke();
                Check(left.MeshFilter.sharedMesh != original && right.MeshFilter.sharedMesh != coarseMesh, "Seam neighbour was not detached.");
                var leftEdge = left.MeshFilter.sharedMesh.vertices.Where(p => p.x == 8).OrderBy(p => p.z).ToArray();
                var rightEdge = right.MeshFilter.sharedMesh.vertices.Where(p => p.x == 0).OrderBy(p => p.z).ToArray();
                foreach (var p in leftEdge)
                {
                    int index = p.z < 4 ? 0 : 1;
                    float y = Mathf.Lerp(rightEdge[index].y, rightEdge[index + 1].y, (p.z - rightEdge[index].z) / 4);
                    Check(Mathf.Abs(p.y - y) < .0001f, "Mixed-resolution merge opened a seam.");
                }
                Check(original.vertices.All(p => p.y == 0) && coarseMesh.vertices.All(p => p.y == 0), "Seam merge modified shared original meshes.");
                session.Dispose(); session = null;

                // Duplicate scene ownership: even with the original scene closed, a sculpt
                // mesh marked editable in another folder must be cloned before ordinary sculpting.
                var foreign = Grid(3, (x, z) => 3); AssetDatabase.CreateAsset(foreign, folder + "/ForeignSceneSculpt.asset");
                var duplicate = Tile(scene, foreign, "Copied terrain", new Vector3(60, 0, 0));
                using (var data = new SerializedObject(duplicate))
                { data.FindProperty("m_EditableSculptMesh").objectReferenceValue = foreign; data.ApplyModifiedPropertiesWithoutUndo(); }
                Undo.IncrementCurrentGroup();
                MGTerrainDirectSculpt.Prepare(MGTerrainTileAuthoring.Modifier(duplicate));
                Check(duplicate.MeshFilter.sharedMesh != foreign, "Copied scene retained another scene's editable sculpt mesh.");

                string savedMeshPath = AssetDatabase.GetAssetPath(first);
                AssetDatabase.SaveAssets(); Check(EditorSceneManager.SaveScene(scene), "Could not persist merge result.");
                EditorSceneManager.CloseScene(scene, true);
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                var reloaded = scene.GetRootGameObjects().First(go => go.name == "Destination").GetComponent<MGTerrain>();
                Check(AssetDatabase.GetAssetPath(reloaded.MeshFilter.sharedMesh) == savedMeshPath
                    && reloaded.MeshFilter.sharedMesh.vertices.All(p => Mathf.Abs(p.y - 10) < .0001f), "Merged sculpt did not survive scene reload.");
                Check(source.vertices.All(p => p.y == 10) && original.vertices.All(p => p.y == 0), "Saving changed original assets.");
                Debug.Log("MG TERRAIN MERGE VALIDATION PASSED: saved-scene preview loading, transformed triangle sampling, snapshot isolation, coverage, soft brush bounds, strength, whole tile, no height stacking, independent meshes and colliders, mixed-resolution seams, source preservation, UV/topology preservation, collision picking, Undo/Redo, cancelled strokes, copied-scene ownership, and save/reload.");
            }
            finally
            {
                session?.Dispose();
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
                // This unique folder contains only fixtures created by this run.
                AssetDatabase.DeleteAsset(folder);
            }
        }
        static void SourceScenePreview(string folder, MGTerrain destination, Mesh source)
        {
            var sourceScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            string path = folder + "/SourceScene.unity";
            try
            {
                Tile(sourceScene, source, "Source", Vector3.zero);
                Check(EditorSceneManager.SaveScene(sourceScene, path), "Could not save source fixture.");
            }
            finally { EditorSceneManager.CloseScene(sourceScene, true); }
            byte[] before = System.IO.File.ReadAllBytes(path);
            var window = ScriptableObject.CreateInstance<MGTerrainMergeWindow>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            try
            {
                typeof(MGTerrainMergeWindow).GetField("destination", flags).SetValue(window, destination.gameObject);
                typeof(MGTerrainMergeWindow).GetField("source", flags).SetValue(window, AssetDatabase.LoadAssetAtPath<SceneAsset>(path));
                typeof(MGTerrainMergeWindow).GetMethod("BuildPreview", flags).Invoke(window, null);
                var preview = (MGTerrainMergeSession)typeof(MGTerrainMergeWindow).GetField("session", flags).GetValue(window);
                Check(preview != null && preview.CoveredCount == 81 && preview.targets[0].sourceHeights.All(y => y == 10),
                    "Saved source scene failed to build a usable merge preview.");
                Check(before.SequenceEqual(System.IO.File.ReadAllBytes(path)) && source.vertices.All(p => p.y == 10),
                    "Opening a source preview changed source assets.");
            }
            finally { Object.DestroyImmediate(window); }
        }

        public static void RunGraphics()
        {
            Run();
            var shader = Shader.Find("Hidden/MashBox/TerrainMergeOverlay");
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Merge overlay shader is missing or has import errors.");
            Check(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "Overlay validation requires a graphics device.");
            var material = new Material(shader);
            var mesh = new Mesh { vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(-1,1,0), new Vector3(1,1,0) },
                triangles = new[] { 0,2,1,1,2,3 }, colors = Enumerable.Repeat(new Color(.1f,.85f,1,1), 4).ToArray() };
            var target = new RenderTexture(32,32,0); var pixels = new Texture2D(32,32,TextureFormat.RGBA32,false);
            var previous = RenderTexture.active;
            try
            {
                target.Create(); RenderTexture.active = target; GL.Clear(true, true, Color.black);
                GL.PushMatrix();
                try
                {
                    GL.LoadProjectionMatrix(Matrix4x4.Ortho(-1,1,-1,1,-1,1)); GL.modelview = Matrix4x4.identity;
                    material.SetFloat("_Opacity", 1);
                    Check(material.SetPass(0), "Merge overlay shader cannot render."); Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
                }
                finally { GL.PopMatrix(); }
                pixels.ReadPixels(new Rect(0,0,32,32),0,0); pixels.Apply();
                Color center = pixels.GetPixel(16,16);
                Check(center.g > .5f && center.b > .5f && center.r < .4f, "Merge overlay failed to render its difference color.");
                Check(!ShaderUtil.ShaderHasError(shader), "Merge overlay shader failed compilation.");
                Debug.Log("MG TERRAIN MERGE OVERLAY VALIDATION PASSED: shader compiled and rendered cyan difference color on " + SystemInfo.graphicsDeviceType);
            }
            finally
            {
                RenderTexture.active = previous; target.Release();
                Object.DestroyImmediate(target); Object.DestroyImmediate(pixels); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material);
            }
        }
    }
}
#endif

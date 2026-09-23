using System;
using MashBoxSDK.Maps.TerrainSystem;
using MashBoxSDK.Maps.TerrainSystem.Editor;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using UnitySpline = UnityEngine.Splines.Spline;
using Object = UnityEngine.Object;

namespace MashBoxSDK.Maps.Roads.Editor
{
    public static class MGRoadValidation
    {
        static int checks;
        static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        static void Near(float a, float b, string message) => Check(Mathf.Abs(a - b) < .003f, message + $" ({a} vs {b})");
        [MenuItem("MashBox/Validation/Validate Roads")]
        public static void Run()
        {
            checks = 0;
            var scene = EditorSceneManager.NewPreviewScene();
            Mesh terrainMesh = null;
            LoftTerrainConformer worker = null;
            string bakedPath = null, duplicatePath = null;
            try
            {
                var root = new GameObject("Road Validation"); SceneManager.MoveGameObjectToScene(root, scene);
                var network = root.AddComponent<MGRoadNetwork>();
                var go = new GameObject("Validation road"); go.transform.SetParent(root.transform, false);
                var road = go.AddComponent<MGRoad>();
                road.width = 6; road.shoulderWidth = 1; road.crown = 0; road.shoulderDrop = 0;
                var spline = new UnitySpline();
                spline.Add(new BezierKnot(new float3(0, 5, 0)), TangentMode.Linear);
                spline.Add(new BezierKnot(new float3(0, 5, 20)), TangentMode.Linear);
                road.Container.Spline = spline; road.Rebuild();
                var mesh = road.GeneratedMesh;
                Near(mesh.bounds.size.x, 8, "Width includes shoulders");
                Near(mesh.bounds.size.z, 20, "Road follows full spline length");
                Check(mesh.subMeshCount == 2 && mesh.GetTriangles(1).Length > 0, "Separate shoulder submesh");
                foreach (var normal in mesh.normals) Check(normal.y > .99f, "Upward winding");
                Near(mesh.uv[mesh.vertexCount - 1].y, 4, "UV metres along road");
                Check(go.GetComponent<MeshCollider>().sharedMesh == mesh, "Generated collider bound");
                road.generateCollider = false; road.shoulderWidth = 0; road.Rebuild();
                Check(!go.GetComponent<MeshCollider>().enabled && go.GetComponent<MeshCollider>().sharedMesh == null, "Collider disabled cleanly");
                Check(mesh.GetTriangles(1).Length == 0, "No degenerate shoulder triangles");
                root.transform.SetPositionAndRotation(new Vector3(12, 3, -7), Quaternion.Euler(0, 35, 0));
                root.transform.localScale = new Vector3(2, 1, 3); road.Rebuild();
                var verts = mesh.vertices;
                Near(Vector3.Distance(go.transform.TransformPoint(verts[1]), go.transform.TransformPoint(verts[3])), 6, "Width remains in world metres under parent scaling");
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.transform.localScale = Vector3.one;

                spline.Clear();
                spline.Add(new BezierKnot(new float3(0, 5, 0)), TangentMode.AutoSmooth);
                spline.Add(new BezierKnot(new float3(10, 5, 10)), TangentMode.AutoSmooth);
                spline.Add(new BezierKnot(new float3(0, 5, 20)), TangentMode.AutoSmooth);
                var before = spline.GetCurve(0);
                MGRoadTool.InsertPoint(spline, 0, .37f);
                for (int i = 0; i <= 20; i++)
                {
                    float t = i / 20f;
                    float3 expected = CurveUtility.EvaluatePosition(before, t);
                    float3 actual = t <= .37f ? CurveUtility.EvaluatePosition(spline.GetCurve(0), t / .37f)
                        : CurveUtility.EvaluatePosition(spline.GetCurve(1), (t - .37f) / .63f);
                    Near(math.distance(actual, expected), 0, "Insertion preserves curve");
                }
                spline.Closed = true; road.Rebuild(); verts = mesh.vertices;
                for (int i = 0; i < 5; i++) Near(Vector3.Distance(verts[i], verts[verts.Length - 5 + i]), 0, "Closed seam matches");
                MGRoadTool.InsertPoint(spline, spline.Count - 1, .5f);
                Check(spline.Count == 5 && spline.Closed, "Can insert in closing curve");
                road.Rebuild();

                var worldGo = new GameObject("World"); worldGo.transform.SetParent(root.transform, false);
                var world = worldGo.AddComponent<MGTerrainWorld>(); network.terrainWorld = world;
                var tileGo = new GameObject("Tile"); tileGo.transform.SetParent(worldGo.transform, false);
                var filter = tileGo.AddComponent<MeshFilter>(); var renderer = tileGo.AddComponent<MeshRenderer>();
                terrainMesh = new Mesh { vertices = new[] { new Vector3(-30, 2, -30), new Vector3(30, 2, -30), new Vector3(-30, 2, 30), new Vector3(30, 2, 30), new Vector3(0, 2, 10) }, triangles = new[] { 0, 2, 4, 2, 3, 4, 3, 1, 4, 1, 0, 4 } };
                terrainMesh.RecalculateNormals(); filter.sharedMesh = terrainMesh;
                var collider = tileGo.AddComponent<MeshCollider>(); collider.sharedMesh = terrainMesh;
                var tile = tileGo.AddComponent<MGTerrain>(); tile.Configure(filter, renderer, collider); world.RefreshChunks();
                network.terrain.mode = RoadTerrainMode.RoadFollowsTerrain; network.terrain.clearance = .2f;
                road.Rebuild();
                foreach (var v in mesh.vertices) Near(go.transform.TransformPoint(v).y, 2.2f, "Road follows assigned terrain world");
                road.overrideTerrain = true; road.terrain.mode = RoadTerrainMode.Independent; road.Rebuild();
                Near(go.transform.TransformPoint(mesh.vertices[2]).y, 5, "Road override ignores network conform");
                var surface = new LoftSurface(new[] { go.GetComponent<MeshFilter>() });
                worker = ScriptableObject.CreateInstance<LoftTerrainConformer>();
                var so = new SerializedObject(worker);
                so.FindProperty("offset").floatValue = 0;
                so.FindProperty("blendDistance").floatValue = 10;
                so.FindProperty("falloffCurve").animationCurveValue = AnimationCurve.Linear(0, 1, 1, 0);
                so.ApplyModifiedPropertiesWithoutUndo();
                Check(worker.Target(surface, new Vector3(0, 0, 0), out float height), "Terrain road footprint sampled");
                Near(height, 5, "Terrain target height under road");
                so.FindProperty("mode").enumValueIndex = 1; so.ApplyModifiedPropertiesWithoutUndo();
                Check(!worker.Target(surface, new Vector3(0, 0, 0), out _), "Lower only cannot raise terrain");
                spline.Clear(); spline.Closed = false;
                spline.Add(new BezierKnot(new float3(0, 5, 0)), TangentMode.Linear);
                spline.Add(new BezierKnot(new float3(0, 5, 20)), TangentMode.Linear);
                road.terrain.mode = RoadTerrainMode.TerrainFollowsRoad;
                road.terrain.terrainOffset = -.1f;
                MGRoadTerrain.Apply(new[] { road });
                Near(tile.MeshFilter.sharedMesh.vertices[4].y, 4.9f, "Terrain conforms to road");
                Near(tile.MeshFilter.sharedMesh.vertices[0].y, 2, "Terrain outside falloff unchanged");
                Undo.PerformUndo();
                Near(tile.MeshFilter.sharedMesh.vertices[4].y, 2, "Terrain Apply undo restores source");
                Undo.PerformRedo();
                Near(tile.MeshFilter.sharedMesh.vertices[4].y, 4.9f, "Terrain Apply redo restores deformation");
                MGRoadTerrain.Persist(road);
                var storage = go.GetComponent<MGRoadMeshStorage>();
                bakedPath = AssetDatabase.GetAssetPath(storage.mesh);
                Check(!string.IsNullOrEmpty(bakedPath) && go.GetComponent<MeshFilter>().sharedMesh == storage.mesh, "Scene mesh is backed by an asset");
                var copy = Object.Instantiate(go, root.transform).GetComponent<MGRoad>();
                copy.width = 12;
                MGRoadTerrain.Persist(copy);
                duplicatePath = AssetDatabase.GetAssetPath(copy.GetComponent<MGRoadMeshStorage>().mesh);
                Check(bakedPath != duplicatePath, "Duplicated roads bake independent assets");
                Near(storage.mesh.bounds.size.x, 6, "Duplicate rebuild leaves original asset unchanged");
                spline.Clear(); road.Rebuild(); Check(mesh.vertexCount == 0, "Empty road clears mesh");
                Debug.Log($"ROAD_VALIDATION_PASS: {checks} checks");
            }
            finally
            {
                if (worker != null) Object.DestroyImmediate(worker);
                EditorSceneManager.ClosePreviewScene(scene);
                if (terrainMesh != null) Object.DestroyImmediate(terrainMesh);
                if (!string.IsNullOrEmpty(bakedPath)) AssetDatabase.DeleteAsset(bakedPath);
                if (!string.IsNullOrEmpty(duplicatePath)) AssetDatabase.DeleteAsset(duplicatePath);
            }
        }
    }
}

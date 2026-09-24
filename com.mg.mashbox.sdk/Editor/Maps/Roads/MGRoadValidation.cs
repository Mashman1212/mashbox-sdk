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
        static void ValidateKnotShape(Transform parent)
        {
            var go = new GameObject("Knot shape validation"); go.transform.SetParent(parent, false);
            try
            {
                var road = go.AddComponent<MGRoad>(); road.width = 6; road.shoulderWidth = 0; road.crown = 0; road.generateCollider = false;
                var spline = road.Container.Spline = new UnitySpline();
                spline.Add(new BezierKnot(new float3(0, 0, 0)), TangentMode.AutoSmooth);
                spline.Add(new BezierKnot(new float3(0, 0, 20)), TangentMode.AutoSmooth);
                Check(Vector3.Dot(MGRoadKnotShape.Frame(spline, 0, 0, go.transform) * Vector3.forward, Vector3.forward) > .999f, "Knot handle follows road direction");
                MGRoadKnotShape.Rotate(spline, 0, Quaternion.AngleAxis(30, Vector3.forward), go.transform);
                MGRoadKnotShape.Rotate(spline, 1, Quaternion.AngleAxis(30, Vector3.forward), go.transform);
                road.Rebuild(); var vertices = road.GeneratedMesh.vertices;
                Near(vertices[3].y - vertices[1].y, 3, "Knot roll banks generated road");
                MGRoadKnotShape.SetScale(spline, 0, new Vector3(2, 1, 1)); road.Rebuild(); vertices = road.GeneratedMesh.vertices;
                Near(Vector3.Distance(vertices[1], vertices[3]), 12, "Knot X scale widens road");
                Near(Vector3.Distance(vertices[vertices.Length - 4], vertices[vertices.Length - 2]), 6, "Width tapers to next knot");
                var ring = new Vector3[5]; MGRoadKnotShape.Ring(road, spline, 0, .5f, null, ring);
                Near(Vector3.Distance(ring[1], ring[3]), 9, "Width interpolates between knots");
                Vector3 sourceScale = MGRoadKnotShape.GetScale(spline, 0);
                var preview = MGRoadTool.ExtensionPreview(spline, new float3(0, 0, -10), true);
                Near(MGRoadKnotShape.GetScale(spline, 0).x, sourceScale.x, "Preview does not change source scale data");
                Near(MGRoadKnotShape.GetScale(preview, 0).x, 2, "Extension inherits endpoint width");
                Near(MGRoadKnotShape.GetScale(preview, 1).x, 2, "Prepending retains original knot width");
                MGRoadTool.InsertPoint(spline, 0, .5f);
                Near(MGRoadKnotShape.GetScale(spline, 1).x, 1.5f, "Inserted knot inherits interpolated scale");
                spline.RemoveAt(1); Near(MGRoadKnotShape.GetScale(spline, 1).x, 1, "Deleting knot retains remaining knot scale");
                var tangent = spline[0].TangentOut;
                MGRoadKnotShape.Scale(spline, 0, new Vector3(2, 1, 2));
                Near(math.length(spline[0].TangentOut), math.length(tangent) * 2, "Knot Z scale changes tangent length");
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(road.Container, "Validate Knot Scale Undo");
                MGRoadKnotShape.Scale(spline, 0, new Vector3(3, 1, 2));
                Undo.PerformUndo(); Near(MGRoadKnotShape.GetScale(road.Container.Spline, 0).x, 2, "Knot scale undo");
                Undo.PerformRedo(); Near(MGRoadKnotShape.GetScale(road.Container.Spline, 0).x, 3, "Knot scale redo");
                var rotatedFrame = MGRoadKnotShape.Frame(road.Container.Spline, 0, 0, go.transform);
                go.transform.rotation = Quaternion.Euler(0, 65, 0);
                var worldFrame = MGRoadKnotShape.Frame(road.Container.Spline, 0, 0, go.transform);
                Check(Quaternion.Angle(go.transform.rotation * rotatedFrame, worldFrame) < .01f, "Handle orientation follows transformed road");
            }
            finally { Object.DestroyImmediate(go); }
        }
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
                ValidateKnotShape(root.transform);
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
                Check(MGRoadTool.ExtendFromStart(spline, go.transform, go.transform.TransformPoint(new Vector3(0, 5, -5))), "Endpoint choice respects transformed start");
                Check(!MGRoadTool.ExtendFromStart(spline, go.transform, go.transform.TransformPoint(new Vector3(0, 5, 25))), "Endpoint choice respects transformed end");
                int sourceCount = spline.Count;
                var sourceFirst = spline[0];
                foreach (bool prepend in new[] { false, true })
                {
                    float3 candidate = new float3(3, 5, prepend ? -10 : 30);
                    var preview = MGRoadTool.ExtensionPreview(spline, candidate, prepend);
                    Check(spline.Count == sourceCount && math.distance(spline[0].Position, sourceFirst.Position) == 0, "Hover preview leaves authored spline untouched");
                    var committed = new UnitySpline(spline);
                    int index = MGRoadTool.AddEndpoint(committed, candidate, prepend);
                    Check(index == (prepend ? 0 : committed.Count - 1), "Added knot selects the intended endpoint");
                    Check(preview.Count == sourceCount + 1, "Preview includes proposed knot");
                    for (int sample = 0; sample <= 20; sample++)
                        Near(math.distance(SplineUtility.EvaluatePosition(preview, sample / 20f), SplineUtility.EvaluatePosition(committed, sample / 20f)), 0, "Preview matches committed spline shape");
                }
                var closedPreviewSource = new UnitySpline(spline) { Closed = true };
                Check(!MGRoadTool.ExtendFromStart(closedPreviewSource, go.transform, Vector3.zero), "Closed loop has deterministic extension end");
                Check(MGRoadTool.ExtensionPreview(closedPreviewSource, new float3(5, 5, 10), false).Closed, "Loop preview stays closed");
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
                road.terrain.matchTerrainCells = false;
                MGRoadTerrain.Apply(new[] { road });
                Near(tile.MeshFilter.sharedMesh.vertices[4].y, 4.9f, "Terrain conforms to road");
                Near(tile.MeshFilter.sharedMesh.vertices[0].y, 4.9f, "Legacy disabled cell support still receives automatic roadbed margin");
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
                // This fixture replaces terrain topology; discard its completed layer history first.
                var oldLayers = tile.GetComponent<MGRoadTerrainLayers>();
                if (oldLayers != null) Object.DestroyImmediate(oldLayers);
                // A 2 m road passing between vertices of an 8 m terrain grid must still deform its support cells.
                spline.Clear(); spline.Closed = false;
                spline.Add(new BezierKnot(new float3(0, 5, 0)), TangentMode.Linear);
                spline.Add(new BezierKnot(new float3(0, 5, 8)), TangentMode.Linear);
                road.width = 2; road.shoulderWidth = 0; road.Rebuild();
                terrainMesh.vertices = new[] { new Vector3(-4, 2, 0), new Vector3(4, 2, 0), new Vector3(-4, 2, 8), new Vector3(4, 2, 8) };
                terrainMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 }; terrainMesh.RecalculateBounds();
                filter.sharedMesh = terrainMesh;
                so.FindProperty("mode").enumValueIndex = 0; so.FindProperty("blendDistance").floatValue = 0; so.ApplyModifiedPropertiesWithoutUndo();
                var narrow = new LoftSurface(new[] { go.GetComponent<MeshFilter>() });
                var missed = worker.BuildEdits(narrow, new[] { tile }, Array.Empty<Terrain>());
                Check(missed.Count == 0, "Narrow road misses coarse vertices without cell support");
                var supported = worker.BuildEdits(narrow, new[] { tile }, Array.Empty<Terrain>(), true);
                Check(supported.Count == 1 && supported[0].count == 4, "Cell support catches terrain triangles spanning road");
                foreach (var delta in supported[0].deltas) Near(delta.delta.y, 3, "Supporting vertices reach road height");
                Check(!worker.Target(narrow, new Vector3(100, 2, 0), out _, 12), "Cell support does not affect distant terrain");
                float margin = LoftTerrainConformer.TerrainCellSupports(terrainMesh, filter.transform, terrainMesh.vertices)[0] * 2;
                Check(margin >= 8, "Roadbed margin includes at least one grid resolution");
                so.FindProperty("blendDistance").floatValue = 6; so.ApplyModifiedPropertiesWithoutUndo();
                foreach (float side in new[] { -1f, 1f })
                {
                    Check(worker.Target(narrow, new Vector3(side * (1 + margin), 2, 4), out float edgeHeight, margin), "Roadbed includes full cell margin on each side");
                    Near(edgeHeight, 5, "Falloff does not reduce height inside cell margin");
                    worker.Target(narrow, new Vector3(side * (1 + margin + 3), 2, 4), out float blendHeight, margin);
                    Near(blendHeight, 3.5f, "Falloff starts after cell margin");
                    Check(!worker.Target(narrow, new Vector3(side * (1 + margin + 6.01f), 2, 4), out _, margin), "Beyond margin and falloff remains unchanged");
                }
                var savedVertices = terrainMesh.vertices; var savedTriangles = terrainMesh.triangles;
                var gridVertices = new Vector3[18]; var gridTriangles = new int[48];
                for (int x = 0; x < 9; x++) { gridVertices[x * 2] = new Vector3((x - 4) * 8, 2, 0); gridVertices[x * 2 + 1] = new Vector3((x - 4) * 8, 2, 8); }
                for (int x = 0; x < 8; x++) { int a = x * 2, t = x * 6; gridTriangles[t] = a; gridTriangles[t + 1] = a + 1; gridTriangles[t + 2] = a + 2; gridTriangles[t + 3] = a + 2; gridTriangles[t + 4] = a + 1; gridTriangles[t + 5] = a + 3; }
                terrainMesh.vertices = gridVertices; terrainMesh.triangles = gridTriangles; terrainMesh.RecalculateBounds();
                var apron = worker.BuildEdits(narrow, new[] { tile }, Array.Empty<Terrain>(), true, 1);
                Check(apron.Count == 1, "Extra cell apron produces terrain edits");
                foreach (int index in new[] { 4, 12 })
                {
                    var delta = apron[0].deltas.Find(d => d.index == index);
                    Near(delta.delta.y, 3, "Second grid row outside road reaches full road height");
                }
                var falling = apron[0].deltas.Find(d => d.index == 14);
                Check(falling.delta.y > 0 && falling.delta.y < 3, "Next terrain row blends beyond the apron");
                Check(!apron[0].deltas.Exists(d => d.index == 16), "Distant terrain row stays unchanged");
                terrainMesh.triangles = Array.Empty<int>(); terrainMesh.vertices = savedVertices; terrainMesh.triangles = savedTriangles; terrainMesh.RecalculateBounds();
                // Both network inheritance and legacy per-road false values must use the mandatory margin.
                road.overrideTerrain = false;
                network.terrain.mode = RoadTerrainMode.TerrainFollowsRoad;
                network.terrain.terrainOffset = -.1f;
                network.terrain.matchTerrainCells = false;
                MGRoadTerrain.Apply(new[] { road });
                var conformedGround = new LoftSurface(new[] { filter }, true);
                Check(conformedGround.Sample(new Vector3(0, 0, 4), 0, out float supportedHeight, out _), "Terrain between grid vertices is sampled");
                Near(supportedHeight, 4.9f, "Coarse triangle under the road reaches its target height");
                Undo.PerformUndo(); Near(filter.sharedMesh.vertices[0].y, 2, "Coarse-cell conform undo restores terrain");
                Undo.PerformRedo(); Near(filter.sharedMesh.vertices[0].y, 4.9f, "Coarse-cell conform redo restores roadbed");
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

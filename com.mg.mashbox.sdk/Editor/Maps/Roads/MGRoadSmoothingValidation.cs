using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
namespace MashBoxSDK.Maps.Roads.Editor
{
    public static class MGRoadSmoothingValidation
    {
        [MenuItem("MashBox/Validation/Validate Road Falloff Smoothing")]
        public static void Run()
        {
            var go = new GameObject("Smoothing validation") { hideFlags = HideFlags.HideAndDontSave };
            var mesh = new Mesh();
            try
            {
                var vertices = new Vector3[49]; var triangles = new int[216];
                for (int z = 0; z < 7; z++) for (int x = 0; x < 7; x++) vertices[z * 7 + x] = new Vector3(x, 4, z);
                for (int z = 0; z < 6; z++) for (int x = 0; x < 6; x++)
                { int a = z * 7 + x, t = (z * 6 + x) * 6; triangles[t] = a; triangles[t+1] = a+7; triangles[t+2] = a+1; triangles[t+3] = a+1; triangles[t+4] = a+7; triangles[t+5] = a+8; }
                mesh.vertices = vertices; mesh.triangles = triangles;
                var before = (Vector3[])vertices.Clone(); vertices[24].y = 12; vertices[23].y = 9; vertices[0].y = 10;
                var raw = (Vector3[])vertices.Clone();
                var layer = new MGRoadTerrainLayers.Layer { smoothingPasses = 4, samples = new[] {
                    new MGRoadTerrainLayers.Sample { index = 24, smoothing = .65f },
                    new MGRoadTerrainLayers.Sample { index = 23, smoothing = 0 },
                    new MGRoadTerrainLayers.Sample { index = 0, smoothing = .65f } } };
                void Check(bool ok, string label) { if (!ok) throw new Exception(label); }
                MGRoadFalloffSmoothing.Apply(mesh, go.transform, before, vertices, layer);
                Check(vertices[24].y < 8 && vertices[24].y > 4, "Falloff spike averaged towards neighbouring heights");
                Check(vertices[23].y == 9, "Roadbed is pinned");
                Check(vertices[0].y == 10, "Tile boundaries remain pinned");
                Check(vertices[25] == raw[25], "Unaffected terrain stays unchanged");
                int graphBuilds = MGRoadFalloffSmoothing.GraphBuildCount;
                var repeated = (Vector3[])raw.Clone(); MGRoadFalloffSmoothing.Apply(mesh, go.transform, before, repeated, layer);
                Check(repeated[24] == vertices[24], "Smoothing is deterministic from layer inputs");
                Check(MGRoadFalloffSmoothing.GraphBuildCount == graphBuilds, "Repeated smoothing reuses connectivity");
                MGRoadFalloffSmoothing.Apply(mesh, go.transform, before, repeated, layer, 123);
                Check(MGRoadFalloffSmoothing.GraphBuildCount == graphBuilds + 1, "Changed topology rebuilds connectivity");
                MGRoadFalloffSmoothing.ClearCache();
                MGRoadFalloffSmoothing.Apply(mesh, go.transform, before, repeated, layer, 123);
                Check(MGRoadFalloffSmoothing.GraphBuildCount == graphBuilds + 2, "Undo cache reset rebuilds connectivity");
                layer.heightMode = RoadHeightMode.RaiseOnly; before[24].y = 11;
                MGRoadFalloffSmoothing.Apply(mesh, go.transform, before, repeated, layer); Check(repeated[24].y >= 11, "Raise only remains enforced");
                layer.heightMode = RoadHeightMode.LowerOnly; before[24].y = 2;
                MGRoadFalloffSmoothing.Apply(mesh, go.transform, before, repeated, layer); Check(repeated[24].y <= 2, "Lower only remains enforced");
                var settings = new RoadTerrainSettings { falloffDistance = 5, minimumFalloffCells = 3, smoothFalloff = true };
                Check(MGRoadFalloffSmoothing.Distance(settings, 8) == 24, "Coarse terrain gets multiple falloff rows");
                settings.smoothFalloff = false; Check(MGRoadFalloffSmoothing.Distance(settings, 8) == 5, "Disabled smoothing preserves explicit falloff");
                Debug.Log("ROAD_SMOOTHING_VALIDATION_PASS: 12 checks");
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(go); }
        }
    }
}

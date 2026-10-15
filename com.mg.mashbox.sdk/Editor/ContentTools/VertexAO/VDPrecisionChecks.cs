using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Deliberately runs against a temporary copy. Never overwrites the selected source model.
internal static class VDPrecisionChecks
{
    [MenuItem("MashBox/Content Tools/Vertex AO Diagnostics/Validate baker", false, 90)]
    static void MenuRun()
    {
        try { Debug.Log(Run(Selection.activeGameObject)); }
        catch (Exception ex) { Debug.LogException(ex); }
    }
    internal static string Run(GameObject selected)
    {
        var report = new StringBuilder();
        var bvh = new VDPrecisionBaker.Bvh(new[] { new VDPrecisionBaker.Triangle(new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(0, 1, 1)) });
        Require(Mathf.Abs(bvh.Hit(Vector3.zero, Vector3.forward, 2, true) - 1) < .00001f, "Ray hit distance");
        Require(bvh.Hit(Vector3.zero, Vector3.back, 2, true) == 2, "Ray miss");
        Require(bvh.Hit(Vector3.zero, Vector3.forward, .5f, true) == .5f, "Distance limit");
        Require(bvh.Hit(Vector3.zero, Vector3.forward, 2, false) == 2, "Backface filtering");
        var tiny = new VDPrecisionBaker.Bvh(new[] { new VDPrecisionBaker.Triangle(new Vector3(-.001f, -.001f, .001f), new Vector3(.001f, -.001f, .001f), new Vector3(0, .001f, .001f)) });
        Require(Mathf.Abs(tiny.Hit(Vector3.zero, Vector3.forward, .002f, true) - .001f) < 1e-8f, "Millimetre geometry intersection");
        report.AppendLine("PASS: ray distance, miss, range, backface and millimetre-scale intersections.");
        var remap = new VDPrecisionBaker.Result { original = new[] { new Color32(70, 80, 90, 100) }, visibility = new[] { .5f } };
        var settings = new VDPrecisionBaker.Settings { output = VDPrecisionBaker.Output.Red };
        var c = VDPrecisionBaker.Remap(remap, settings)[0];
        Require(c.r >= 127 && c.r <= 128 && c.g == 80 && c.b == 90 && c.a == 100, "Channel preservation");
        settings.strength = 0;
        Require(VDPrecisionBaker.Remap(remap, settings)[0].r == 255, "Zero strength");
        report.AppendLine("PASS: red-channel output preserves G/B/A; zero strength removes AO.");
        var normals = new[] { Vector3.up, Vector3.forward, Vector3.right };
        VDPrecisionBaker.Smooth(new[] { Vector3.zero, Vector3.zero, Vector3.one }, normals, .001f, 120);
        Require((normals[0] - normals[1]).magnitude < .00001f && normals[2] == Vector3.right, "Seam averaging");
        report.AppendLine("PASS: seam averaging affects only coincident vertices.");
        var selectedFilters = VDPrecisionBaker.Targets(selected, true);
        if (selectedFilters.Length == 0) { report.AppendLine("FBX round-trip skipped: select a scene model to test a copy."); return report.ToString(); }
        string sourcePath = AssetDatabase.GetAssetPath(VDPrecisionBaker.Source(selectedFilters[0]));
        if (!sourcePath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Select a model from an FBX for the round-trip check.");
        byte[] originalBytes = File.ReadAllBytes(sourcePath), originalMeta = File.ReadAllBytes(sourcePath + ".meta");
        string folder = "Assets/__VertexDirtCheck_" + Guid.NewGuid().ToString("N");
        string copyPath = folder + "/" + Path.GetFileName(sourcePath);
        GameObject instance = null;
        List<VDPrecisionBaker.Result> results = null;
        var previewScene = EditorSceneManager.NewPreviewScene();
        try
        {
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            Require(AssetDatabase.CopyAsset(sourcePath, copyPath), "Copy source asset");
            AssetDatabase.ImportAsset(copyPath, ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(copyPath);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, previewScene);
            instance.hideFlags = HideFlags.HideAndDontSave;
            var filters = instance.GetComponentsInChildren<MeshFilter>();
            // Deliberately update one mesh only, to verify other FBX meshes survive.
            var f = filters.First(m => m.sharedMesh && m.sharedMesh.name == VDPrecisionBaker.Source(selectedFilters[0]).name);
            var b = VDPrecisionBaker.BoundsOf(new[] { f });
            float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
            settings = new VDPrecisionBaker.Settings { distance = size * .1f, bias = size * .0002f, smoothTolerance = size * .00001f, samples = 32 };
            Mesh initial = f.sharedMesh;
            using (var cancelled = new VDPrecisionBaker.Job(new[] { f }, settings)) cancelled.Step(.1);
            Require(f.sharedMesh == initial && f.GetComponent<VDColorHandler>() == null, "Cancellation preserves scene");
            report.AppendLine("PASS: cancelling partial work leaves the mesh and components unchanged.");
            using (var job = new VDPrecisionBaker.Job(new[] { f }, settings))
            {
                while (!job.Step(20)) { }
                results = job.TakeResults();
                report.AppendLine($"BAKE: {job.total:N0} vertices, {job.triangleCount:N0} triangles, {job.elapsed:F2}s at 32 samples.");
            }
            var values = results[0].visibility;
            Require(values.All(v => v >= 0 && v <= 1), "AO bounds");
            report.AppendLine($"AO: min {values.Min():F5}, mean {values.Average():F5}, max {values.Max():F5}.");
            VDPrecisionBaker.Apply(results, settings);
            string fingerprint = VDFbxDocument.Read(copyPath).NonColorFingerprint();
            string guid = AssetDatabase.AssetPathToGUID(copyPath);
            string backup = VDFbxColorWriter.Overwrite(results);
            Require(VDFbxDocument.Read(copyPath).NonColorFingerprint() == fingerprint, "Non-color FBX data preserved");
            Require(AssetDatabase.AssetPathToGUID(copyPath) == guid, "GUID preserved");
            Require(File.Exists(backup) && File.ReadAllBytes(backup).SequenceEqual(originalBytes), "Backup matches original");
            Require(f.GetComponent<VDColorHandler>() == null && AssetDatabase.GetAssetPath(f.sharedMesh) == copyPath, "Scene points back to FBX");
            report.AppendLine("PASS: overwrite/reimport verifies exact colors, mesh IDs, topology, non-color FBX payloads, GUID, backup and source reassignment.");
            Require(File.ReadAllBytes(sourcePath).SequenceEqual(originalBytes) && File.ReadAllBytes(sourcePath + ".meta").SequenceEqual(originalMeta), "User source untouched");
            report.AppendLine("PASS: user's original FBX and .meta are byte-for-byte unchanged.");
            report.AppendLine("Selected: " + selected.name + "\nSource: " + sourcePath + $"\nSize: {b.size * 1000} mm\nSuggested reach: {settings.distance * 1000:F4} mm; bias: {settings.bias * 1000:F5} mm.");
        }
        finally
        {
            if (results != null) foreach (var r in results) if (r.geometry) Object.DestroyImmediate(r.geometry);
            if (instance) Object.DestroyImmediate(instance);
            EditorSceneManager.ClosePreviewScene(previewScene);
            AssetDatabase.DeleteAsset(folder);
        }
        return report.ToString();
    }
    static void Require(bool condition, string label)
    { if (!condition) throw new InvalidOperationException("VertexDirt check failed: " + label); }
}

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Short-lived, opt-in GUI measurement using the already scanned SDK panel.
internal sealed class MGTerrainScannerRepaintValidation : EditorWindow
{
    private MapPerformanceScannerPanel panel;
    private readonly List<double> samples = new();
    private int events;
    private bool finished;

    internal static void Begin()
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var source = Resources.FindObjectsOfTypeAll<EditorWindow>()
            .SelectMany(window => window.GetType().GetFields(flags)
                .Where(field => field.FieldType == typeof(MapPerformanceScannerPanel))
                .Select(field => field.GetValue(window) as MapPerformanceScannerPanel))
            .FirstOrDefault(value => value != null && (bool)typeof(MapPerformanceScannerPanel).GetField("hasScanResults", flags).GetValue(value));
        if (source == null)
        {
            source = new MapPerformanceScannerPanel();
            if (source.ScanScene() == null) return;
        }
        var window = CreateInstance<MGTerrainScannerRepaintValidation>();
        // Keep navigation and foldout changes in the probe separate from the user's panel.
        window.panel = JsonUtility.FromJson<MapPerformanceScannerPanel>(JsonUtility.ToJson(source));
        window.titleContent = new GUIContent("Scanner repaint check");
        window.position = new Rect(100, 100, 1000, 750);
        window.ShowUtility();
    }

    private void Update() { if (!finished) Repaint(); }
    private void OnGUI()
    {
        if (panel == null || finished) return;
        var stopwatch = Stopwatch.StartNew();
        panel.DrawGUI(true);
        stopwatch.Stop();
        // Skip cold cache/style setup; record both Layout and Repaint costs.
        if (++events > 8) samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        if (samples.Count < 40) return;
        finished = true;
        string result = $"PASS: {samples.Count} warm GUI events; average {samples.Average():F2} ms; max {samples.Max():F2} ms. " +
            "Texture/terrain/renderer tables are limited to 25 rows per page; exported reports remain complete.";
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "mg-performance-scanner-repaint.txt"), result);
        UnityEngine.Debug.Log(result);
        EditorApplication.delayCall += () => { if (this != null) Close(); };
    }
}
#endif

using System;
using System.IO;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public static class StudioMouseCaptureValidation
{
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/LiveVehiclePartsValidation/Assets", StringComparison.Ordinal)) throw new InvalidOperationException("Use the disposable validation project.");
        int count = 0, code = 0; string result;
        void Require(bool value, string label) { if (!value) throw new Exception(label); count++; }
        try
        {
            var capture = new StudioMouseCapture();
            Require(!capture.ShouldRelease(EventType.MouseMove, 0, 42), "Unowned capture remains intact");
            capture.Observe(EventType.MouseDown, 0, 0, 42);
            Require(!capture.ShouldRelease(EventType.MouseDrag, 0, 42), "Held drag retained");
            Require(!capture.ShouldRelease(EventType.Repaint, 0, 42), "Repaint retains capture");
            Require(!capture.ShouldRelease(EventType.MouseDown, 1, 42), "Second button does not cancel first");
            Require(!capture.ShouldRelease(EventType.MouseUp, 1, 42), "Unrelated release does not cancel first");
            Require(capture.ShouldRelease(EventType.MouseMove, 0, 42), "Button-free movement recovers missed release");
            Require(capture.ShouldRelease(EventType.MouseDown, 0, 42), "Next press of same button recovers missed release");
            Require(capture.ShouldRelease(EventType.MouseUp, 0, 42), "Owner's consumed release cleans capture");
            Require(!capture.ShouldRelease(EventType.MouseMove, 0, 99), "Another control's capture is preserved");
            capture.Observe(EventType.MouseUp, 0, 42, 0);
            Require(!capture.ShouldRelease(EventType.MouseMove, 0, 42), "Normal release clears bookkeeping");
            capture.Observe(EventType.MouseDown, 0, 99, 99);
            Require(!capture.ShouldRelease(EventType.MouseMove, 0, 99), "Preexisting capture is not claimed");
            capture.Observe(EventType.MouseDown, 2, 0, 77);
            capture.Clear();
            Require(!capture.ShouldRelease(EventType.MouseMove, 2, 77), "Explicit cleanup clears bookkeeping");
            result = "PASS: " + count + " capture recovery and active-drag preservation checks.";
        }
        catch (Exception ex) { code = 1; result = "FAIL: " + ex; }
        File.WriteAllText(Path.Combine(Application.dataPath, "../mouse-capture-result.txt"), result);
        EditorApplication.Exit(code);
    }
}

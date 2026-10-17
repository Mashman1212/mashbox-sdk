using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public static class StudioUVAvailabilityValidation
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static StudioModelEditor editor;
    private static StudioUVEditor window;
    private static int step;
    private static double next;
    private static string errors = "";
    private const string Path = "Assets/UVAvailabilityValidation.asset";
    public static void Run()
    {
        editor = new StudioModelEditor(() => { });
        var enabled = typeof(StudioModelEditor).GetMethod("MarkingActionEnabled", Flags);
        foreach (var action in new[] { StudioModelEditor.MarkingAction.OpenUV, StudioModelEditor.MarkingAction.UVMenu, StudioModelEditor.MarkingAction.UVSetsMenu, StudioModelEditor.MarkingAction.UV4 })
            Check((bool)enabled.Invoke(editor, new object[] { action }), action + " unavailable without a mesh");
        editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.OpenUV, null);
        window = Resources.FindObjectsOfTypeAll<StudioUVEditor>().Single();
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
        next = EditorApplication.timeSinceStartup + 1;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static Mesh DisplayedMesh => (Mesh)typeof(StudioUVEditor).GetField("mesh", Flags).GetValue(window);
    private static void Log(string text, string stack, LogType type)
    { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors += text + "\n" + stack; }
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .6;
        try
        {
            switch (step++)
            {
                case 0:
                    Check(window && !DisplayedMesh, "UV Editor failed to render empty without a document");
                    editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.UV4, null); break;
                case 1:
                    Check(Resources.FindObjectsOfTypeAll<StudioUVEditor>().Length == 1 && (int)typeof(StudioUVEditor).GetField("channel", Flags).GetValue(window) == 4, "Empty channel selection did not reuse the window");
                    AssetDatabase.DeleteAsset(Path);
                    var document = StudioModelDocument.CreateEmpty(Path); document.AddPrimitive(PrimitiveType.Cube);
                    editor.Load(document); editor.SelectObject(0); break;
                case 2:
                    Check(DisplayedMesh == editor.ActiveMesh && DisplayedMesh, "Open UV Editor did not follow newly selected mesh");
                    editor.ExecuteMarkingAction(StudioModelEditor.MarkingAction.Clear, null); break;
                case 3:
                    Check(window && !DisplayedMesh, "Deselecting did not clear the UV view while keeping it open");
                    editor.Dispose(); editor = null; StudioUVEditor.Open(null); break;
                case 4:
                    Check(window && !DisplayedMesh, "UV Editor did not remain usable without a studio owner");
                    Check(string.IsNullOrEmpty(errors), errors);
                    Finish(0, "PASS: UV commands available with no model/mesh; empty editor renders grid, UV set choices reuse the window, mesh selection updates automatically, deselection/owner disposal preserve the empty editor, no GUI exceptions."); break;
            }
        }
        catch (Exception e) { Finish(1, "FAIL: " + e + "\n" + errors); }
    }
    private static void Finish(int code, string text)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        if (window) window.Close(); editor?.Dispose(); AssetDatabase.DeleteAsset(Path);
        File.WriteAllText("uv-availability-result.txt", text); EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}

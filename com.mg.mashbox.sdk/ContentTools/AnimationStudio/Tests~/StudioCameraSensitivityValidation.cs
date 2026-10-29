using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;

public static class StudioCameraSensitivityValidation
{
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    public static void Run()
    {
        EditorApplication.delayCall += () =>
        {
            var view = ScriptableObject.CreateInstance<MashBoxStudioViewport>();
            view.Show();
            EditorApplication.delayCall += () =>
            {
                int exit = 0;
                try { Validate(view); }
                catch (Exception error)
                {
                    exit = 1;
                    File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../camera-sensitivity-result.txt")), error.ToString());
                }
                view.Close();
                EditorApplication.delayCall += () => EditorApplication.Exit(exit);
            };
        };
    }
    public static void Validate(MashBoxStudioViewport view)
    {
        StudioPreferenceStore.DeleteKey(MashBoxStudioViewport.NavigationSensitivityKey);
        Check(MashBoxStudioViewport.NavigationSensitivity == 1, "Missing preference must preserve standard speed");
        foreach (bool ortho in new[] { false, true })
        foreach (float size in new[] { .02f, 200f })
        foreach (var action in new[] { StudioShortcut.Orbit, StudioShortcut.Pan, StudioShortcut.Dolly })
        {
            view.in2DMode = false; view.isRotationLocked = false; view.orthographic = ortho; view.camera.orthographic = ortho;
            var startRotation = Quaternion.Euler(20, 35, 0);
            var startPivot = new Vector3(2, 3, 4);
            var delta = action == StudioShortcut.Dolly ? new Vector2(0, 8) : new Vector2(12, -6);
            MashBoxStudioViewport.NavigationSensitivity = .25f;
            view.LookAtDirect(startPivot, startRotation, size);
            view.ApplyStudioNavigation(action, delta);
            var pivot = view.pivot; var rotation = view.rotation; float resultSize = view.size;
            MashBoxStudioViewport.NavigationSensitivity = 1;
            view.LookAtDirect(startPivot, startRotation, size);
            view.ApplyStudioNavigation(action, delta * .25f);
            Check(Vector3.Distance(view.pivot, pivot) < .00001f, "Pan multiplier mismatch");
            Check(Quaternion.Angle(view.rotation, rotation) < .01f, "Orbit multiplier mismatch");
            Check(Mathf.Abs(view.size - resultSize) < .00001f, "Zoom multiplier mismatch");
        }
        MashBoxStudioViewport.NavigationSensitivity = -1;
        Check(MashBoxStudioViewport.NavigationSensitivity == .05f, "Minimum not clamped");
        MashBoxStudioViewport.NavigationSensitivity = 100;
        Check(MashBoxStudioViewport.NavigationSensitivity == 4, "Maximum not clamped");
        MashBoxStudioViewport.NavigationSensitivity = float.NaN;
        MashBoxStudioViewport.NavigationSensitivity = float.PositiveInfinity;
        Check(MashBoxStudioViewport.NavigationSensitivity == 4, "Invalid input changed sensitivity");
        MashBoxStudioViewport.NavigationSensitivity = .35f;
        StudioPreferenceStore.Flush();
        Check(File.ReadAllText(StudioPreferenceStore.SettingsPath).Contains("Studio.Camera.NavigationSensitivity"), "Preference was not saved");
        Check(StudioPreferenceStore.GetFloat(MashBoxStudioViewport.NavigationSensitivityKey) == .35f, "Saved value mismatch");
        StudioModelEditor.ResetViewportCamera(view);
        Check(MashBoxStudioViewport.NavigationSensitivity == 1, "Camera reset must restore standard speed");
        view.in2DMode = true;
        var lockedRotation = view.rotation;
        view.ApplyStudioNavigation(StudioShortcut.Orbit, new Vector2(30, 20));
        Check(!view.in2DMode && !view.isRotationLocked && Quaternion.Angle(view.rotation, lockedRotation) > .01f, "Orbit did not release native 2D/rotation lock");
        view.in2DMode = false;
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../camera-sensitivity-result.txt")), "PASS " + checks + " camera sensitivity assertions");
    }
}

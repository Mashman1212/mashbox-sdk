using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

// Run only in the disposable compatibility project: exits Unity on completion.
public static class StudioCompatibilitySmoke
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> Results = new List<string>();
    static object Get(object value, string name) => value.GetType().GetField(name, Members).GetValue(value);
    static void Set(object value, string name, object field) => value.GetType().GetField(name, Members).SetValue(value, field);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Members).Invoke(value, args);
    static bool HasPreviewScene(SceneView view) => ((UnityEngine.SceneManagement.Scene)
        typeof(SceneView).GetProperty("customScene", Members).GetValue(view)).IsValid();
    static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
    public static void Run()
    {
        int result = 1;
        try
        {
            Results.Add("Unity " + Application.unityVersion);
            AnimationStudioWindow.Open();
            var window = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            Call(window, "NewTake");
            var take = (AnimationTake)Get(window, "take");
            Check(take && !EditorUtility.IsPersistent(take), "New animation take stays in memory");
            Check(take.character && take.body && take.clothing.Length > 0, "Default human body and outfit load on new take");
            var view = (MashBoxStudioViewport)Get(window, "viewport");
            Check(view && HasPreviewScene(view), "Animation preview scene and viewport open");
            var overlays = (IEnumerable<UnityEditor.Overlays.Overlay>)typeof(UnityEditor.Overlays.OverlayCanvas)
                .GetProperty("overlays", Members).GetValue(view.overlayCanvas);
            Check(overlays.Any() && overlays.All(o => !o.displayed), "Built-in overlays are hidden by compatibility path");
            Check((bool)typeof(MashBoxStudioViewport).GetProperty("HasStudioBindings", Members).GetValue(view), "Studio panels remain bound");
            Call(window, "EnsureModelEditor");
            Set(window, "modelingMode", true);
            Call(window, "BindStudioMode");
            Check(HasPreviewScene(view) && Get(window, "modelEditor") != null, "Modeling mode opens");
            Call(window, "OpenSkinningMode");
            var skin = Get(window, "skinningEditor");
            Check(skin != null && HasPreviewScene(view), "Skinning mode opens with default references");
            Check(Get(skin, "previewClip") is AnimationClip, "Skinning Test clip resolves");
            Call(skin, "SamplePreviewAnimation", .1f, true);
            Check(Mathf.Abs((float)Get(skin, "previewTime") - .1f) < .0001f, "Skinning timeline samples the animation");
            Call(window, "OpenRetopologyMode");
            Check(Get(window, "retopologyEditor") != null && HasPreviewScene(view), "Retopology mode opens");
            Set(window, "retopologyMode", false);
            Set(window, "skinningMode", false);
            Set(window, "modelingMode", false);
            Call(window, "OpenViewport");
            Check(ReferenceEquals(Get(window, "take"), take), "Switching modes preserves the in-memory animation");
            Check((bool)typeof(MashBoxStudioViewport).GetProperty("HasStudioBindings", Members).GetValue(view), "Animation panels rebind after switching modes");
            Results.Add("SUCCESS");
            result = 0;
        }
        catch (Exception error) { Results.Add(error.ToString()); Debug.LogException(error); }
        finally
        {
            File.WriteAllLines("compatibility-results.txt", Results);
            EditorApplication.Exit(result);
        }
    }
}
